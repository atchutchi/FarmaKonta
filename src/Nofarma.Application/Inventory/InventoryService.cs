using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Inventory;

public sealed class InventoryService(
    IInventoryStore store,
    AuthorizationService authorization,
    IStockOperationPolicy stockPolicy,
    IUtcClock clock)
{
    public async Task<StockConfirmationResult> ConfirmEntryAsync(
        LocalSession actor,
        StockEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        authorization.EnsureAllowed(actor, EntryCapability(request.Type));
        await EnsureConfirmationAllowedAsync(cancellationToken).ConfigureAwait(false);
        InventoryActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        StockProductRules rules = await GetProductRulesAsync(
            context.PharmacyId,
            request.ProductId,
            cancellationToken).ConfigureAwait(false);
        ValidateEntry(request, rules);
        EntityId movementId = EntityId.New();
        EntityId lotId = EntityId.New();
        UtcInstant occurredUtc = clock.GetCurrentInstant();
        string lotNumber = string.IsNullOrWhiteSpace(request.LotNumber)
            ? "SEM-LOTE"
            : request.LotNumber.Trim();
        var operation = new StockOperation(
            movementId,
            context.PharmacyId,
            request.ProductId,
            lotId,
            request.QuantityBase,
            request.Type,
            request.Reason,
            request.SourceDocumentId,
            actor.UserId,
            occurredUtc,
            request.IdempotencyKey);
        var confirmation = new InventoryConfirmation(
            operation,
            new StockLotDefinition(
                lotId,
                lotNumber,
                request.Expiry,
                request.SupplierId,
                request.OriginCostXof));
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            movementId,
            EntryAuditAction(request.Type),
            occurredUtc);
        return await store.ConfirmAsync(
            context,
            confirmation,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<StockConfirmationResult> ConfirmAdjustmentAsync(
        LocalSession actor,
        StockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        authorization.EnsureAllowed(actor, Capability.AdjustStock);
        await EnsureConfirmationAllowedAsync(cancellationToken).ConfigureAwait(false);
        ValidateAdjustment(request);
        InventoryActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        StockProductRules rules = await GetProductRulesAsync(
            context.PharmacyId,
            request.ProductId,
            cancellationToken).ConfigureAwait(false);
        if (!rules.IsActive)
        {
            throw new InventoryValidationException("O produto indicado não está activo.");
        }

        EntityId movementId = EntityId.New();
        UtcInstant occurredUtc = clock.GetCurrentInstant();
        var operation = new StockOperation(
            movementId,
            context.PharmacyId,
            request.ProductId,
            request.LotId,
            request.QuantityBase,
            request.Type,
            request.Reason,
            null,
            actor.UserId,
            occurredUtc,
            request.IdempotencyKey);
        var confirmation = new InventoryConfirmation(operation, null);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            movementId,
            "stock.adjustment_confirmed",
            occurredUtc);
        return await store.ConfirmAsync(
            context,
            confirmation,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<StockConfirmationResult> CompensateAsync(
        LocalSession actor,
        StockCompensationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        authorization.EnsureAllowed(actor, Capability.CompensateStock);
        await EnsureConfirmationAllowedAsync(cancellationToken).ConfigureAwait(false);
        if (request.OriginalMovementId.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new InventoryValidationException(
                "O movimento original, o motivo e a chave idempotente são obrigatórios.");
        }

        InventoryActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        EntityId movementId = EntityId.New();
        UtcInstant occurredUtc = clock.GetCurrentInstant();
        var command = new StockCompensationCommand(
            request.OriginalMovementId,
            movementId,
            actor.UserId,
            request.Reason.Trim(),
            request.IdempotencyKey.Trim(),
            occurredUtc);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            movementId,
            "stock.compensation_confirmed",
            occurredUtc);
        return await store.CompensateAsync(
            context,
            command,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateEntry(StockEntryRequest request, StockProductRules rules)
    {
        if (!rules.IsActive)
        {
            throw new InventoryValidationException("O produto indicado não está activo.");
        }

        if (request.QuantityBase <= 0)
        {
            throw new InventoryValidationException("A quantidade de entrada deve ser positiva.");
        }

        if (request.Type is not StockMovementType.OpeningInventory and
            not StockMovementType.PurchaseReceipt and
            not StockMovementType.QuickEntry and
            not StockMovementType.PositiveAdjustment)
        {
            throw new InventoryValidationException("O tipo indicado não é uma entrada de stock.");
        }

        if (rules.RequiresLot && string.IsNullOrWhiteSpace(request.LotNumber))
        {
            throw new InventoryValidationException("O número do lote é obrigatório.");
        }

        if (rules.RequiresExpiry && request.Expiry is null)
        {
            throw new InventoryValidationException("A validade do lote é obrigatória.");
        }

        if (request.OriginCostXof < 0)
        {
            throw new InventoryValidationException("O custo de origem não pode ser negativo.");
        }

        EnsureCommonFields(request.Reason, request.IdempotencyKey, request.Type);
    }

    private static void ValidateAdjustment(StockAdjustmentRequest request)
    {
        if (request.LotId.Value == Guid.Empty || request.QuantityBase == 0)
        {
            throw new InventoryValidationException("O lote e a quantidade do ajuste são obrigatórios.");
        }

        if (request.Type is not StockMovementType.PositiveAdjustment and
            not StockMovementType.NegativeAdjustment and
            not StockMovementType.Loss and
            not StockMovementType.Damage and
            not StockMovementType.Expiration and
            not StockMovementType.SupplierReturn)
        {
            throw new InventoryValidationException("O tipo indicado não é um ajuste de stock.");
        }

        EnsureCommonFields(request.Reason, request.IdempotencyKey, request.Type);
    }

    private static void EnsureCommonFields(
        string? reason,
        string idempotencyKey,
        StockMovementType type)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InventoryValidationException("A chave idempotente é obrigatória.");
        }

        if (type != StockMovementType.OpeningInventory &&
            type != StockMovementType.PurchaseReceipt &&
            string.IsNullOrWhiteSpace(reason))
        {
            throw new InventoryValidationException("O motivo do movimento é obrigatório.");
        }
    }

    private async Task EnsureConfirmationAllowedAsync(CancellationToken cancellationToken)
    {
        StockOperationPolicyResult result = await stockPolicy.CanConfirmAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsAllowed)
        {
            throw new StockOperationBlockedException(result.Code ?? "STOCK_CONFIRMATION_BLOCKED");
        }
    }

    private async Task<InventoryActorContext> GetContextAsync(
        LocalSession actor,
        CancellationToken cancellationToken) =>
        await store.GetContextAsync(actor.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");

    private async Task<StockProductRules> GetProductRulesAsync(
        EntityId pharmacyId,
        EntityId productId,
        CancellationToken cancellationToken) =>
        await store.GetProductRulesAsync(pharmacyId, productId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InventoryValidationException("O produto indicado não existe.");

    private static string EntryAuditAction(StockMovementType type) => type switch
    {
        StockMovementType.OpeningInventory => "stock.opening_inventory_confirmed",
        StockMovementType.PurchaseReceipt => "stock.purchase_receipt_confirmed",
        StockMovementType.QuickEntry => "stock.quick_entry_confirmed",
        _ => "stock.adjustment_confirmed"
    };

    private static Capability EntryCapability(StockMovementType type) => type switch
    {
        StockMovementType.OpeningInventory => Capability.ImportInventory,
        StockMovementType.PurchaseReceipt => Capability.ManagePurchases,
        StockMovementType.PositiveAdjustment => Capability.AdjustStock,
        _ => Capability.ManageStock
    };

    private static AuditEvent CreateAudit(
        InventoryActorContext context,
        EntityId actorUserId,
        EntityId movementId,
        string action,
        UtcInstant occurredUtc) => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actorUserId,
            action,
            "StockMovement",
            movementId.Value.ToString("D"),
            occurredUtc,
            AuditOutcome.Success,
            null,
            "{}");
}
