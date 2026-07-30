namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class StockLotRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid ProductId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string NormalizedNumber { get; set; } = string.Empty;
    public int? ExpiryYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryDay { get; set; }
    public Guid? SupplierId { get; set; }
    public long OriginCostXof { get; set; }
    public long QuantityReceivedBase { get; set; }
    public long AvailableQuantityBase { get; set; }
    public DateTimeOffset FirstEntryAtUtc { get; set; }
    public long RowVersion { get; set; }
}
