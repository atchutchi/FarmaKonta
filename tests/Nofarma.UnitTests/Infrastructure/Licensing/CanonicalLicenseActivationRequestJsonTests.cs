using System.Text;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.UnitTests.Infrastructure.Licensing;

public sealed class CanonicalLicenseActivationRequestJsonTests
{
    [Fact]
    public void SerializesOnlyTheExactVersionedCanonicalFields()
    {
        LicenseActivationRequest request = Request();

        byte[] document = CanonicalLicenseActivationRequestJson.Serialize(
            request,
            LicenseBuildChannel.Qa);

        const string expected = "{\"schemaVersion\":1,\"channel\":1,\"pharmacyId\":\"11111111-1111-1111-1111-111111111111\",\"establishmentId\":\"11111111-1111-1111-1111-111111111111\",\"deviceId\":\"33333333-3333-3333-3333-333333333333\",\"deviceKeyThumbprint\":\"SHA256:DEVICE-TEST\"}";
        Assert.Equal(expected, Encoding.UTF8.GetString(document));
        Assert.False(document.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.DoesNotContain("password", expected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", expected, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAcceptsTheCanonicalDocumentForTheFutureIssuer()
    {
        byte[] document = CanonicalLicenseActivationRequestJson.Serialize(
            Request(),
            LicenseBuildChannel.Commercial);

        bool parsed = CanonicalLicenseActivationRequestJson.TryParse(
            document,
            out LicenseActivationRequestEnvelope? envelope);

        Assert.True(parsed);
        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(LicenseBuildChannel.Commercial, envelope.Channel);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), envelope.PharmacyId);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), envelope.DeviceId);
        Assert.Equal("SHA256:DEVICE-TEST", envelope.DeviceKeyThumbprint);
    }

    [Fact]
    public void ParseRejectsNonCanonicalUnknownOrDuplicateFields()
    {
        byte[] unknown = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"channel\":1,\"pharmacyId\":\"11111111-1111-1111-1111-111111111111\",\"establishmentId\":\"11111111-1111-1111-1111-111111111111\",\"deviceId\":\"33333333-3333-3333-3333-333333333333\",\"deviceKeyThumbprint\":\"SHA256:DEVICE-TEST\",\"sales\":[]}");
        byte[] duplicate = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1,\"channel\":1,\"pharmacyId\":\"11111111-1111-1111-1111-111111111111\",\"establishmentId\":\"11111111-1111-1111-1111-111111111111\",\"deviceId\":\"33333333-3333-3333-3333-333333333333\",\"deviceKeyThumbprint\":\"SHA256:DEVICE-TEST\"}");

        Assert.False(CanonicalLicenseActivationRequestJson.TryParse(unknown, out _));
        Assert.False(CanonicalLicenseActivationRequestJson.TryParse(duplicate, out _));
    }

    [Fact]
    public void ParseRejectsOversizedInputBeforeJsonProcessing()
    {
        byte[] document = new byte[CanonicalLicenseActivationRequestJson.MaximumDocumentBytes + 1];

        bool parsed = CanonicalLicenseActivationRequestJson.TryParse(document, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void SerializationRejectsMismatchedOrEmptyIdentity()
    {
        LicenseActivationRequest mismatch = Request() with
        {
            Device = Request().Device with
            {
                PharmacyId = new EntityId(Guid.Parse("99999999-9999-9999-9999-999999999999"))
            }
        };
        LicenseActivationRequest empty = Request() with
        {
            Context = Request().Context with { DeviceId = new EntityId(Guid.Empty) }
        };

        Assert.Throws<ArgumentException>(() =>
            CanonicalLicenseActivationRequestJson.Serialize(mismatch, LicenseBuildChannel.Qa));
        Assert.Throws<ArgumentException>(() =>
            CanonicalLicenseActivationRequestJson.Serialize(empty, LicenseBuildChannel.Qa));
    }

    private static LicenseActivationRequest Request()
    {
        var pharmacyId = new EntityId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var deviceId = new EntityId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        return new LicenseActivationRequest(
            new LicenseContext(pharmacyId, pharmacyId, deviceId),
            new DeviceLicenseIdentity(pharmacyId, deviceId, "SHA256:DEVICE-TEST"));
    }
}
