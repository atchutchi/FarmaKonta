using Nofarma.Application.Inventory.Import;

namespace Nofarma.Application.Abstractions;

public interface IInventoryFileReader
{
    Task<IReadOnlyList<string>> ListWorksheetsAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<InventoryFileReadResult> ReadAsync(
        string filePath,
        string? worksheetName = null,
        CancellationToken cancellationToken = default);
}
