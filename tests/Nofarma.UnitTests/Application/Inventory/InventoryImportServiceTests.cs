using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Application.Inventory.Import;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Application.Inventory;

public sealed class InventoryImportServiceTests
{
    private static readonly UtcInstant Now = UtcInstant.From(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateDraftMatchesBarcodeAndNeverMatchesName()
    {
        EntityId barcodeProduct = EntityId.New();
        var store = new Store([
            new ExistingImportProduct(barcodeProduct, "OTHER", ProductType.General, ["560123"])
        ]);
        InventoryImportService service = CreateService(store, ["P-001", "560123", "Produto existente", "Unidade", "2"]);

        InventoryImportDraft draft = await service.CreateDraftAsync(
            Session(UserRole.StockManager),
            Request(),
            TestContext.Current.CancellationToken);

        InventoryImportDraftRow row = Assert.Single(draft.Rows);
        Assert.Equal(InventoryImportMatchType.Barcode, row.MatchType);
        Assert.Equal(barcodeProduct, row.MatchedProductId);
    }

    [Fact]
    public async Task CreateDraftBlocksConflictingBarcodeAndInternalCode()
    {
        var store = new Store([
            new ExistingImportProduct(EntityId.New(), "OTHER", ProductType.General, ["560123"]),
            new ExistingImportProduct(EntityId.New(), "P-001", ProductType.General, [])
        ]);
        InventoryImportService service = CreateService(store, ["P-001", "560123", "Produto", "Unidade", "1"]);

        InventoryImportDraft draft = await service.CreateDraftAsync(
            Session(UserRole.Administrator), Request(), TestContext.Current.CancellationToken);

        Assert.Contains(Assert.Single(draft.Rows).Errors, error => error.Code == "identifier_conflict");
    }

    [Fact]
    public async Task CreateDraftBlocksMedicineWithoutLotOrExpiry()
    {
        var store = new Store([]);
        InventoryImportService service = CreateService(store, ["P-002", "", "Amoxicilina", "Comprimido", "4", "Medicamento"]);

        InventoryImportDraft draft = await service.CreateDraftAsync(
            Session(UserRole.Administrator),
            Request(includeType: true),
            TestContext.Current.CancellationToken);

        InventoryImportDraftRow row = Assert.Single(draft.Rows);
        Assert.Equal(InventoryImportRowStatus.Blocked, row.Status);
        Assert.Contains(row.Errors, error => error.Code == "medicine_lot_required");
        Assert.Contains(row.Errors, error => error.Code == "medicine_expiry_required");
    }

    [Fact]
    public async Task ConfirmRequiresAdministratorEvenWhenRoleCanPrepareDrafts()
    {
        var store = new Store([]);
        InventoryImportService service = CreateService(store, ["P-001", "", "Produto", "Unidade", "1"]);

        await Assert.ThrowsAsync<AuthorizationException>(() => service.ConfirmAsync(
            Session(UserRole.StockManager),
            EntityId.New(),
            "confirm-1",
            TestContext.Current.CancellationToken));
    }

    private static InventoryImportService CreateService(Store store, IReadOnlyList<string?> cells) => new(
        new Reader(cells),
        store,
        new ErrorWriter(),
        new AuthorizationService(new Clock()),
        new Policy(),
        new Clock());

    private static CreateInventoryImportDraftRequest Request(bool includeType = false)
    {
        var mappings = new Dictionary<ImportColumn, string>
        {
            [ImportColumn.InternalCode] = "codigo",
            [ImportColumn.Barcode] = "barras",
            [ImportColumn.CommercialName] = "nome",
            [ImportColumn.BaseUnit] = "unidade",
            [ImportColumn.InitialQuantity] = "quantidade"
        };
        if (includeType) mappings[ImportColumn.ProductType] = "tipo";
        return new CreateInventoryImportDraftRequest("inventory.csv", null, mappings);
    }

    private static LocalSession Session(UserRole role) => new(EntityId.New(), EntityId.New(), role, Now, Now, null);

    private sealed class Reader(IReadOnlyList<string?> cells) : IInventoryFileReader
    {
        public Task<IReadOnlyList<string>> ListWorksheetsAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<InventoryFileReadResult> ReadAsync(string filePath, string? worksheetName = null, CancellationToken cancellationToken = default)
        {
            string[] headers = cells.Count == 6 ? ["codigo", "barras", "nome", "unidade", "quantidade", "tipo"] : ["codigo", "barras", "nome", "unidade", "quantidade"];
            return Task.FromResult(new InventoryFileReadResult("inventory.csv", new string('A', 64), null, headers, [new InventoryFileRow(2, cells)]));
        }
    }

    private sealed class Store(IReadOnlyList<ExistingImportProduct> products) : IInventoryImportStore
    {
        public Task<InventoryImportStoreContext?> GetContextAsync(EntityId userId, CancellationToken cancellationToken) => Task.FromResult<InventoryImportStoreContext?>(new(EntityId.New(), EntityId.New()));
        public Task<IReadOnlyList<ExistingImportProduct>> FindProductsAsync(EntityId pharmacyId, IReadOnlyCollection<string> barcodes, IReadOnlyCollection<string> internalCodes, CancellationToken cancellationToken) => Task.FromResult(products);
        public Task<InventoryImportDraft> SaveDraftAsync(InventoryImportStoreContext context, EntityId userId, string fileName, string fileHash, string? worksheetName, IReadOnlyList<InventoryImportDraftRow> rows, DateTimeOffset createdAtUtc, CancellationToken cancellationToken) => Task.FromResult(new InventoryImportDraft(EntityId.New(), fileName, worksheetName, InventoryImportStatus.Draft, rows.Count, rows.Count(row => row.Status == InventoryImportRowStatus.Valid), rows.Count(row => row.Status == InventoryImportRowStatus.Blocked), rows));
        public Task<InventoryImportDraft?> GetDraftAsync(EntityId pharmacyId, EntityId importId, CancellationToken cancellationToken) => Task.FromResult<InventoryImportDraft?>(null);
        public Task<InventoryImportDraft> UpdateRowAsync(EntityId pharmacyId, EntityId importId, InventoryImportDraftRow row, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InventoryImportConfirmationResult> ConfirmAsync(InventoryImportStoreContext context, EntityId userId, EntityId importId, string idempotencyKey, DateTimeOffset confirmedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryImportError>> GetErrorsAsync(EntityId pharmacyId, EntityId importId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InventoryImportError>>([]);
    }

    private sealed class ErrorWriter : IInventoryImportErrorWriter { public Task WriteAsync(Stream destination, IReadOnlyList<InventoryImportError> errors, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class Clock : IUtcClock { public UtcInstant GetCurrentInstant() => Now; }
    private sealed class Policy : IStockOperationPolicy { public Task<StockOperationPolicyResult> CanConfirmAsync(CancellationToken cancellationToken) => Task.FromResult(new StockOperationPolicyResult(true, null)); }
}
