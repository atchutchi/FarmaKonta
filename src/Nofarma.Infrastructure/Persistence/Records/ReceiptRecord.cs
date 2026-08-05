namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class ReceiptRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid SaleId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int Type { get; set; }
    public string ContentJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
