using System.Security.Cryptography;
using System.Text;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.UnitTests.Infrastructure.Licensing;

public sealed class EcdsaLicenseDocumentVerifierTests : IDisposable
{
    private static readonly int[] UnsupportedCapabilities = [1];
    private readonly ECDsa _signingKey;
    private readonly EcdsaLicenseDocumentVerifier _verifier;

    public EcdsaLicenseDocumentVerifierTests()
    {
        _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var registry = new TrustedLicenseKeyRegistry(
            LicenseBuildChannel.Qa,
            new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                [LicenseDocumentTestData.KeyId] = _signingKey.ExportSubjectPublicKeyInfo(),
                [LicenseDocumentTestData.AlternateKeyId] = _signingKey.ExportSubjectPublicKeyInfo()
            });
        _verifier = new EcdsaLicenseDocumentVerifier(registry);
    }

    [Fact]
    public void ValidP1363DocumentReturnsTheBoundLicense()
    {
        byte[] document = ValidDocument();

        LicenseVerification result = _verifier.Verify(
            document,
            LicenseDocumentTestData.DeviceIdentity,
            LicenseDocumentTestData.Context);

        Assert.True(result.IsValid);
        Assert.Null(result.Code);
        Assert.NotNull(result.License);
        Assert.Equal(LicenseDocumentTestData.LicenseId, result.License.Grant.Id.Value);
        Assert.Equal(LicensePlan.Monthly, result.License.Grant.Plan);
        Assert.Equal(7, result.License.Grant.Sequence);
        Assert.Equal("QA", result.License.Channel);
        Assert.Equal(LicenseDocumentTestData.KeyId, result.License.KeyId);
        Assert.Equal(document, result.License.Document.ToArray());
    }

    [Fact]
    public void ChangingAnySignedLicenseFieldInvalidatesTheSignature()
    {
        SignedLicenseEnvelope original = SignedEnvelope();
        SignedLicenseEnvelope[] mutations =
        [
            original with { KeyId = LicenseDocumentTestData.AlternateKeyId },
            original with { LicenseId = Guid.Parse("55555555-5555-5555-5555-555555555555") },
            original with { PharmacyId = Guid.Parse("55555555-5555-5555-5555-555555555555") },
            original with { EstablishmentId = Guid.Parse("55555555-5555-5555-5555-555555555555") },
            original with { DeviceId = Guid.Parse("55555555-5555-5555-5555-555555555555") },
            original with { DeviceKeyThumbprint = "SHA256:CHANGED" },
            original with { Plan = LicensePlan.Annual },
            original with { Sequence = original.Sequence + 1 },
            original with { IssuedAtUtc = original.IssuedAtUtc.AddSeconds(1) },
            original with { ValidFromUtc = original.ValidFromUtc.AddSeconds(1) },
            original with { ValidUntilUtc = original.ValidUntilUtc.AddSeconds(1) },
            original with { GraceUntilUtc = original.GraceUntilUtc.AddSeconds(1) },
            original with { Capabilities = new[] { 1 } }
        ];

        foreach (SignedLicenseEnvelope changed in mutations)
        {
            LicenseVerification result = _verifier.Verify(
                CanonicalLicenseJson.SerializeEnvelope(changed),
                LicenseDocumentTestData.DeviceIdentity,
                LicenseDocumentTestData.Context);

            Assert.False(result.IsValid);
            Assert.Equal("SIGNATURE_INVALID", result.Code);
        }
    }

    [Fact]
    public void AChangedSignatureIsRejected()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope();
        byte[] signature = envelope.Signature.ToArray();
        signature[0] ^= 0x01;

        LicenseVerification result = Verify(envelope with { Signature = signature });

        Assert.False(result.IsValid);
        Assert.Equal("SIGNATURE_INVALID", result.Code);
    }

    [Fact]
    public void DerEncodedSignatureIsNeverAcceptedAsP1363()
    {
        SignedLicenseEnvelope unsigned = LicenseDocumentTestData.UnsignedEnvelope();
        byte[] signature = _signingKey.SignData(
            CanonicalLicenseJson.SerializePayload(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        LicenseVerification result = Verify(unsigned with { Signature = signature });

        Assert.False(result.IsValid);
        Assert.Equal("SIGNATURE_INVALID", result.Code);
    }

    [Theory]
    [InlineData(65537)]
    [InlineData(1048576)]
    public void RejectsOversizedDocuments(int bytes)
    {
        LicenseVerification result = _verifier.Verify(
            new byte[bytes],
            LicenseDocumentTestData.DeviceIdentity,
            LicenseDocumentTestData.Context);

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_TOO_LARGE", result.Code);
    }

    [Fact]
    public void RejectsDeterministicMalformedCorpusWithoutEscapingVerifier()
    {
        var random = new Random(20260728);
        for (int index = 0; index < 10_000; index++)
        {
            byte[] document = new byte[random.Next(0, 4097)];
            random.NextBytes(document);

            LicenseVerification result = _verifier.Verify(
                document,
                LicenseDocumentTestData.DeviceIdentity,
                LicenseDocumentTestData.Context);

            Assert.False(result.IsValid);
        }
    }

    [Fact]
    public void RejectsUnknownProperties()
    {
        string document = ValidJson().Replace(
            "{",
            "{\"unexpected\":1,",
            StringComparison.Ordinal);

        AssertDocumentInvalid(document);
    }

    [Fact]
    public void RejectsRepeatedProperties()
    {
        string document = ValidJson().Replace(
            "\"schemaVersion\":1,",
            "\"schemaVersion\":1,\"schemaVersion\":1,",
            StringComparison.Ordinal);

        AssertDocumentInvalid(document);
    }

    [Fact]
    public void RejectsMissingProperties()
    {
        string document = ValidJson().Replace(
            "\"capabilities\":[],",
            string.Empty,
            StringComparison.Ordinal);

        AssertDocumentInvalid(document);
    }

    [Fact]
    public void RejectsNonCanonicalWhitespaceEvenWhenTheSignedValuesAreUnchanged()
    {
        string document = ValidJson().Replace("{", "{ ", StringComparison.Ordinal);

        AssertDocumentInvalid(document);
    }

    [Theory]
    [InlineData("/*comment*/")]
    [InlineData(",")]
    public void RejectsCommentsAndTrailingCommas(string suffix)
    {
        string document = ValidJson();
        document = string.Concat(document.AsSpan(0, document.Length - 1), suffix, "}");

        AssertDocumentInvalid(document);
    }

    [Fact]
    public void RejectsJsonDeeperThanSixteenLevels()
    {
        string document = new string('[', 17) + "0" + new string(']', 17);

        AssertDocumentInvalid(document);
    }

    [Fact]
    public void RejectsFutureSchemaVersionsBeforeReturningALicense()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            SchemaVersion = 2
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_VERSION_UNSUPPORTED", result.Code);
        Assert.Null(result.License);
    }

    [Fact]
    public void QaBuildRejectsCommercialDocumentsEvenWithAValidSignature()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            Channel = LicenseBuildChannel.Commercial
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("LICENSE_CHANNEL_INVALID", result.Code);
    }

    [Fact]
    public void RejectsUndefinedChannels()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            Channel = (LicenseBuildChannel)99
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("LICENSE_CHANNEL_INVALID", result.Code);
    }

    [Fact]
    public void RejectsUnknownSignatureAlgorithms()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            SignatureAlgorithm = 2
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("SIGNATURE_ALGORITHM_UNSUPPORTED", result.Code);
    }

    [Fact]
    public void RejectsUnknownSigningKeysWithoutTryingAnotherKey()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            KeyId = "qa-unknown"
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("SIGNING_KEY_UNKNOWN", result.Code);
    }

    [Fact]
    public void RejectsAValidSignatureBoundToAnotherPharmacy()
    {
        Guid otherPharmacy = Guid.Parse("55555555-5555-5555-5555-555555555555");
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            PharmacyId = otherPharmacy,
            EstablishmentId = otherPharmacy
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("LICENSE_BINDING_INVALID", result.Code);
    }

    [Fact]
    public void RejectsAValidSignatureBoundToAnotherDevice()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            DeviceId = Guid.Parse("55555555-5555-5555-5555-555555555555")
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("LICENSE_BINDING_INVALID", result.Code);
    }

    [Fact]
    public void RejectsAValidSignatureBoundToAnotherDeviceKey()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            DeviceKeyThumbprint = "SHA256:OTHER-DEVICE"
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("LICENSE_BINDING_INVALID", result.Code);
    }

    [Fact]
    public void RejectsSignedCapabilitiesThatThisSchemaDoesNotImplement()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            Capabilities = UnsupportedCapabilities
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("LICENSE_CAPABILITIES_UNSUPPORTED", result.Code);
    }

    [Fact]
    public void RejectsSignedValuesThatCannotCreateAValidGrant()
    {
        SignedLicenseEnvelope envelope = SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope() with
        {
            Sequence = 0
        });

        LicenseVerification result = Verify(envelope);

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_INVALID", result.Code);
    }

    [Fact]
    public void RegistryRejectsPublicKeysOutsideP256()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<ArgumentException>(() => new TrustedLicenseKeyRegistry(
            LicenseBuildChannel.Qa,
            new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                ["qa-p384"] = p384.ExportSubjectPublicKeyInfo()
            }));
    }

    public void Dispose() => _signingKey.Dispose();

    private byte[] ValidDocument() =>
        CanonicalLicenseJson.SerializeEnvelope(SignedEnvelope());

    private string ValidJson() => Encoding.UTF8.GetString(ValidDocument());

    private SignedLicenseEnvelope SignedEnvelope() =>
        SignedEnvelope(LicenseDocumentTestData.UnsignedEnvelope());

    private SignedLicenseEnvelope SignedEnvelope(SignedLicenseEnvelope unsigned)
    {
        byte[] signature = _signingKey.SignData(
            CanonicalLicenseJson.SerializePayload(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return unsigned with { Signature = signature };
    }

    private LicenseVerification Verify(SignedLicenseEnvelope envelope) =>
        _verifier.Verify(
            CanonicalLicenseJson.SerializeEnvelope(envelope),
            LicenseDocumentTestData.DeviceIdentity,
            LicenseDocumentTestData.Context);

    private void AssertDocumentInvalid(string document)
    {
        LicenseVerification result = _verifier.Verify(
            Encoding.UTF8.GetBytes(document),
            LicenseDocumentTestData.DeviceIdentity,
            LicenseDocumentTestData.Context);

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_INVALID", result.Code);
        Assert.Null(result.License);
    }
}

internal static class LicenseDocumentTestData
{
    internal const string KeyId = "qa-2026-01";
    internal const string AlternateKeyId = "qa-2026-02";
    internal const string DeviceKeyThumbprint = "SHA256:DEVICE-TEST";

    internal static readonly Guid LicenseId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly EntityId PharmacyId = Id("11111111-1111-1111-1111-111111111111");
    private static readonly EntityId DeviceId = Id("33333333-3333-3333-3333-333333333333");

    internal static readonly LicenseContext Context = new(PharmacyId, PharmacyId, DeviceId);
    internal static readonly DeviceLicenseIdentity DeviceIdentity = new(
        PharmacyId,
        DeviceId,
        DeviceKeyThumbprint);

    internal static SignedLicenseEnvelope UnsignedEnvelope() => new(
        SchemaVersion: 1,
        Channel: LicenseBuildChannel.Qa,
        KeyId: KeyId,
        SignatureAlgorithm: 1,
        LicenseId: LicenseId,
        PharmacyId: PharmacyId.Value,
        EstablishmentId: PharmacyId.Value,
        DeviceId: DeviceId.Value,
        DeviceKeyThumbprint: DeviceKeyThumbprint,
        Plan: LicensePlan.Monthly,
        Sequence: 7,
        IssuedAtUtc: Utc("2026-08-01T00:00:00Z"),
        ValidFromUtc: Utc("2026-08-01T00:00:00Z"),
        ValidUntilUtc: Utc("2026-08-31T23:59:59Z"),
        GraceUntilUtc: Utc("2026-09-07T23:59:59Z"),
        Capabilities: Array.Empty<int>(),
        Signature: ReadOnlyMemory<byte>.Empty);

    private static EntityId Id(string value) => new(Guid.Parse(value));

    private static DateTimeOffset Utc(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            .ToUniversalTime();
}
