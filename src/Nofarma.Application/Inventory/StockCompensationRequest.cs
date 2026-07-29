using Nofarma.Domain.Common;

namespace Nofarma.Application.Inventory;

public sealed record StockCompensationRequest(
    EntityId OriginalMovementId,
    string Reason,
    string IdempotencyKey);

public sealed record StockCompensationCommand(
    EntityId OriginalMovementId,
    EntityId MovementId,
    EntityId UserId,
    string Reason,
    string IdempotencyKey,
    UtcInstant OccurredUtc,
    string RequestFingerprint);
