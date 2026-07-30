using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.Licensing.Qa;

public sealed record QaKeyProvisioningResult(
    string KeyId,
    ReadOnlyMemory<byte> PublicKey);

public interface IQaKeyProvisioningFaultInjector
{
    bool SimulatesProcessTermination => false;

    void BeforeManifestTemporaryCreate(string path)
    {
    }

    void BeforePublicCommit();

    void BeforeRollback();

    void AfterPrivateCommit()
    {
    }

    void AfterPublicCommit()
    {
    }
}

public interface IQaPrivateKeyParametersExporter
{
    ECParameters Export(ECDsa key);
}

public interface IQaPathSecurity
{
    void EnsureSafePath(string path);

    void ProtectPrivateFile(string path);
}

public sealed class QaKeyStore
{
    public const string RotationConfirmation = "ROTATE-QA-KEY";

    private const int MaximumProtectedKeyBytes = 32 * 1024;
    private const int MaximumManifestTemporaryCreateAttempts = 20;
    private const int ManifestTemporaryCreateRetryDelayMilliseconds = 50;
    private const int ErrorFileExistsHResult = unchecked((int)0x80070050);
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);
    private const string NistP256Oid = "1.2.840.10045.3.1.7";
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("ABIPTOM.Nofarma-QA.Issuer.Key.v1");

    private readonly string _privateKeyPath;
    private readonly ILocalDataProtector _protector;
    private readonly IQaKeyProvisioningFaultInjector _faultInjector;
    private readonly IQaPrivateKeyParametersExporter _parametersExporter;
    private readonly IQaPathSecurity _pathSecurity;
    private readonly IQaPublicKeyDecoder _publicKeyDecoder;
    private readonly IQaKeyStoreLock _keyStoreLock;

    public QaKeyStore(
        string privateKeyPath,
        ILocalDataProtector protector,
        IQaKeyProvisioningFaultInjector? faultInjector = null,
        IQaPrivateKeyParametersExporter? parametersExporter = null,
        IQaPathSecurity? pathSecurity = null,
        IQaPublicKeyDecoder? publicKeyDecoder = null,
        IQaKeyStoreLock? keyStoreLock = null)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new ArgumentException(
                "The QA private key path is required.",
                nameof(privateKeyPath));
        }

        _privateKeyPath = Path.GetFullPath(privateKeyPath);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _faultInjector = faultInjector ?? NoProvisioningFaults.Instance;
        _parametersExporter = parametersExporter ??
            DefaultPrivateKeyParametersExporter.Instance;
        _pathSecurity = pathSecurity ?? DefaultPathSecurity.Instance;
        _publicKeyDecoder = publicKeyDecoder ?? QaPublicKeyValidator.DefaultDecoder;
        _keyStoreLock = keyStoreLock ?? new QaNamedKeyStoreLock(_privateKeyPath);
    }

    public static string DefaultPrivateKeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ABIPTOM",
        "Nofarma-QA",
        "issuer",
        "qa-signing-key.bin");

    [SupportedOSPlatform("windows")]
    public static QaKeyStore CreateDefault() =>
        new(DefaultPrivateKeyPath, new CurrentUserProtector());

    public QaKeyProvisioningResult Provision(
        string publicOutputPath,
        bool rotate,
        TextReader confirmationInput)
    {
        using IDisposable keyStoreLock = _keyStoreLock.Acquire();
        return ProvisionCore(publicOutputPath, rotate, confirmationInput);
    }

    private QaKeyProvisioningResult ProvisionCore(
        string publicOutputPath,
        bool rotate,
        TextReader confirmationInput)
    {
        ArgumentNullException.ThrowIfNull(confirmationInput);
        string fullPublicOutputPath = RequireFullPath(
            publicOutputPath,
            nameof(publicOutputPath));
        _pathSecurity.EnsureSafePath(_privateKeyPath);
        _pathSecurity.EnsureSafePath(fullPublicOutputPath);
        _ = RecoverInterruptedProvisioning(fullPublicOutputPath);
        if (File.Exists(_privateKeyPath))
        {
            _pathSecurity.ProtectPrivateFile(_privateKeyPath);
        }
        if (string.Equals(
                fullPublicOutputPath,
                _privateKeyPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new QaIssuerException(
                "QA_PUBLIC_OUTPUT_INVALID",
                "The QA public output cannot replace the protected private key.");
        }

        bool exists = File.Exists(_privateKeyPath);

        if (exists && !rotate)
        {
            using ECDsa existingKey = OpenSigningKeyCore();
            byte[] existingPublicKey = existingKey.ExportSubjectPublicKeyInfo();
            try
            {
                if (File.Exists(fullPublicOutputPath))
                {
                    byte[] outputPublicKey;
                    try
                    {
                        outputPublicKey = ReadPublicKey(fullPublicOutputPath);
                    }
                    catch (Exception exception) when (
                        exception is IOException
                            or UnauthorizedAccessException
                            or FormatException
                            or CryptographicException
                            or PlatformNotSupportedException)
                    {
                        throw PublicOutputConflict(exception);
                    }

                    try
                    {
                        if (!PublicKeysEqual(existingPublicKey, outputPublicKey))
                        {
                            throw PublicOutputConflict();
                        }
                    }
                    catch (Exception exception) when (
                        exception is CryptographicException
                            or PlatformNotSupportedException)
                    {
                        throw PublicOutputConflict(exception);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(outputPublicKey);
                    }

                    return Result(existingPublicKey);
                }

                WritePublicKey(fullPublicOutputPath, existingPublicKey);
                return Result(existingPublicKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(existingPublicKey);
            }
        }

        if (exists && rotate)
        {
            string? confirmation = confirmationInput.ReadLine();
            if (!string.Equals(
                    confirmation,
                    RotationConfirmation,
                    StringComparison.Ordinal))
            {
                throw new QaIssuerException(
                    "QA_ROTATION_CONFIRMATION_REQUIRED",
                    $"QA key rotation requires the exact confirmation {RotationConfirmation}.");
            }
        }
        else if (!exists && File.Exists(fullPublicOutputPath))
        {
            throw PublicOutputConflict();
        }

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] privateKey = key.ExportPkcs8PrivateKey();
        byte[] protectedKey = Array.Empty<byte>();
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] previousPublicKey = Array.Empty<byte>();
        try
        {
            if (exists)
            {
                using ECDsa previousKey = OpenSigningKeyCore();
                previousPublicKey = previousKey.ExportSubjectPublicKeyInfo();
            }

            protectedKey = _protector.Protect(privateKey, Entropy);
            CommitKeyPair(
                protectedKey,
                publicKey,
                previousPublicKey,
                fullPublicOutputPath,
                privateExisted: exists);
            return Result(publicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            if (protectedKey.Length > 0)
            {
                CryptographicOperations.ZeroMemory(protectedKey);
            }

            if (previousPublicKey.Length > 0)
            {
                CryptographicOperations.ZeroMemory(previousPublicKey);
            }
        }
    }

    public void UseSigningKey(Action<ECDsa> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using IDisposable keyStoreLock = _keyStoreLock.Acquire();
        _pathSecurity.EnsureSafePath(_privateKeyPath);
        _ = RecoverInterruptedProvisioning();
        using ECDsa signingKey = OpenSigningKeyCore();
        operation(signingKey);
    }

    public ReadOnlyMemory<byte> GetPublicKey()
    {
        byte[] publicKey = Array.Empty<byte>();
        UseSigningKey(key => publicKey = key.ExportSubjectPublicKeyInfo());
        return publicKey;
    }

    private ECDsa OpenSigningKeyCore()
    {
        if (File.Exists(_privateKeyPath))
        {
            _pathSecurity.ProtectPrivateFile(_privateKeyPath);
        }

        byte[] protectedKey = ReadProtectedKey();
        byte[] privateKey = Array.Empty<byte>();
        try
        {
            privateKey = _protector.Unprotect(protectedKey, Entropy);
            var key = ECDsa.Create();
            try
            {
                key.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
                ValidateP256PrivateKey(key, bytesRead, privateKey.Length);
                return key;
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException)
        {
            throw new CryptographicException(
                "The protected QA signing key is invalid or unavailable.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            if (privateKey.Length > 0)
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }
        }
    }

    private byte[] ReadProtectedKey()
    {
        using var stream = new FileStream(
            _privateKeyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length < 1 || stream.Length > MaximumProtectedKeyBytes)
        {
            throw new CryptographicException(
                "The protected QA signing key has an invalid size.");
        }

        byte[] protectedKey = new byte[checked((int)stream.Length)];
        stream.ReadExactly(protectedKey);
        return protectedKey;
    }

    private void CommitKeyPair(
        ReadOnlySpan<byte> protectedKey,
        byte[] publicKey,
        byte[] previousPublicKey,
        string publicOutputPath,
        bool privateExisted)
    {
        ValidateP256PublicKey(publicKey);
        string privateDirectory = EnsureDirectory(_privateKeyPath);
        string publicDirectory = EnsureDirectory(publicOutputPath);
        string privateTemporary = TemporaryPath(privateDirectory, _privateKeyPath);
        string publicTemporary = TemporaryPath(publicDirectory, publicOutputPath);
        string manifestPath = RotationManifestPath();
        string privateBackup = RotationBackupPath(_privateKeyPath);
        string publicBackup = RotationBackupPath(publicOutputPath);
        bool publicExisted = File.Exists(publicOutputPath);
        byte[] encodedPublic = Encoding.ASCII.GetBytes(
            string.Concat(Convert.ToBase64String(publicKey), Environment.NewLine));
        var manifest = new RotationManifest(
            1,
            _privateKeyPath,
            publicOutputPath,
            privateExisted,
            publicExisted,
            Convert.ToBase64String(previousPublicKey),
            Convert.ToBase64String(publicKey));
        try
        {
            WriteNewPrivateFile(privateTemporary, protectedKey);
            WriteNewFile(publicTemporary, encodedPublic);
            WriteRotationManifest(manifestPath, manifest);
            if (privateExisted)
            {
                CopyRecoveryFile(_privateKeyPath, privateBackup, privateFile: true);
            }

            if (publicExisted)
            {
                CopyRecoveryFile(publicOutputPath, publicBackup, privateFile: false);
            }

            _pathSecurity.EnsureSafePath(_privateKeyPath);
            _pathSecurity.EnsureSafePath(publicOutputPath);

            if (privateExisted)
            {
                File.Replace(
                    privateTemporary,
                    _privateKeyPath,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(privateTemporary, _privateKeyPath, overwrite: false);
            }

            _pathSecurity.ProtectPrivateFile(_privateKeyPath);
            _faultInjector.AfterPrivateCommit();
            _faultInjector.BeforePublicCommit();

            if (publicExisted)
            {
                File.Replace(
                    publicTemporary,
                    publicOutputPath,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(publicTemporary, publicOutputPath, overwrite: false);
            }

            _faultInjector.AfterPublicCommit();
            VerifyActivePair(publicKey, publicOutputPath);
            CleanupRecoveryArtifacts(manifest);
        }
        catch (Exception) when (_faultInjector.SimulatesProcessTermination)
        {
            throw;
        }
        catch (QaIssuerException exception) when (
            exception.Code == "QA_ROTATION_RECOVERY_REQUIRED")
        {
            throw;
        }
        catch (Exception exception)
        {
            RecoveryOutcome recoveryOutcome;
            try
            {
                _faultInjector.BeforeRollback();
                recoveryOutcome = RecoverInterruptedProvisioning();
            }
            catch (Exception recoveryException)
            {
                throw new QaIssuerException(
                    "QA_ROTATION_RECOVERY_REQUIRED",
                    "QA key provisioning failed and automatic recovery could not be verified.",
                    new AggregateException(exception, recoveryException));
            }

            if (recoveryOutcome == RecoveryOutcome.New)
            {
                return;
            }

            throw new QaIssuerException(
                privateExisted ? "QA_ROTATION_FAILED" : "QA_PROVISIONING_FAILED",
                privateExisted
                    ? "QA key rotation failed and the previous key remains active."
                    : "QA key provisioning failed without activating the new key.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encodedPublic);
            DeleteOwnedTemporary(privateTemporary, privateDirectory, _privateKeyPath);
            DeleteOwnedTemporary(publicTemporary, publicDirectory, publicOutputPath);
        }
    }

    private RecoveryOutcome RecoverInterruptedProvisioning(
        string? expectedPublicOutputPath = null)
    {
        string manifestPath = RotationManifestPath();
        string manifestTemporary = RotationManifestTemporaryPath();
        if (!File.Exists(manifestPath))
        {
            for (int attempt = 0; RecoveryArtifactsExist(
                     manifestTemporary,
                     expectedPublicOutputPath); attempt++)
            {
                if (attempt >= 19)
                {
                    throw RecoveryRequired();
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(50));
            }

            return RecoveryOutcome.None;
        }

        try
        {
            RotationManifest manifest = ReadRotationManifest(manifestPath);
            ValidateRotationManifest(manifest);
            byte[] previousPublicKey = DecodeManifestPublicKey(
                manifest.PreviousPublicKey,
                required: manifest.PrivateExisted);
            byte[] newPublicKey = DecodeManifestPublicKey(
                manifest.NewPublicKey,
                required: true);
            try
            {
                if (CurrentPairMatches(manifest, newPublicKey, newPair: true))
                {
                    CleanupRecoveryArtifacts(manifest);
                    return RecoveryOutcome.New;
                }

                if (CurrentPairMatches(
                        manifest,
                        previousPublicKey,
                        newPair: false))
                {
                    CleanupRecoveryArtifacts(manifest);
                    return RecoveryOutcome.Previous;
                }

                RestorePreviousState(manifest, previousPublicKey);
                CleanupRecoveryArtifacts(manifest);
                return RecoveryOutcome.Previous;
            }
            finally
            {
                if (previousPublicKey.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(previousPublicKey);
                }

                CryptographicOperations.ZeroMemory(newPublicKey);
            }
        }
        catch (QaIssuerException exception) when (
            exception.Code == "QA_ROTATION_RECOVERY_REQUIRED")
        {
            throw;
        }
        catch (Exception exception)
        {
            throw RecoveryRequired(exception);
        }
    }

    private bool RecoveryArtifactsExist(
        string manifestTemporary,
        string? expectedPublicOutputPath) =>
        File.Exists(manifestTemporary)
        || File.Exists(RotationBackupPath(_privateKeyPath))
        || expectedPublicOutputPath is not null
            && File.Exists(RotationBackupPath(expectedPublicOutputPath));

    private RotationManifest ReadRotationManifest(string manifestPath)
    {
        _pathSecurity.EnsureSafePath(manifestPath);
        using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length < 1 || stream.Length > 64 * 1024)
        {
            throw new InvalidDataException("The QA rotation manifest size is invalid.");
        }

        byte[] json = new byte[checked((int)stream.Length)];
        try
        {
            stream.ReadExactly(json);
            return JsonSerializer.Deserialize<RotationManifest>(json)
                ?? throw new InvalidDataException("The QA rotation manifest is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private void ValidateRotationManifest(RotationManifest manifest)
    {
        if (manifest.Version != 1
            || !string.Equals(
                manifest.PrivateKeyPath,
                _privateKeyPath,
                StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(manifest.PublicOutputPath)
            || !string.Equals(
                Path.GetFullPath(manifest.PublicOutputPath),
                manifest.PublicOutputPath,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                manifest.PublicOutputPath,
                _privateKeyPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The QA rotation manifest is invalid.");
        }

        _pathSecurity.EnsureSafePath(manifest.PrivateKeyPath);
        _pathSecurity.EnsureSafePath(manifest.PublicOutputPath);
        _pathSecurity.EnsureSafePath(RotationBackupPath(manifest.PrivateKeyPath));
        _pathSecurity.EnsureSafePath(RotationBackupPath(manifest.PublicOutputPath));
    }

    private static byte[] DecodeManifestPublicKey(string encoded, bool required)
    {
        if (!required && string.IsNullOrEmpty(encoded))
        {
            return Array.Empty<byte>();
        }

        byte[] publicKey = Convert.FromBase64String(encoded);
        ValidateP256PublicKey(publicKey);
        return publicKey;
    }

    private bool CurrentPairMatches(
        RotationManifest manifest,
        byte[] expectedPublicKey,
        bool newPair)
    {
        bool privateShouldExist = newPair || manifest.PrivateExisted;
        bool publicShouldExist = newPair || manifest.PublicExisted;
        bool privateMatches = privateShouldExist
            ? CurrentPrivateMatches(expectedPublicKey)
            : !File.Exists(_privateKeyPath);
        bool publicMatches = publicShouldExist
            ? CurrentPublicMatches(manifest.PublicOutputPath, expectedPublicKey)
            : !File.Exists(manifest.PublicOutputPath);
        return privateMatches && publicMatches;
    }

    private bool CurrentPrivateMatches(byte[] expectedPublicKey)
    {
        if (!File.Exists(_privateKeyPath))
        {
            return false;
        }

        using ECDsa key = OpenSigningKeyCore();
        byte[] actualPublicKey = key.ExportSubjectPublicKeyInfo();
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                expectedPublicKey,
                actualPublicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualPublicKey);
        }
    }

    private bool CurrentPublicMatches(
        string publicOutputPath,
        byte[] expectedPublicKey)
    {
        if (!File.Exists(publicOutputPath))
        {
            return false;
        }

        byte[] actualPublicKey = ReadPublicKey(publicOutputPath);
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                expectedPublicKey,
                actualPublicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualPublicKey);
        }
    }

    private void RestorePreviousState(
        RotationManifest manifest,
        byte[] previousPublicKey)
    {
        RestoreRecoveryTarget(
            _privateKeyPath,
            RotationBackupPath(_privateKeyPath),
            manifest.PrivateExisted,
            privateFile: true);
        RestoreRecoveryTarget(
            manifest.PublicOutputPath,
            RotationBackupPath(manifest.PublicOutputPath),
            manifest.PublicExisted,
            privateFile: false);
        if (!CurrentPairMatches(manifest, previousPublicKey, newPair: false))
        {
            throw new CryptographicException(
                "The previous QA key pair could not be restored.");
        }
    }

    private void RestoreRecoveryTarget(
        string targetPath,
        string backupPath,
        bool targetExisted,
        bool privateFile)
    {
        _pathSecurity.EnsureSafePath(targetPath);
        _pathSecurity.EnsureSafePath(backupPath);
        if (!targetExisted)
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            return;
        }

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException(
                "A required QA recovery backup is unavailable.");
        }

        string directory = EnsureDirectory(targetPath);
        string temporaryPath = TemporaryPath(directory, targetPath);
        try
        {
            CopyRecoveryFile(backupPath, temporaryPath, privateFile);
            if (File.Exists(targetPath))
            {
                File.Replace(
                    temporaryPath,
                    targetPath,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
            }

            if (privateFile)
            {
                _pathSecurity.ProtectPrivateFile(targetPath);
            }
        }
        finally
        {
            DeleteOwnedTemporary(temporaryPath, directory, targetPath);
        }
    }

    private void CleanupRecoveryArtifacts(RotationManifest manifest)
    {
        DeleteRecoveryArtifact(
            RotationBackupPath(manifest.PublicOutputPath),
            RotationBackupPath(manifest.PublicOutputPath));
        DeleteRecoveryArtifact(
            RotationBackupPath(_privateKeyPath),
            RotationBackupPath(_privateKeyPath));
        DeleteRecoveryArtifact(RotationManifestPath(), RotationManifestPath());
    }

    private void DeleteRecoveryArtifact(string artifactPath, string expectedPath)
    {
        string fullArtifactPath = Path.GetFullPath(artifactPath);
        string fullExpectedPath = Path.GetFullPath(expectedPath);
        if (!string.Equals(
                fullArtifactPath,
                fullExpectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new QaIssuerException(
                "QA_RECOVERY_ARTIFACT_INVALID",
                "A QA recovery artefact could not be safely identified.");
        }

        _pathSecurity.EnsureSafePath(fullArtifactPath);
        if (File.Exists(fullArtifactPath))
        {
            File.Delete(fullArtifactPath);
        }
    }

    private static QaIssuerException RecoveryRequired(Exception? exception = null) =>
        new(
            "QA_ROTATION_RECOVERY_REQUIRED",
            "QA key recovery could not establish a coherent private and public pair.",
            exception);

    private void VerifyActivePair(byte[] expectedPublicKey, string publicOutputPath)
    {
        using ECDsa activePrivateKey = OpenSigningKeyCore();
        byte[] activePublicKey = activePrivateKey.ExportSubjectPublicKeyInfo();
        byte[] publicFileKey = ReadPublicKey(publicOutputPath);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    expectedPublicKey,
                    activePublicKey)
                || !CryptographicOperations.FixedTimeEquals(
                    expectedPublicKey,
                    publicFileKey))
            {
                throw new CryptographicException(
                    "The QA private and public keys are not an active pair.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activePublicKey);
            CryptographicOperations.ZeroMemory(publicFileKey);
        }
    }

    private byte[] ReadPublicKey(string publicOutputPath)
    {
        string encoded = File.ReadAllText(publicOutputPath, Encoding.ASCII);
        byte[] publicKey = Convert.FromBase64String(encoded);
        QaDecodedPublicKey decoded = _publicKeyDecoder.Decode(publicKey);
        if (decoded.BytesRead != publicKey.Length
            || decoded.KeySize != 256
            || decoded.X is not { Length: 32 }
            || decoded.Y is not { Length: 32 }
            || !string.Equals(
                decoded.CurveOid,
                NistP256Oid,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "The QA public key is not an ECDSA NIST P-256 SPKI.");
        }

        return publicKey;
    }

    private static bool PublicKeysEqual(byte[] first, byte[] second)
    {
        using ECDsa firstKey = ECDsa.Create();
        using ECDsa secondKey = ECDsa.Create();
        firstKey.ImportSubjectPublicKeyInfo(first, out int firstBytesRead);
        secondKey.ImportSubjectPublicKeyInfo(second, out int secondBytesRead);
        ECParameters firstParameters = firstKey.ExportParameters(
            includePrivateParameters: false);
        ECParameters secondParameters = secondKey.ExportParameters(
            includePrivateParameters: false);
        return firstBytesRead == first.Length
            && secondBytesRead == second.Length
            && firstParameters.Q.X is not null
            && firstParameters.Q.Y is not null
            && secondParameters.Q.X is not null
            && secondParameters.Q.Y is not null
            && firstParameters.Q.X.Length == secondParameters.Q.X.Length
            && firstParameters.Q.Y.Length == secondParameters.Q.Y.Length
            && CryptographicOperations.FixedTimeEquals(
                firstParameters.Q.X,
                secondParameters.Q.X)
            && CryptographicOperations.FixedTimeEquals(
                firstParameters.Q.Y,
                secondParameters.Q.Y);
    }

    private static QaIssuerException PublicOutputConflict(
        Exception? innerException = null) =>
        new(
            "QA_PUBLIC_OUTPUT_CONFLICT",
            "The existing QA public output does not match the protected QA key.",
            innerException);

    private void WritePublicKey(string publicOutputPath, byte[] publicKey)
    {
        ValidateP256PublicKey(publicKey);
        string directory = EnsureDirectory(publicOutputPath);
        string temporaryPath = TemporaryPath(directory, publicOutputPath);
        byte[] encoded = Encoding.ASCII.GetBytes(
            string.Concat(Convert.ToBase64String(publicKey), Environment.NewLine));
        try
        {
            WriteNewFile(temporaryPath, encoded);
            File.Move(temporaryPath, publicOutputPath, overwrite: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
            DeleteOwnedTemporary(temporaryPath, directory, publicOutputPath);
        }
    }

    private static void WriteNewFile(string path, ReadOnlySpan<byte> contents)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private void WriteRotationManifest(
        string manifestPath,
        RotationManifest manifest)
    {
        string temporaryPath = RotationManifestTemporaryPath();
        string[] recoveryPaths =
        [
            manifestPath,
            temporaryPath,
            RotationBackupPath(_privateKeyPath),
            RotationBackupPath(manifest.PublicOutputPath)
        ];
        if (recoveryPaths.Any(File.Exists))
        {
            throw RecoveryRequired();
        }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest);
        try
        {
            WriteManifestTemporaryWithRetry(
                temporaryPath,
                json,
                recoveryPaths);
            File.Move(temporaryPath, manifestPath, overwrite: false);
            _pathSecurity.ProtectPrivateFile(manifestPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private void WriteManifestTemporaryWithRetry(
        string temporaryPath,
        ReadOnlySpan<byte> contents,
        IReadOnlyList<string> recoveryPaths)
    {
        for (int attempt = 1;
             attempt <= MaximumManifestTemporaryCreateAttempts;
             attempt++)
        {
            try
            {
                _faultInjector.BeforeManifestTemporaryCreate(temporaryPath);
                WriteNewPrivateFile(temporaryPath, contents);
                return;
            }
            catch (IOException exception) when (
                IsExclusiveCreateCollision(exception))
            {
                bool recoveryArtifactIsVisible = recoveryPaths.Any(File.Exists);
                if (recoveryArtifactIsVisible
                    || attempt == MaximumManifestTemporaryCreateAttempts)
                {
                    throw RecoveryRequired(exception);
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(
                    ManifestTemporaryCreateRetryDelayMilliseconds));
            }
        }
    }

    private static bool IsExclusiveCreateCollision(IOException exception) =>
        exception.HResult is ErrorFileExistsHResult or ErrorAlreadyExistsHResult;

    private void CopyRecoveryFile(
        string sourcePath,
        string destinationPath,
        bool privateFile)
    {
        _pathSecurity.EnsureSafePath(sourcePath);
        _pathSecurity.EnsureSafePath(destinationPath);
        using (new FileStream(
                   destinationPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
        }

        if (privateFile)
        {
            _pathSecurity.ProtectPrivateFile(destinationPath);
        }

        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var destination = new FileStream(
            destinationPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    private void WriteNewPrivateFile(string path, ReadOnlySpan<byte> contents)
    {
        using (new FileStream(
                   path,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
        }

        _pathSecurity.ProtectPrivateFile(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private string EnsureDirectory(string path)
    {
        _pathSecurity.EnsureSafePath(path);
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            throw new InvalidOperationException("The QA key path is invalid.");
        }

        Directory.CreateDirectory(directory);
        _pathSecurity.EnsureSafePath(path);
        return directory;
    }

    private static string TemporaryPath(string directory, string targetPath) =>
        Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    private string RotationManifestPath() =>
        string.Concat(_privateKeyPath, ".rotation.json");

    private string RotationManifestTemporaryPath() =>
        string.Concat(RotationManifestPath(), ".tmp");

    private static string RotationBackupPath(string targetPath) =>
        string.Concat(targetPath, ".rotation.bak");

    private void DeleteOwnedTemporary(
        string temporaryPath,
        string directory,
        string targetPath)
    {
        DeleteOwnedArtifact(temporaryPath, directory, targetPath, ".tmp");
    }

    private void DeleteOwnedArtifact(
        string artifactPath,
        string directory,
        string targetPath,
        string suffix)
    {
        string fullArtifactPath = Path.GetFullPath(artifactPath);
        string fullDirectory = Path.GetFullPath(directory);
        string? artifactDirectory = Path.GetDirectoryName(fullArtifactPath);
        string fileName = Path.GetFileName(fullArtifactPath);
        string prefix = $".{Path.GetFileName(targetPath)}.";
        int identifierLength = fileName.Length - prefix.Length - suffix.Length;
        bool owned = identifierLength == 32
            && string.Equals(
                artifactDirectory,
                fullDirectory,
                StringComparison.OrdinalIgnoreCase)
            && fileName.StartsWith(prefix, StringComparison.Ordinal)
            && fileName.EndsWith(suffix, StringComparison.Ordinal)
            && Guid.TryParseExact(
                fileName.AsSpan(prefix.Length, identifierLength),
                "N",
                out _);
        if (!owned)
        {
            throw new QaIssuerException(
                "QA_RECOVERY_ARTIFACT_INVALID",
                "A QA temporary artefact could not be safely identified.");
        }

        _pathSecurity.EnsureSafePath(fullArtifactPath);
        if (File.Exists(fullArtifactPath))
        {
            File.Delete(fullArtifactPath);
        }
    }

    private static string RequireFullPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The path is required.", parameterName);
        }

        return Path.GetFullPath(path);
    }

    private static QaKeyProvisioningResult Result(byte[] publicKey)
    {
        byte[] hash = SHA256.HashData(publicKey);
        string keyId = $"qa-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
        return new QaKeyProvisioningResult(keyId, publicKey.ToArray());
    }

    private void ValidateP256PrivateKey(
        ECDsa key,
        int bytesRead,
        int inputLength)
    {
        ECParameters parameters = default;
        try
        {
            parameters = _parametersExporter.Export(key);
            if (bytesRead != inputLength
                || key.KeySize != 256
                || parameters.D is null
                || !string.Equals(
                    parameters.Curve.Oid.Value,
                    NistP256Oid,
                    StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    "The protected QA signing key is not an ECDSA NIST P-256 private key.");
            }
        }
        finally
        {
            if (parameters.D is not null)
            {
                CryptographicOperations.ZeroMemory(parameters.D);
            }
        }
    }

    private static void ValidateP256PublicKey(byte[] subjectPublicKey)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(subjectPublicKey, out int bytesRead);
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        if (bytesRead != subjectPublicKey.Length
            || key.KeySize != 256
            || !string.Equals(
                parameters.Curve.Oid.Value,
                NistP256Oid,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "The QA public key is not an ECDSA NIST P-256 SPKI.");
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class CurrentUserProtector : ILocalDataProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> clear, ReadOnlySpan<byte> entropy)
        {
            byte[] clearCopy = clear.ToArray();
            byte[] entropyCopy = entropy.ToArray();
            try
            {
                return ProtectedData.Protect(
                    clearCopy,
                    entropyCopy,
                    DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearCopy);
                CryptographicOperations.ZeroMemory(entropyCopy);
            }
        }

        public byte[] Unprotect(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> entropy)
        {
            byte[] encryptedCopy = encrypted.ToArray();
            byte[] entropyCopy = entropy.ToArray();
            try
            {
                return ProtectedData.Unprotect(
                    encryptedCopy,
                    entropyCopy,
                    DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptedCopy);
                CryptographicOperations.ZeroMemory(entropyCopy);
            }
        }
    }

    private sealed class NoProvisioningFaults : IQaKeyProvisioningFaultInjector
    {
        internal static readonly NoProvisioningFaults Instance = new();

        public void BeforePublicCommit()
        {
        }

        public void BeforeRollback()
        {
        }
    }

    private sealed class DefaultPathSecurity : IQaPathSecurity
    {
        internal static readonly DefaultPathSecurity Instance = new();

        public void EnsureSafePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath)
                ?? throw new InvalidOperationException("The QA key path is invalid.");
            string current = root;
            foreach (string segment in fullPath[root.Length..].Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(current);
                }
                catch (Exception exception) when (
                    exception is FileNotFoundException
                        or DirectoryNotFoundException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new QaIssuerException(
                        "QA_PATH_REPARSE_POINT",
                        "QA key paths cannot traverse filesystem reparse points.");
                }
            }
        }

        public void ProtectPrivateFile(string path)
        {
            QaFileSecurity.ProtectForCurrentUser(path);
        }
    }

    internal sealed class DefaultPrivateKeyParametersExporter
        : IQaPrivateKeyParametersExporter
    {
        internal static readonly DefaultPrivateKeyParametersExporter Instance = new();

        public ECParameters Export(ECDsa key) =>
            key.ExportParameters(includePrivateParameters: true);
    }

    private sealed record RotationManifest(
        int Version,
        string PrivateKeyPath,
        string PublicOutputPath,
        bool PrivateExisted,
        bool PublicExisted,
        string PreviousPublicKey,
        string NewPublicKey);

    private enum RecoveryOutcome
    {
        None,
        Previous,
        New
    }
}
