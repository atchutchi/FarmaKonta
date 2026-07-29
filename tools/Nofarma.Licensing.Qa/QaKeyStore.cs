using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.Licensing.Qa;

public sealed record QaKeyProvisioningResult(
    string KeyId,
    ReadOnlyMemory<byte> PublicKey);

public sealed class QaKeyStore
{
    public const string RotationConfirmation = "ROTATE-QA-KEY";

    private const int MaximumProtectedKeyBytes = 32 * 1024;
    private const string NistP256Oid = "1.2.840.10045.3.1.7";
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("ABIPTOM.Nofarma-QA.Issuer.Key.v1");

    private readonly string _privateKeyPath;
    private readonly ILocalDataProtector _protector;

    public QaKeyStore(string privateKeyPath, ILocalDataProtector protector)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new ArgumentException(
                "The QA private key path is required.",
                nameof(privateKeyPath));
        }

        _privateKeyPath = Path.GetFullPath(privateKeyPath);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
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
            WritePublicKey(fullPublicOutputPath, existingPublicKey);
            return Result(existingPublicKey);
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

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] privateKey = key.ExportPkcs8PrivateKey();
        byte[] protectedKey = Array.Empty<byte>();
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        try
        {
            protectedKey = _protector.Protect(privateKey, Entropy);
            WriteProtectedKey(protectedKey, replace: exists);
            WritePublicKey(fullPublicOutputPath, publicKey);
            return Result(publicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            if (protectedKey.Length > 0)
            {
                CryptographicOperations.ZeroMemory(protectedKey);
            }
        }
    }

    public ECDsa OpenSigningKey()
    {
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

    private void WriteProtectedKey(ReadOnlySpan<byte> protectedKey, bool replace)
    {
        string directory = EnsureDirectory(_privateKeyPath);
        string temporaryPath = TemporaryPath(directory, _privateKeyPath);
        try
        {
            WriteNewFile(temporaryPath, protectedKey);
            if (replace)
            {
                File.Replace(
                    temporaryPath,
                    _privateKeyPath,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _privateKeyPath, overwrite: false);
            }
        }
        finally
        {
            DeleteOwnedTemporary(temporaryPath, directory, _privateKeyPath);
        }
    }

    private static void WritePublicKey(string publicOutputPath, byte[] publicKey)
    {
        ValidateP256PublicKey(publicKey);
        string directory = EnsureDirectory(publicOutputPath);
        string temporaryPath = TemporaryPath(directory, publicOutputPath);
        byte[] encoded = Encoding.ASCII.GetBytes(
            string.Concat(Convert.ToBase64String(publicKey), Environment.NewLine));
        try
        {
            WriteNewFile(temporaryPath, encoded);
            File.Move(temporaryPath, publicOutputPath, overwrite: true);
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

    private static string EnsureDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            throw new InvalidOperationException("The QA key path is invalid.");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string TemporaryPath(string directory, string targetPath) =>
        Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    private static void DeleteOwnedTemporary(
        string temporaryPath,
        string directory,
        string targetPath)
    {
        string fullTemporaryPath = Path.GetFullPath(temporaryPath);
        string fullDirectory = Path.GetFullPath(directory);
        string? temporaryDirectory = Path.GetDirectoryName(fullTemporaryPath);
        string fileName = Path.GetFileName(fullTemporaryPath);
        string prefix = $".{Path.GetFileName(targetPath)}.";
        bool owned = string.Equals(
                temporaryDirectory,
                fullDirectory,
                StringComparison.OrdinalIgnoreCase)
            && fileName.StartsWith(prefix, StringComparison.Ordinal)
            && fileName.EndsWith(".tmp", StringComparison.Ordinal);
        if (owned && File.Exists(fullTemporaryPath))
        {
            File.Delete(fullTemporaryPath);
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

    private static void ValidateP256PrivateKey(
        ECDsa key,
        int bytesRead,
        int inputLength)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: true);
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
}
