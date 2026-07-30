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
    IUtcClock clock) : ILicenseStatusProvider
{
    private const int MaximumReplaceAttempts = 3;

    public async Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        LicenseContext context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
        StoredLicense? stored = await store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return MissingStatus();
        }

        if (!stored.HasValidIntegrity)
        {
            return InvalidStatus();
        }

        try
        {
            DeviceLicenseIdentity device = deviceIdentityStore.GetOrCreate(
                context.PharmacyId,
                context.DeviceId);
            LicenseVerification verification = verifier.Verify(stored.Document, device, context);
            if (!verification.IsValid || verification.License is null)
            {
                return InvalidStatus();
            }

            VerifiedLicense trusted = verification.License;
            return Evaluate(
                trusted.Grant,
                CreateBinding(context, device, trusted.Channel));
        }
        catch (Exception exception) when (IsProtectedStateFailure(exception))
        {
            return InvalidStatus();
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
        UtcInstant now = clock.GetCurrentInstant();
        LicenseClockBinding binding = CreateBinding(
            context,
            device,
            verifiedLicense.Channel);
        for (int attempt = 0; attempt < MaximumReplaceAttempts; attempt++)
        {
            StoredLicense? current = await store.GetAsync(cancellationToken).ConfigureAwait(false);
            VerifiedLicense? trustedCurrent = VerifyCurrent(current, device, context);
            bool sameDocument = current is not null
                && document.AsSpan().SequenceEqual(current.Document.Span);
            if (trustedCurrent is not null
                && current is not null
                && current.HasValidIntegrity
                && sameDocument)
            {
                return Evaluate(
                    trustedCurrent.Grant,
                    CreateBinding(context, device, trustedCurrent.Channel));
            }

            if (trustedCurrent is not null
                && !sameDocument
                && verifiedLicense.Grant.Sequence <= trustedCurrent.Grant.Sequence)
            {
                throw new LicenseImportException("LICENSE_ROLLBACK");
            }

            if (trustedCurrent is null)
            {
                InitializeCheckpoint(binding, now);
            }
            else
            {
                LicenseClockCheck checkpoint = CheckFailClosed(binding, now);
                if (checkpoint.RollbackDetected)
                {
                    return EvaluateBlocked(trustedCurrent.Grant);
                }
            }

            AuditEvent audit = CreateAudit(
                context,
                verifiedLicense,
                now,
                trustedCurrent is null ? "license.imported" : "license.renewed");
            LicenseStorePrecondition precondition = current is null
                ? LicenseStorePrecondition.ExpectedAbsent()
                : LicenseStorePrecondition.Matching(current.ConcurrencyToken);
            LicenseStoreReplaceResult result = await store.ReplaceAsync(
                verifiedLicense,
                audit,
                precondition,
                cancellationToken).ConfigureAwait(false);
            if (result is LicenseStoreReplaceResult.Applied
                or LicenseStoreReplaceResult.AlreadyCurrent)
            {
                return Evaluate(verifiedLicense.Grant, binding);
            }
        }

        throw new LicenseImportException("LICENSE_CONFLICT");
    }

    public async Task EnsureNewOperationsAllowedAsync(CancellationToken cancellationToken)
    {
        LicenseStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.AllowsNewOperations)
        {
            throw new LicenseOperationBlockedException(LicenseOperationPolicyRules.For(status.State).Code!);
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

    private VerifiedLicense? VerifyCurrent(
        StoredLicense? current,
        DeviceLicenseIdentity device,
        LicenseContext context)
    {
        if (current is null)
        {
            return null;
        }

        LicenseVerification currentVerification = verifier.Verify(
            current.Document,
            device,
            context);
        return currentVerification.IsValid ? currentVerification.License : null;
    }

    private void InitializeCheckpoint(LicenseClockBinding binding, UtcInstant now)
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

    private static AuditEvent CreateAudit(
        LicenseContext context,
        VerifiedLicense license,
        UtcInstant now,
        string action) =>
        new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            null,
            action,
            "License",
            license.Grant.Id.Value.ToString("D"),
            now,
            AuditOutcome.Success,
            null,
            LicenseAuditMetadata.Serialize(license.Grant));

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

    private static LicenseStatus InvalidStatus() =>
        new(LicenseState.Invalid, false, true, null);

}
