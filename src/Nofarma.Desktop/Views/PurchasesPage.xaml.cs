using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Application.Purchasing;
using Nofarma.Desktop.ViewModels;

namespace Nofarma.Desktop.Views;

public sealed partial class PurchasesPage : Page
{
    private readonly PurchasesViewModel _viewModel;
    private bool _loaded;

    public PurchasesPage()
    {
        _viewModel = App.Services.GetRequiredService<PurchasesViewModel>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await _viewModel.LoadAsync(CancellationToken.None);
        PurchasesList.ItemsSource = _viewModel.Purchases;
        PurchasesEmptyState.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnPurchaseSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PurchasesList.SelectedItem is not PurchaseSummary purchase) return;
        await _viewModel.SelectAsync(purchase.Id, CancellationToken.None);
        PurchaseLineBox.ItemsSource = _viewModel.SelectedPurchase?.Lines;
        PurchaseLineBox.SelectedIndex = _viewModel.SelectedPurchase?.Lines.Count > 0 ? 0 : -1;
        ValidateForm();
    }

    private void OnReceiptFieldChanged(object sender, object e) => ValidateForm();
    private void OnReceiptDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => ValidateForm();

    private async void OnConfirmReceipt(object sender, RoutedEventArgs e)
    {
        if (PurchasesList.SelectedItem is not PurchaseSummary purchase ||
            !TryInput(out PurchaseReceiptEditorInput? candidate) ||
            candidate is not { } input)
        {
            return;
        }
        ConfirmReceiptButton.IsEnabled = false;
        bool confirmed = await _viewModel.ConfirmReceiptAsync(purchase.Id, input, CancellationToken.None);
        PurchaseMessage.IsOpen = true;
        PurchaseMessage.Severity = confirmed ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        PurchaseMessage.Message = confirmed ? "Mercadoria recebida e stock actualizado." : _viewModel.ErrorMessage ?? "Revê os campos da recepção.";
        PurchasesList.ItemsSource = _viewModel.Purchases;
        ValidateForm();
    }

    private void ValidateForm()
    {
        bool valid = TryInput(out PurchaseReceiptEditorInput? candidate) &&
            candidate is { } input &&
            _viewModel.ValidateReceipt(input);
        ConfirmReceiptButton.IsEnabled = valid && !_viewModel.IsConfirming && _viewModel.SelectedPurchase is not null;
        ReceiptValidationText.Text = string.Join(Environment.NewLine, _viewModel.ValidationErrors.Values);
        ReceiptValidationText.Visibility = !valid && _viewModel.ValidationErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool TryInput(out PurchaseReceiptEditorInput? input)
    {
        if (PurchaseLineBox.SelectedItem is not PurchaseLineDetails line)
        {
            input = null;
            return false;
        }
        string expiry = ReceiptExpiryPicker.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        input = new PurchaseReceiptEditorInput(line.Id, ReceiptDocumentBox.Text, ReceiptLotBox.Text, expiry, ReceiptQuantityBox.Text, ReceiptCostBox.Text);
        return true;
    }
}
