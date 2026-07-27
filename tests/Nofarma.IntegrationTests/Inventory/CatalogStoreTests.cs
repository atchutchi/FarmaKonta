using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Catalog;
using Nofarma.Application.Supply;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class CatalogStoreTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-catalog-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ConcurrentGeneratedCodesRemainUniqueAndAudited()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DatabaseFixture fixture = await CreateDatabaseAsync();
        var store = new SqliteCatalogStore(fixture.Options);
        CatalogActorContext context = Assert.IsType<CatalogActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));

        Task<ProductDetails> first = store.CreateAsync(
            context,
            EntityId.New(),
            EntityId.New(),
            Request("Produto A", fixture.CategoryId),
            Audit(context, fixture.UserId, "product.created"),
            cancellationToken);
        Task<ProductDetails> second = store.CreateAsync(
            context,
            EntityId.New(),
            EntityId.New(),
            Request("Produto B", fixture.CategoryId),
            Audit(context, fixture.UserId, "product.created"),
            cancellationToken);

        ProductDetails[] products = await Task.WhenAll(first, second);

        Assert.Equal(["PRD-000001", "PRD-000002"], products.Select(x => x.Code).Order().ToArray());
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(2, await verification.Products.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.ProductPackages.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.AuditEvents.CountAsync(
            record => record.Action == "product.created",
            cancellationToken));
    }

    [Fact]
    public async Task ExplicitDuplicateCodeReturnsStableConflict()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DatabaseFixture fixture = await CreateDatabaseAsync();
        var store = new SqliteCatalogStore(fixture.Options);
        CatalogActorContext context = Assert.IsType<CatalogActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        CreateProductRequest request = Request("Produto", fixture.CategoryId) with { Code = "MED-01" };
        await store.CreateAsync(
            context,
            EntityId.New(),
            EntityId.New(),
            request,
            Audit(context, fixture.UserId, "product.created"),
            cancellationToken);

        await Assert.ThrowsAsync<CatalogConflictException>(() => store.CreateAsync(
            context,
            EntityId.New(),
            EntityId.New(),
            request with { Code = " med-01 " },
            Audit(context, fixture.UserId, "product.created"),
            cancellationToken));
    }

    [Fact]
    public async Task SupplierCreateSearchAndDeactivateAreAudited()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DatabaseFixture fixture = await CreateDatabaseAsync();
        var store = new SqliteSupplierStore(fixture.Options);
        SupplierActorContext context = Assert.IsType<SupplierActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        EntityId supplierId = EntityId.New();
        SupplierDetails created = await store.CreateAsync(
            context,
            supplierId,
            new CreateSupplierRequest(
                "Distribuidora Bissau",
                "500123456",
                "955000000",
                "compras@example.test",
                "Bissau",
                null),
            Audit(context, fixture.UserId, supplierId, "supplier.created"),
            cancellationToken);
        SupplierDetails updated = await store.UpdateAsync(
            context,
            supplierId,
            new UpdateSupplierRequest(
                "Distribuidora Nacional",
                "500123456",
                "966000000",
                "compras@example.test",
                "Bissau",
                null),
            Audit(context, fixture.UserId, supplierId, "supplier.updated"),
            cancellationToken);

        IReadOnlyList<SupplierSummary> found = await store.SearchAsync(
            context.PharmacyId,
            "nacional",
            cancellationToken);
        await store.DeactivateAsync(
            context,
            supplierId,
            Audit(context, fixture.UserId, supplierId, "supplier.deactivated"),
            cancellationToken);

        Assert.Equal(supplierId, created.Id);
        Assert.Equal("Distribuidora Nacional", updated.Name);
        Assert.Equal(supplierId, Assert.Single(found).Id);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.False(await verification.Suppliers
            .Where(record => record.Id == supplierId.Value)
            .Select(record => record.IsActive)
            .SingleAsync(cancellationToken));
        Assert.Equal(3, await verification.AuditEvents.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task PackageBarcodeAndSettingsPersistAtomically()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DatabaseFixture fixture = await CreateDatabaseAsync();
        var store = new SqliteCatalogStore(fixture.Options);
        CatalogActorContext context = Assert.IsType<CatalogActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        ProductDetails product = await store.CreateAsync(
            context,
            EntityId.New(),
            EntityId.New(),
            Request("Produto", fixture.CategoryId),
            Audit(context, fixture.UserId, "product.created"),
            cancellationToken);
        ProductPackageDetails package = await store.AddPackageAsync(
            context,
            product.Id,
            EntityId.New(),
            EntityId.New(),
            new AddProductPackageRequest("Caixa", 12, "5600000000011"),
            Audit(context, fixture.UserId, "product.package_added"),
            cancellationToken);
        ProductDetails updated = await store.UpdateSettingsAsync(
            context,
            product.Id,
            new UpdateProductSettingsRequest(150, 80, 6, true, true),
            Audit(context, fixture.UserId, "product.settings_changed"),
            cancellationToken);

        Assert.Equal("5600000000011", Assert.Single(package.Barcodes));
        Assert.Equal(150, updated.SalePriceXof);
        Assert.Equal(80, updated.IndicativePurchasePriceXof);
        Assert.Equal(6, updated.MinimumStockBase);
        Assert.True(updated.RequiresExpiry);
    }

    [Fact]
    public async Task CategoryAndMedicineMetadataPersistAndCanBeSearched()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DatabaseFixture fixture = await CreateDatabaseAsync();
        var store = new SqliteCatalogStore(fixture.Options);
        CatalogActorContext context = Assert.IsType<CatalogActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        EntityId categoryId = EntityId.New();
        ProductCategorySummary category = await store.CreateCategoryAsync(
            context,
            categoryId,
            "Analgésicos",
            Audit(context, fixture.UserId, categoryId, "product_category.created"),
            cancellationToken);
        CreateProductRequest request = Request("Paracetamol 500", category.Id) with
        {
            Type = ProductType.Medicine,
            BaseUnit = "Comprimido",
            ActiveIngredient = "Paracetamol",
            Dosage = "500 mg",
            PharmaceuticalForm = "Comprimido",
            Manufacturer = "Laboratório Bissau",
            RequiresExpiry = true,
            RequiresLot = true
        };

        ProductDetails created = await store.CreateAsync(
            context,
            EntityId.New(),
            EntityId.New(),
            request,
            Audit(context, fixture.UserId, "product.created"),
            cancellationToken);
        IReadOnlyList<ProductSummary> found = await store.SearchAsync(
            context.PharmacyId,
            "laboratório",
            cancellationToken);
        ProductDetails loaded = Assert.IsType<ProductDetails>(
            await store.GetDetailsAsync(context.PharmacyId, created.Id, cancellationToken));

        Assert.Equal("Analgésicos", category.Name);
        Assert.Equal("Paracetamol", loaded.ActiveIngredient);
        Assert.Equal("500 mg", loaded.Dosage);
        Assert.Equal("Comprimido", loaded.PharmaceuticalForm);
        Assert.Equal("Laboratório Bissau", loaded.Manufacturer);
        Assert.Equal(created.Id, Assert.Single(found).Id);
        Assert.Contains(
            await store.ListCategoriesAsync(context.PharmacyId, cancellationToken),
            item => item.Id == categoryId);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private async Task<DatabaseFixture> CreateDatabaseAsync()
    {
        string path = Path.Combine(_directory, $"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite($"Data Source={path};Default Timeout=15")
            .Options;
        Guid pharmacyId = Guid.NewGuid();
        Guid deviceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        await using var context = new NofarmaDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        context.Pharmacies.Add(new PharmacyRecord
        {
            Id = pharmacyId,
            Name = "Farmácia de teste",
            TaxIdentifier = $"5{Random.Shared.Next(10000000, 99999999)}",
            Address = "Bissau",
            Contact = string.Empty,
            TimeZoneId = "Africa/Bissau"
        });
        context.Devices.Add(new DeviceRecord { Id = deviceId, PharmacyId = pharmacyId, Name = "PC" });
        context.LocalUsers.Add(new LocalUserRecord
        {
            Id = userId,
            PharmacyId = pharmacyId,
            DisplayName = "Administrador",
            LoginName = "admin",
            NormalizedLoginName = "ADMIN",
            Role = (int)UserRole.Administrator,
            CredentialKind = (int)CredentialKind.Password,
            IsPrimaryAdministrator = true,
            Status = (int)UserStatus.Active,
            CreatedAtUtc = now
        });
        context.Installations.Add(new InstallationRecord
        {
            Id = Guid.NewGuid(),
            PharmacyId = pharmacyId,
            DeviceId = deviceId,
            PrimaryAdministratorId = userId,
            Status = (int)InstallationStatus.Active,
            CreatedAtUtc = now
        });
        context.ProductCategories.Add(new ProductCategoryRecord
        {
            Id = categoryId,
            PharmacyId = pharmacyId,
            Name = "Geral",
            NormalizedName = "GERAL",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new DatabaseFixture(
            options,
            new EntityId(userId),
            new EntityId(categoryId));
    }

    private static CreateProductRequest Request(string name, EntityId categoryId) => new(
        null,
        name,
        categoryId,
        "Unidade",
        ProductType.General,
        100,
        50,
        10,
        false,
        false,
        false);

    private static AuditEvent Audit(
        CatalogActorContext context,
        EntityId userId,
        string action) => Audit(context, userId, EntityId.New(), action);

    private static AuditEvent Audit(
        CatalogActorContext context,
        EntityId userId,
        EntityId objectId,
        string action) => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            userId,
            action,
            "Product",
            objectId.Value.ToString("D"),
            UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)),
            AuditOutcome.Success,
            null,
            "{}");

    private static AuditEvent Audit(
        SupplierActorContext context,
        EntityId userId,
        EntityId objectId,
        string action) => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            userId,
            action,
            "Supplier",
            objectId.Value.ToString("D"),
            UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)),
            AuditOutcome.Success,
            null,
            "{}");

    private sealed record DatabaseFixture(
        DbContextOptions<NofarmaDbContext> Options,
        EntityId UserId,
        EntityId CategoryId);
}
