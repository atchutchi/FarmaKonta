using Nofarma.Domain.Common;

namespace Nofarma.Domain.Inventory;

public sealed class StockMovement
{
    internal StockMovement(
        StockOperation operation,
        long resultingLotBalance,
        EntityId? compensatesMovementId = null)
    {
        Id = operation.MovementId;
        PharmacyId = operation.PharmacyId;
        ProductId = operation.ProductId;
        LotId = operation.LotId;
        QuantityBase = operation.QuantityBase;
        Type = operation.Type;
        Reason = NormalizeOptional(operation.Reason);
        SourceDocumentId = operation.SourceDocumentId;
        UserId = operation.UserId;
        OccurredUtc = operation.OccurredUtc;
        IdempotencyKey = operation.IdempotencyKey.Trim();
        ResultingLotBalance = resultingLotBalance;
        CompensatesMovementId = compensatesMovementId;
    }

    public EntityId Id { get; }

    public EntityId PharmacyId { get; }

    public EntityId ProductId { get; }

    public EntityId? LotId { get; }

    public long QuantityBase { get; }

    public StockMovementType Type { get; }

    public string? Reason { get; }

    public EntityId? SourceDocumentId { get; }

    public EntityId UserId { get; }

    public UtcInstant OccurredUtc { get; }

    public string IdempotencyKey { get; }

    public long ResultingLotBalance { get; }

    public EntityId? CompensatesMovementId { get; }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
