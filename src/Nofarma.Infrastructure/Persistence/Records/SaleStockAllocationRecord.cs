namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class SaleStockAllocationRecord
{
    public Guid Id { get; set; }
    public Guid SaleId { get; set; }
    public Guid SaleLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid StockLotId { get; set; }
    public Guid StockMovementId { get; set; }
    public long QuantityBase { get; set; }
    public long OriginUnitCostXof { get; set; }
    public long PreviousLotBalance { get; set; }
    public long ResultingLotBalance { get; set; }
}
