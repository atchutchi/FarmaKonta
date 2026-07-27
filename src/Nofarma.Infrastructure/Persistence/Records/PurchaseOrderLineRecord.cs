namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class PurchaseOrderLineRecord
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid PackageId { get; set; }
    public long OrderedPackageQuantity { get; set; }
    public long ReceivedPackageQuantity { get; set; }
    public long FactorToBaseUnit { get; set; }
    public long UnitCostXof { get; set; }
    public long DiscountXof { get; set; }
    public string? Notes { get; set; }
}
