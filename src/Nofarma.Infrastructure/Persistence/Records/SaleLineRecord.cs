namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class SaleLineRecord
{
    public Guid Id { get; set; }
    public Guid SaleId { get; set; }
    public int Sequence { get; set; }
    public Guid ProductId { get; set; }
    public Guid PackageId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public long PackageFactor { get; set; }
    public long QuantityPackages { get; set; }
    public long QuantityBase { get; set; }
    public long UnitPriceXof { get; set; }
    public long GrossXof { get; set; }
    public long DiscountXof { get; set; }
    public long NetXof { get; set; }
    public long CapturedCostXof { get; set; }
    public Guid? DiscountAuthorizedByUserId { get; set; }
}
