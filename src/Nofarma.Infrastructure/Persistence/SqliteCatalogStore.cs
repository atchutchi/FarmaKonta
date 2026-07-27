using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Catalog;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteCatalogStore(
    DbContextOptions<NofarmaDbContext> options) : ICatalogStore
{
    public async Task<CatalogActorContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        LocalUserRecord? actor = await context.LocalUsers.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.Id == actorUserId.Value &&
                    record.Status == (int)UserStatus.Active,
                cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            return null;
        }

        Guid deviceId = await context.Installations.AsNoTracking()
            .Where(record => record.PharmacyId == actor.PharmacyId)
            .Select(record => record.DeviceId)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        return new CatalogActorContext(
            new EntityId(actor.PharmacyId),
            new EntityId(deviceId));
    }

    public async Task<IReadOnlyList<ProductSummary>> SearchAsync(
        EntityId pharmacyId,
        string query,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        string normalized = FoldSearchText(query);
        ProductRecord[] records = await context.Products.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return records
            .Where(record => normalized.Length == 0 ||
                FoldSearchText(record.Code).Contains(normalized, StringComparison.Ordinal) ||
                FoldSearchText(record.Name).Contains(normalized, StringComparison.Ordinal) ||
                FoldSearchText(record.ActiveIngredient).Contains(normalized, StringComparison.Ordinal) ||
                FoldSearchText(record.Manufacturer).Contains(normalized, StringComparison.Ordinal))
            .OrderBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(record => new ProductSummary(
                new EntityId(record.Id),
                record.Code,
                record.Name,
                (ProductType)record.Type,
                record.BaseUnit,
                record.SalePriceXof,
                record.RequiresLot,
                record.RequiresExpiry,
                record.IsActive,
                record.MinimumStockBase))
            .ToArray();
    }

    public async Task<ProductDetails?> GetDetailsAsync(
        EntityId pharmacyId,
        EntityId productId,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        ProductRecord? product = await context.Products.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == pharmacyId.Value &&
                    record.Id == productId.Value,
                cancellationToken).ConfigureAwait(false);
        return product is null
            ? null
            : await MapDetailsAsync(context, product, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductDetails> CreateAsync(
        CatalogActorContext context,
        EntityId productId,
        EntityId basePackageId,
        CreateProductRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var dbContext = new NofarmaDbContext(options);
        await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        dbContext.Database.UseTransaction(transaction);
        try
        {
            bool categoryExists = await dbContext.ProductCategories.AnyAsync(
                record => record.Id == request.CategoryId.Value &&
                    record.PharmacyId == context.PharmacyId.Value &&
                    record.IsActive,
                cancellationToken).ConfigureAwait(false);
            if (!categoryExists)
            {
                throw new ProductValidationException("A categoria indicada não está activa.");
            }

            long? generatedSequence = null;
            ProductCode code;
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                long currentMaximum = await dbContext.Products
                    .Where(record => record.PharmacyId == context.PharmacyId.Value)
                    .MaxAsync(record => (long?)record.GeneratedSequence, cancellationToken)
                    .ConfigureAwait(false) ?? 0;
                generatedSequence = checked(currentMaximum + 1);
                code = ProductCode.FromSequence(generatedSequence.Value);
            }
            else
            {
                code = ProductCode.Parse(request.Code);
            }

            Product product = request.Type == ProductType.Medicine
                ? Product.CreateMedicine(
                    productId,
                    context.PharmacyId,
                    code,
                    request.Name,
                    request.CategoryId,
                    basePackageId,
                    request.BaseUnit,
                    Money.Xof(request.SalePriceXof),
                    Money.Xof(request.IndicativePurchasePriceXof),
                    request.MinimumStockBase)
                : Product.CreateGeneral(
                    productId,
                    context.PharmacyId,
                    code,
                    request.Name,
                    request.CategoryId,
                    basePackageId,
                    request.BaseUnit,
                    Money.Xof(request.SalePriceXof),
                    Money.Xof(request.IndicativePurchasePriceXof),
                    request.MinimumStockBase,
                    request.RequiresLot,
                    request.RequiresExpiry);
            DateTimeOffset now = auditEvent.OccurredAtUtc.Value;
            dbContext.Products.Add(MapProduct(product, request, generatedSequence, now));
            dbContext.ProductPackages.Add(MapPackage(product.Packages.Single(), product.Id));
            dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return MapNewProductDetails(product, request);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            throw new CatalogConflictException(
                "O código interno ou código de barras já está em uso.");
        }
    }

    public async Task DeactivateAsync(
        CatalogActorContext context,
        EntityId productId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        ProductRecord product = await dbContext.Products.SingleOrDefaultAsync(
            record => record.PharmacyId == context.PharmacyId.Value &&
                record.Id == productId.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O produto indicado não existe.");
        product.IsActive = false;
        product.UpdatedAtUtc = auditEvent.OccurredAtUtc.Value;
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductPackageDetails> AddPackageAsync(
        CatalogActorContext context,
        EntityId productId,
        EntityId packageId,
        EntityId? barcodeId,
        AddProductPackageRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.FactorToBaseUnit <= 0)
        {
            throw new ProductValidationException(
                "A embalagem exige nome e factor de conversão positivo.");
        }

        string? barcode = string.IsNullOrWhiteSpace(request.Barcode)
            ? null
            : request.Barcode.Trim();
        if (barcode is not null && barcodeId is null)
        {
            throw new ProductValidationException("O código de barras deve ter um identificador.");
        }

        await using var dbContext = new NofarmaDbContext(options);
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        bool productExists = await dbContext.Products.AnyAsync(
            record => record.Id == productId.Value &&
                record.PharmacyId == context.PharmacyId.Value &&
                record.IsActive,
            cancellationToken).ConfigureAwait(false);
        if (!productExists)
        {
            throw new InvalidOperationException("O produto indicado não está activo.");
        }

        dbContext.ProductPackages.Add(new ProductPackageRecord
        {
            Id = packageId.Value,
            ProductId = productId.Value,
            Name = request.Name.Trim(),
            FactorToBaseUnit = request.FactorToBaseUnit,
            IsBaseUnit = false,
            IsActive = true
        });
        if (barcode is not null)
        {
            dbContext.ProductBarcodes.Add(new ProductBarcodeRecord
            {
                Id = barcodeId!.Value.Value,
                PharmacyId = context.PharmacyId.Value,
                ProductId = productId.Value,
                PackageId = packageId.Value,
                Value = barcode
            });
        }

        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            throw new CatalogConflictException(
                "A embalagem ou código de barras já está em uso.");
        }

        return new ProductPackageDetails(
            packageId,
            request.Name.Trim(),
            request.FactorToBaseUnit,
            false,
            barcode is null ? [] : [barcode]);
    }

    public async Task<ProductDetails> UpdateSettingsAsync(
        CatalogActorContext context,
        EntityId productId,
        UpdateProductSettingsRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        ProductRecord record = await dbContext.Products.SingleOrDefaultAsync(
            candidate => candidate.Id == productId.Value &&
                candidate.PharmacyId == context.PharmacyId.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O produto indicado não existe.");
        Product product = RestoreProduct(record);
        product.ChangePrices(
            Money.Xof(request.SalePriceXof),
            Money.Xof(request.IndicativePurchasePriceXof));
        product.ChangeMinimumStock(request.MinimumStockBase);
        product.ChangeTrackingRequirements(request.RequiresLot, request.RequiresExpiry);
        record.SalePriceXof = product.SalePrice.Amount;
        record.IndicativePurchasePriceXof = product.IndicativePurchasePrice.Amount;
        record.MinimumStockBase = product.MinimumStock;
        record.RequiresLot = product.RequiresLot;
        record.RequiresExpiry = product.RequiresExpiry;
        record.UpdatedAtUtc = auditEvent.OccurredAtUtc.Value;
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapDetailsAsync(dbContext, record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(
        EntityId pharmacyId,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        return await context.ProductCategories.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value && record.IsActive)
            .OrderBy(record => record.Name)
            .Select(record => new ProductCategorySummary(
                new EntityId(record.Id),
                record.Name,
                record.IsActive))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductCategorySummary> CreateCategoryAsync(
        CatalogActorContext context,
        EntityId categoryId,
        string name,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ProductCategory category = ProductCategory.Create(
            categoryId,
            context.PharmacyId,
            name);
        DateTimeOffset now = auditEvent.OccurredAtUtc.Value;
        await using var dbContext = new NofarmaDbContext(options);
        dbContext.ProductCategories.Add(new ProductCategoryRecord
        {
            Id = category.Id.Value,
            PharmacyId = category.PharmacyId.Value,
            Name = category.Name,
            NormalizedName = category.Name.ToUpperInvariant(),
            IsActive = category.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            throw new CatalogConflictException("Já existe uma categoria com este nome.");
        }

        return new ProductCategorySummary(category.Id, category.Name, category.IsActive);
    }

    private static ProductRecord MapProduct(
        Product product,
        CreateProductRequest request,
        long? generatedSequence,
        DateTimeOffset now) => new()
        {
            Id = product.Id.Value,
            PharmacyId = product.PharmacyId.Value,
            CategoryId = product.CategoryId.Value,
            Code = product.Code.Value,
            NormalizedCode = product.Code.Value,
            GeneratedSequence = generatedSequence,
            Name = product.Name,
            ActiveIngredient = NormalizeOptional(request.ActiveIngredient),
            Dosage = NormalizeOptional(request.Dosage),
            PharmaceuticalForm = NormalizeOptional(request.PharmaceuticalForm),
            Manufacturer = NormalizeOptional(request.Manufacturer),
            Type = (int)product.Type,
            RequiresPrescription = request.RequiresPrescription,
            RequiresLot = product.RequiresLot,
            RequiresExpiry = product.RequiresExpiry,
            BasePackageId = product.BasePackageId.Value,
            BaseUnit = product.BaseUnit,
            SalePriceXof = product.SalePrice.Amount,
            IndicativePurchasePriceXof = product.IndicativePurchasePrice.Amount,
            MinimumStockBase = product.MinimumStock,
            IsActive = product.IsActive,
            HasMovements = product.HasMovements,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static Product RestoreProduct(ProductRecord record)
    {
        Product product = (ProductType)record.Type == ProductType.Medicine
            ? Product.CreateMedicine(
                new EntityId(record.Id),
                new EntityId(record.PharmacyId),
                ProductCode.Parse(record.Code),
                record.Name,
                new EntityId(record.CategoryId),
                new EntityId(record.BasePackageId),
                record.BaseUnit,
                Money.Xof(record.SalePriceXof),
                Money.Xof(record.IndicativePurchasePriceXof),
                record.MinimumStockBase)
            : Product.CreateGeneral(
                new EntityId(record.Id),
                new EntityId(record.PharmacyId),
                ProductCode.Parse(record.Code),
                record.Name,
                new EntityId(record.CategoryId),
                new EntityId(record.BasePackageId),
                record.BaseUnit,
                Money.Xof(record.SalePriceXof),
                Money.Xof(record.IndicativePurchasePriceXof),
                record.MinimumStockBase,
                record.RequiresLot,
                record.RequiresExpiry);
        if (record.HasMovements)
        {
            product.MarkHasMovements();
        }

        if (!record.IsActive)
        {
            product.Deactivate();
        }

        return product;
    }

    private static ProductPackageRecord MapPackage(ProductPackage package, EntityId productId) => new()
    {
        Id = package.Id.Value,
        ProductId = productId.Value,
        Name = package.Name,
        FactorToBaseUnit = package.FactorToBaseUnit,
        IsBaseUnit = package.IsBaseUnit,
        IsActive = true
    };

    private static ProductDetails MapNewProductDetails(
        Product product,
        CreateProductRequest request)
    {
        ProductPackage basePackage = product.Packages.Single();
        return new ProductDetails(
            product.Id,
            product.Code.Value,
            product.Name,
            product.Type,
            product.BaseUnit,
            product.SalePrice.Amount,
            product.IndicativePurchasePrice.Amount,
            product.MinimumStock,
            request.RequiresPrescription,
            product.RequiresLot,
            product.RequiresExpiry,
            product.IsActive,
            [new ProductPackageDetails(
                basePackage.Id,
                basePackage.Name,
                basePackage.FactorToBaseUnit,
                basePackage.IsBaseUnit,
                [])],
            NormalizeOptional(request.ActiveIngredient),
            NormalizeOptional(request.Dosage),
            NormalizeOptional(request.PharmaceuticalForm),
            NormalizeOptional(request.Manufacturer));
    }

    private static async Task<ProductDetails> MapDetailsAsync(
        NofarmaDbContext context,
        ProductRecord product,
        CancellationToken cancellationToken)
    {
        ProductPackageRecord[] packages = await context.ProductPackages.AsNoTracking()
            .Where(record => record.ProductId == product.Id)
            .OrderByDescending(record => record.IsBaseUnit)
            .ThenBy(record => record.Name)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        ProductBarcodeRecord[] barcodes = await context.ProductBarcodes.AsNoTracking()
            .Where(record => record.ProductId == product.Id)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        ProductPackageDetails[] details = packages.Select(package => new ProductPackageDetails(
            new EntityId(package.Id),
            package.Name,
            package.FactorToBaseUnit,
            package.IsBaseUnit,
            barcodes.Where(barcode => barcode.PackageId == package.Id)
                .Select(barcode => barcode.Value)
                .Order()
                .ToArray())).ToArray();
        return new ProductDetails(
            new EntityId(product.Id),
            product.Code,
            product.Name,
            (ProductType)product.Type,
            product.BaseUnit,
            product.SalePriceXof,
            product.IndicativePurchasePriceXof,
            product.MinimumStockBase,
            product.RequiresPrescription,
            product.RequiresLot,
            product.RequiresExpiry,
            product.IsActive,
            details,
            product.ActiveIngredient,
            product.Dosage,
            product.PharmaceuticalForm,
            product.Manufacturer);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FoldSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsUniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };
}
