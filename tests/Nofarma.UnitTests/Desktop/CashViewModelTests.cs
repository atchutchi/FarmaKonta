using Nofarma.Application.Sales;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.UnitTests.Desktop;

public sealed class CashViewModelTests
{
    [Fact]
    public async Task LoadExposesProgressUntilTheCurrentShiftIsKnown()
    {
        var operations = new CashOperations { DelayLoad = true };
        var viewModel = new CashViewModel(operations);

        Task load = viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.IsNoShift);
        operations.CompleteLoad(null);
        await load;
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task LoadWithoutCurrentShiftShowsAnHonestOpeningState()
    {
        var viewModel = new CashViewModel(new CashOperations());

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsNoShift);
        Assert.False(viewModel.HasOpenShift);
        Assert.Null(viewModel.CurrentShift);
        Assert.Contains("não existe um turno aberto", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1,50")]
    [InlineData("texto")]
    public async Task OpeningRejectsAmountsThatAreNotNonNegativeWholeXof(string amount)
    {
        var operations = new CashOperations();
        var viewModel = new CashViewModel(operations);

        bool opened = await viewModel.OpenAsync(amount, TestContext.Current.CancellationToken);

        Assert.False(opened);
        Assert.Contains("Fundo inicial", viewModel.ValidationErrors.Keys);
        Assert.Equal(0, operations.OpenCalls);
    }

    [Fact]
    public async Task OpeningIgnoresASecondSubmissionWhileTheFirstIsRunning()
    {
        var operations = new CashOperations { DelayOpen = true };
        var viewModel = new CashViewModel(operations);

        Task<bool> first = viewModel.OpenAsync("50000", TestContext.Current.CancellationToken);
        bool second = await viewModel.OpenAsync("50000", TestContext.Current.CancellationToken);
        operations.CompleteOpen(Shift(openingCash: 50_000, expectedCash: 50_000));

        Assert.True(await first);
        Assert.False(second);
        Assert.Equal(1, operations.OpenCalls);
    }

    [Theory]
    [InlineData(CashMovementType.ManualEntry, "0", "Reposição")]
    [InlineData(CashMovementType.ManualExit, "-100", "Pagamento")]
    [InlineData(CashMovementType.ManualEntry, "100", "")]
    [InlineData(CashMovementType.ManualExit, "100", "   ")]
    public async Task ManualEntryAndExitRequireAPositiveWholeAmountAndAReason(
        CashMovementType type,
        string amount,
        string reason)
    {
        var operations = new CashOperations();
        var viewModel = new CashViewModel(operations);

        bool recorded = await viewModel.RecordManualMovementAsync(
            type,
            amount,
            reason,
            TestContext.Current.CancellationToken);

        Assert.False(recorded);
        Assert.NotEmpty(viewModel.ValidationErrors);
        Assert.Equal(0, operations.MovementCalls);
    }

    [Fact]
    public async Task CurrentShiftDisplaysOnlyCashTotalsReturnedByTheService()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(
                openingCash: 50_000,
                expectedCash: 183_400,
                totalEntries: 145_750,
                totalExits: 12_350,
                movementCount: 4)
        };
        var viewModel = new CashViewModel(operations);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("50 000 XOF", viewModel.OpeningCashText);
        Assert.Equal("145 750 XOF", viewModel.TotalEntriesText);
        Assert.Equal("12 350 XOF", viewModel.TotalExitsText);
        Assert.Equal("183 400 XOF", viewModel.ExpectedCashText);
        Assert.Equal("4 movimentos registados", viewModel.MovementCountText);
    }

    [Theory]
    [InlineData("183400", 0, CashDifferenceState.Exact, "Sem diferença")]
    [InlineData("180000", -3400, CashDifferenceState.Shortage, "Falta")]
    [InlineData("185000", 1600, CashDifferenceState.Overage, "Excesso")]
    public async Task CountedCashPreviewsTheDifferenceWithTextAndSemanticState(
        string counted,
        long expectedDifference,
        CashDifferenceState expectedState,
        string expectedText)
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 183_400)
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        bool valid = viewModel.PreviewClose(counted);

        Assert.True(valid);
        Assert.Equal(expectedDifference, viewModel.CloseDifferenceXof);
        Assert.Equal(expectedState, viewModel.DifferenceState);
        Assert.Contains(expectedText, viewModel.DifferenceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulCloseClearsTheShiftAndTheClosingPreview()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 51_000)
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.PreviewClose("50500");

        bool closed = await viewModel.CloseAsync("50500", TestContext.Current.CancellationToken);

        Assert.True(closed);
        Assert.True(viewModel.IsNoShift);
        Assert.Null(viewModel.CurrentShift);
        Assert.Null(viewModel.CloseDifferenceXof);
        Assert.Equal(CashDifferenceState.None, viewModel.DifferenceState);
        Assert.Empty(viewModel.ValidationErrors);
    }

    [Fact]
    public async Task ValidationNearOneFormIsNotClearedByValidatingAnotherForm()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 51_000)
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.PreviewClose(string.Empty));
        Assert.True(viewModel.ValidateManualMovement(
            CashMovementType.ManualEntry,
            "100",
            "Reposição"));

        Assert.Contains("Valor contado", viewModel.ValidationErrors.Keys);
    }

    [Fact]
    public void ManualMovementRejectsReasonLongerThanFiveHundredCharacters()
    {
        var viewModel = new CashViewModel(new CashOperations());

        bool valid = viewModel.ValidateManualMovement(
            CashMovementType.ManualEntry,
            "100",
            new string('a', 501));

        Assert.False(valid);
        Assert.Contains("Motivo", viewModel.ValidationErrors.Keys);
    }

    [Fact]
    public async Task ClosingRejectsNegativeCountedCash()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 51_000)
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.PreviewClose("-1"));
        Assert.Contains("Valor contado", viewModel.ValidationErrors.Keys);
    }

    [Fact]
    public async Task ManualMovementIgnoresASecondSubmissionWhileTheFirstIsRunning()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 50_000),
            DelayMovement = true
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Task<bool> first = viewModel.RecordManualMovementAsync(
            CashMovementType.ManualEntry,
            "100",
            "Reforço",
            TestContext.Current.CancellationToken);
        bool second = await viewModel.RecordManualMovementAsync(
            CashMovementType.ManualEntry,
            "100",
            "Reforço",
            TestContext.Current.CancellationToken);
        operations.CompleteMovement(Shift(
            openingCash: 50_000,
            expectedCash: 50_100,
            totalEntries: 100,
            movementCount: 1));

        Assert.True(await first);
        Assert.False(second);
        Assert.Equal(1, operations.MovementCalls);
    }

    [Fact]
    public async Task ClosingIgnoresASecondSubmissionWhileTheFirstIsRunning()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 50_000),
            DelayClose = true
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Task<bool> first = viewModel.CloseAsync("50000", TestContext.Current.CancellationToken);
        bool second = await viewModel.CloseAsync("50000", TestContext.Current.CancellationToken);
        operations.CompleteClose(Shift(
            openingCash: 50_000,
            expectedCash: 50_000,
            status: CashShiftStatus.Closed,
            countedCash: 50_000,
            difference: 0));

        Assert.True(await first);
        Assert.False(second);
        Assert.Equal(1, operations.CloseCalls);
    }

    [Fact]
    public async Task ResetManualMovementFormClearsValidationCreatedByEmptyUiFields()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 50_000)
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(await viewModel.RecordManualMovementAsync(
            CashMovementType.ManualEntry,
            "100",
            "Reforço",
            TestContext.Current.CancellationToken));
        Assert.False(viewModel.ValidateManualMovement(CashMovementType.ManualEntry, string.Empty, string.Empty));

        viewModel.ResetManualMovementForm();

        Assert.DoesNotContain("Valor do movimento", viewModel.ValidationErrors.Keys);
        Assert.DoesNotContain("Motivo", viewModel.ValidationErrors.Keys);
    }

    [Fact]
    public async Task ManualAvailabilityCheckIsPureAndExplicitValidationWritesFieldErrors()
    {
        var operations = new CashOperations
        {
            CurrentShift = Shift(openingCash: 50_000, expectedCash: 50_000)
        };
        var viewModel = new CashViewModel(operations);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        bool canSubmit = viewModel.CanSubmitManualMovement(
            CashMovementType.ManualEntry,
            string.Empty,
            string.Empty);

        Assert.False(canSubmit);
        Assert.Empty(viewModel.ValidationErrors);

        bool valid = viewModel.ValidateManualMovement(
            CashMovementType.ManualEntry,
            string.Empty,
            string.Empty);

        Assert.False(valid);
        Assert.Contains("Valor do movimento", viewModel.ValidationErrors.Keys);
        Assert.Contains("Motivo", viewModel.ValidationErrors.Keys);
    }

    private static CashShiftSummary Shift(
        long openingCash,
        long expectedCash,
        long totalEntries = 0,
        long totalExits = 0,
        int movementCount = 0,
        CashShiftStatus status = CashShiftStatus.Open,
        long? countedCash = null,
        long? difference = null) => new(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            status,
            openingCash,
            totalEntries,
            totalExits,
            expectedCash,
            countedCash,
            difference,
            UtcInstant.From(new DateTimeOffset(2026, 7, 28, 8, 0, 0, TimeSpan.Zero)),
            status == CashShiftStatus.Closed
                ? UtcInstant.From(new DateTimeOffset(2026, 7, 28, 16, 0, 0, TimeSpan.Zero))
                : null,
            movementCount);

    private sealed class CashOperations : ICashPageOperations
    {
        private readonly TaskCompletionSource<CashShiftSummary?> _load =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CashShiftSummary> _open =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CashShiftSummary> _movement =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CashShiftSummary> _close =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CashShiftSummary? CurrentShift { get; init; }
        public bool DelayLoad { get; init; }
        public bool DelayOpen { get; init; }
        public bool DelayMovement { get; init; }
        public bool DelayClose { get; init; }
        public int OpenCalls { get; private set; }
        public int MovementCalls { get; private set; }
        public int CloseCalls { get; private set; }

        public Task<CashShiftSummary?> GetCurrentAsync(CancellationToken cancellationToken) =>
            DelayLoad ? _load.Task : Task.FromResult(CurrentShift);

        public Task<CashShiftSummary> OpenAsync(OpenCashShiftRequest request, CancellationToken cancellationToken)
        {
            OpenCalls++;
            return DelayOpen
                ? _open.Task
                : Task.FromResult(Shift(request.OpeningCashXof, request.OpeningCashXof));
        }

        public Task<CashShiftSummary> RecordManualMovementAsync(
            ManualCashMovementRequest request,
            CancellationToken cancellationToken)
        {
            MovementCalls++;
            long expected = (CurrentShift?.ExpectedCashXof ?? 0) +
                (request.Type == CashMovementType.ManualExit ? -request.AmountXof : request.AmountXof);
            CashShiftSummary result = Shift(
                CurrentShift?.OpeningCashXof ?? 0,
                expected,
                (CurrentShift?.TotalEntriesXof ?? 0) +
                    (request.Type == CashMovementType.ManualEntry ? request.AmountXof : 0),
                (CurrentShift?.TotalExitsXof ?? 0) +
                    (request.Type == CashMovementType.ManualExit ? request.AmountXof : 0),
                (CurrentShift?.MovementCount ?? 0) + 1);
            return DelayMovement ? _movement.Task : Task.FromResult(result);
        }

        public Task<CashShiftSummary> CloseAsync(CloseCashShiftRequest request, CancellationToken cancellationToken)
        {
            CloseCalls++;
            CashShiftSummary result = Shift(
                CurrentShift?.OpeningCashXof ?? 0,
                CurrentShift?.ExpectedCashXof ?? 0,
                CurrentShift?.TotalEntriesXof ?? 0,
                CurrentShift?.TotalExitsXof ?? 0,
                CurrentShift?.MovementCount ?? 0,
                CashShiftStatus.Closed,
                request.CountedCashXof,
                request.CountedCashXof - (CurrentShift?.ExpectedCashXof ?? 0));
            return DelayClose ? _close.Task : Task.FromResult(result);
        }

        public void CompleteLoad(CashShiftSummary? shift) => _load.SetResult(shift);
        public void CompleteOpen(CashShiftSummary shift) => _open.SetResult(shift);
        public void CompleteMovement(CashShiftSummary shift) => _movement.SetResult(shift);
        public void CompleteClose(CashShiftSummary shift) => _close.SetResult(shift);
    }
}
