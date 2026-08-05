using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

public sealed record SaleStockAllocation(
    EntityId SaleLineId,
    EntityId ProductId,
    EntityId LotId,
    EntityId StockMovementId,
    long QuantityBase,
    long OriginUnitCostXof,
    long ExpectedLotVersion,
    long PreviousLotBalance,
    long ResultingLotBalance);

public sealed record SaleOutboxEvent(
    EntityId Id,
    EntityId PharmacyId,
    EntityId DeviceId,
    string EventType,
    EntityId AggregateId,
    string PayloadJson,
    UtcInstant OccurredAtUtc);

public sealed record SaleCompletion(
    SaleActorContext Context,
    Sale Sale,
    Receipt Receipt,
    IReadOnlyList<SaleStockAllocation> Allocations,
    IReadOnlyList<StockMovement> StockMovements,
    StoredCashShift Shift,
    CashMovement? CashMovement,
    long ExpectedCashShiftVersion,
    SaleCommandEnvelope Command,
    AuditEvent Audit,
    SaleOutboxEvent Outbox);
