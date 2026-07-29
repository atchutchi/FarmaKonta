using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Inventory.Import;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteInventoryImportStore(DbContextOptions<NofarmaDbContext> options) : IInventoryImportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InventoryImportStoreContext?> GetContextAsync(EntityId userId, CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        LocalUserRecord? user = await context.LocalUsers.AsNoTracking().SingleOrDefaultAsync(
            record => record.Id == userId.Value && record.Status == (int)UserStatus.Active,
            cancellationToken);
        if (user is null) return null;
        Guid deviceId = await context.Installations.AsNoTracking().Where(record => record.PharmacyId == user.PharmacyId)
            .Select(record => record.DeviceId).SingleAsync(cancellationToken);
        return new InventoryImportStoreContext(new EntityId(user.PharmacyId), new EntityId(deviceId));
    }

    public async Task<IReadOnlyList<ExistingImportProduct>> FindProductsAsync(
        EntityId pharmacyId,
        IReadOnlyCollection<string> barcodes,
        IReadOnlyCollection<string> internalCodes,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        string[] normalizedCodes = internalCodes.Select(Normalize).ToArray();
        string[] barcodeValues = barcodes.Select(value => value.Trim()).ToArray();
        ProductRecord[] products = await context.Products.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value && normalizedCodes.Contains(record.NormalizedCode))
            .ToArrayAsync(cancellationToken);
        Guid[] barcodeProductIds = await context.ProductBarcodes.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value && barcodeValues.Contains(record.Value))
            .Select(record => record.ProductId).Distinct().ToArrayAsync(cancellationToken);
        if (barcodeProductIds.Length > 0)
        {
            ProductRecord[] additional = await context.Products.AsNoTracking()
                .Where(record => record.PharmacyId == pharmacyId.Value && barcodeProductIds.Contains(record.Id))
                .ToArrayAsync(cancellationToken);
            products = products.Concat(additional).DistinctBy(record => record.Id).ToArray();
        }

        Guid[] ids = products.Select(record => record.Id).ToArray();
        ProductBarcodeRecord[] storedBarcodes = await context.ProductBarcodes.AsNoTracking()
            .Where(record => ids.Contains(record.ProductId)).ToArrayAsync(cancellationToken);
        return products.Select(record => new ExistingImportProduct(
            new EntityId(record.Id), record.Code, (ProductType)record.Type,
            storedBarcodes.Where(barcode => barcode.ProductId == record.Id).Select(barcode => barcode.Value).ToArray())).ToArray();
    }

    public async Task<InventoryImportDraft> SaveDraftAsync(
        InventoryImportStoreContext context,
        EntityId userId,
        string fileName,
        string fileHash,
        string? worksheetName,
        IReadOnlyList<InventoryImportDraftRow> rows,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        Guid importId = Guid.NewGuid();
        db.InventoryImports.Add(new InventoryImportRecord
        {
            Id = importId,
            PharmacyId = context.PharmacyId.Value,
            FileHash = fileHash,
            FileName = Path.GetFileName(fileName),
            SheetName = worksheetName,
            Status = (int)InventoryImportStatus.Draft,
            CreatedByUserId = userId.Value,
            CreatedAtUtc = createdAtUtc,
            TotalRows = rows.Count,
            ValidRows = rows.Count(row => row.Status == InventoryImportRowStatus.Valid),
            ErrorRows = rows.Count(row => row.Status == InventoryImportRowStatus.Blocked)
        });
        foreach (InventoryImportDraftRow row in rows)
        {
            db.InventoryImportRows.Add(new InventoryImportRowRecord
            {
                Id = row.Id.Value,
                InventoryImportId = importId,
                RowNumber = row.RowNumber,
                DataJson = JsonSerializer.Serialize(row.Data, JsonOptions),
                MatchType = (int)row.MatchType,
                MatchedProductId = row.MatchedProductId?.Value,
                Status = (int)row.Status
            });
            foreach (InventoryImportError error in row.Errors)
            {
                db.InventoryImportErrors.Add(new InventoryImportErrorRecord
                {
                    Id = Guid.NewGuid(),
                    InventoryImportId = importId,
                    InventoryImportRowId = row.Id.Value,
                    RowNumber = error.RowNumber,
                    Field = error.Field,
                    ReceivedValue = Truncate(error.ReceivedValue, 1000),
                    Code = error.Code,
                    Message = error.Message
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new InventoryImportDraft(new EntityId(importId), Path.GetFileName(fileName), worksheetName,
            InventoryImportStatus.Draft, rows.Count, rows.Count(row => row.Status == InventoryImportRowStatus.Valid),
            rows.Count(row => row.Status == InventoryImportRowStatus.Blocked), rows);
    }

    public async Task<InventoryImportDraft?> GetDraftAsync(EntityId pharmacyId, EntityId importId, CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        InventoryImportRecord? record = await db.InventoryImports.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == importId.Value && item.PharmacyId == pharmacyId.Value, cancellationToken);
        if (record is null) return null;
        InventoryImportRowRecord[] rows = await db.InventoryImportRows.AsNoTracking().Where(item => item.InventoryImportId == record.Id)
            .OrderBy(item => item.RowNumber).ToArrayAsync(cancellationToken);
        InventoryImportErrorRecord[] errors = await db.InventoryImportErrors.AsNoTracking().Where(item => item.InventoryImportId == record.Id)
            .ToArrayAsync(cancellationToken);
        return new InventoryImportDraft(new EntityId(record.Id), record.FileName, record.SheetName,
            (InventoryImportStatus)record.Status, record.TotalRows, record.ValidRows, record.ErrorRows,
            rows.Select(row => MapRow(row, errors)).ToArray());
    }

    public async Task<InventoryImportConfirmationResult?> GetConfirmationResultAsync(
        EntityId pharmacyId,
        EntityId importId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        InventoryImportRecord? import = await db.InventoryImports.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == importId.Value &&
                    item.PharmacyId == pharmacyId.Value &&
                    item.Status == (int)InventoryImportStatus.Confirmed &&
                    item.ConfirmationIdempotencyKey == idempotencyKey.Trim(),
                cancellationToken).ConfigureAwait(false);
        if (import is null)
        {
            return null;
        }

        int movements = await db.StockMovements.AsNoTracking()
            .CountAsync(item => item.SourceDocumentId == import.Id, cancellationToken)
            .ConfigureAwait(false);
        return new InventoryImportConfirmationResult(importId, 0, movements, true);
    }

    public async Task<InventoryImportConfirmationResult> ConfirmAsync(
        InventoryImportStoreContext context,
        EntityId userId,
        EntityId importId,
        string idempotencyKey,
        DateTimeOffset confirmedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        InventoryImportRecord import = await db.InventoryImports.SingleOrDefaultAsync(
            item => item.Id == importId.Value && item.PharmacyId == context.PharmacyId.Value, cancellationToken)
            ?? throw new InventoryValidationException("A importação indicada não existe.");
        if (import.Status == (int)InventoryImportStatus.Confirmed)
        {
            int existing = await db.StockMovements.CountAsync(item => item.SourceDocumentId == import.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new InventoryImportConfirmationResult(importId, 0, existing, true);
        }
        if (import.ErrorRows > 0) throw new InventoryValidationException("Corrige todas as linhas bloqueadas antes de confirmar.");
        InventoryImportRowRecord[] rows = await db.InventoryImportRows.Where(item => item.InventoryImportId == import.Id)
            .OrderBy(item => item.RowNumber).ToArrayAsync(cancellationToken);
        ProductCategoryRecord category = await ResolveCategoryAsync(db, context.PharmacyId.Value, confirmedAtUtc, cancellationToken);
        long sequence = await db.Products.Where(item => item.PharmacyId == context.PharmacyId.Value)
            .MaxAsync(item => (long?)item.GeneratedSequence, cancellationToken) ?? 0;
        int created = 0;
        foreach (InventoryImportRowRecord storedRow in rows)
        {
            InventoryImportNormalizedRow row = Deserialize(storedRow.DataJson);
            ProductRecord product;
            if (storedRow.MatchedProductId is Guid matchedId)
            {
                product = await db.Products.SingleAsync(item => item.Id == matchedId && item.PharmacyId == context.PharmacyId.Value, cancellationToken);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(row.InternalCode))
                {
                    do
                    {
                        sequence = checked(sequence + 1);
                    }
                    while (await GeneratedCodeExistsAsync(
                        db,
                        context.PharmacyId.Value,
                        sequence,
                        cancellationToken));
                }
                product = CreateProduct(context.PharmacyId.Value, category.Id, row, sequence, confirmedAtUtc);
                db.Products.Add(product);
                db.ProductPackages.Add(new ProductPackageRecord { Id = product.BasePackageId, ProductId = product.Id, Name = row.Package, FactorToBaseUnit = row.ConversionFactor, IsBaseUnit = row.ConversionFactor == 1, IsActive = true });
                if (!string.IsNullOrWhiteSpace(row.Barcode)) db.ProductBarcodes.Add(new ProductBarcodeRecord { Id = Guid.NewGuid(), PharmacyId = context.PharmacyId.Value, ProductId = product.Id, PackageId = product.BasePackageId, Value = row.Barcode.Trim() });
                created++;
            }
            long quantityBase = checked(row.InitialQuantity * row.ConversionFactor);
            Guid lotId = Guid.NewGuid();
            string lotNumber = string.IsNullOrWhiteSpace(row.LotNumber) ? "SEM-LOTE" : row.LotNumber.Trim();
            db.StockLots.Add(new StockLotRecord
            {
                Id = lotId,
                PharmacyId = context.PharmacyId.Value,
                ProductId = product.Id,
                Number = lotNumber,
                NormalizedNumber = Normalize(lotNumber),
                ExpiryYear = row.ExpiryDate?.Year,
                ExpiryMonth = row.ExpiryDate?.Month,
                ExpiryDay = row.ExpiryDate?.Day,
                OriginCostXof = row.PurchasePriceXof,
                QuantityReceivedBase = quantityBase,
                AvailableQuantityBase = quantityBase,
                FirstEntryAtUtc = confirmedAtUtc,
                RowVersion = 1
            });
            db.StockMovements.Add(new StockMovementRecord
            {
                Id = Guid.NewGuid(),
                PharmacyId = context.PharmacyId.Value,
                ProductId = product.Id,
                StockLotId = lotId,
                QuantityBase = quantityBase,
                Type = (int)StockMovementType.OpeningInventory,
                SourceDocumentId = import.Id,
                UserId = userId.Value,
                OccurredAtUtc = confirmedAtUtc,
                IdempotencyKey = $"{idempotencyKey}:{storedRow.Id:N}",
                ResultingLotBalance = quantityBase
            });
            product.HasMovements = true;
            product.UpdatedAtUtc = confirmedAtUtc;
        }
        import.Status = (int)InventoryImportStatus.Confirmed;
        import.ConfirmedAtUtc = confirmedAtUtc;
        import.ConfirmationIdempotencyKey = idempotencyKey;
        db.AuditEvents.Add(new AuditEventRecord { Id = Guid.NewGuid(), PharmacyId = context.PharmacyId.Value, DeviceId = context.DeviceId.Value, UserId = userId.Value, Action = "stock.opening_inventory_import_confirmed", ObjectType = "InventoryImport", ObjectId = import.Id.ToString("D"), OccurredAtUtc = confirmedAtUtc, Outcome = (int)AuditOutcome.Success });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InventoryImportConfirmationResult(importId, created, rows.Length, false);
    }

    public async Task<InventoryImportDraft> UpdateRowAsync(
        EntityId pharmacyId,
        EntityId importId,
        InventoryImportDraftRow row,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        InventoryImportRecord import = await db.InventoryImports.SingleOrDefaultAsync(
            item => item.Id == importId.Value && item.PharmacyId == pharmacyId.Value,
            cancellationToken) ?? throw new InventoryValidationException("A importação indicada não existe.");
        if (import.Status != (int)InventoryImportStatus.Draft)
        {
            throw new InventoryValidationException("Uma importação confirmada não pode ser alterada.");
        }

        InventoryImportRowRecord stored = await db.InventoryImportRows.SingleOrDefaultAsync(
            item => item.Id == row.Id.Value && item.InventoryImportId == import.Id,
            cancellationToken) ?? throw new InventoryValidationException("A linha indicada não existe.");
        InventoryImportErrorRecord[] previousErrors = await db.InventoryImportErrors
            .Where(item => item.InventoryImportRowId == stored.Id).ToArrayAsync(cancellationToken);
        db.InventoryImportErrors.RemoveRange(previousErrors);
        stored.DataJson = JsonSerializer.Serialize(row.Data, JsonOptions);
        stored.MatchType = (int)row.MatchType;
        stored.MatchedProductId = row.MatchedProductId?.Value;
        stored.Status = (int)row.Status;
        foreach (InventoryImportError error in row.Errors)
        {
            db.InventoryImportErrors.Add(new InventoryImportErrorRecord
            {
                Id = Guid.NewGuid(),
                InventoryImportId = import.Id,
                InventoryImportRowId = stored.Id,
                RowNumber = error.RowNumber,
                Field = error.Field,
                ReceivedValue = Truncate(error.ReceivedValue, 1000),
                Code = error.Code,
                Message = error.Message
            });
        }

        import.ValidRows = await db.InventoryImportRows.CountAsync(
            item => item.InventoryImportId == import.Id && item.Id != stored.Id && item.Status == (int)InventoryImportRowStatus.Valid,
            cancellationToken) + (row.Status == InventoryImportRowStatus.Valid ? 1 : 0);
        import.ErrorRows = import.TotalRows - import.ValidRows;
        await db.SaveChangesAsync(cancellationToken);
        return await GetDraftAsync(pharmacyId, importId, cancellationToken)
            ?? throw new InventoryValidationException("Não foi possível actualizar a importação.");
    }

    public async Task<IReadOnlyList<InventoryImportError>> GetErrorsAsync(EntityId pharmacyId, EntityId importId, CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        bool exists = await db.InventoryImports.AnyAsync(item => item.Id == importId.Value && item.PharmacyId == pharmacyId.Value, cancellationToken);
        if (!exists) return [];
        return await db.InventoryImportErrors.AsNoTracking().Where(item => item.InventoryImportId == importId.Value)
            .OrderBy(item => item.RowNumber).Select(item => new InventoryImportError(item.RowNumber, item.Field, item.ReceivedValue, item.Code, item.Message)).ToArrayAsync(cancellationToken);
    }

    private static async Task<ProductCategoryRecord> ResolveCategoryAsync(NofarmaDbContext db, Guid pharmacyId, DateTimeOffset now, CancellationToken token)
    {
        ProductCategoryRecord? category = await db.ProductCategories.FirstOrDefaultAsync(item => item.PharmacyId == pharmacyId && item.NormalizedName == "IMPORTACAO", token);
        if (category is not null) return category;
        category = new ProductCategoryRecord { Id = Guid.NewGuid(), PharmacyId = pharmacyId, Name = "Importação", NormalizedName = "IMPORTACAO", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.ProductCategories.Add(category);
        return category;
    }

    private static ProductRecord CreateProduct(Guid pharmacyId, Guid categoryId, InventoryImportNormalizedRow row, long sequence, DateTimeOffset now)
    {
        Guid packageId = Guid.NewGuid();
        string code = string.IsNullOrWhiteSpace(row.InternalCode) ? $"PRD-{sequence:000000}" : row.InternalCode.Trim();
        bool medicine = row.ProductType == ProductType.Medicine;
        return new ProductRecord { Id = Guid.NewGuid(), PharmacyId = pharmacyId, CategoryId = categoryId, Code = code, NormalizedCode = Normalize(code), GeneratedSequence = string.IsNullOrWhiteSpace(row.InternalCode) ? sequence : null, Name = row.CommercialName, ActiveIngredient = row.ActiveIngredient, Dosage = row.Dosage, PharmaceuticalForm = row.PharmaceuticalForm, Manufacturer = row.Manufacturer, Type = (int)row.ProductType, RequiresPrescription = row.RequiresPrescription, RequiresLot = medicine || !string.IsNullOrWhiteSpace(row.LotNumber), RequiresExpiry = medicine || row.ExpiryDate is not null, BasePackageId = packageId, BaseUnit = row.BaseUnit, SalePriceXof = row.SalePriceXof, IndicativePurchasePriceXof = row.PurchasePriceXof, MinimumStockBase = row.MinimumStockBase, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    private static async Task<bool> GeneratedCodeExistsAsync(
        NofarmaDbContext db,
        Guid pharmacyId,
        long sequence,
        CancellationToken cancellationToken)
    {
        string code = $"PRD-{sequence:000000}";
        return db.Products.Local.Any(item => item.PharmacyId == pharmacyId && item.NormalizedCode == code)
            || await db.Products.AsNoTracking().AnyAsync(
                item => item.PharmacyId == pharmacyId && item.NormalizedCode == code,
                cancellationToken);
    }

    private static InventoryImportDraftRow MapRow(InventoryImportRowRecord row, IReadOnlyList<InventoryImportErrorRecord> errors) => new(new EntityId(row.Id), row.RowNumber, Deserialize(row.DataJson), (InventoryImportMatchType)row.MatchType, row.MatchedProductId is Guid productId ? new EntityId(productId) : null, (InventoryImportRowStatus)row.Status, errors.Where(error => error.InventoryImportRowId == row.Id).Select(error => new InventoryImportError(error.RowNumber, error.Field, error.ReceivedValue, error.Code, error.Message)).ToArray());
    private static InventoryImportNormalizedRow Deserialize(string json) => JsonSerializer.Deserialize<InventoryImportNormalizedRow>(json, JsonOptions) ?? throw new InventoryValidationException("Os dados normalizados da importação são inválidos.");
    private static string Normalize(string value) => value.Trim().ToUpperInvariant().Normalize();
    private static string? Truncate(string? value, int length) => value is not null && value.Length > length ? value[..length] : value;
}
