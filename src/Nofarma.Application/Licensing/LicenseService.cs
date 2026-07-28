using Nofarma.Application.Abstractions;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;

namespace Nofarma.Application.Licensing;

public sealed class LicenseService(
    ILicenseStore store,
    ILicenseContextStore contextStore,
    ILicenseDocumentVerifier verifier,
    IDeviceLicenseIdentityStore deviceIdentityStore,
    ILicenseClockCheckpoint clockCheckpoint,
    IUtcClock clock)
{
    public async Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await GetContextAsync(cancellationToken).ConfigureAwait(false);
        StoredLicense? stored = await store.GetAsync(cancellationToken).ConfigureAwait(false);
        return stored is null
            ? MissingStatus()
            : Evaluate(stored.Grant);
    }

    public async Task<LicenseActivationRequest> CreateActivationRequestAsync(
        CancellationToken cancellationToken)
    {
        LicenseContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
        DeviceLicenseIdentity device = deviceIdentityStore.GetOrCreate(
            context.PharmacyId,
            context.DeviceId);
        return new LicenseActivationRequest(context, device);
    }

    public async Task<LicenseStatus> ImportAsync(
        LicenseImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LicenseContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
        DeviceLicenseIdentity device = deviceIdentityStore.GetOrCreate(
            context.PharmacyId,
            context.DeviceId);
        LicenseVerification verification = verifier.Verify(request.Document, device, context);
        if (!verification.IsValid || verification.License is null)
        {
            throw new LicenseImportException(verification.Code ?? "LICENSE_INVALID");
        }

        VerifiedLicense verifiedLicense = verification.License with
        {
            Document = request.Document
        };
        StoredLicense? current = await store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null && request.Document.AsSpan().SequenceEqual(current.Document.Span))
        {
            return Evaluate(current.Grant);
        }

        if (current is not null && verifiedLicense.Grant.Sequence <= current.Grant.Sequence)
        {
            throw new LicenseImportException("LICENSE_ROLLBACK");
        }

        UtcInstant now = clock.GetCurrentInstant();
        AuditEvent audit = new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            null,
            current is null ? "license.imported" : "license.renewed",
            "License",
            verifiedLicense.Grant.Id.Value.ToString("D"),
            now,
            AuditOutcome.Success,
            null,
            "{}");
        await store.ReplaceAsync(verifiedLicense, audit, cancellationToken).ConfigureAwait(false);
        return Evaluate(verifiedLicense.Grant);
    }

    public async Task EnsureNewOperationsAllowedAsync(CancellationToken cancellationToken)
    {
        LicenseStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.AllowsNewOperations)
        {
            throw new LicenseOperationBlockedException(StatusCode(status.State));
        }
    }

    private async Task<LicenseContext> GetContextAsync(CancellationToken cancellationToken) =>
        await contextStore.GetAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new LicenseContextUnavailableException();

    private LicenseStatus Evaluate(LicenseGrant grant)
    {
        UtcInstant now = clock.GetCurrentInstant();
        LicenseClockCheck checkpoint = clockCheckpoint.CheckAndAdvance(now);
        LicenseEvaluation evaluation = LicenseEvaluator.Evaluate(
            grant,
            now,
            checkpoint.RollbackDetected);
        return new LicenseStatus(
            evaluation.State,
            evaluation.AllowsNewOperations,
            evaluation.AllowsReadOnlyAccess,
            grant);
    }

    private static LicenseStatus MissingStatus() =>
        new(LicenseState.Missing, false, true, null);

    private static string StatusCode(LicenseState state) => state switch
    {
        LicenseState.Missing => "LICENSE_MISSING",
        LicenseState.Invalid => "LICENSE_INVALID",
        LicenseState.NotYetValid => "LICENSE_NOT_YET_VALID",
        LicenseState.ExpiredReadOnly => "LICENSE_EXPIRED_READ_ONLY",
        LicenseState.ClockRollback => "LICENSE_CLOCK_ROLLBACK",
        _ => "LICENSE_OPERATION_BLOCKED"
    };
}
