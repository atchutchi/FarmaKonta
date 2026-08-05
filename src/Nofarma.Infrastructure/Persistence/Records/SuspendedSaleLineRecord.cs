namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class SuspendedSaleLineRecord
{
    public Guid Id { get; set; }
    public Guid SuspendedSaleId { get; set; }
    public int Sequence { get; set; }
    public Guid ProductId { get; set; }
    public Guid PackageId { get; set; }
    public long QuantityPackages { get; set; }
    public long DiscountXof { get; set; }
}
