namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class PurchaseOrderRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid SupplierId { get; set; }
    public int Status { get; set; }
    public string? DocumentNumber { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
