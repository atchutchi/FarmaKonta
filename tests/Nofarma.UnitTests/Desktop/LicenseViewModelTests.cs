using Nofarma.Application.Licensing;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;

namespace Nofarma.UnitTests.Desktop;

public sealed class LicenseViewModelTests
{
    [Theory]
    [InlineData(LicenseState.Missing, "Sem licença")]
    [InlineData(LicenseState.Valid, "Licença activa")]
    [InlineData(LicenseState.Grace, "Tolerância")]
    [InlineData(LicenseState.ExpiredReadOnly, "Só consulta")]
    [InlineData(LicenseState.Invalid, "Licença inválida")]
    [InlineData(LicenseState.ClockRollback, "Verificar relógio")]
    [InlineData(LicenseState.NotYetValid, "Ainda não válida")]
    public async Task ShowsHonestLocalizedState(LicenseState state, string expected)
    {
        var operations = new StubLicensePageOperations(Snapshot(state));
        var viewModel = new LicenseViewModel(operations);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, viewModel.StatusText);
    }

    [Fact]
    public async Task ShowsPlanInclusiveDatesGraceAndPublicDeviceIdentifier()
    {
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Valid));
        var viewModel = new LicenseViewModel(operations);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("Mensal", viewModel.PlanText);
        Assert.Equal("01/08/2026", viewModel.ValidFromText);
        Assert.Equal("31/08/2026, inclusive", viewModel.ValidUntilText);
        Assert.Equal("07/09/2026, inclusive", viewModel.GraceUntilText);
        Assert.Equal("SHA256:DEVICE-TEST", viewModel.DeviceIdentifierText);
    }

    [Fact]
    public async Task MissingLicenceDoesNotInventPlanOrDates()
    {
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Missing));
        var viewModel = new LicenseViewModel(operations);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("Não disponível", viewModel.PlanText);
        Assert.Equal("Não disponível", viewModel.ValidFromText);
        Assert.Equal("Não disponível", viewModel.ValidUntilText);
        Assert.Equal("Não disponível", viewModel.GraceUntilText);
    }

    [Fact]
    public async Task LoadResetsBusyAfterTheOperationCompletes()
    {
        var completion = new TaskCompletionSource<LicensePageSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Missing))
        {
            LoadOverride = _ => completion.Task
        };
        var viewModel = new LicenseViewModel(operations);

        Task loading = viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsBusy);
        completion.SetResult(Snapshot(LicenseState.Valid));
        await loading;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task FailedImportPreservesCurrentStatusAndShowsRecoveryMessage()
    {
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Valid))
        {
            ImportException = new LicenseImportException("DEVICE_MISMATCH")
        };
        var viewModel = new LicenseViewModel(operations);
        await viewModel.LoadAsync(CancellationToken.None);

        bool imported = await viewModel.ImportAsync([1, 2, 3], CancellationToken.None);

        Assert.False(imported);
        Assert.Equal("Licença activa", viewModel.StatusText);
        Assert.Contains("outro computador", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SuccessfulImportReloadsTheStatusAndShowsConfirmation()
    {
        var operations = new StubLicensePageOperations(
            Snapshot(LicenseState.Missing),
            Snapshot(LicenseState.Valid));
        var viewModel = new LicenseViewModel(operations);
        await viewModel.LoadAsync(CancellationToken.None);

        bool imported = await viewModel.ImportAsync([1, 2, 3], CancellationToken.None);

        Assert.True(imported);
        Assert.Equal(2, operations.LoadCalls);
        Assert.Equal("Licença activa", viewModel.StatusText);
        Assert.Equal("Licença importada e estado actualizado.", viewModel.SuccessMessage);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.LastImportWasPersisted);
    }

    [Fact]
    public async Task PersistedImportWithFailedReloadReportsInstalledButUnconfirmedState()
    {
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Valid));
        var viewModel = new LicenseViewModel(operations);
        await viewModel.LoadAsync(CancellationToken.None);
        operations.LoadOverride = _ =>
            Task.FromException<LicensePageSnapshot>(new IOException("reload failed"));

        bool imported = await viewModel.ImportAsync([1, 2, 3], CancellationToken.None);

        Assert.False(imported);
        Assert.True(viewModel.LastImportWasPersisted);
        Assert.Equal("Estado por confirmar", viewModel.StatusText);
        Assert.Equal(
            "A licença foi instalada. O estado actualizado ainda não foi confirmado.",
            viewModel.StatusDescription);
        Assert.Equal("Por confirmar", viewModel.PlanText);
        Assert.Equal("Por confirmar", viewModel.ValidUntilText);
        Assert.Equal(
            "A licença foi instalada, mas não foi possível confirmar o novo estado. Volta a abrir esta página para verificar a licença.",
            viewModel.ErrorMessage);
        Assert.Null(viewModel.SuccessMessage);
        Assert.DoesNotContain("mantivemos", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task PersistedImportWithCancelledReloadReportsInstalledButUnconfirmedState()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Valid));
        var viewModel = new LicenseViewModel(operations);
        await viewModel.LoadAsync(CancellationToken.None);
        operations.LoadOverride = token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<LicensePageSnapshot>(token);
        };

        bool imported = await viewModel.ImportAsync([1, 2, 3], cancellation.Token);

        Assert.False(imported);
        Assert.True(viewModel.LastImportWasPersisted);
        Assert.Equal("Estado por confirmar", viewModel.StatusText);
        Assert.Equal(
            "A licença foi instalada. O estado actualizado ainda não foi confirmado.",
            viewModel.StatusDescription);
        Assert.Equal(
            "A licença foi instalada, mas não foi possível confirmar o novo estado. Volta a abrir esta página para verificar a licença.",
            viewModel.ErrorMessage);
        Assert.Null(viewModel.SuccessMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData("PHARMACY_MISMATCH", "outra farmácia")]
    [InlineData("LICENSE_ROLLBACK", "licença mais recente")]
    [InlineData("CHANNEL_MISMATCH", "outro canal")]
    [InlineData("LICENSE_CHANNEL_INVALID", "outro canal")]
    [InlineData("DOCUMENT_INVALID", "ficheiro não é válido")]
    [InlineData("DOCUMENT_TOO_DEEP", "ficheiro não é válido")]
    [InlineData("DOCUMENT_TOO_LARGE", "demasiado grande")]
    [InlineData("LICENSE_CLOCK_CHECKPOINT", "relógio")]
    public async Task ImportCodeHasPlainRecoveryGuidance(string code, string expectedFragment)
    {
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Grace))
        {
            ImportException = new LicenseImportException(code)
        };
        var viewModel = new LicenseViewModel(operations);
        await viewModel.LoadAsync(CancellationToken.None);

        bool imported = await viewModel.ImportAsync([1], CancellationToken.None);

        Assert.False(imported);
        Assert.Contains(expectedFragment, viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportCancellationPropagatesAndResetsBusy()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Valid))
        {
            ImportOverride = token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            }
        };
        var viewModel = new LicenseViewModel(operations);
        await viewModel.LoadAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.ImportAsync([1], cancellation.Token));

        Assert.False(viewModel.IsBusy);
        Assert.Equal("Licença activa", viewModel.StatusText);
        Assert.False(viewModel.LastImportWasPersisted);
    }

    [Fact]
    public async Task RepeatedSubmissionIsIgnoredWhileImportIsBusy()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Missing))
        {
            ImportOverride = _ => completion.Task
        };
        var viewModel = new LicenseViewModel(operations);

        Task<bool> first = viewModel.ImportAsync([1], CancellationToken.None);
        bool second = await viewModel.ImportAsync([2], CancellationToken.None);

        Assert.False(second);
        Assert.Equal(1, operations.ImportCalls);
        completion.SetResult();
        Assert.True(await first);
    }

    [Fact]
    public async Task RequestBytesAreCopiedBeforeReturningToThePage()
    {
        byte[] source = [1, 2, 3];
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Missing))
        {
            RequestBytes = source
        };
        var viewModel = new LicenseViewModel(operations);

        byte[]? request = await viewModel.CreateRequestAsync(CancellationToken.None);
        source[0] = 9;

        Assert.NotNull(request);
        Assert.Equal(new byte[] { 1, 2, 3 }, request);
        request[1] = 8;
        Assert.Equal(2, source[1]);
    }

    [Fact]
    public async Task ImportCopiesCallerBytesBeforeCallingTheService()
    {
        byte[] document = [1, 2, 3];
        var operations = new StubLicensePageOperations(
            Snapshot(LicenseState.Missing),
            Snapshot(LicenseState.Valid));
        var viewModel = new LicenseViewModel(operations);

        Task<bool> importing = viewModel.ImportAsync(document, CancellationToken.None);
        document[0] = 9;

        Assert.True(await importing);
        Assert.Equal(new byte[] { 1, 2, 3 }, operations.ImportedDocument);
    }

    [Fact]
    public async Task QaModeIsAlwaysVisibleInTheViewModel()
    {
        var operations = new StubLicensePageOperations(Snapshot(LicenseState.Missing))
        {
            IsQa = true
        };
        var viewModel = new LicenseViewModel(operations);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsQaMode);
        Assert.Equal("Modo QA", viewModel.ChannelText);
    }

    private static LicensePageSnapshot Snapshot(LicenseState state)
    {
        LicenseGrant? grant = state is LicenseState.Missing or LicenseState.Invalid
            ? null
            : new LicenseGrant(
                new EntityId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
                new EntityId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new EntityId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new EntityId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
                LicensePlan.Monthly,
                1,
                Instant("2026-07-31T09:00:00Z"),
                Instant("2026-08-01T00:00:00Z"),
                Instant("2026-08-31T23:59:59Z"),
                Instant("2026-09-07T23:59:59Z"));
        return new LicensePageSnapshot(
            new LicenseStatus(state, state is LicenseState.Valid or LicenseState.Grace, true, grant),
            "SHA256:DEVICE-TEST");
    }

    private static UtcInstant Instant(string value) =>
        UtcInstant.From(DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

    private sealed class StubLicensePageOperations(params LicensePageSnapshot[] snapshots)
        : ILicensePageOperations
    {
        private readonly Queue<LicensePageSnapshot> _snapshots = new(snapshots);
        private LicensePageSnapshot _last = snapshots[0];

        public bool IsQa { get; set; }
        public int LoadCalls { get; private set; }
        public int ImportCalls { get; private set; }
        public Exception? ImportException { get; set; }
        public byte[] RequestBytes { get; set; } = [4, 5, 6];
        public byte[]? ImportedDocument { get; private set; }
        public Func<CancellationToken, Task<LicensePageSnapshot>>? LoadOverride { get; set; }
        public Func<CancellationToken, Task>? ImportOverride { get; set; }

        public async Task<LicensePageSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            if (LoadOverride is not null)
            {
                return await LoadOverride(cancellationToken);
            }

            if (_snapshots.Count > 0)
            {
                _last = _snapshots.Dequeue();
            }

            return _last;
        }

        public Task<ReadOnlyMemory<byte>> CreateRequestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(RequestBytes);

        public async Task ImportAsync(
            ReadOnlyMemory<byte> document,
            CancellationToken cancellationToken)
        {
            ImportCalls++;
            ImportedDocument = document.ToArray();
            if (ImportOverride is not null)
            {
                await ImportOverride(cancellationToken);
                return;
            }

            if (ImportException is not null)
            {
                throw ImportException;
            }
        }
    }
}
