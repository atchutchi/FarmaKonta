namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class ProductBarcodeRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid ProductId { get; set; }
    public Guid PackageId { get; set; }
    public string Value { get; set; } = string.Empty;
}
