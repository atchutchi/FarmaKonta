using Nofarma.Domain.Common;

namespace Nofarma.Domain.Inventory;

public sealed record StockOperation(
    EntityId MovementId,
    EntityId PharmacyId,
    EntityId ProductId,
    EntityId? LotId,
    long QuantityBase,
    StockMovementType Type,
    string? Reason,
    EntityId? SourceDocumentId,
    EntityId UserId,
    UtcInstant OccurredUtc,
    string IdempotencyKey);
