using Nofarma.Application.Licensing;
using Nofarma.Domain.Licensing;
using Nofarma.UnitTests.TestSupport.Licensing;

namespace Nofarma.UnitTests.Application.Licensing;

public sealed class LicenseServiceTests
{
    private static readonly byte[] ValidBytes = [4, 5, 6];

    [Fact]
    public async Task MissingLicenseReportsMissingStatus()
    {
        var store = new RecordingLicenseStore(existing: null);
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")));

        LicenseStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Missing, status.State);
        Assert.False(status.AllowsNewOperations);
    }

    [Fact]
    public async Task ActivationRequestUsesTheConfiguredDeviceIdentity()
    {
        var identity = LicenseServiceTestFactory.DeviceIdentity;
        var service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(existing: null),
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")));

        LicenseActivationRequest request = await service.CreateActivationRequestAsync(
            CancellationToken.None);

        Assert.Equal(LicenseServiceTestFactory.Context, request.Context);
        Assert.Equal(identity, request.Device);
    }

    [Fact]
    public async Task InvalidImportDoesNotReplaceCurrentLicense()
    {
        var store = new RecordingLicenseStore(existing: LicenseTestData.StoredActive(sequence: 4));
        var verifier = new StubVerifier(LicenseVerification.Invalid("SIGNATURE_INVALID"));
        var service = LicenseServiceTestFactory.Create(store, verifier);

        LicenseImportException error = await Assert.ThrowsAsync<LicenseImportException>(
            () => service.ImportAsync(new LicenseImportRequest([1, 2, 3]), CancellationToken.None));

        Assert.Equal("SIGNATURE_INVALID", error.Code);
        Assert.Equal(0, store.ReplaceCalls);
    }

    [Fact]
    public async Task OlderRenewalCannotReplaceNewerSequence()
    {
        var store = new RecordingLicenseStore(existing: LicenseTestData.StoredActive(sequence: 4));
        var verifier = new StubVerifier(LicenseVerification.Valid(LicenseTestData.Active(sequence: 3)));
        var service = LicenseServiceTestFactory.Create(store, verifier);

        LicenseImportException error = await Assert.ThrowsAsync<LicenseImportException>(
            () => service.ImportAsync(new LicenseImportRequest(ValidBytes), CancellationToken.None));

        Assert.Equal("LICENSE_ROLLBACK", error.Code);
        Assert.Equal(0, store.ReplaceCalls);
    }

    [Fact]
    public async Task IdenticalImportIsIdempotentAndDoesNotReplaceTheCurrentLicense()
    {
        var store = new RecordingLicenseStore(
            existing: LicenseTestData.StoredActive(sequence: 4, document: ValidBytes));
        var verifier = new StubVerifier(LicenseVerification.Valid(
            LicenseTestData.Active(sequence: 4),
            document: ValidBytes));
        var service = LicenseServiceTestFactory.Create(store, verifier);

        await service.ImportAsync(new LicenseImportRequest(ValidBytes), CancellationToken.None);

        Assert.Equal(0, store.ReplaceCalls);
    }

    [Fact]
    public async Task ImportPersistsADefensiveCopyOfTheVerifiedDocument()
    {
        byte[] document = [7, 8, 9];
        var store = new RecordingLicenseStore(existing: null);
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Valid(LicenseTestData.Active(sequence: 1))));

        await service.ImportAsync(new LicenseImportRequest(document), CancellationToken.None);
        document[0] = 99;

        Assert.Equal(new byte[] { 7, 8, 9 }, store.Current!.Document.ToArray());
    }

    [Fact]
    public async Task ImportedAuditDoesNotContainTheLicenseDocumentOrSignature()
    {
        byte[] document = [7, 8, 9];
        var store = new RecordingLicenseStore(existing: null);
        var verifier = new StubVerifier(LicenseVerification.Valid(
            LicenseTestData.Active(sequence: 1),
            document: document));
        var service = LicenseServiceTestFactory.Create(store, verifier);

        await service.ImportAsync(new LicenseImportRequest(document), CancellationToken.None);

        Assert.NotNull(store.LastAudit);
        Assert.Equal("{}", store.LastAudit!.DetailsJson);
    }

    [Fact]
    public async Task MissingContextDoesNotCreateDeviceIdentity()
    {
        var identities = new RecordingDeviceLicenseIdentityStore();
        var service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(existing: null),
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")),
            hasContext: false,
            identities: identities);

        await Assert.ThrowsAsync<LicenseContextUnavailableException>(
            () => service.CreateActivationRequestAsync(CancellationToken.None));

        Assert.Equal(0, identities.GetOrCreateCalls);
    }

    [Fact]
    public async Task ExpiredLicenseBlocksNewOperationsWithStableCode()
    {
        var store = new RecordingLicenseStore(
            LicenseTestData.Stored(
                LicenseTestData.Create(
                    validUntil: "2026-08-01T23:59:59Z",
                    graceUntil: "2026-08-08T23:59:59Z"),
                ValidBytes));
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")));

        LicenseOperationBlockedException error = await Assert.ThrowsAsync<LicenseOperationBlockedException>(
            () => service.EnsureNewOperationsAllowedAsync(CancellationToken.None));

        Assert.Equal("LICENSE_EXPIRED_READ_ONLY", error.Code);
    }
}
