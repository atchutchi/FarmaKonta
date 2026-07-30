using Nofarma.Application.Catalog;
using Nofarma.Application.Inventory;
using Nofarma.Application.Inventory.Import;
using Nofarma.Application.Purchasing;
using Nofarma.Application.Supply;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Desktop;

public sealed class InventoryViewModelTests
{
    [Fact]
    public void ProductsInitialStateIsHonestAndEmpty()
    {
        var viewModel = new ProductsViewModel(new ProductOperations());

        Assert.Empty(viewModel.Products);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ProductSearchKeepsTheLatestResponse()
    {
        var operations = new ProductOperations { DelaySearches = true };
        var viewModel = new ProductsViewModel(operations);

        Task first = viewModel.SearchAsync("para", TestContext.Current.CancellationToken);
        Task second = viewModel.SearchAsync("amox", TestContext.Current.CancellationToken);
        operations.CompleteSearch("amox", [Product("P-002", "Amoxicilina")]);
        await second;
        operations.CompleteSearch("para", [Product("P-001", "Paracetamol")]);
        await first;

        Assert.Equal("Amoxicilina", Assert.Single(viewModel.Products).Name);
    }

    [Fact]
    public async Task ProductCreateShowsFieldErrorsBeforeCallingApplication()
    {
        var operations = new ProductOperations();
        var viewModel = new ProductsViewModel(operations);

        bool created = await viewModel.CreateAsync(
            new ProductEditorInput("", "", null, "", ProductType.Medicine, "-1", "0", "", false, true, true),
            TestContext.Current.CancellationToken);

        Assert.False(created);
        Assert.Contains("Nome", viewModel.ValidationErrors.Keys);
        Assert.Contains("Categoria", viewModel.ValidationErrors.Keys);
        Assert.Contains("Preço de venda", viewModel.ValidationErrors.Keys);
        Assert.Equal(0, operations.CreateCalls);
    }

    [Fact]
    public async Task SupplierCreateIgnoresSecondSubmissionWhileBusy()
    {
        var operations = new SupplierOperations { DelayCreate = true };
        var viewModel = new SuppliersViewModel(operations);
        var input = new SupplierEditorInput("Fornecedor A", null, "+245 955 000 000", null, null, null);

        Task<bool> first = viewModel.CreateAsync(input, TestContext.Current.CancellationToken);
        bool second = await viewModel.CreateAsync(input, TestContext.Current.CancellationToken);
        operations.CompleteCreate();
        bool created = await first;

        Assert.True(created);
        Assert.False(second);
        Assert.Equal(1, operations.CreateCalls);
    }

    [Fact]
    public async Task StockLoadExposesOnlyRealAlertCounts()
    {
        var operations = new StockOperations(new StockOverview(
            [new StockOverviewItem(EntityId.New(), "P-01", "Produto", null, "Sem lote", 0, "Unidade", null, null, StockAlertLevel.OutOfStock, StockAlertLevel.Normal)],
            0, 1, 0));
        var viewModel = new StockViewModel(operations);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, viewModel.OutOfStockProducts);
        Assert.Equal(0, viewModel.LowStockProducts);
        Assert.Single(viewModel.Items);
    }

    [Fact]
    public void PurchaseReceiptRequiresLotExpiryCostAndQuantity()
    {
        var viewModel = new PurchasesViewModel(new PurchaseOperations());

        bool valid = viewModel.ValidateReceipt(new PurchaseReceiptEditorInput(
            EntityId.New(), "REC-1", "", "", "", ""));

        Assert.False(valid);
        Assert.Contains("Lote", viewModel.ValidationErrors.Keys);
        Assert.Contains("Validade", viewModel.ValidationErrors.Keys);
        Assert.Contains("Quantidade", viewModel.ValidationErrors.Keys);
        Assert.Contains("Custo", viewModel.ValidationErrors.Keys);
    }

    [Fact]
    public async Task InventoryImportKeepsMappingsWhenReturningToPreviousStep()
    {
        var operations = new ImportOperations();
        var viewModel = new InventoryImportViewModel(operations);

        await viewModel.PrepareFileAsync("inventario.csv", null, TestContext.Current.CancellationToken);
        viewModel.SetMapping(ImportColumn.CommercialName, "Nome");
        viewModel.SetMapping(ImportColumn.BaseUnit, "Unidade");
        viewModel.SetMapping(ImportColumn.InitialQuantity, "Quantidade");
        await viewModel.CreateDraftAsync(TestContext.Current.CancellationToken);
        viewModel.GoBack();

        Assert.Equal(InventoryImportStep.Mapping, viewModel.Step);
        Assert.Equal("Nome", viewModel.ColumnMappings[ImportColumn.CommercialName]);
        Assert.Equal("Unidade", viewModel.ColumnMappings[ImportColumn.BaseUnit]);
        Assert.Equal("Quantidade", viewModel.ColumnMappings[ImportColumn.InitialQuantity]);
    }

    [Fact]
    public async Task InventoryImportBlockedConfirmationKeepsDraftAndShowsSafeLicenseMessage()
    {
        var operations = new ImportOperations { BlockConfirmation = true };
        var viewModel = new InventoryImportViewModel(operations);
        await viewModel.PrepareFileAsync("inventario.csv", null, TestContext.Current.CancellationToken);
        viewModel.SetMapping(ImportColumn.CommercialName, "Nome");
        viewModel.SetMapping(ImportColumn.BaseUnit, "Unidade");
        viewModel.SetMapping(ImportColumn.InitialQuantity, "Quantidade");
        await viewModel.CreateDraftAsync(TestContext.Current.CancellationToken);
        viewModel.ContinueToConfirmation();

        bool confirmed = await viewModel.ConfirmAsync(true, TestContext.Current.CancellationToken);

        Assert.False(confirmed);
        Assert.NotNull(viewModel.Draft);
        Assert.Contains("licença", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("STOCK_CONFIRMATION_BLOCKED", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InventoryImportCorrectionUpdatesSavedDraftWithoutLeavingValidation()
    {
        var operations = new ImportOperations();
        var viewModel = new InventoryImportViewModel(operations);
        await viewModel.PrepareFileAsync("inventario.csv", null, TestContext.Current.CancellationToken);
        viewModel.SetMapping(ImportColumn.CommercialName, "Nome");
        viewModel.SetMapping(ImportColumn.BaseUnit, "Unidade");
        viewModel.SetMapping(ImportColumn.InitialQuantity, "Quantidade");
        await viewModel.CreateDraftAsync(TestContext.Current.CancellationToken);
        InventoryImportDraftRow row = Assert.Single(viewModel.Draft!.Rows);

        bool corrected = await viewModel.CorrectRowAsync(
            row.Id,
            row.Data with { InitialQuantity = 5 },
            TestContext.Current.CancellationToken);

        Assert.True(corrected);
        Assert.Equal(InventoryImportStep.Validation, viewModel.Step);
        Assert.Equal(5, Assert.Single(viewModel.Draft!.Rows).Data.InitialQuantity);
        Assert.Equal(1, operations.CorrectCalls);
    }

    private static ProductSummary Product(string code, string name) => new(
        EntityId.New(), code, name, ProductType.General, "Unidade", 100, false, false, true, null);

    private sealed class ProductOperations : IProductPageOperations
    {
        private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<ProductSummary>>> _searches = [];
        public bool DelaySearches { get; init; }
        public int CreateCalls { get; private set; }

        public Task<IReadOnlyList<ProductSummary>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            if (!DelaySearches) return Task.FromResult<IReadOnlyList<ProductSummary>>([]);
            var source = new TaskCompletionSource<IReadOnlyList<ProductSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _searches[query] = source;
            return source.Task;
        }

        public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductCategorySummary>>([]);

        public Task CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.CompletedTask;
        }

        public void CompleteSearch(string query, IReadOnlyList<ProductSummary> results) => _searches[query].SetResult(results);
    }

    private sealed class SupplierOperations : ISupplierPageOperations
    {
        private readonly TaskCompletionSource _create = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DelayCreate { get; init; }
        public int CreateCalls { get; private set; }
        public Task<IReadOnlyList<SupplierSummary>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SupplierSummary>>([]);
        public Task CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return DelayCreate ? _create.Task : Task.CompletedTask;
        }

        public void CompleteCreate() => _create.SetResult();
    }

    private sealed class StockOperations(StockOverview overview) : IStockPageOperations
    {
        public Task<StockOverview> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult(overview);
    }

    private sealed class PurchaseOperations : IPurchasesPageOperations
    {
        public Task<IReadOnlyList<PurchaseSummary>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PurchaseSummary>>([]);
        public Task<PurchaseDetails> GetAsync(EntityId purchaseId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ConfirmReceiptAsync(EntityId purchaseId, ConfirmPurchaseReceiptRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ImportOperations : IInventoryImportPageOperations
    {
        public bool BlockConfirmation { get; init; }
        public int CorrectCalls { get; private set; }

        public Task<InventoryImportFilePreview> InspectAsync(string filePath, string? worksheetName, CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryImportFilePreview(filePath, null, [], ["Nome", "Unidade", "Quantidade"]));

        public Task<InventoryImportDraft> CreateDraftAsync(string filePath, string? worksheetName, IReadOnlyDictionary<ImportColumn, string> mappings, CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryImportDraft(EntityId.New(), "inventario.csv", null, InventoryImportStatus.Draft, 1, 1, 0,
                [new InventoryImportDraftRow(EntityId.New(), 2, new InventoryImportNormalizedRow("P-1", null, "Produto", null, null, null, null, "Geral", ProductType.General, "Unidade", "Unidade", 1, 0, 100, 1, 2, null, null, null, null, false), InventoryImportMatchType.NewProduct, null, InventoryImportRowStatus.Valid, [])]));

        public Task<InventoryImportConfirmationResult> ConfirmAsync(EntityId importId, string idempotencyKey, CancellationToken cancellationToken) =>
            BlockConfirmation
                ? Task.FromException<InventoryImportConfirmationResult>(new StockOperationBlockedException("STOCK_CONFIRMATION_BLOCKED"))
                : Task.FromResult(new InventoryImportConfirmationResult(importId, 1, 1, false));

        public Task<InventoryImportDraft> CorrectRowAsync(EntityId importId, EntityId rowId, InventoryImportNormalizedRow correctedData, CancellationToken cancellationToken)
        {
            CorrectCalls++;
            return CreateDraftAsync("inventario.csv", null, new Dictionary<ImportColumn, string>(), cancellationToken)
                .ContinueWith(task => task.Result with
                {
                    Id = importId,
                    Rows = [task.Result.Rows[0] with { Id = rowId, Data = correctedData }]
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
