namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class InventoryImportRowRecord
{
    public Guid Id { get; set; }
    public Guid InventoryImportId { get; set; }
    public int RowNumber { get; set; }
    public string DataJson { get; set; } = "{}";
    public int MatchType { get; set; }
    public Guid? MatchedProductId { get; set; }
    public int Status { get; set; }
}
