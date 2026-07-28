using System.Security.Cryptography;

namespace Nofarma.Infrastructure.Licensing;

public sealed class TrustedLicenseKeyRegistry
{
    private const string NistP256Oid = "1.2.840.10045.3.1.7";
    private readonly Dictionary<string, byte[]> _subjectPublicKeys;

    public TrustedLicenseKeyRegistry(
        LicenseBuildChannel buildChannel,
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<byte>>> subjectPublicKeys)
    {
        if (!Enum.IsDefined(buildChannel))
        {
            throw new ArgumentOutOfRangeException(nameof(buildChannel));
        }

        ArgumentNullException.ThrowIfNull(subjectPublicKeys);
        BuildChannel = buildChannel;
        _subjectPublicKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach ((string keyId, ReadOnlyMemory<byte> subjectPublicKey) in subjectPublicKeys)
        {
            ValidateKeyId(keyId);
            byte[] keyBytes = subjectPublicKey.ToArray();
            ValidateP256SubjectPublicKey(keyBytes, keyId);
            if (!_subjectPublicKeys.TryAdd(keyId, keyBytes))
            {
                throw new ArgumentException(
                    "The trusted signing key identifier is repeated.",
                    nameof(subjectPublicKeys));
            }
        }

        if (BuildChannel == LicenseBuildChannel.Unlicensed && _subjectPublicKeys.Count != 0)
        {
            throw new ArgumentException(
                "An unlicensed build cannot contain trusted signing keys.",
                nameof(subjectPublicKeys));
        }
    }

    public LicenseBuildChannel BuildChannel { get; }

    internal bool TryCreateVerifier(
        LicenseBuildChannel documentChannel,
        string keyId,
        out ECDsa? verifier)
    {
        verifier = null;
        if (documentChannel != BuildChannel
            || BuildChannel == LicenseBuildChannel.Unlicensed
            || !_subjectPublicKeys.TryGetValue(keyId, out byte[]? subjectPublicKey))
        {
            return false;
        }

        var created = ECDsa.Create();
        try
        {
            created.ImportSubjectPublicKeyInfo(subjectPublicKey, out int bytesRead);
            if (bytesRead != subjectPublicKey.Length)
            {
                created.Dispose();
                return false;
            }

            verifier = created;
            return true;
        }
        catch
        {
            created.Dispose();
            throw;
        }
    }

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 128)
        {
            throw new ArgumentException("The trusted signing key identifier is invalid.", nameof(keyId));
        }
    }

    private static void ValidateP256SubjectPublicKey(byte[] subjectPublicKey, string keyId)
    {
        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(subjectPublicKey, out int bytesRead);
            ECParameters parameters = verifier.ExportParameters(includePrivateParameters: false);
            if (bytesRead != subjectPublicKey.Length
                || verifier.KeySize != 256
                || !string.Equals(parameters.Curve.Oid.Value, NistP256Oid, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The trusted signing key must be an ECDSA NIST P-256 public key.",
                    nameof(subjectPublicKey));
            }
        }
        catch (CryptographicException error)
        {
            throw new ArgumentException(
                $"The trusted signing key '{keyId}' is not a valid ECDSA public key.",
                nameof(subjectPublicKey),
                error);
        }
    }
}
