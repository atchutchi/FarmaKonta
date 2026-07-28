using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

public sealed class CashShiftService(
    ICashShiftStore store,
    AuthorizationService authorization,
    IStockOperationPolicy stockPolicy,
    IUtcClock clock)
{
    private const int MaxConcurrencyAttempts = 3;
    private const int MaxIdempotencyKeyLength = 160;

    public async Task<CashShiftSummary> OpenAsync(
        LocalSession actor,
        OpenCashShiftRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageCashShift);
        ArgumentNullException.ThrowIfNull(request);
        string key = NormalizeKey(request.IdempotencyKey);
        CashShiftActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        string fingerprint = CashCommandFingerprint.ForOpen(
            context.PharmacyId,
            context.DeviceId,
            request.OpeningCashXof);
        CashShiftSummary? duplicate = await GetDuplicateAsync(
            context.PharmacyId,
            key,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            return duplicate;
        }

        await EnsureNewOperationAllowedAsync(cancellationToken).ConfigureAwait(false);
        if (await store.GetCurrentAggregateAsync(
            context.PharmacyId,
            context.DeviceId,
            cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new SalesValidationException("Já existe um turno aberto neste posto de caixa.");
        }

        UtcInstant openedAt = clock.GetCurrentInstant();
        CashShift shift = CashShift.Open(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actor.UserId,
            Money.Xof(request.OpeningCashXof),
            openedAt);
        var command = new CashCommandEnvelope(
            key,
            CashCommandType.OpenShift,
            fingerprint);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            shift.Id,
            "cash_shift.opened",
            "CashShift",
            openedAt);
        try
        {
            return await store.SaveOpenedAsync(
                context,
                shift,
                command,
                audit,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CashShiftConcurrencyException)
        {
            CashShiftSummary? concurrentDuplicate = await GetDuplicateAsync(
                context.PharmacyId,
                key,
                fingerprint,
                cancellationToken).ConfigureAwait(false);
            if (concurrentDuplicate is not null)
            {
                return concurrentDuplicate;
            }

            throw;
        }
    }

    public async Task<CashShiftSummary?> GetCurrentAsync(
        LocalSession actor,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageCashShift);
        CashShiftActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        StoredCashShift? stored = await store.GetCurrentAggregateAsync(
            context.PharmacyId,
            context.DeviceId,
            cancellationToken).ConfigureAwait(false);
        return stored is null ? null : Map(stored.Shift);
    }

    public async Task<CashShiftSummary> RecordManualMovementAsync(
        LocalSession actor,
        ManualCashMovementRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageCashShift);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Type is not CashMovementType.ManualEntry and
            not CashMovementType.ManualExit)
        {
            throw new SalesValidationException(
                "A operação indicada não é um movimento manual de caixa.");
        }

        string key = NormalizeKey(request.IdempotencyKey);
        string reason = request.Reason?.Trim() ?? string.Empty;
        CashShiftActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        string fingerprint = CashCommandFingerprint.ForManual(
            context.PharmacyId,
            context.DeviceId,
            request.Type,
            request.AmountXof,
            reason);
        CashShiftSummary? duplicate = await GetDuplicateAsync(
            context.PharmacyId,
            key,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            return duplicate;
        }

        await EnsureNewOperationAllowedAsync(cancellationToken).ConfigureAwait(false);
        EntityId movementId = EntityId.New();
        CashCommandType commandType = request.Type == CashMovementType.ManualEntry
            ? CashCommandType.ManualEntry
            : CashCommandType.ManualExit;
        var command = new CashCommandEnvelope(key, commandType, fingerprint);

        for (int attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            StoredCashShift stored = await GetRequiredCurrentAsync(
                context,
                cancellationToken).ConfigureAwait(false);
            UtcInstant occurredAt = GetEffectiveActivityTime(stored.Shift);
            CashMovement movement = stored.Shift.RecordMovement(
                movementId,
                request.Type,
                Money.Xof(request.AmountXof),
                sourceSaleId: null,
                reason,
                occurredAt);
            AuditEvent audit = CreateAudit(
                context,
                actor.UserId,
                movement.Id,
                request.Type == CashMovementType.ManualEntry
                    ? "cash_shift.manual_entry_recorded"
                    : "cash_shift.manual_exit_recorded",
                "CashMovement",
                occurredAt);
            try
            {
                return await store.SaveMovementAsync(
                    context,
                    stored.Shift,
                    movement,
                    stored.Version,
                    command,
                    audit,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (CashShiftConcurrencyException)
            {
                CashShiftSummary? concurrentDuplicate = await GetDuplicateAsync(
                    context.PharmacyId,
                    key,
                    fingerprint,
                    cancellationToken).ConfigureAwait(false);
                if (concurrentDuplicate is not null)
                {
                    return concurrentDuplicate;
                }

                if (attempt == MaxConcurrencyAttempts)
                {
                    throw;
                }
            }
        }

        throw new CashShiftConcurrencyException();
    }

    public async Task<CashShiftSummary> CloseAsync(
        LocalSession actor,
        CloseCashShiftRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageCashShift);
        ArgumentNullException.ThrowIfNull(request);
        string key = NormalizeKey(request.IdempotencyKey);
        CashShiftActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        string fingerprint = CashCommandFingerprint.ForClose(
            context.PharmacyId,
            context.DeviceId,
            request.CountedCashXof);
        CashShiftSummary? duplicate = await GetDuplicateAsync(
            context.PharmacyId,
            key,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var command = new CashCommandEnvelope(
            key,
            CashCommandType.CloseShift,
            fingerprint);
        for (int attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            StoredCashShift stored = await GetRequiredCurrentAsync(
                context,
                cancellationToken).ConfigureAwait(false);
            UtcInstant closedAt = GetEffectiveActivityTime(stored.Shift);
            stored.Shift.Close(Money.Xof(request.CountedCashXof), closedAt);
            AuditEvent audit = CreateAudit(
                context,
                actor.UserId,
                stored.Shift.Id,
                "cash_shift.closed",
                "CashShift",
                closedAt);
            try
            {
                return await store.SaveClosedAsync(
                    context,
                    stored.Shift,
                    stored.Version,
                    command,
                    audit,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (CashShiftConcurrencyException)
            {
                CashShiftSummary? concurrentDuplicate = await GetDuplicateAsync(
                    context.PharmacyId,
                    key,
                    fingerprint,
                    cancellationToken).ConfigureAwait(false);
                if (concurrentDuplicate is not null)
                {
                    return concurrentDuplicate;
                }

                if (attempt == MaxConcurrencyAttempts)
                {
                    throw;
                }
            }
        }

        throw new CashShiftConcurrencyException();
    }

    private async Task<CashShiftActorContext> GetContextAsync(
        LocalSession actor,
        CancellationToken cancellationToken)
    {
        CashShiftActorContext? context = await store.GetActorContextAsync(
            actor.UserId,
            cancellationToken).ConfigureAwait(false);
        if (context is null ||
            context.ActorUserId != actor.UserId ||
            context.PharmacyId.Value == Guid.Empty ||
            context.DeviceId.Value == Guid.Empty)
        {
            throw new AuthorizationException("A sessão actual deixou de ser válida.");
        }

        return context;
    }

    private async Task<StoredCashShift> GetRequiredCurrentAsync(
        CashShiftActorContext context,
        CancellationToken cancellationToken) =>
        await store.GetCurrentAggregateAsync(
            context.PharmacyId,
            context.DeviceId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new SalesValidationException("Não existe um turno aberto neste posto de caixa.");

    private async Task<CashShiftSummary?> GetDuplicateAsync(
        EntityId pharmacyId,
        string key,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        CashCommandResult? existing = await store.GetCommandResultAsync(
            pharmacyId,
            key,
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(
            existing.RequestFingerprint,
            fingerprint,
            StringComparison.Ordinal))
        {
            throw new CashShiftConflictException();
        }

        return existing.Result;
    }

    private async Task EnsureNewOperationAllowedAsync(
        CancellationToken cancellationToken)
    {
        StockOperationPolicyResult policy = await stockPolicy.CanConfirmAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!policy.IsAllowed)
        {
            throw new CashShiftOperationBlockedException(
                policy.Code ?? "CASH_OPERATION_BLOCKED");
        }
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new SalesValidationException("A chave idempotente é obrigatória.");
        }

        string normalized = key.Trim();
        if (normalized.Length > MaxIdempotencyKeyLength)
        {
            throw new SalesValidationException(
                "A chave idempotente não pode exceder 160 caracteres.");
        }

        return normalized;
    }

    private static CashShiftSummary Map(CashShift shift) => new(
        shift.Id,
        shift.PharmacyId,
        shift.DeviceId,
        shift.UserId,
        shift.Status,
        shift.OpeningCash.Amount,
        shift.ExpectedCash.Amount,
        shift.CountedCash?.Amount,
        shift.Difference?.Amount,
        shift.OpenedAt,
        shift.ClosedAt,
        shift.Movements.Count);

    private UtcInstant GetEffectiveActivityTime(CashShift shift)
    {
        UtcInstant current = clock.GetCurrentInstant();
        UtcInstant latest = shift.Movements.Count == 0
            ? shift.OpenedAt
            : shift.Movements[^1].OccurredAt;
        return current.Value < latest.Value ? latest : current;
    }

    private static AuditEvent CreateAudit(
        CashShiftActorContext context,
        EntityId actorUserId,
        EntityId objectId,
        string action,
        string objectType,
        UtcInstant occurredAt) => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actorUserId,
            action,
            objectType,
            objectId.Value.ToString("D"),
            occurredAt,
            AuditOutcome.Success,
            null,
            "{}");
}
