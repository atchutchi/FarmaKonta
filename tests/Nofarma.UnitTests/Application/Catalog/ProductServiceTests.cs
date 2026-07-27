using Nofarma.Application.Abstractions;
using Nofarma.Application.Catalog;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Application.Catalog;

public sealed class ProductServiceTests
{
    [Fact]
    public async Task CashierCanSearchSaleSafeProductSummary()
    {
        var store = new CatalogStore();
        var service = CreateService(store);

        IReadOnlyList<ProductSummary> products = await service.SearchAsync(
            Session(UserRole.Cashier),
            "para",
            CancellationToken.None);

        ProductSummary product = Assert.Single(products);
        Assert.Equal("Paracetamol", product.Name);
        Assert.Equal(100, product.SalePriceXof);
    }

    [Fact]
    public async Task CashierCannotReadPurchaseCost()
    {
        ProductService service = CreateService(new CatalogStore());

        await Assert.ThrowsAsync<AuthorizationException>(() => service.GetDetailsAsync(
            Session(UserRole.Cashier),
            EntityId.New(),
            CancellationToken.None));
    }

    [Fact]
    public async Task AdministratorCreatesProductWithAuditContext()
    {
        var store = new CatalogStore();
        ProductService service = CreateService(store);
        var request = new CreateProductRequest(
            Code: null,
            Name: "Paracetamol",
            CategoryId: EntityId.New(),
            BaseUnit: "Comprimido",
            Type: ProductType.Medicine,
            SalePriceXof: 100,
            IndicativePurchasePriceXof: 60,
            MinimumStockBase: 10,
            RequiresPrescription: false,
            RequiresLot: true,
            RequiresExpiry: true);

        ProductDetails result = await service.CreateAsync(
            Session(UserRole.Administrator),
            request,
            CancellationToken.None);

        Assert.Equal("PRD-000001", result.Code);
        Assert.Equal("Paracetamol", result.Name);
        Assert.Equal("product.created", store.LastAudit?.Action);
        Assert.Equal(result.Id.Value.ToString("D"), store.LastAudit?.ObjectId);
    }

    [Fact]
    public async Task ProductCreationRejectsSessionMissingFromLocalStore()
    {
        var store = new CatalogStore { Context = null };
        ProductService service = CreateService(store);

        await Assert.ThrowsAsync<AuthorizationException>(() => service.CreateAsync(
            Session(UserRole.Administrator),
            ValidRequest(),
            CancellationToken.None));
    }

    [Fact]
    public async Task AdministratorAddsPackageWithBarcodeAndAudit()
    {
        var store = new CatalogStore();
        ProductService service = CreateService(store);

        ProductPackageDetails package = await service.AddPackageAsync(
            Session(UserRole.Administrator),
            EntityId.New(),
            new AddProductPackageRequest("Caixa", 100, "5600000000011"),
            CancellationToken.None);

        Assert.Equal(100, package.FactorToBaseUnit);
        Assert.Equal("5600000000011", Assert.Single(package.Barcodes));
        Assert.Equal("product.package_added", store.LastAudit?.Action);
    }

    [Fact]
    public async Task AdministratorChangesProductCommercialSettings()
    {
        var store = new CatalogStore();
        ProductService service = CreateService(store);

        ProductDetails product = await service.UpdateSettingsAsync(
            Session(UserRole.Administrator),
            EntityId.New(),
            new UpdateProductSettingsRequest(125, 75, 8, true, true),
            CancellationToken.None);

        Assert.Equal(125, product.SalePriceXof);
        Assert.Equal(75, product.IndicativePurchasePriceXof);
        Assert.Equal(8, product.MinimumStockBase);
        Assert.True(product.RequiresLot);
        Assert.True(product.RequiresExpiry);
    }

    [Fact]
    public async Task AdministratorCreatesCategoryWithAudit()
    {
        var store = new CatalogStore();
        ProductService service = CreateService(store);

        ProductCategorySummary category = await service.CreateCategoryAsync(
            Session(UserRole.Administrator),
            "Analgésicos",
            CancellationToken.None);

        Assert.Equal("Analgésicos", category.Name);
        Assert.Equal("product_category.created", store.LastAudit?.Action);
        Assert.Equal(category.Id.Value.ToString("D"), store.LastAudit?.ObjectId);
    }

    [Fact]
    public async Task CashierCanListActiveCategories()
    {
        ProductService service = CreateService(new CatalogStore());

        IReadOnlyList<ProductCategorySummary> categories = await service.ListCategoriesAsync(
            Session(UserRole.Cashier),
            CancellationToken.None);

        Assert.Equal("Geral", Assert.Single(categories).Name);
    }

    private static ProductService CreateService(ICatalogStore store)
    {
        var clock = new FixedClock();
        return new ProductService(store, new AuthorizationService(clock), clock);
    }

    private static CreateProductRequest ValidRequest() => new(
        null,
        "Produto",
        EntityId.New(),
        "Unidade",
        ProductType.General,
        100,
        50,
        null,
        false,
        false,
        false);

    private static LocalSession Session(UserRole role) => new(
        EntityId.New(),
        EntityId.New(),
        role,
        FixedClock.Now,
        FixedClock.Now,
        null);

    private sealed class CatalogStore : ICatalogStore
    {
        public CatalogActorContext? Context { get; set; } = new(
            EntityId.New(),
            EntityId.New());

        public AuditEvent? LastAudit { get; private set; }

        public Task<CatalogActorContext?> GetContextAsync(
            EntityId actorUserId,
            CancellationToken cancellationToken) => Task.FromResult(Context);

        public Task<IReadOnlyList<ProductSummary>> SearchAsync(
            EntityId pharmacyId,
            string query,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductSummary>>(
            [new(
                EntityId.New(),
                "MED-001",
                "Paracetamol",
                ProductType.Medicine,
                "Comprimido",
                100,
                true,
                true,
                true,
                10)]);

        public Task<ProductDetails?> GetDetailsAsync(
            EntityId pharmacyId,
            EntityId productId,
            CancellationToken cancellationToken) => Task.FromResult<ProductDetails?>(null);

        public Task<ProductDetails> CreateAsync(
            CatalogActorContext context,
            EntityId productId,
            EntityId basePackageId,
            CreateProductRequest request,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(new ProductDetails(
                productId,
                "PRD-000001",
                request.Name,
                request.Type,
                request.BaseUnit,
                request.SalePriceXof,
                request.IndicativePurchasePriceXof,
                request.MinimumStockBase,
                request.RequiresPrescription,
                request.RequiresLot,
                request.RequiresExpiry,
                true,
                []));
        }

        public Task DeactivateAsync(
            CatalogActorContext context,
            EntityId productId,
            AuditEvent auditEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProductPackageDetails> AddPackageAsync(
            CatalogActorContext context,
            EntityId productId,
            EntityId packageId,
            EntityId? barcodeId,
            AddProductPackageRequest request,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(new ProductPackageDetails(
                packageId,
                request.Name,
                request.FactorToBaseUnit,
                false,
                request.Barcode is null ? [] : [request.Barcode]));
        }

        public Task<ProductDetails> UpdateSettingsAsync(
            CatalogActorContext context,
            EntityId productId,
            UpdateProductSettingsRequest request,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(new ProductDetails(
                productId,
                "GEN-001",
                "Produto",
                ProductType.General,
                "Unidade",
                request.SalePriceXof,
                request.IndicativePurchasePriceXof,
                request.MinimumStockBase,
                false,
                request.RequiresLot,
                request.RequiresExpiry,
                true,
                []));
        }

        public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(
            EntityId pharmacyId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductCategorySummary>>(
                [new(EntityId.New(), "Geral", true)]);

        public Task<ProductCategorySummary> CreateCategoryAsync(
            CatalogActorContext context,
            EntityId categoryId,
            string name,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(new ProductCategorySummary(categoryId, name, true));
        }
    }

    private sealed class FixedClock : IUtcClock
    {
        public static readonly UtcInstant Now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        public UtcInstant GetCurrentInstant() => Now;
    }
}
