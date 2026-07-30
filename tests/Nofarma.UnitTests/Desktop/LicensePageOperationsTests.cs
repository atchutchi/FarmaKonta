using System.Text;
using Nofarma.Application.Licensing;
using Nofarma.Desktop.Services;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;
using Nofarma.UnitTests.TestSupport.Licensing;

namespace Nofarma.UnitTests.Desktop;

public sealed class LicensePageOperationsTests
{
    [Fact]
    public async Task LoadReturnsVerifiedStatusAndPublicDeviceIdentifier()
    {
        LicenseService service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(existing: null),
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")));
        var operations = new LicensePageOperations(
            service,
            new LicenseChannelContext(LicenseBuildChannel.Unlicensed));

        LicensePageSnapshot snapshot = await operations.LoadAsync(
            CancellationToken.None);

        Assert.Equal(LicenseState.Missing, snapshot.Status.State);
        Assert.Equal(
            "SHA256:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            snapshot.DeviceKeyThumbprint);
        Assert.False(operations.IsQa);
    }

    [Fact]
    public async Task CreateRequestUsesTheBuildChannelAndCanonicalPublicFields()
    {
        LicenseService service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(existing: null),
            new StubVerifier(LicenseVerification.Invalid("NOT_USED")));
        var operations = new LicensePageOperations(
            service,
            new LicenseChannelContext(LicenseBuildChannel.Qa));

        ReadOnlyMemory<byte> request = await operations.CreateRequestAsync(
            CancellationToken.None);

        const string expected = "{\"schemaVersion\":1,\"channel\":1,\"pharmacyId\":\"11111111-1111-1111-1111-111111111111\",\"establishmentId\":\"11111111-1111-1111-1111-111111111111\",\"deviceId\":\"33333333-3333-3333-3333-333333333333\",\"deviceKeyThumbprint\":\"SHA256:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF\"}";
        Assert.Equal(expected, Encoding.UTF8.GetString(request.Span));
        Assert.True(operations.IsQa);
    }

    [Fact]
    public async Task SuccessfulImportRaisesStatusChangedAfterPersistence()
    {
        var store = new RecordingLicenseStore(existing: null);
        var verifier = new StubVerifier(
            LicenseVerification.Valid(
                LicenseTestData.Active(sequence: 1),
                document: new byte[] { 1, 2, 3 }));
        LicenseService service = LicenseServiceTestFactory.Create(store, verifier);
        var operations = new LicensePageOperations(
            service,
            new LicenseChannelContext(LicenseBuildChannel.Qa));
        int notifications = 0;
        operations.StatusChanged += (_, _) => notifications++;

        await operations.ImportAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.Equal(1, store.ReplaceCalls);
        Assert.Equal(1, notifications);
        Assert.Equal(new byte[] { 1, 2, 3 }, verifier.Documents[0]);
    }

    [Fact]
    public async Task FailedImportNeverRaisesStatusChanged()
    {
        LicenseService service = LicenseServiceTestFactory.Create(
            new RecordingLicenseStore(existing: null),
            new StubVerifier(LicenseVerification.Invalid("DOCUMENT_INVALID")));
        var operations = new LicensePageOperations(
            service,
            new LicenseChannelContext(LicenseBuildChannel.Qa));
        int notifications = 0;
        operations.StatusChanged += (_, _) => notifications++;

        await Assert.ThrowsAsync<LicenseImportException>(() =>
            operations.ImportAsync(new byte[] { 1 }, CancellationToken.None));

        Assert.Equal(0, notifications);
    }
}
