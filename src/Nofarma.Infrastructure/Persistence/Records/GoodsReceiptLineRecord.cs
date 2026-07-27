namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class GoodsReceiptLineRecord
{
    public Guid Id { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? StockLotId { get; set; }
    public long PackageQuantity { get; set; }
    public long FactorToBaseUnit { get; set; }
    public long QuantityBase { get; set; }
    public long UnitCostXof { get; set; }
}
