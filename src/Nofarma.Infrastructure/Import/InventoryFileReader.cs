using Nofarma.Application.Abstractions;
using Nofarma.Application.Inventory.Import;

namespace Nofarma.Infrastructure.Import;

public sealed class InventoryFileReader : IInventoryFileReader
{
    public const long MaximumFileSizeBytes = 20L * 1024 * 1024;
    public const int MaximumDataRows = 50_000;

    public Task<IReadOnlyList<string>> ListWorksheetsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(filePath);
        string extension = Path.GetExtension(filePath);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        return OpenXmlInventoryFileReader.ListWorksheetsAsync(filePath, cancellationToken);
    }

    public async Task<InventoryFileReadResult> ReadAsync(
        string filePath,
        string? worksheetName = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(filePath);
        string extension = Path.GetExtension(filePath);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return await CsvInventoryFileReader.ReadAsync(filePath, cancellationToken);
        }

        return await OpenXmlInventoryFileReader.ReadAsync(filePath, worksheetName, cancellationToken);
    }

    private static void ValidateFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedType();
        }

        try
        {
            var information = new FileInfo(filePath);
            if (!information.Exists)
            {
                throw new InventoryFileException(
                    "inventory_file_not_found",
                    "O ficheiro seleccionado não está disponível.");
            }

            if (information.Length > MaximumFileSizeBytes)
            {
                throw new InventoryFileException(
                    "inventory_file_too_large",
                    "O ficheiro excede o limite de 20 MiB.");
            }
        }
        catch (InventoryFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InventoryFileException(
                "inventory_file_unavailable",
                "Não foi possível aceder ao ficheiro seleccionado.",
                exception);
        }
    }

    private static InventoryFileException UnsupportedType() => new(
        "inventory_file_type_invalid",
        "Selecciona um ficheiro Excel (.xlsx) ou CSV (.csv).");
}
