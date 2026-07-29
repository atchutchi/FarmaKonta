using Nofarma.Application.Abstractions;
using Nofarma.Application.Idempotency;
using Nofarma.Application.Licensing;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Purchasing;

namespace Nofarma.Application.Purchasing;

public sealed class PurchaseService(
    IPurchaseStore store,
    AuthorizationService authorization,
    ILicensedOperationPolicy licensedOperationPolicy,
    IUtcClock clock)
{
    public async Task<PurchaseDetails> CreateAsync(
        LocalSession actor,
        CreatePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManagePurchases);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0)
        {
            throw new PurchaseValidationException("A compra deve ter pelo menos uma linha.");
        }

        PurchaseActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        EntityId purchaseId = EntityId.New();
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            purchaseId,
            "purchase.created",
            clock.GetCurrentInstant());
        return await store.CreateAsync(
            context,
            purchaseId,
            request.Lines.Select(_ => EntityId.New()).ToArray(),
            actor.UserId,
            request,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PurchaseDetails> GetAsync(
        LocalSession actor,
        EntityId purchaseId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewPurchases);
        PurchaseActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        bool includeCosts = RolePermissions.IsAllowed(actor.Role, Capability.ViewPurchasePrices);
        return await store.GetAsync(
            context.PharmacyId,
            purchaseId,
            includeCosts,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A compra indicada não existe.");
    }

    public async Task<IReadOnlyList<PurchaseSummary>> SearchAsync(
        LocalSession actor,
        string? query,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewPurchases);
        PurchaseActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.SearchAsync(
            context.PharmacyId,
            query?.Trim() ?? string.Empty,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PurchaseDetails> UpdateAsync(
        LocalSession actor,
        EntityId purchaseId,
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManagePurchases);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0)
        {
            throw new PurchaseValidationException("A compra deve ter pelo menos uma linha.");
        }

        PurchaseActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            purchaseId,
            "purchase.updated",
            clock.GetCurrentInstant());
        return await store.UpdateAsync(
            context,
            purchaseId,
            request,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelAsync(
        LocalSession actor,
        EntityId purchaseId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManagePurchases);
        PurchaseActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            purchaseId,
            "purchase.cancelled",
            clock.GetCurrentInstant());
        await store.CancelAsync(
            context,
            purchaseId,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PurchaseReceiptDetails> ConfirmReceiptAsync(
        LocalSession actor,
        EntityId purchaseId,
        ConfirmPurchaseReceiptRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManagePurchases);
        ArgumentNullException.ThrowIfNull(request);
        ValidateReceipt(request);
        PurchaseActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        string normalizedKey = request.IdempotencyKey.Trim();
        string requestFingerprint = OperationRequestFingerprint.ForPurchaseReceipt(
            context,
            purchaseId,
            actor.UserId,
            request);
        PurchaseReceiptDetails? repeated = await store.GetReceiptResultAsync(
            context.PharmacyId,
            normalizedKey,
            requestFingerprint,
            cancellationToken).ConfigureAwait(false);
        if (repeated is not null)
        {
            return repeated;
        }

        LicensedOperationPolicyResult policy = await licensedOperationPolicy.CanCreateAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!policy.IsAllowed)
        {
            throw new StockOperationBlockedException(policy.Code ?? "LICENSE_OPERATION_BLOCKED");
        }

        EntityId receiptId = EntityId.New();
        UtcInstant occurredUtc = clock.GetCurrentInstant();
        ConfirmPurchaseReceiptLineCommand[] lines = request.Lines.Select((line, index) =>
            new ConfirmPurchaseReceiptLineCommand(
                EntityId.New(),
                line.PurchaseOrderLineId,
                EntityId.New(),
                EntityId.New(),
                line.PackageQuantity,
                line.UnitCostXof,
                string.IsNullOrWhiteSpace(line.LotNumber)
                    ? "SEM-LOTE"
                    : line.LotNumber.Trim().ToUpperInvariant(),
                line.Expiry,
                $"{request.IdempotencyKey.Trim()}:line:{index + 1}"))
            .ToArray();
        var command = new ConfirmPurchaseReceiptCommand(
            receiptId,
            purchaseId,
            actor.UserId,
            request.DocumentNumber!.Trim(),
            request.DocumentDate,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            normalizedKey,
            occurredUtc,
            lines,
            requestFingerprint);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            receiptId,
            "purchase.receipt_confirmed",
            occurredUtc,
            "GoodsReceipt");
        return await store.ConfirmReceiptAsync(
            context,
            command,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateReceipt(ConfirmPurchaseReceiptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentNumber) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.Lines.Count == 0)
        {
            throw new PurchaseValidationException(
                "O documento, a chave idempotente e pelo menos uma linha são obrigatórios.");
        }

        if (request.Lines.Any(line => line.PackageQuantity <= 0 || line.UnitCostXof <= 0))
        {
            throw new PurchaseValidationException(
                "As quantidades e os custos recebidos devem ser positivos.");
        }
    }

    private async Task<PurchaseActorContext> GetContextAsync(
        LocalSession actor,
        CancellationToken cancellationToken) =>
        await store.GetContextAsync(actor.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");

    private static AuditEvent CreateAudit(
        PurchaseActorContext context,
        EntityId actorUserId,
        EntityId objectId,
        string action,
        UtcInstant occurredUtc,
        string objectType = "PurchaseOrder") => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actorUserId,
            action,
            objectType,
            objectId.Value.ToString("D"),
            occurredUtc,
            AuditOutcome.Success,
            null,
            "{}");
}
