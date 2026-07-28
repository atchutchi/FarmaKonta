using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Sales;

namespace Nofarma.Desktop.Views;

public sealed partial class CashPage : Page
{
    private readonly CashViewModel _viewModel;
    private bool _loaded;

    public CashPage()
    {
        _viewModel = App.Services.GetRequiredService<CashViewModel>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        Render();
        await _viewModel.LoadAsync(CancellationToken.None);
        Render();
        if (_viewModel.IsNoShift)
        {
            OpeningCashBox.Focus(FocusState.Programmatic);
        }
    }

    private async void OnRetryLoad(object sender, RoutedEventArgs e)
    {
        RenderBusy();
        await _viewModel.LoadAsync(CancellationToken.None);
        Render();
    }

    private void OnOpeningCashChanged(object sender, TextChangedEventArgs e) =>
        OpenShiftButton.IsEnabled = IsOpeningInputValid();

    private void OnOpeningCashLostFocus(object sender, RoutedEventArgs e)
    {
        _ = IsOpeningInputValid();
        RenderValidation();
    }

    private async void OnOpenShift(object sender, RoutedEventArgs e) => await OpenShiftAsync();

    private async void OnOpenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_viewModel.IsNoShift || _viewModel.IsSubmitting)
        {
            return;
        }

        args.Handled = true;
        await OpenShiftAsync();
    }

    private async Task OpenShiftAsync()
    {
        SetActionsEnabled(false);
        bool opened = await _viewModel.OpenAsync(OpeningCashBox.Text, CancellationToken.None);
        if (opened)
        {
            OpeningCashBox.Text = string.Empty;
            ShowMessage(InfoBarSeverity.Success, "Turno aberto", "O turno está aberto neste posto de caixa.");
        }
        else
        {
            ShowFailureIfNeeded("Revê o fundo inicial.");
        }
        Render();
    }

    private void OnMovementFieldChanged(object sender, TextChangedEventArgs e) => UpdateMovementActions();

    private void OnMovementFieldLostFocus(object sender, RoutedEventArgs e)
    {
        _ = IsMovementInputValid();
        RenderValidation();
    }

    private async void OnRecordEntry(object sender, RoutedEventArgs e) =>
        await RecordMovementAsync(CashMovementType.ManualEntry);

    private async void OnRecordExit(object sender, RoutedEventArgs e) =>
        await RecordMovementAsync(CashMovementType.ManualExit);

    private async Task RecordMovementAsync(CashMovementType type)
    {
        SetActionsEnabled(false);
        bool recorded = await _viewModel.RecordManualMovementAsync(
            type,
            MovementAmountBox.Text,
            MovementReasonBox.Text,
            CancellationToken.None);
        if (recorded)
        {
            MovementAmountBox.Text = string.Empty;
            MovementReasonBox.Text = string.Empty;
            string title = type == CashMovementType.ManualEntry ? "Entrada registada" : "Saída registada";
            ShowMessage(InfoBarSeverity.Success, title, _viewModel.StatusMessage);
        }
        else
        {
            ShowFailureIfNeeded("Revê o valor e o motivo do movimento.");
        }
        Render();
    }

    private void OnCountedCashChanged(object sender, TextChangedEventArgs e)
    {
        _ = _viewModel.PreviewClose(CountedCashBox.Text);
        RenderDifference();
        CloseShiftButton.IsEnabled =
            _viewModel.CloseDifferenceXof is not null && !_viewModel.IsSubmitting;
    }

    private void OnCountedCashLostFocus(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.PreviewClose(CountedCashBox.Text);
        RenderValidation();
        RenderDifference();
    }

    private async void OnCloseShift(object sender, RoutedEventArgs e) => await CloseShiftAsync();

    private async void OnCloseAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_viewModel.HasOpenShift || _viewModel.IsSubmitting)
        {
            return;
        }

        args.Handled = true;
        await CloseShiftAsync();
    }

    private async Task CloseShiftAsync()
    {
        SetActionsEnabled(false);
        bool closed = await _viewModel.CloseAsync(CountedCashBox.Text, CancellationToken.None);
        if (closed)
        {
            CountedCashBox.Text = string.Empty;
            ShowMessage(InfoBarSeverity.Success, "Turno fechado", "O turno foi fechado e o caixa está pronto para nova abertura.");
        }
        else
        {
            ShowFailureIfNeeded("Revê o valor contado.");
        }
        Render();
    }

    private void Render()
    {
        LoadingPanel.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        NoShiftPanel.Visibility = _viewModel.IsNoShift ? Visibility.Visible : Visibility.Collapsed;
        OpenShiftPanel.Visibility = _viewModel.HasOpenShift ? Visibility.Visible : Visibility.Collapsed;
        LoadErrorPanel.Visibility = !_viewModel.IsLoading && !_viewModel.IsNoShift && !_viewModel.HasOpenShift
            ? Visibility.Visible
            : Visibility.Collapsed;

        OpeningCashText.Text = _viewModel.OpeningCashText;
        ExpectedCashText.Text = _viewModel.ExpectedCashText;
        CloseExpectedCashText.Text = _viewModel.ExpectedCashText;
        OpenedAtText.Text = _viewModel.OpenedAtText;
        MovementCountText.Text = _viewModel.MovementCountText;
        RenderValidation();
        RenderDifference();
        SetActionsEnabled(!_viewModel.IsLoading && !_viewModel.IsSubmitting);

        if (_viewModel.ErrorMessage is not null)
        {
            ShowMessage(InfoBarSeverity.Error, "Operação não concluída", _viewModel.ErrorMessage);
        }
    }

    private void RenderBusy()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        NoShiftPanel.Visibility = Visibility.Collapsed;
        OpenShiftPanel.Visibility = Visibility.Collapsed;
        LoadErrorPanel.Visibility = Visibility.Collapsed;
        SetActionsEnabled(false);
    }

    private void RenderValidation()
    {
        SetValidation(OpeningCashError, "Fundo inicial");
        SetValidation(MovementAmountError, "Valor do movimento");
        SetValidation(MovementReasonError, "Motivo");
        SetValidation(CountedCashError, "Valor contado");
    }

    private void RenderDifference()
    {
        DifferenceText.Text = _viewModel.DifferenceText;
        DifferencePanel.Background = _viewModel.DifferenceState switch
        {
            CashDifferenceState.Exact => Brush("NofarmaSuccessSoftBrush"),
            CashDifferenceState.Shortage => Brush("NofarmaDangerSoftBrush"),
            CashDifferenceState.Overage => Brush("NofarmaWarningSoftBrush"),
            _ => Brush("NofarmaCanvasBrush")
        };
        DifferenceText.Foreground = _viewModel.DifferenceState switch
        {
            CashDifferenceState.Exact => Brush("NofarmaGreenBrush"),
            CashDifferenceState.Shortage => Brush("NofarmaDangerBrush"),
            CashDifferenceState.Overage => Brush("NofarmaWarningBrush"),
            _ => Brush("NofarmaMutedBrush")
        };
    }

    private void SetValidation(TextBlock target, string key)
    {
        bool hasError = _viewModel.ValidationErrors.TryGetValue(key, out string? error);
        target.Text = error ?? string.Empty;
        target.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsOpeningInputValid() =>
        _viewModel.IsNoShift &&
        !_viewModel.IsSubmitting &&
        _viewModel.ValidateOpeningAmount(OpeningCashBox.Text);

    private bool IsMovementInputValid() =>
        _viewModel.HasOpenShift &&
        !_viewModel.IsSubmitting &&
        _viewModel.ValidateManualMovement(
            CashMovementType.ManualEntry,
            MovementAmountBox.Text,
            MovementReasonBox.Text);

    private void UpdateMovementActions()
    {
        bool valid = IsMovementInputValid();
        RecordEntryButton.IsEnabled = valid;
        RecordExitButton.IsEnabled = valid;
    }

    private void SetActionsEnabled(bool enabled)
    {
        OpenShiftButton.IsEnabled = enabled && IsOpeningInputValid();
        bool movementEnabled = enabled && IsMovementInputValid();
        RecordEntryButton.IsEnabled = movementEnabled;
        RecordExitButton.IsEnabled = movementEnabled;
        CloseShiftButton.IsEnabled = enabled && _viewModel.CloseDifferenceXof is not null;
    }

    private void ShowFailureIfNeeded(string validationMessage)
    {
        string message = _viewModel.ErrorMessage ?? validationMessage;
        ShowMessage(InfoBarSeverity.Error, "Operação não concluída", message);
    }

    private void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        CashMessage.Severity = severity;
        CashMessage.Title = title;
        CashMessage.Message = message;
        CashMessage.IsOpen = true;
    }

    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources[key];
}
