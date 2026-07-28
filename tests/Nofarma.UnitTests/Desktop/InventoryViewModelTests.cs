using Nofarma.Application.Catalog;
using Nofarma.Application.Supply;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

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
}
