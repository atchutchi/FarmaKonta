using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Inventory;

public sealed record StockAdjustmentRequest(
    EntityId ProductId,
    EntityId LotId,
    long QuantityBase,
    StockMovementType Type,
    string? Reason,
    string IdempotencyKey);
