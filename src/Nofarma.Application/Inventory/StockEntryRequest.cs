using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Inventory;

public sealed record StockEntryRequest(
    EntityId ProductId,
    long QuantityBase,
    StockMovementType Type,
    string? LotNumber,
    ExpiryDate? Expiry,
    EntityId? SupplierId,
    long OriginCostXof,
    string? Reason,
    EntityId? SourceDocumentId,
    string IdempotencyKey);
