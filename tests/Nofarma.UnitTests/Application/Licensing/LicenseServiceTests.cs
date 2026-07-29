using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Licensing;
using Nofarma.UnitTests.TestSupport.Licensing;

namespace Nofarma.UnitTests.Application.Licensing;

public sealed class LicenseServiceTests
{
    private static readonly byte[] ValidBytes = [4, 5, 6];

    [Fact]
    public void StoredLicenseDoesNotExposeItsMutableDocumentBuffer()
    {
        byte[] source = [1, 2, 3];
        var stored = new StoredLicense(source);
        source[0] = 9;
        Assert.True(MemoryMarshal.TryGetArray(stored.Document, out ArraySegment<byte> exposed));
        exposed.Array![exposed.Offset] = 8;

        Assert.Equal(new byte[] { 1, 2, 3 }, stored.Document.ToArray());
    }

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
    public async Task MissingContextStopsBeforeReadingLicenseOrProtectedState()
    {
        var store = new RecordingLicenseStore(existing: null);
        var identities = new RecordingDeviceLicenseIdentityStore();
        var checkpoint = new RecordingLicenseClockCheckpoint();
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")),
            hasContext: false,
            identities: identities,
            checkpoint: checkpoint);

        await Assert.ThrowsAsync<LicenseContextUnavailableException>(
            () => service.GetStatusAsync(CancellationToken.None));

        Assert.Equal(0, store.GetCalls);
        Assert.Equal(0, identities.GetOrCreateCalls);
        Assert.Equal(0, checkpoint.InitializeCalls);
        Assert.Equal(0, checkpoint.CheckCalls);
    }

    [Fact]
    public async Task MissingLicenseWithContextDoesNotCreateProtectedState()
    {
        var store = new RecordingLicenseStore(existing: null);
        var identities = new RecordingDeviceLicenseIdentityStore();
        var checkpoint = new RecordingLicenseClockCheckpoint();
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")),
            identities: identities,
            checkpoint: checkpoint);

        LicenseStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Missing, status.State);
        Assert.Equal(1, store.GetCalls);
        Assert.Equal(0, identities.GetOrCreateCalls);
        Assert.Equal(0, checkpoint.InitializeCalls);
        Assert.Equal(0, checkpoint.CheckCalls);
    }

    [Fact]
    public async Task StoredLicenseChecksCheckpointWithFullBinding()
    {
        var checkpoint = new RecordingLicenseClockCheckpoint();
        var service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(LicenseTestData.StoredActive(sequence: 1)),
            new StubVerifier(LicenseVerification.Valid(LicenseTestData.Active(sequence: 1))),
            checkpoint: checkpoint);

        await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(1, checkpoint.CheckCalls);
        Assert.Equal(
            new LicenseClockBinding(
                LicenseTestData.PharmacyId,
                LicenseTestData.DeviceId,
                "QA",
                LicenseServiceTestFactory.DeviceIdentity.PublicKeyThumbprint),
            checkpoint.LastBinding);
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
        var verifier = new StubVerifier(
            LicenseVerification.Valid(LicenseTestData.Active(sequence: 4)),
            LicenseVerification.Invalid("SIGNATURE_INVALID"));
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
        var verifier = new StubVerifier(
            LicenseVerification.Valid(LicenseTestData.Active(sequence: 4)),
            LicenseVerification.Valid(LicenseTestData.Active(sequence: 3)));
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
    public async Task FirstValidImportInitializesCheckpointBeforePersistingLicense()
    {
        var events = new List<string>();
        var checkpoint = new RecordingLicenseClockCheckpoint(events: events);
        var store = new RecordingLicenseStore(existing: null, events: events);
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Valid(LicenseTestData.Active(sequence: 1))),
            checkpoint: checkpoint);

        await service.ImportAsync(
            new LicenseImportRequest(ValidBytes),
            CancellationToken.None);

        Assert.Equal(1, checkpoint.InitializeCalls);
        Assert.Equal(["checkpoint.initialize", "license.replace"], events);
    }

    [Fact]
    public async Task FailedCheckpointInitializationDoesNotPersistFirstLicense()
    {
        var checkpoint = new RecordingLicenseClockCheckpoint(
            initializeException: new CryptographicException("cannot initialize"));
        var store = new RecordingLicenseStore(existing: null);
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Valid(LicenseTestData.Active(sequence: 1))),
            checkpoint: checkpoint);

        LicenseImportException error = await Assert.ThrowsAsync<LicenseImportException>(() =>
            service.ImportAsync(
                new LicenseImportRequest(ValidBytes),
                CancellationToken.None));

        Assert.Equal("LICENSE_CLOCK_CHECKPOINT", error.Code);
        Assert.Equal(0, store.ReplaceCalls);
    }

    [Fact]
    public async Task RollbackCheckpointDoesNotPersistRenewal()
    {
        var checkpoint = new RecordingLicenseClockCheckpoint(rollbackDetected: true);
        var store = new RecordingLicenseStore(
            LicenseTestData.StoredActive(sequence: 1));
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(
                LicenseVerification.Valid(LicenseTestData.Active(sequence: 1)),
                LicenseVerification.Valid(LicenseTestData.Active(sequence: 2))),
            checkpoint: checkpoint);

        LicenseStatus status = await service.ImportAsync(
            new LicenseImportRequest(ValidBytes),
            CancellationToken.None);

        Assert.Equal(LicenseState.ClockRollback, status.State);
        Assert.Equal(0, store.ReplaceCalls);
    }

    [Fact]
    public async Task CheckpointCryptographicFailureBlocksPersistedLicense()
    {
        var checkpoint = new RecordingLicenseClockCheckpoint(
            checkException: new CryptographicException("invalid checkpoint"));
        var service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(LicenseTestData.StoredActive(sequence: 1)),
            new StubVerifier(LicenseVerification.Valid(LicenseTestData.Active(sequence: 1))),
            checkpoint: checkpoint);

        LicenseStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.ClockRollback, status.State);
        Assert.False(status.AllowsNewOperations);
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
        LicenseGrant expiredGrant = LicenseTestData.Create(
            validUntil: "2026-08-01T23:59:59Z",
            graceUntil: "2026-08-08T23:59:59Z");
        var store = new RecordingLicenseStore(
            LicenseTestData.Stored(expiredGrant, ValidBytes));
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(LicenseVerification.Valid(expiredGrant)));

        LicenseOperationBlockedException error = await Assert.ThrowsAsync<LicenseOperationBlockedException>(
            () => service.EnsureNewOperationsAllowedAsync(CancellationToken.None));

        Assert.Equal("LICENSE_EXPIRED_READ_ONLY", error.Code);
    }

    [Fact]
    public async Task StoredDocumentIsReverifiedBeforeItsGrantIsEvaluated()
    {
        byte[] persisted = [21, 22, 23];
        var verifier = new StubVerifier(
            LicenseVerification.Valid(
                LicenseTestData.Active(sequence: 9),
                document: persisted,
                channel: "QA",
                keyId: "trusted-key"));
        var service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(new StoredLicense(persisted)),
            verifier);

        LicenseStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Valid, status.State);
        Assert.Equal(9, status.Grant!.Sequence);
        Assert.Equal([21, 22, 23], Assert.Single(verifier.Documents));
    }

    [Fact]
    public async Task InvalidPersistedDocumentFailsClosedWithoutAdvancingCheckpoint()
    {
        var checkpoint = new RecordingLicenseClockCheckpoint();
        var service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(new StoredLicense(new byte[] { 31, 32, 33 })),
            new StubVerifier(LicenseVerification.Invalid("SIGNATURE_INVALID")),
            checkpoint: checkpoint);

        LicenseStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Invalid, status.State);
        Assert.False(status.AllowsNewOperations);
        Assert.True(status.AllowsReadOnlyAccess);
        Assert.Null(status.Grant);
        Assert.Equal(0, checkpoint.CheckCalls);
    }

    [Fact]
    public async Task PersistedHashMismatchIsReportedAsInvalidWithoutReadingProtectedState()
    {
        var identities = new RecordingDeviceLicenseIdentityStore();
        var service = LicenseServiceTestFactory.Create(
            new ThrowingLicenseStore(new LicensePersistenceIntegrityException()),
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")),
            identities: identities);

        LicenseStatus status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Invalid, status.State);
        Assert.False(status.AllowsNewOperations);
        Assert.Equal(0, identities.GetOrCreateCalls);
    }

    [Fact]
    public async Task ValidImportReplacesAnInvalidPersistedDocumentAsRecovery()
    {
        var store = new RecordingLicenseStore(
            new StoredLicense(new byte[] { 41, 42, 43 }));
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(
                LicenseVerification.Invalid("SIGNATURE_INVALID"),
                LicenseVerification.Valid(LicenseTestData.Active(sequence: 1))));

        LicenseStatus status = await service.ImportAsync(
            new LicenseImportRequest([51, 52, 53]),
            CancellationToken.None);

        Assert.Equal(LicenseState.Valid, status.State);
        Assert.Equal(1, store.ReplaceCalls);
        Assert.Equal([51, 52, 53], store.Current!.Document.ToArray());
    }

    [Fact]
    public async Task ValidImportRecoversFromAPersistedHashMismatch()
    {
        var store = new RecoverableIntegrityLicenseStore();
        var service = LicenseServiceTestFactory.Create(
            store,
            new StubVerifier(
                LicenseVerification.Valid(LicenseTestData.Active(sequence: 1))));

        LicenseStatus status = await service.ImportAsync(
            new LicenseImportRequest([61, 62, 63]),
            CancellationToken.None);

        Assert.Equal(LicenseState.Valid, status.State);
        Assert.Equal(1, store.ReplaceCalls);
        Assert.Equal([61, 62, 63], store.Current!.Document.ToArray());
    }
}
