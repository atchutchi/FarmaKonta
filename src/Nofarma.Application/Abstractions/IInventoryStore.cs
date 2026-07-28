using Nofarma.Application.Inventory;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Abstractions;

public interface IInventoryStore
{
    Task<InventoryActorContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken);

    Task<StockProductRules?> GetProductRulesAsync(
        EntityId pharmacyId,
        EntityId productId,
        CancellationToken cancellationToken);

    Task<StockConfirmationResult> ConfirmAsync(
        InventoryActorContext context,
        InventoryConfirmation confirmation,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<StockConfirmationResult> CompensateAsync(
        InventoryActorContext context,
        StockCompensationCommand command,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<ProductStockDetails?> GetProductStockAsync(
        EntityId pharmacyId,
        EntityId productId,
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<StockOverview> SearchStockAsync(
        EntityId pharmacyId,
        string query,
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockAllocation>> AllocateFefoAsync(
        EntityId pharmacyId,
        EntityId productId,
        long requiredQuantityBase,
        DateOnly businessDate,
        CancellationToken cancellationToken);
}
