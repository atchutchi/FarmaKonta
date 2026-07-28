using System.Security.Cryptography;
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
        StoredLicense? stored = await store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return MissingStatus();
        }

        LicenseContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeviceLicenseIdentity device = deviceIdentityStore.GetOrCreate(
                context.PharmacyId,
                context.DeviceId);
            return Evaluate(stored.Grant, CreateBinding(context, device, stored.License.Channel));
        }
        catch (Exception exception) when (IsProtectedStateFailure(exception))
        {
            return EvaluateBlocked(stored.Grant);
        }
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

        byte[] document = request.Document.ToArray();
        LicenseContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
        DeviceLicenseIdentity device = deviceIdentityStore.GetOrCreate(
            context.PharmacyId,
            context.DeviceId);
        LicenseVerification verification = verifier.Verify(document, device, context);
        if (!verification.IsValid || verification.License is null)
        {
            throw new LicenseImportException(verification.Code ?? "LICENSE_INVALID");
        }

        VerifiedLicense verifiedLicense = verification.License with
        {
            Document = document
        };
        StoredLicense? current = await store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null && document.AsSpan().SequenceEqual(current.Document.Span))
        {
            return Evaluate(
                current.Grant,
                CreateBinding(context, device, current.License.Channel));
        }

        if (current is not null && verifiedLicense.Grant.Sequence <= current.Grant.Sequence)
        {
            throw new LicenseImportException("LICENSE_ROLLBACK");
        }

        UtcInstant now = clock.GetCurrentInstant();
        LicenseClockBinding binding = CreateBinding(
            context,
            device,
            verifiedLicense.Channel);
        if (current is null)
        {
            try
            {
                clockCheckpoint.Initialize(binding, now);
            }
            catch (Exception exception) when (IsProtectedStateFailure(exception))
            {
                throw new LicenseImportException("LICENSE_CLOCK_CHECKPOINT");
            }
        }
        else
        {
            LicenseClockCheck checkpoint = CheckFailClosed(binding, now);
            if (checkpoint.RollbackDetected)
            {
                return EvaluateBlocked(current.Grant);
            }
        }

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
        return Evaluate(verifiedLicense.Grant, binding);
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

    private LicenseStatus Evaluate(LicenseGrant grant, LicenseClockBinding binding)
    {
        UtcInstant now = clock.GetCurrentInstant();
        LicenseClockCheck checkpoint = CheckFailClosed(binding, now);
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

    private LicenseClockCheck CheckFailClosed(LicenseClockBinding binding, UtcInstant now)
    {
        try
        {
            return clockCheckpoint.CheckAndAdvance(binding, now);
        }
        catch (Exception exception) when (IsProtectedStateFailure(exception))
        {
            return new LicenseClockCheck(true);
        }
    }

    private static LicenseClockBinding CreateBinding(
        LicenseContext context,
        DeviceLicenseIdentity device,
        string channel) =>
        new(
            context.PharmacyId,
            context.DeviceId,
            channel,
            device.PublicKeyThumbprint);

    private LicenseStatus EvaluateBlocked(LicenseGrant grant)
    {
        LicenseEvaluation evaluation = LicenseEvaluator.Evaluate(
            grant,
            clock.GetCurrentInstant(),
            clockRollback: true);
        return new LicenseStatus(
            evaluation.State,
            evaluation.AllowsNewOperations,
            evaluation.AllowsReadOnlyAccess,
            grant);
    }

    private static bool IsProtectedStateFailure(Exception exception) =>
        exception is CryptographicException
            or IOException
            or UnauthorizedAccessException;

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
