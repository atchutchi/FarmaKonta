namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class InventoryImportErrorRecord
{
    public Guid Id { get; set; }
    public Guid InventoryImportId { get; set; }
    public Guid? InventoryImportRowId { get; set; }
    public int RowNumber { get; set; }
    public string? Field { get; set; }
    public string? ReceivedValue { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
