using System.Security.Cryptography;
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

    public Task<LicenseStoreReplaceResult> ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        LicenseStorePrecondition precondition,
        CancellationToken cancellationToken)
    {
        _events?.Add("license.replace");
        ReplaceCalls++;
        LastAudit = audit;
        byte[] document = license.Document.ToArray();
        _current = new StoredLicense(
            document,
            SHA256.HashData(document),
            hasValidIntegrity: true);
        return Task.FromResult(LicenseStoreReplaceResult.Applied);
    }
}

internal sealed class RecoverableIntegrityLicenseStore : ILicenseStore
{
    internal int ReplaceCalls { get; private set; }

    internal StoredLicense? Current { get; private set; }

    public Task<StoredLicense?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<StoredLicense?>(new StoredLicense(
            new byte[] { 201, 202, 203 },
            new byte[32],
            hasValidIntegrity: false));

    public Task<LicenseStoreReplaceResult> ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        LicenseStorePrecondition precondition,
        CancellationToken cancellationToken)
    {
        ReplaceCalls++;
        byte[] document = license.Document.ToArray();
        Current = new StoredLicense(
            document,
            SHA256.HashData(document),
            hasValidIntegrity: true);
        return Task.FromResult(LicenseStoreReplaceResult.Applied);
    }
}

internal sealed class ScriptedLicenseStore(
    IEnumerable<StoredLicense?> reads,
    IEnumerable<LicenseStoreReplaceResult> replacements) : ILicenseStore
{
    private readonly Queue<StoredLicense?> _reads = new(reads);
    private readonly Queue<LicenseStoreReplaceResult> _replacements = new(replacements);

    internal int ReplaceCalls { get; private set; }

    internal List<LicenseStorePrecondition> Preconditions { get; } = [];

    public Task<StoredLicense?> GetAsync(CancellationToken cancellationToken)
    {
        if (_reads.Count == 0)
        {
            throw new InvalidOperationException("No scripted license read remains.");
        }

        return Task.FromResult(_reads.Dequeue());
    }

    public Task<LicenseStoreReplaceResult> ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        LicenseStorePrecondition precondition,
        CancellationToken cancellationToken)
    {
        ReplaceCalls++;
        Preconditions.Add(precondition);
        if (_replacements.Count == 0)
        {
            throw new InvalidOperationException("No scripted replacement remains.");
        }

        return Task.FromResult(_replacements.Dequeue());
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
        "SHA256:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");

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
