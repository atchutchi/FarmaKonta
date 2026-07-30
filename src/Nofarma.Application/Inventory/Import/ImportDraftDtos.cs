namespace Nofarma.Application.Inventory.Import;

using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

public sealed record InventoryFileRow(
    int RowNumber,
    IReadOnlyList<string?> Cells);

public sealed record InventoryFileReadResult(
    string FileName,
    string FileHashSha256,
    string? WorksheetName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<InventoryFileRow> Rows);

public sealed class InventoryFileException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public enum InventoryImportStatus { Draft = 1, Confirmed = 2 }
public enum InventoryImportRowStatus { Valid = 1, Blocked = 2 }
public enum InventoryImportMatchType { NewProduct = 1, Barcode = 2, InternalCode = 3 }

public sealed record CreateInventoryImportDraftRequest(
    string FilePath,
    string? WorksheetName,
    IReadOnlyDictionary<ImportColumn, string> ColumnMappings);

public sealed record InventoryImportNormalizedRow(
    string? InternalCode,
    string? Barcode,
    string CommercialName,
    string? ActiveIngredient,
    string? Dosage,
    string? PharmaceuticalForm,
    string? Manufacturer,
    string Category,
    ProductType ProductType,
    string BaseUnit,
    string Package,
    long ConversionFactor,
    long PurchasePriceXof,
    long SalePriceXof,
    long? MinimumStockBase,
    long InitialQuantity,
    string? LotNumber,
    DateOnly? ExpiryDate,
    string? Supplier,
    string? Tax,
    bool RequiresPrescription);

public sealed record InventoryImportError(
    int RowNumber,
    string? Field,
    string? ReceivedValue,
    string Code,
    string Message);

public sealed record InventoryImportDraftRow(
    EntityId Id,
    int RowNumber,
    InventoryImportNormalizedRow Data,
    InventoryImportMatchType MatchType,
    EntityId? MatchedProductId,
    InventoryImportRowStatus Status,
    IReadOnlyList<InventoryImportError> Errors);

public sealed record InventoryImportDraft(
    EntityId Id,
    string FileName,
    string? WorksheetName,
    InventoryImportStatus Status,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    IReadOnlyList<InventoryImportDraftRow> Rows);

public sealed record ExistingImportProduct(
    EntityId Id,
    string InternalCode,
    ProductType Type,
    IReadOnlyList<string> Barcodes);

public sealed record InventoryImportConfirmationResult(
    EntityId ImportId,
    int ProductsCreated,
    int MovementsCreated,
    bool AlreadyConfirmed);

public sealed record InventoryImportStoreContext(EntityId PharmacyId, EntityId DeviceId);
