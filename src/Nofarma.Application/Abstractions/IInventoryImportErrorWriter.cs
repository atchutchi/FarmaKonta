using Nofarma.Application.Inventory.Import;

namespace Nofarma.Application.Abstractions;

public interface IInventoryImportErrorWriter
{
    Task WriteAsync(
        Stream destination,
        IReadOnlyList<InventoryImportError> errors,
        CancellationToken cancellationToken = default);
}
