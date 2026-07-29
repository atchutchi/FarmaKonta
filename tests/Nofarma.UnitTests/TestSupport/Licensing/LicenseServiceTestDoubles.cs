using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.TestSupport.Licensing;

internal sealed class RecordingLicenseStore : ILicenseStore
{
    private StoredLicense? _current;
    private readonly List<string>? _events;

    internal RecordingLicenseStore(StoredLicense? existing, List<string>? events = null)
    {
        _current = existing;
        _events = events;
    }

    internal int ReplaceCalls { get; private set; }

    internal int GetCalls { get; private set; }

    internal AuditEvent? LastAudit { get; private set; }

    internal StoredLicense? Current => _current;

    public Task<StoredLicense?> GetAsync(CancellationToken cancellationToken)
    {
        GetCalls++;
        return Task.FromResult(_current);
    }

    public Task ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        _events?.Add("license.replace");
        ReplaceCalls++;
        LastAudit = audit;
        _current = new StoredLicense(license.Document);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingLicenseStore(Exception exception) : ILicenseStore
{
    public Task<StoredLicense?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromException<StoredLicense?>(exception);

    public Task ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        CancellationToken cancellationToken) => Task.FromException(exception);
}

internal sealed class RecoverableIntegrityLicenseStore : ILicenseStore
{
    internal int ReplaceCalls { get; private set; }

    internal StoredLicense? Current { get; private set; }

    public Task<StoredLicense?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromException<StoredLicense?>(new LicensePersistenceIntegrityException());

    public Task ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        ReplaceCalls++;
        Current = new StoredLicense(license.Document);
        return Task.CompletedTask;
    }
}

internal sealed class StubVerifier(params LicenseVerification[] results) : ILicenseDocumentVerifier
{
    private readonly Queue<LicenseVerification> _results = new(results);

    internal int Calls { get; private set; }

    internal List<byte[]> Documents { get; } = [];

    public LicenseVerification Verify(
        ReadOnlyMemory<byte> document,
        DeviceLicenseIdentity device,
        LicenseContext context)
    {
        Calls++;
        Documents.Add(document.ToArray());
        if (_results.Count == 0)
        {
            throw new InvalidOperationException("No verifier result was configured for this call.");
        }

        LicenseVerification result = _results.Peek();
        if (_results.Count > 1)
        {
            _results.Dequeue();
        }

        return result;
    }
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
        IDeviceLicenseIdentityStore? identities = null,
        ILicenseClockCheckpoint? checkpoint = null) =>
        new(
            store,
            new ContextStore(hasContext ? context ?? Context : null),
            verifier,
            identities ?? new RecordingDeviceLicenseIdentityStore(),
            checkpoint ?? new RecordingLicenseClockCheckpoint(),
            new FixedClock());

    private sealed class ContextStore(LicenseContext? context) : ILicenseContextStore
    {
        public Task<LicenseContext?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(context);
    }

    private sealed class FixedClock : IUtcClock
    {
        public UtcInstant GetCurrentInstant() => LicenseTestData.Instant("2026-08-10T12:00:00Z");
    }
}

internal sealed class RecordingLicenseClockCheckpoint(
    bool rollbackDetected = false,
    Exception? checkException = null,
    Exception? initializeException = null,
    List<string>? events = null) : ILicenseClockCheckpoint
{
    internal int InitializeCalls { get; private set; }

    internal int CheckCalls { get; private set; }

    internal LicenseClockBinding? LastBinding { get; private set; }

    public void Initialize(LicenseClockBinding binding, UtcInstant now)
    {
        events?.Add("checkpoint.initialize");
        InitializeCalls++;
        LastBinding = binding;
        if (initializeException is not null)
        {
            throw initializeException;
        }
    }

    public LicenseClockCheck CheckAndAdvance(LicenseClockBinding binding, UtcInstant now)
    {
        CheckCalls++;
        LastBinding = binding;
        if (checkException is not null)
        {
            throw checkException;
        }

        return new LicenseClockCheck(rollbackDetected);
    }
}
