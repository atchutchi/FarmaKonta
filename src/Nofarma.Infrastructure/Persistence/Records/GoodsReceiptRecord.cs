namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class GoodsReceiptRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? Notes { get; set; }
    public Guid ReceivedByUserId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RequestFingerprint { get; set; }
}
