using Nofarma.Application.Sales;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.UnitTests.Desktop;

public sealed class SalesViewModelTests
{
    [Fact]
    public async Task EmptySearchClearsResultsWithoutCallingOperations()
    {
        var operations = new SalesOperations { SearchResults = [Product()] };
        var viewModel = new SalesViewModel(operations);
        await viewModel.SearchAsync("MED", TestContext.Current.CancellationToken);

        await viewModel.SearchAsync("   ", TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.SearchResults);
        Assert.Equal(1, operations.SearchCalls);
    }

    [Fact]
    public async Task AddingTheSamePackageAggregatesQuantityAndRefocusesSearch()
    {
        var operations = new SalesOperations { SearchResults = [Product()] };
        var viewModel = new SalesViewModel(operations);
        await viewModel.SearchAsync("MED-1", TestContext.Current.CancellationToken);
        SaleProductResult product = Assert.Single(viewModel.SearchResults);

        Assert.True(viewModel.AddProduct(product));
        Assert.True(viewModel.AddProduct(product));

        SaleCartLineViewModel line = Assert.Single(viewModel.CartLines);
        Assert.Equal(2, line.QuantityPackages);
        Assert.True(viewModel.ShouldRefocusSearch);
        Assert.Equal("4 000 XOF", viewModel.TotalText);
    }

    [Fact]
    public void ProductWithoutStockDoesNotEnterTheCart()
    {
        var viewModel = new SalesViewModel(new SalesOperations());

        bool added = viewModel.AddProduct(Product(availableQuantityBase: 0));

        Assert.False(added);
        Assert.Empty(viewModel.CartLines);
        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Fact]
    public void InvalidDiscountDoesNotChangeTheLineAndValidDiscountUpdatesTotals()
    {
        var viewModel = new SalesViewModel(new SalesOperations());
        viewModel.AddProduct(Product());
        SaleCartLineViewModel line = Assert.Single(viewModel.CartLines);

        Assert.False(viewModel.SetLineDiscount(line, "3000"));
        Assert.Equal(0, line.DiscountXof);
        Assert.True(viewModel.SetLineDiscount(line, "500"));

        Assert.Equal(500, line.DiscountXof);
        Assert.Equal("1 500 XOF", viewModel.TotalText);
    }

    [Fact]
    public async Task UnderpaymentKeepsPaymentPanelOpenAndShowsFieldError()
    {
        var operations = new SalesOperations();
        var viewModel = new SalesViewModel(operations);
        viewModel.AddProduct(Product());
        viewModel.OpenPayment();

        bool completed = await viewModel.CompleteAsync(
            [new PaymentEntryInput(PaymentMethod.Cash, "1000", null)],
            TestContext.Current.CancellationToken);

        Assert.False(completed);
        Assert.True(viewModel.IsPaymentOpen);
        Assert.Contains("Total pago", viewModel.ValidationErrors.Keys);
        Assert.Equal(0, operations.CompleteCalls);
    }

    [Fact]
    public async Task CashPaymentCalculatesChangeAndMixedPaymentCompletes()
    {
        var operations = new SalesOperations();
        var viewModel = new SalesViewModel(operations);
        viewModel.AddProduct(Product());
        viewModel.OpenPayment();

        bool completed = await viewModel.CompleteAsync(
            [
                new PaymentEntryInput(PaymentMethod.Card, "1000", "POS-7"),
                new PaymentEntryInput(PaymentMethod.Cash, "1500", null)
            ],
            TestContext.Current.CancellationToken);

        Assert.True(completed);
        Assert.Equal("500 XOF", viewModel.ChangeText);
        Assert.Empty(viewModel.CartLines);
        Assert.NotNull(viewModel.LastReceipt);
    }

    [Fact]
    public async Task DoubleSubmissionUsesOneCallAndOneIdempotencyKey()
    {
        var operations = new SalesOperations { DelayCompletion = true };
        var viewModel = new SalesViewModel(operations);
        viewModel.AddProduct(Product());
        viewModel.OpenPayment();
        PaymentEntryInput[] payments = [new(PaymentMethod.Cash, "2000", null)];

        Task<bool> first = viewModel.CompleteAsync(
            payments,
            TestContext.Current.CancellationToken);
        bool second = await viewModel.CompleteAsync(
            payments,
            TestContext.Current.CancellationToken);
        string key = Assert.IsType<CompleteSaleRequest>(operations.LastCompleteRequest).IdempotencyKey;
        operations.CompleteSale(Summary());

        Assert.True(await first);
        Assert.False(second);
        Assert.Equal(1, operations.CompleteCalls);
        Assert.Equal(key, operations.LastCompleteRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task TransientFailureKeepsTheSameKeyForAnUnchangedRetry()
    {
        var operations = new SalesOperations { FailuresRemaining = 1 };
        var viewModel = new SalesViewModel(operations);
        viewModel.AddProduct(Product());
        viewModel.OpenPayment();
        PaymentEntryInput[] payments = [new(PaymentMethod.Cash, "2000", null)];

        Assert.False(await viewModel.CompleteAsync(
            payments,
            TestContext.Current.CancellationToken));
        Assert.True(await viewModel.CompleteAsync(
            payments,
            TestContext.Current.CancellationToken));

        Assert.Equal(2, operations.CompletionKeys.Count);
        Assert.Equal(operations.CompletionKeys[0], operations.CompletionKeys[1]);
    }

    [Fact]
    public async Task SuspendClearsCartAndResumeRestoresCurrentReviewState()
    {
        EntityId suspendedId = EntityId.New();
        var operations = new SalesOperations
        {
            SuspendedSummary = new SuspendedSaleSummary(
                suspendedId,
                "Cliente",
                1,
                2_000,
                Now()),
            Resumed = new ResumedSaleDetails(
                suspendedId,
                "Cliente",
                [new ResumedSaleLineDetails(
                    ProductId,
                    PackageId,
                    "MED-1",
                    "Amoxicilina",
                    "Caixa",
                    1,
                    2,
                    0,
                    2_500,
                    1,
                    true)],
                Now())
        };
        var viewModel = new SalesViewModel(operations);
        viewModel.AddProduct(Product());

        Assert.True(await viewModel.SuspendAsync("Cliente", TestContext.Current.CancellationToken));
        Assert.Empty(viewModel.CartLines);
        Assert.True(await viewModel.ResumeSuspendedAsync(
            suspendedId,
            TestContext.Current.CancellationToken));

        SaleCartLineViewModel line = Assert.Single(viewModel.CartLines);
        Assert.Equal(2_500, line.UnitPriceXof);
        Assert.True(line.RequiresReview);
        Assert.Contains("stock", line.ReviewMessage!, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly EntityId ProductId = EntityId.New();
    private static readonly EntityId PackageId = EntityId.New();

    private static SaleProductResult Product(long availableQuantityBase = 10) => new(
        ProductId,
        PackageId,
        "MED-1",
        "Amoxicilina",
        "Caixa",
        1,
        availableQuantityBase,
        2_000,
        "L-1",
        new DateOnly(2026, 9, 30),
        false);

    private static UtcInstant Now() => UtcInstant.From(
        new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

    private static SaleSummary Summary() => new(
        EntityId.New(),
        "V-20260805-000001",
        2_000,
        2_500,
        500,
        Now(),
        EntityId.New());

    private sealed class SalesOperations : ISalesPageOperations
    {
        private readonly TaskCompletionSource<SaleSummary> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SaleProductResult> SearchResults { get; init; } = [];
        public SuspendedSaleSummary? SuspendedSummary { get; init; }
        public ResumedSaleDetails? Resumed { get; init; }
        public bool DelayCompletion { get; init; }
        public int FailuresRemaining { get; set; }
        public int SearchCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public CompleteSaleRequest? LastCompleteRequest { get; private set; }
        public List<string> CompletionKeys { get; } = [];

        public Task<IReadOnlyList<SaleProductResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult(SearchResults);
        }

        public Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SuspendedSaleSummary>>(
                SuspendedSummary is null ? [] : [SuspendedSummary]);

        public Task<SuspendedSaleSummary> SuspendAsync(
            SuspendSaleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(SuspendedSummary ?? throw new InvalidOperationException());

        public Task<ResumedSaleDetails> ResumeSuspendedAsync(
            EntityId suspendedSaleId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Resumed ?? throw new InvalidOperationException());

        public Task<bool> DeleteSuspendedAsync(
            EntityId suspendedSaleId,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<SaleSummary> CompleteAsync(
            CompleteSaleRequest request,
            CancellationToken cancellationToken)
        {
            CompleteCalls++;
            LastCompleteRequest = request;
            CompletionKeys.Add(request.IdempotencyKey);
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new SaleConcurrencyException(SaleConcurrencyReason.Stock);
            }
            return DelayCompletion ? _completion.Task : Task.FromResult(Summary());
        }

        public Task<ReceiptDetails?> GetReceiptAsync(
            EntityId receiptId,
            CancellationToken cancellationToken) => Task.FromResult<ReceiptDetails?>(new ReceiptDetails(
                receiptId,
                EntityId.New(),
                "V-20260805-000001",
                "Farmácia Central",
                "Maria Caixa",
                Now(),
                [],
                [],
                2_000,
                500,
                "Recibo interno não fiscal"));

        public void CompleteSale(SaleSummary summary) => _completion.SetResult(summary);
    }
}
