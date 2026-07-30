using System.Text;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.UnitTests.Infrastructure.Licensing;

public sealed class CanonicalLicenseJsonTests
{
    [Fact]
    public void PayloadUsesTheExactVersionedCanonicalRepresentation()
    {
        SignedLicenseEnvelope envelope = LicenseDocumentTestData.UnsignedEnvelope();

        byte[] payload = CanonicalLicenseJson.SerializePayload(envelope);

        const string expected = "{\"schemaVersion\":1,\"channel\":1,\"keyId\":\"qa-2026-01\",\"signatureAlgorithm\":1,\"licenseId\":\"44444444-4444-4444-4444-444444444444\",\"pharmacyId\":\"11111111-1111-1111-1111-111111111111\",\"establishmentId\":\"11111111-1111-1111-1111-111111111111\",\"deviceId\":\"33333333-3333-3333-3333-333333333333\",\"deviceKeyThumbprint\":\"SHA256:DEVICE-TEST\",\"plan\":1,\"sequence\":7,\"issuedAtUtc\":\"2026-08-01T00:00:00.0000000+00:00\",\"validFromUtc\":\"2026-08-01T00:00:00.0000000+00:00\",\"validUntilUtc\":\"2026-08-31T23:59:59.0000000+00:00\",\"graceUntilUtc\":\"2026-09-07T23:59:59.0000000+00:00\",\"capabilities\":[]}";
        Assert.Equal(expected, Encoding.UTF8.GetString(payload));
        Assert.False(payload.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
    }

    [Fact]
    public void EnvelopeAppendsTheSignatureAfterTheSignedPayloadFields()
    {
        SignedLicenseEnvelope envelope = LicenseDocumentTestData.UnsignedEnvelope() with
        {
            Signature = new byte[] { 1, 2, 3, 4 }
        };

        byte[] document = CanonicalLicenseJson.SerializeEnvelope(envelope);

        const string expected = "{\"schemaVersion\":1,\"channel\":1,\"keyId\":\"qa-2026-01\",\"signatureAlgorithm\":1,\"licenseId\":\"44444444-4444-4444-4444-444444444444\",\"pharmacyId\":\"11111111-1111-1111-1111-111111111111\",\"establishmentId\":\"11111111-1111-1111-1111-111111111111\",\"deviceId\":\"33333333-3333-3333-3333-333333333333\",\"deviceKeyThumbprint\":\"SHA256:DEVICE-TEST\",\"plan\":1,\"sequence\":7,\"issuedAtUtc\":\"2026-08-01T00:00:00.0000000+00:00\",\"validFromUtc\":\"2026-08-01T00:00:00.0000000+00:00\",\"validUntilUtc\":\"2026-08-31T23:59:59.0000000+00:00\",\"graceUntilUtc\":\"2026-09-07T23:59:59.0000000+00:00\",\"capabilities\":[],\"signature\":\"AQIDBA==\"}";
        Assert.Equal(expected, Encoding.UTF8.GetString(document));
    }

    [Fact]
    public void SignatureNeverChangesTheBytesThatAreSigned()
    {
        SignedLicenseEnvelope first = LicenseDocumentTestData.UnsignedEnvelope() with
        {
            Signature = new byte[] { 1 }
        };
        SignedLicenseEnvelope second = first with
        {
            Signature = new byte[] { 2, 3 }
        };

        Assert.Equal(
            CanonicalLicenseJson.SerializePayload(first),
            CanonicalLicenseJson.SerializePayload(second));
    }
}
