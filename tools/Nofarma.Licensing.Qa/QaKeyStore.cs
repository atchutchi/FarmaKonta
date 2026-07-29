using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.Licensing.Qa;

public sealed record QaKeyProvisioningResult(
    string KeyId,
    ReadOnlyMemory<byte> PublicKey);

public interface IQaKeyProvisioningFaultInjector
{
    void BeforePublicCommit();

    void BeforeRollback();
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
    private const string NistP256Oid = "1.2.840.10045.3.1.7";
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("ABIPTOM.Nofarma-QA.Issuer.Key.v1");

    private readonly string _privateKeyPath;
    private readonly ILocalDataProtector _protector;
    private readonly IQaKeyProvisioningFaultInjector _faultInjector;
    private readonly IQaPrivateKeyParametersExporter _parametersExporter;
    private readonly IQaPathSecurity _pathSecurity;

    public QaKeyStore(
        string privateKeyPath,
        ILocalDataProtector protector,
        IQaKeyProvisioningFaultInjector? faultInjector = null,
        IQaPrivateKeyParametersExporter? parametersExporter = null,
        IQaPathSecurity? pathSecurity = null)
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
        ArgumentNullException.ThrowIfNull(confirmationInput);
        string fullPublicOutputPath = RequireFullPath(
            publicOutputPath,
            nameof(publicOutputPath));
        _pathSecurity.EnsureSafePath(_privateKeyPath);
        _pathSecurity.EnsureSafePath(fullPublicOutputPath);
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
            using ECDsa existingKey = OpenSigningKey();
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
                            or CryptographicException)
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
                using ECDsa previousKey = OpenSigningKey();
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

    public ECDsa OpenSigningKey()
    {
        _pathSecurity.EnsureSafePath(_privateKeyPath);
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
        string privateBackup = BackupPath(privateDirectory, _privateKeyPath);
        string publicBackup = BackupPath(publicDirectory, publicOutputPath);
        bool publicExisted = File.Exists(publicOutputPath);
        bool privateCommitted = false;
        bool publicCommitted = false;
        bool preserveBackups = false;
        byte[] encodedPublic = Encoding.ASCII.GetBytes(
            string.Concat(Convert.ToBase64String(publicKey), Environment.NewLine));
        try
        {
            WriteNewPrivateFile(privateTemporary, protectedKey);
            WriteNewFile(publicTemporary, encodedPublic);

            _pathSecurity.EnsureSafePath(_privateKeyPath);
            _pathSecurity.EnsureSafePath(publicOutputPath);

            if (privateExisted)
            {
                File.Replace(privateTemporary, _privateKeyPath, privateBackup);
            }
            else
            {
                File.Move(privateTemporary, _privateKeyPath, overwrite: false);
            }

            privateCommitted = true;
            _pathSecurity.ProtectPrivateFile(_privateKeyPath);
            if (File.Exists(privateBackup))
            {
                _pathSecurity.ProtectPrivateFile(privateBackup);
            }

            _faultInjector.BeforePublicCommit();

            if (publicExisted)
            {
                File.Replace(publicTemporary, publicOutputPath, publicBackup);
            }
            else
            {
                File.Move(publicTemporary, publicOutputPath, overwrite: false);
            }

            publicCommitted = true;
            VerifyActivePair(publicKey, publicOutputPath);
        }
        catch (Exception exception)
        {
            try
            {
                _faultInjector.BeforeRollback();
                RollBackKeyPair(
                    privateExisted,
                    publicExisted,
                    privateCommitted,
                    publicCommitted,
                    privateBackup,
                    publicBackup,
                    publicOutputPath);
                if (privateExisted)
                {
                    VerifyActivePair(previousPublicKey, publicOutputPath);
                }
                else if (File.Exists(_privateKeyPath))
                {
                    throw new IOException(
                        "The failed QA provisioning left a private key active.");
                }
            }
            catch (Exception recoveryException)
            {
                preserveBackups = true;
                throw new QaIssuerException(
                    "QA_ROTATION_RECOVERY_REQUIRED",
                    "QA key provisioning failed and automatic recovery could not be verified.",
                    new AggregateException(exception, recoveryException));
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
            if (!preserveBackups)
            {
                DeleteOwnedBackup(privateBackup, privateDirectory, _privateKeyPath);
                DeleteOwnedBackup(publicBackup, publicDirectory, publicOutputPath);
            }
        }
    }

    private void RollBackKeyPair(
        bool privateExisted,
        bool publicExisted,
        bool privateCommitted,
        bool publicCommitted,
        string privateBackup,
        string publicBackup,
        string publicOutputPath)
    {
        if (publicCommitted)
        {
            if (publicExisted)
            {
                File.Replace(publicBackup, publicOutputPath, destinationBackupFileName: null);
            }
            else
            {
                File.Delete(publicOutputPath);
            }
        }

        if (privateCommitted)
        {
            if (privateExisted)
            {
                File.Replace(privateBackup, _privateKeyPath, destinationBackupFileName: null);
            }
            else
            {
                File.Delete(_privateKeyPath);
            }

            if (File.Exists(_privateKeyPath))
            {
                _pathSecurity.ProtectPrivateFile(_privateKeyPath);
            }
        }
    }

    private void VerifyActivePair(byte[] expectedPublicKey, string publicOutputPath)
    {
        using ECDsa activePrivateKey = OpenSigningKey();
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

    private static byte[] ReadPublicKey(string publicOutputPath)
    {
        string encoded = File.ReadAllText(publicOutputPath, Encoding.ASCII);
        byte[] publicKey = Convert.FromBase64String(encoded);
        ValidateP256PublicKey(publicKey);
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

    private static string BackupPath(string directory, string targetPath) =>
        Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.bak");

    private static void DeleteOwnedTemporary(
        string temporaryPath,
        string directory,
        string targetPath)
    {
        DeleteOwnedArtifact(temporaryPath, directory, targetPath, ".tmp");
    }

    private static void DeleteOwnedBackup(
        string backupPath,
        string directory,
        string targetPath) =>
        DeleteOwnedArtifact(backupPath, directory, targetPath, ".bak");

    private static void DeleteOwnedArtifact(
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
        if (owned && File.Exists(fullArtifactPath))
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
            if (OperatingSystem.IsWindows())
            {
                ProtectPrivateFileWindows(path);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void ProtectPrivateFileWindows(string path)
        {
            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException(
                    "The current Windows user identity is unavailable.");
            var security = new FileSecurity();
            security.SetOwner(currentUser);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
    }

    internal sealed class DefaultPrivateKeyParametersExporter
        : IQaPrivateKeyParametersExporter
    {
        internal static readonly DefaultPrivateKeyParametersExporter Instance = new();

        public ECParameters Export(ECDsa key) =>
            key.ExportParameters(includePrivateParameters: true);
    }
}
