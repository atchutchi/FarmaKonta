namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class StockMovementRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? StockLotId { get; set; }
    public long QuantityBase { get; set; }
    public int Type { get; set; }
    public string? Reason { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RequestFingerprint { get; set; }
    public Guid? CompensatesMovementId { get; set; }
    public long ResultingLotBalance { get; set; }
}
