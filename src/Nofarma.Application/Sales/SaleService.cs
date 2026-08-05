using System.Text.Json;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

public sealed class SaleService(
    ISaleStore store,
    AuthorizationService authorization,
    ILicensedOperationPolicy licensedOperationPolicy,
    IUtcClock clock)
{
    private const int MaxSequenceAttempts = 3;
    private const int MaxIdempotencyKeyLength = 160;
    private const int SearchLimit = 30;

    public async Task<IReadOnlyList<SaleProductResult>> SearchProductsAsync(
        LocalSession actor,
        string query,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.CreateSale);
        SaleActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        string normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        return await store.SearchProductsAsync(
            context.PharmacyId,
            normalizedQuery,
            GetBusinessDate(context),
            SearchLimit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<SaleSummary> CompleteAsync(
        LocalSession actor,
        CompleteSaleRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.CreateSale);
        ArgumentNullException.ThrowIfNull(request);
        string key = NormalizeKey(request.IdempotencyKey);
        ValidateRequestCollections(request);
        SaleActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        string fingerprint = SaleCommandFingerprint.ForComplete(context, request);
        SaleSummary? duplicate = await GetDuplicateAsync(
            context.PharmacyId,
            key,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            return duplicate;
        }

        if (request.TotalDiscountXof > 0 || request.Lines.Any(line => line.DiscountXof > 0))
        {
            authorization.EnsureAllowed(actor, Capability.ApplySaleDiscount);
        }

        LicensedOperationPolicyResult policy = await licensedOperationPolicy
            .CanCreateAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!policy.IsAllowed)
        {
            throw new SaleOperationBlockedException(
                policy.Code ?? "LICENSE_OPERATION_BLOCKED");
        }

        for (int attempt = 1; attempt <= MaxSequenceAttempts; attempt++)
        {
            try
            {
                SaleCompletion completion = await PrepareCompletionAsync(
                    actor,
                    context,
                    request,
                    key,
                    fingerprint,
                    cancellationToken).ConfigureAwait(false);
                return await store.CompleteAsync(completion, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SaleConcurrencyException exception)
                when (exception.Reason == SaleConcurrencyReason.Sequence &&
                      attempt < MaxSequenceAttempts)
            {
                SaleSummary? concurrentDuplicate = await GetDuplicateAsync(
                    context.PharmacyId,
                    key,
                    fingerprint,
                    cancellationToken).ConfigureAwait(false);
                if (concurrentDuplicate is not null)
                {
                    return concurrentDuplicate;
                }
            }
        }

        throw new SaleConcurrencyException(SaleConcurrencyReason.Sequence);
    }

    public async Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
        LocalSession actor,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.CreateSale);
        SaleActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.GetSuspendedAsync(
            context.PharmacyId,
            context.DeviceId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<SuspendedSaleSummary> SuspendAsync(
        LocalSession actor,
        SuspendSaleRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.CreateSale);
        ArgumentNullException.ThrowIfNull(request);
        ValidateSuspendedLines(request.Lines);
        if (request.Lines.Any(line => line.DiscountXof > 0))
        {
            authorization.EnsureAllowed(actor, Capability.ApplySaleDiscount);
        }
        LicensedOperationPolicyResult policy = await licensedOperationPolicy
            .CanCreateAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!policy.IsAllowed)
        {
            throw new SaleOperationBlockedException(
                policy.Code ?? "LICENSE_OPERATION_BLOCKED");
        }

        SaleActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        DateOnly businessDate = GetBusinessDate(context);
        var lines = new List<SaleLine>(request.Lines.Count);
        foreach (CompleteSaleLineRequest lineRequest in request.Lines)
        {
            SaleProductSnapshot snapshot = await store.GetProductSnapshotAsync(
                context.PharmacyId,
                lineRequest.ProductId,
                lineRequest.PackageId,
                businessDate,
                cancellationToken).ConfigureAwait(false)
                ?? throw new SalesValidationException(
                    "Um produto do carrinho deixou de estar disponível para suspensão.");
            ValidateSnapshot(lineRequest, snapshot);
            lines.Add(SaleLine.Create(
                EntityId.New(),
                snapshot.ProductId,
                snapshot.PackageId,
                snapshot.Name,
                snapshot.PackageName,
                snapshot.PackageFactor,
                lineRequest.QuantityPackages,
                Money.Xof(snapshot.SalePriceXof),
                Money.Xof(lineRequest.DiscountXof),
                Money.Xof(0),
                lineRequest.DiscountXof > 0 ? actor.UserId : null));
        }

        UtcInstant suspendedAt = clock.GetCurrentInstant();
        SuspendedSale suspended = SuspendedSale.Create(
            request.Id ?? EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actor.UserId,
            request.Name,
            lines,
            suspendedAt);
        AuditEvent audit = CreateSuspensionAudit(
            context,
            actor.UserId,
            suspended.Id,
            "sale.suspended",
            suspendedAt);
        return await store.SaveSuspendedAsync(suspended, audit, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ResumedSaleDetails> ResumeSuspendedAsync(
        LocalSession actor,
        EntityId suspendedSaleId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.CreateSale);
        EnsureIdentifier(suspendedSaleId, "A venda suspensa é obrigatória.");
        SaleActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        SuspendedSaleDetails stored = await store.GetSuspendedDetailsAsync(
            context.PharmacyId,
            context.DeviceId,
            suspendedSaleId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new SalesValidationException("A venda suspensa já não está disponível.");
        DateOnly businessDate = GetBusinessDate(context);
        var lines = new List<ResumedSaleLineDetails>(stored.Lines.Count);
        foreach (SuspendedSaleLineDetails line in stored.Lines)
        {
            SaleProductSnapshot? snapshot = await store.GetProductSnapshotAsync(
                context.PharmacyId,
                line.ProductId,
                line.PackageId,
                businessDate,
                cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                lines.Add(new ResumedSaleLineDetails(
                    line.ProductId,
                    line.PackageId,
                    null,
                    null,
                    null,
                    null,
                    line.QuantityPackages,
                    line.DiscountXof,
                    null,
                    0,
                    true));
                continue;
            }
            long requiredQuantityBase;
            try
            {
                requiredQuantityBase = checked(
                    snapshot.PackageFactor * line.QuantityPackages);
            }
            catch (OverflowException)
            {
                requiredQuantityBase = long.MaxValue;
            }
            long available = snapshot.Lots.Sum(lot => lot.AvailableQuantityBase);
            lines.Add(new ResumedSaleLineDetails(
                line.ProductId,
                line.PackageId,
                snapshot.Code,
                snapshot.Name,
                snapshot.PackageName,
                snapshot.PackageFactor,
                line.QuantityPackages,
                line.DiscountXof,
                snapshot.SalePriceXof,
                available,
                available < requiredQuantityBase));
        }
        return new ResumedSaleDetails(
            stored.Id,
            stored.Name,
            lines.AsReadOnly(),
            stored.SuspendedAtUtc);
    }

    public async Task<bool> DeleteSuspendedAsync(
        LocalSession actor,
        EntityId suspendedSaleId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.CreateSale);
        EnsureIdentifier(suspendedSaleId, "A venda suspensa é obrigatória.");
        SaleActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        UtcInstant occurredAt = clock.GetCurrentInstant();
        AuditEvent audit = CreateSuspensionAudit(
            context,
            actor.UserId,
            suspendedSaleId,
            "sale.suspension_deleted",
            occurredAt);
        return await store.DeleteSuspendedAsync(
            context.PharmacyId,
            context.DeviceId,
            suspendedSaleId,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReceiptDetails?> GetReceiptAsync(
        LocalSession actor,
        EntityId receiptId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.CreateSale);
        EnsureIdentifier(receiptId, "O recibo é obrigatório.");
        SaleActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.GetReceiptAsync(
            context.PharmacyId,
            receiptId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SaleCompletion> PrepareCompletionAsync(
        LocalSession actor,
        SaleActorContext context,
        CompleteSaleRequest request,
        string key,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        StoredCashShift storedShift = await store.GetOpenShiftAsync(
            context.PharmacyId,
            context.DeviceId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new SalesValidationException(
                "Não existe um turno aberto neste dispositivo.");
        if (storedShift.Shift.UserId != actor.UserId)
        {
            throw new SalesValidationException(
                "O turno aberto pertence a outro utilizador. Abre o teu próprio turno.");
        }

        DateOnly businessDate = GetBusinessDate(context);
        EntityId saleId = EntityId.New();
        var lines = new List<SaleLine>();
        var allocations = new List<SaleStockAllocation>();
        var movements = new List<StockMovement>();
        var workingBalances = new Dictionary<EntityId, long>();
        var knownLots = new Dictionary<EntityId, SaleLotSnapshot>();
        UtcInstant completedAt = GetEffectiveActivityTime(storedShift.Shift);

        foreach (CompleteSaleLineRequest lineRequest in request.Lines)
        {
            SaleProductSnapshot snapshot = await store.GetProductSnapshotAsync(
                context.PharmacyId,
                lineRequest.ProductId,
                lineRequest.PackageId,
                businessDate,
                cancellationToken).ConfigureAwait(false)
                ?? throw new SalesValidationException(
                    "Um produto do carrinho deixou de estar disponível para venda.");
            ValidateSnapshot(lineRequest, snapshot);
            long requiredQuantityBase;
            try
            {
                requiredQuantityBase = checked(
                    snapshot.PackageFactor * lineRequest.QuantityPackages);
            }
            catch (OverflowException)
            {
                throw new SalesValidationException(
                    "A quantidade pedida excede o limite permitido.");
            }

            foreach (SaleLotSnapshot lot in snapshot.Lots)
            {
                knownLots.TryAdd(lot.Lot.Id, lot);
                workingBalances.TryAdd(lot.Lot.Id, lot.AvailableQuantityBase);
            }
            StockLotAvailability[] availability = snapshot.Lots
                .Select(lot => new StockLotAvailability(
                    lot.Lot,
                    workingBalances[lot.Lot.Id]))
                .ToArray();
            IReadOnlyList<StockAllocation> selected = FefoAllocator.Allocate(
                requiredQuantityBase,
                availability,
                businessDate);
            EntityId lineId = EntityId.New();
            long capturedCost = 0;
            foreach (StockAllocation selection in selected)
            {
                SaleLotSnapshot lot = knownLots[selection.LotId];
                long previous = workingBalances[selection.LotId];
                long resulting = checked(previous - selection.QuantityBase);
                capturedCost = checked(
                    capturedCost + checked(lot.Lot.OriginCost.Amount * selection.QuantityBase));
                workingBalances[selection.LotId] = resulting;
                StockMovement movement = StockLedger.CreateMovement(
                    new StockOperation(
                        EntityId.New(),
                        context.PharmacyId,
                        snapshot.ProductId,
                        selection.LotId,
                        -selection.QuantityBase,
                        StockMovementType.Sale,
                        null,
                        saleId,
                        actor.UserId,
                        completedAt,
                        $"sale:{saleId.Value:N}:line:{lineId.Value:N}:lot:{selection.LotId.Value:N}"),
                    previous);
                allocations.Add(new SaleStockAllocation(
                    lineId,
                    snapshot.ProductId,
                    selection.LotId,
                    movement.Id,
                    selection.QuantityBase,
                    lot.Lot.OriginCost.Amount,
                    lot.Version,
                    previous,
                    resulting));
                movements.Add(movement);
            }
            lines.Add(SaleLine.Create(
                lineId,
                snapshot.ProductId,
                snapshot.PackageId,
                snapshot.Name,
                snapshot.PackageName,
                snapshot.PackageFactor,
                lineRequest.QuantityPackages,
                Money.Xof(snapshot.SalePriceXof),
                Money.Xof(lineRequest.DiscountXof),
                Money.Xof(capturedCost),
                lineRequest.DiscountXof > 0 ? actor.UserId : null));
        }

        var payments = request.Payments
            .Select(payment => SalePayment.Create(
                EntityId.New(),
                payment.Method,
                Money.Xof(payment.AmountXof),
                payment.Reference))
            .ToArray();
        long sequence = await store.GetNextSaleSequenceAsync(
            context.PharmacyId,
            businessDate,
            cancellationToken).ConfigureAwait(false);
        Sale sale = Sale.Create(
            saleId,
            context.PharmacyId,
            context.DeviceId,
            actor.UserId,
            storedShift.Shift.Id,
            SaleNumber.Create(businessDate, sequence),
            lines,
            Money.Xof(request.TotalDiscountXof),
            request.TotalDiscountXof > 0 ? actor.UserId : null,
            payments,
            completedAt);
        long retainedCash = checked(sale.CashReceived.Amount - sale.Change.Amount);
        CashMovement? cashMovement = retainedCash == 0
            ? null
            : storedShift.Shift.RecordMovement(
                EntityId.New(),
                CashMovementType.Sale,
                Money.Xof(retainedCash),
                sale.Id,
                $"Venda {sale.Number.Value}",
                completedAt);
        Receipt receipt = Receipt.Create(
            EntityId.New(),
            sale,
            context.PharmacyName,
            context.ActorDisplayName,
            completedAt);
        AuditEvent audit = new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actor.UserId,
            "sale.completed",
            "Sale",
            sale.Id.Value.ToString("D"),
            completedAt,
            AuditOutcome.Success,
            null,
            "{}");
        string payload = JsonSerializer.Serialize(new
        {
            SaleId = sale.Id.Value,
            Number = sale.Number.Value,
            TotalXof = sale.Total.Amount,
            CompletedAtUtc = sale.CompletedAt.Value
        });
        var outbox = new SaleOutboxEvent(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            "sale.completed.v1",
            sale.Id,
            payload,
            completedAt);
        return new SaleCompletion(
            context,
            sale,
            receipt,
            allocations.AsReadOnly(),
            movements.AsReadOnly(),
            storedShift,
            cashMovement,
            storedShift.Version,
            new SaleCommandEnvelope(key, fingerprint),
            audit,
            outbox);
    }

    private async Task<SaleActorContext> GetContextAsync(
        LocalSession actor,
        CancellationToken cancellationToken)
    {
        SaleActorContext? context = await store.GetActorContextAsync(
            actor.UserId,
            cancellationToken).ConfigureAwait(false);
        if (context is null ||
            context.ActorUserId != actor.UserId ||
            context.PharmacyId.Value == Guid.Empty ||
            context.DeviceId.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.PharmacyName) ||
            string.IsNullOrWhiteSpace(context.ActorDisplayName) ||
            string.IsNullOrWhiteSpace(context.TimeZoneId))
        {
            throw new AuthorizationException("A sessão actual deixou de ser válida.");
        }
        return context;
    }

    private async Task<SaleSummary?> GetDuplicateAsync(
        EntityId pharmacyId,
        string key,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        SaleCommandResult? existing = await store.GetCommandResultAsync(
            pharmacyId,
            key,
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }
        if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new SaleConflictException();
        }
        return existing.Result;
    }

    private DateOnly GetBusinessDate(SaleActorContext context)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new SalesValidationException("O fuso horário da farmácia não é válido.");
        }
        DateTimeOffset local = TimeZoneInfo.ConvertTime(clock.GetCurrentInstant().Value, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private UtcInstant GetEffectiveActivityTime(CashShift shift)
    {
        UtcInstant current = clock.GetCurrentInstant();
        UtcInstant latest = shift.Movements.Count == 0
            ? shift.OpenedAt
            : shift.Movements[^1].OccurredAt;
        return current.Value < latest.Value ? latest : current;
    }

    private static void ValidateRequestCollections(CompleteSaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Lines);
        ArgumentNullException.ThrowIfNull(request.Payments);
        if (request.Lines.Count == 0)
        {
            throw new SalesValidationException("A venda deve ter pelo menos uma linha.");
        }
        if (request.Lines
                .Select(line => (line.ProductId, line.PackageId))
                .Distinct()
                .Count() != request.Lines.Count)
        {
            throw new SalesValidationException(
                "O mesmo produto e embalagem não podem aparecer em linhas repetidas.");
        }
    }

    private static void ValidateSuspendedLines(
        IReadOnlyCollection<CompleteSaleLineRequest> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new SalesValidationException(
                "A venda suspensa deve ter pelo menos uma linha.");
        }
        if (lines.Select(line => (line.ProductId, line.PackageId)).Distinct().Count() != lines.Count)
        {
            throw new SalesValidationException(
                "O mesmo produto e embalagem não podem aparecer em linhas repetidas.");
        }
    }

    private static AuditEvent CreateSuspensionAudit(
        SaleActorContext context,
        EntityId actorUserId,
        EntityId suspendedSaleId,
        string action,
        UtcInstant occurredAt) => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actorUserId,
            action,
            "SuspendedSale",
            suspendedSaleId.Value.ToString("D"),
            occurredAt,
            AuditOutcome.Success,
            null,
            "{}");

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new SalesValidationException(message);
        }
    }

    private static void ValidateSnapshot(
        CompleteSaleLineRequest request,
        SaleProductSnapshot snapshot)
    {
        if (snapshot.ProductId != request.ProductId ||
            snapshot.PackageId != request.PackageId ||
            snapshot.PackageFactor <= 0 ||
            snapshot.SalePriceXof < 0 ||
            snapshot.Lots.Any(lot => lot.Lot.ProductId != request.ProductId ||
                                     lot.AvailableQuantityBase < 0 ||
                                     lot.Version < 0))
        {
            throw new SalesValidationException(
                "Os dados actuais do produto não são válidos para venda.");
        }
    }

    private static string NormalizeKey(string key)
    {
        string normalized = key?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new SalesValidationException("A chave idempotente é obrigatória.");
        }
        if (normalized.Length > MaxIdempotencyKeyLength)
        {
            throw new SalesValidationException(
                "A chave idempotente não pode exceder 160 caracteres.");
        }
        return normalized;
    }
}
