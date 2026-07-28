using Nofarma.Application.Inventory.Import;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface IInventoryImportStore
{
    Task<InventoryImportStoreContext?> GetContextAsync(EntityId userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExistingImportProduct>> FindProductsAsync(
        EntityId pharmacyId,
        IReadOnlyCollection<string> barcodes,
        IReadOnlyCollection<string> internalCodes,
        CancellationToken cancellationToken);
    Task<InventoryImportDraft> SaveDraftAsync(
        InventoryImportStoreContext context,
        EntityId userId,
        string fileName,
        string fileHash,
        string? worksheetName,
        IReadOnlyList<InventoryImportDraftRow> rows,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);
    Task<InventoryImportDraft?> GetDraftAsync(EntityId pharmacyId, EntityId importId, CancellationToken cancellationToken);
    Task<InventoryImportConfirmationResult> ConfirmAsync(
        InventoryImportStoreContext context,
        EntityId userId,
        EntityId importId,
        string idempotencyKey,
        DateTimeOffset confirmedAtUtc,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryImportError>> GetErrorsAsync(
        EntityId pharmacyId,
        EntityId importId,
        CancellationToken cancellationToken);
}
