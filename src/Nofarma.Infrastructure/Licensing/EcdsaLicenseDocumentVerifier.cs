using System.Security.Cryptography;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;

namespace Nofarma.Infrastructure.Licensing;

public sealed class EcdsaLicenseDocumentVerifier(TrustedLicenseKeyRegistry keys)
    : ILicenseDocumentVerifier
{
    public const int MaximumDocumentBytes = 64 * 1024;
    public const int CurrentSchemaVersion = 1;
    public const int EcdsaP256Sha256P1363Algorithm = 1;
    private const int P1363SignatureBytes = 64;

    private readonly TrustedLicenseKeyRegistry _keys =
        keys ?? throw new ArgumentNullException(nameof(keys));

    public LicenseVerification Verify(
        ReadOnlyMemory<byte> document,
        DeviceLicenseIdentity device,
        LicenseContext context)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(context);

        if (document.Length > MaximumDocumentBytes)
        {
            return LicenseVerification.Invalid("DOCUMENT_TOO_LARGE");
        }

        byte[] documentSnapshot = document.ToArray();
        ReadOnlyMemory<byte> stableDocument = documentSnapshot;

        if (!CanonicalLicenseJson.TryParseEnvelope(
                stableDocument,
                out SignedLicenseEnvelope? envelope,
                out CanonicalLicenseParseFailure parseFailure)
            || envelope is null)
        {
            return LicenseVerification.Invalid(
                parseFailure == CanonicalLicenseParseFailure.TooDeep
                    ? "DOCUMENT_TOO_DEEP"
                    : "DOCUMENT_INVALID");
        }

        byte[] canonicalDocument = CanonicalLicenseJson.SerializeEnvelope(envelope);
        if (!stableDocument.Span.SequenceEqual(canonicalDocument))
        {
            return LicenseVerification.Invalid("DOCUMENT_INVALID");
        }

        if (envelope.SchemaVersion != CurrentSchemaVersion)
        {
            return LicenseVerification.Invalid("DOCUMENT_VERSION_UNSUPPORTED");
        }

        if (envelope.Channel is not (LicenseBuildChannel.Qa or LicenseBuildChannel.Commercial)
            || envelope.Channel != _keys.BuildChannel)
        {
            return LicenseVerification.Invalid("LICENSE_CHANNEL_INVALID");
        }

        if (envelope.SignatureAlgorithm != EcdsaP256Sha256P1363Algorithm)
        {
            return LicenseVerification.Invalid("SIGNATURE_ALGORITHM_UNSUPPORTED");
        }

        if (!_keys.TryCreateVerifier(envelope.Channel, envelope.KeyId, out ECDsa? verifier)
            || verifier is null)
        {
            return LicenseVerification.Invalid("SIGNING_KEY_UNKNOWN");
        }

        using (verifier)
        {
            byte[] canonicalPayload = CanonicalLicenseJson.SerializePayload(envelope);
            byte[] signature = envelope.Signature.ToArray();
            if (signature.Length != P1363SignatureBytes
                || !VerifyP1363(verifier, canonicalPayload, signature))
            {
                return LicenseVerification.Invalid("SIGNATURE_INVALID");
            }
        }

        if (envelope.Capabilities.Count != 0)
        {
            return LicenseVerification.Invalid("LICENSE_CAPABILITIES_UNSUPPORTED");
        }

        if (!HasExpectedBinding(envelope, device, context))
        {
            return LicenseVerification.Invalid("LICENSE_BINDING_INVALID");
        }

        LicenseGrant grant;
        try
        {
            grant = new LicenseGrant(
                new EntityId(envelope.LicenseId),
                new EntityId(envelope.PharmacyId),
                new EntityId(envelope.EstablishmentId),
                new EntityId(envelope.DeviceId),
                envelope.Plan,
                envelope.Sequence,
                UtcInstant.From(envelope.IssuedAtUtc),
                UtcInstant.From(envelope.ValidFromUtc),
                UtcInstant.From(envelope.ValidUntilUtc),
                UtcInstant.From(envelope.GraceUntilUtc));
        }
        catch (ArgumentException)
        {
            return LicenseVerification.Invalid("DOCUMENT_INVALID");
        }

        var verified = new VerifiedLicense(
            grant,
            ChannelName(envelope.Channel),
            envelope.KeyId,
            documentSnapshot);
        return LicenseVerification.Valid(verified);
    }

    private static bool VerifyP1363(ECDsa verifier, byte[] payload, byte[] signature)
    {
        try
        {
            return verifier.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool HasExpectedBinding(
        SignedLicenseEnvelope envelope,
        DeviceLicenseIdentity device,
        LicenseContext context) =>
        envelope.PharmacyId == context.PharmacyId.Value
        && envelope.EstablishmentId == context.EstablishmentId.Value
        && envelope.DeviceId == context.DeviceId.Value
        && envelope.PharmacyId == device.PharmacyId.Value
        && envelope.DeviceId == device.DeviceId.Value
        && string.Equals(
            envelope.DeviceKeyThumbprint,
            device.PublicKeyThumbprint,
            StringComparison.Ordinal);

    private static string ChannelName(LicenseBuildChannel channel) => channel switch
    {
        LicenseBuildChannel.Qa => "QA",
        LicenseBuildChannel.Commercial => "Commercial",
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };
}
