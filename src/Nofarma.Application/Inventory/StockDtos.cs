using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Inventory;

public sealed record InventoryActorContext(EntityId PharmacyId, EntityId DeviceId);

public sealed record StockProductRules(
    ProductType Type,
    bool RequiresLot,
    bool RequiresExpiry,
    bool IsActive);

public sealed record StockLotDefinition(
    EntityId ProposedId,
    string Number,
    ExpiryDate? Expiry,
    EntityId? SupplierId,
    long OriginCostXof);

public sealed record InventoryConfirmation(
    StockOperation Operation,
    StockLotDefinition? Lot);

public sealed record StockConfirmationResult(
    EntityId MovementId,
    EntityId ProductId,
    EntityId? LotId,
    long QuantityBase,
    long ResultingLotBalance,
    StockMovementType Type,
    UtcInstant OccurredUtc);

public sealed record StockLotSummary(
    EntityId Id,
    string Number,
    long AvailableQuantityBase,
    DateOnly? ExpiryBlockingDate,
    StockAlertLevel ExpiryAlertLevel);

public sealed record StockMovementSummary(
    EntityId Id,
    EntityId? LotId,
    long QuantityBase,
    StockMovementType Type,
    string? Reason,
    UtcInstant OccurredUtc,
    EntityId? CompensatesMovementId);

public sealed record ProductStockDetails(
    EntityId ProductId,
    string ProductCode,
    string ProductName,
    long TotalQuantityBase,
    long StockThreshold,
    StockAlertLevel StockAlertLevel,
    IReadOnlyList<StockLotSummary> Lots,
    IReadOnlyList<StockMovementSummary> Movements);
