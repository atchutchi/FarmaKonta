using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.TestSupport.Licensing;

internal sealed class RecordingLicenseStore : ILicenseStore
{
    private StoredLicense? _current;

    internal RecordingLicenseStore(StoredLicense? existing)
    {
        _current = existing;
    }

    internal int ReplaceCalls { get; private set; }

    internal AuditEvent? LastAudit { get; private set; }

    internal StoredLicense? Current => _current;

    public Task<StoredLicense?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(_current);

    public Task ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        ReplaceCalls++;
        LastAudit = audit;
        _current = new StoredLicense(license);
        return Task.CompletedTask;
    }
}

internal sealed class StubVerifier(LicenseVerification result) : ILicenseDocumentVerifier
{
    public LicenseVerification Verify(
        ReadOnlyMemory<byte> document,
        DeviceLicenseIdentity device,
        LicenseContext context) => result;
}

internal sealed class RecordingDeviceLicenseIdentityStore(DeviceLicenseIdentity identity)
    : IDeviceLicenseIdentityStore
{
    internal RecordingDeviceLicenseIdentityStore()
        : this(LicenseServiceTestFactory.DeviceIdentity)
    {
    }

    internal int GetOrCreateCalls { get; private set; }

    public DeviceLicenseIdentity GetOrCreate(EntityId pharmacyId, EntityId deviceId)
    {
        GetOrCreateCalls++;
        return identity;
    }
}

internal static class LicenseServiceTestFactory
{
    internal static readonly LicenseContext Context = new(
        LicenseTestData.PharmacyId,
        LicenseTestData.PharmacyId,
        LicenseTestData.DeviceId);

    internal static readonly DeviceLicenseIdentity DeviceIdentity = new(
        LicenseTestData.PharmacyId,
        LicenseTestData.DeviceId,
        "test-thumbprint");

    internal static LicenseService Create(
        ILicenseStore store,
        ILicenseDocumentVerifier verifier,
        LicenseContext? context = null,
        bool hasContext = true,
        IDeviceLicenseIdentityStore? identities = null) =>
        new(
            store,
            new ContextStore(hasContext ? context ?? Context : null),
            verifier,
            identities ?? new RecordingDeviceLicenseIdentityStore(),
            new FixedCheckpoint(),
            new FixedClock());

    private sealed class ContextStore(LicenseContext? context) : ILicenseContextStore
    {
        public Task<LicenseContext?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(context);
    }

    private sealed class FixedCheckpoint : ILicenseClockCheckpoint
    {
        public LicenseClockCheck CheckAndAdvance(UtcInstant now) => new(false);
    }

    private sealed class FixedClock : IUtcClock
    {
        public UtcInstant GetCurrentInstant() => LicenseTestData.Instant("2026-08-10T12:00:00Z");
    }
}
