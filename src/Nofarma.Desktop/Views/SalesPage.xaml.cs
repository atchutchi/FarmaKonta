using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nofarma.Application.Sales;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Sales;

namespace Nofarma.Desktop.Views;

public sealed partial class SalesPage : Page
{
    private readonly SalesViewModel _viewModel;
    private readonly List<PaymentRow> _paymentRows = [];
    private SaleProductResult? _selectedProduct;

    public SalesPage()
    {
        _viewModel = App.Services.GetRequiredService<SalesViewModel>();
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshSurface();
        ProductSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args) =>
        await SearchAsync(args.QueryText, addUniqueResult: true);

    private async void OnSearchClick(object sender, RoutedEventArgs e) =>
        await SearchAsync(ProductSearchBox.Text, addUniqueResult: false);

    private async Task SearchAsync(string query, bool addUniqueResult)
    {
        SearchProgress.IsActive = true;
        SearchProgress.Visibility = Visibility.Visible;
        await _viewModel.SearchAsync(query, CancellationToken.None);
        SearchProgress.IsActive = false;
        SearchProgress.Visibility = Visibility.Collapsed;

        if (addUniqueResult && _viewModel.SearchResults.Count == 1)
        {
            _ = _viewModel.AddProduct(_viewModel.SearchResults[0]);
            ProductSearchBox.Text = string.Empty;
        }

        RefreshSurface();
        RefocusSearchWhenRequested();
    }

    private void OnProductClicked(object sender, ItemClickEventArgs e)
    {
        _selectedProduct = e.ClickedItem as SaleProductResult;
        RefreshProductDetail();
    }

    private void OnAddSelectedProduct(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct is not null)
        {
            _ = _viewModel.AddProduct(_selectedProduct);
        }
        RefreshSurface();
        RefocusSearchWhenRequested();
    }

    private void OnCartLineControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control &&
            control.DataContext is SaleCartLineViewModel line &&
            control.Tag is string action)
        {
            AutomationProperties.SetName(control, $"{action} {line.Name}");
        }
    }

    private void OnRemoveCartLine(object sender, RoutedEventArgs e)
    {
        if (LineFrom(sender) is { } line)
        {
            _ = _viewModel.RemoveLine(line);
            RefreshSurface();
        }
    }

    private void OnDecreaseQuantity(object sender, RoutedEventArgs e)
    {
        if (LineFrom(sender) is { } line)
        {
            if (line.QuantityPackages == 1)
            {
                _ = _viewModel.RemoveLine(line);
            }
            else
            {
                _ = _viewModel.SetQuantity(line, line.QuantityPackages - 1);
            }
            RefreshSurface();
        }
    }

    private void OnIncreaseQuantity(object sender, RoutedEventArgs e)
    {
        if (LineFrom(sender) is { } line)
        {
            _ = _viewModel.SetQuantity(line, line.QuantityPackages + 1);
            RefreshSurface();
        }
    }

    private void OnLineDiscountLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.DataContext is SaleCartLineViewModel line)
        {
            _ = _viewModel.SetLineDiscount(line, box.Text);
            RefreshSurface();
        }
    }

    private void OnTotalDiscountLostFocus(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.SetTotalDiscount(TotalDiscountBox.Text);
        RefreshSurface();
    }

    private void OnOpenPayment(object sender, RoutedEventArgs e) => OpenPayment();

    private void OpenPayment()
    {
        if (_viewModel.CartLines.Count == 0)
        {
            ShowMessage("Adiciona pelo menos um produto antes de abrir o pagamento.", InfoBarSeverity.Warning);
            return;
        }

        _viewModel.OpenPayment();
        _paymentRows.Clear();
        PaymentRows.Children.Clear();
        AddPaymentRow(PaymentMethod.Cash, _viewModel.TotalXofValue.ToString(CultureInfo.InvariantCulture));
        ShowOnlyPanel(PaymentPanel);
        UpdatePaymentSummary();
    }

    private void OnAddPaymentMethod(object sender, RoutedEventArgs e)
    {
        AddPaymentRow(PaymentMethod.Card, string.Empty);
        UpdatePaymentSummary();
    }

    private void AddPaymentRow(PaymentMethod method, string amount)
    {
        var root = new Grid { ColumnSpacing = 8 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        ComboBox methodBox = CreatePaymentMethodBox(method);
        var amountBox = new TextBox
        {
            MinHeight = 44,
            PlaceholderText = "Valor XOF",
            Text = amount,
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }
        };
        AutomationProperties.SetName(amountBox, "Valor do pagamento em XOF");
        amountBox.TextChanged += OnPaymentValueChanged;

        var referenceBox = new TextBox { MinHeight = 44, PlaceholderText = "Referência" };
        AutomationProperties.SetName(referenceBox, "Referência do pagamento");

        Grid.SetColumn(amountBox, 1);
        Grid.SetColumn(referenceBox, 2);
        root.Children.Add(methodBox);
        root.Children.Add(amountBox);
        root.Children.Add(referenceBox);
        PaymentRows.Children.Add(root);
        _paymentRows.Add(new PaymentRow(root, methodBox, amountBox, referenceBox));
    }

    private static ComboBox CreatePaymentMethodBox(PaymentMethod selected)
    {
        var box = new ComboBox { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(box, "Método de pagamento");
        box.Items.Add(new ComboBoxItem { Content = "Dinheiro", Tag = PaymentMethod.Cash });
        box.Items.Add(new ComboBoxItem { Content = "Cartão", Tag = PaymentMethod.Card });
        box.Items.Add(new ComboBoxItem { Content = "Dinheiro móvel", Tag = PaymentMethod.MobileMoney });
        box.Items.Add(new ComboBoxItem { Content = "Transferência", Tag = PaymentMethod.BankTransfer });
        box.SelectedIndex = selected == PaymentMethod.Cash ? 0 : 1;
        return box;
    }

    private void OnPaymentValueChanged(object sender, TextChangedEventArgs e) => UpdatePaymentSummary();

    private async void OnCompleteSale(object sender, RoutedEventArgs e)
    {
        IReadOnlyCollection<PaymentEntryInput> payments = _paymentRows.Select(row =>
            new PaymentEntryInput(
                (PaymentMethod)((ComboBoxItem)row.Method.SelectedItem).Tag,
                row.Amount.Text,
                row.Reference.Text)).ToArray();

        SubmitPaymentButton.IsEnabled = false;
        bool completed = await _viewModel.CompleteAsync(payments, CancellationToken.None);
        RefreshSurface();
        if (completed)
        {
            HidePanels();
            ProductSearchBox.Focus(FocusState.Programmatic);
        }
        else
        {
            UpdatePaymentSummary();
        }
    }

    private void OnOpenSuspend(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CartLines.Count == 0)
        {
            ShowMessage("Adiciona pelo menos um produto antes de suspender a venda.", InfoBarSeverity.Warning);
            return;
        }
        ShowOnlyPanel(SuspendPanel);
        SuspendedNameBox.Focus(FocusState.Programmatic);
    }

    private async void OnSuspendSale(object sender, RoutedEventArgs e)
    {
        bool suspended = await _viewModel.SuspendAsync(SuspendedNameBox.Text, CancellationToken.None);
        RefreshSurface();
        if (suspended)
        {
            SuspendedNameBox.Text = string.Empty;
            HidePanels();
            ProductSearchBox.Focus(FocusState.Programmatic);
        }
    }

    private async void OnOpenSuspendedSales(object sender, RoutedEventArgs e) =>
        await OpenSuspendedSalesAsync();

    private async Task OpenSuspendedSalesAsync()
    {
        await _viewModel.LoadSuspendedAsync(CancellationToken.None);
        SuspendedSalesList.ItemsSource = _viewModel.SuspendedSales;
        ShowOnlyPanel(SuspendedSalesPanel);
        RefreshMessage();
    }

    private async void OnResumeSuspendedSale(object sender, RoutedEventArgs e)
    {
        if (SuspendedSalesList.SelectedItem is not SuspendedSaleSummary selected)
        {
            ShowMessage("Selecciona a venda suspensa que pretendes retomar.", InfoBarSeverity.Warning);
            return;
        }
        bool resumed = await _viewModel.ResumeSuspendedAsync(selected.Id, CancellationToken.None);
        RefreshSurface();
        if (resumed)
        {
            HidePanels();
            ProductSearchBox.Focus(FocusState.Programmatic);
        }
    }

    private async void OnDeleteSuspendedSale(object sender, RoutedEventArgs e)
    {
        if (SuspendedSalesList.SelectedItem is not SuspendedSaleSummary selected)
        {
            ShowMessage("Selecciona a venda suspensa que pretendes eliminar.", InfoBarSeverity.Warning);
            return;
        }

        var confirmation = new ContentDialog
        {
            Title = "Eliminar venda suspensa?",
            Content = "Esta acção remove apenas o rascunho local. O stock e o caixa não são alterados.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = await _viewModel.DeleteSuspendedAsync(selected.Id, CancellationToken.None);
        SuspendedSalesList.ItemsSource = null;
        SuspendedSalesList.ItemsSource = _viewModel.SuspendedSales;
        RefreshMessage();
    }

    private void OnClosePanel(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseActivePanel();
        HidePanels();
        ProductSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        HidePanels();
        ProductSearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void OnSuspendAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OnOpenSuspend(sender, new RoutedEventArgs());
        args.Handled = true;
    }

    private async void OnSuspendedSalesAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        await OpenSuspendedSalesAsync();
        args.Handled = true;
    }

    private void OnPaymentAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenPayment();
        args.Handled = true;
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _viewModel.CloseActivePanel();
        HidePanels();
        ProductSearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void RefreshSurface()
    {
        SearchResultsList.ItemsSource = null;
        SearchResultsList.ItemsSource = _viewModel.SearchResults;
        CartList.ItemsSource = null;
        CartList.ItemsSource = _viewModel.CartLines;
        SearchEmptyState.Visibility = _viewModel.SearchResults.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CartEmptyState.Visibility = _viewModel.CartLines.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CartCountText.Text = _viewModel.CartLines.Count == 1
            ? "1 linha"
            : $"{_viewModel.CartLines.Count} linhas";
        SubtotalText.Text = _viewModel.SubtotalText;
        TotalText.Text = _viewModel.TotalText;
        CompleteSaleButton.IsEnabled = !_viewModel.IsSubmitting && _viewModel.CartLines.Count > 0;
        SubmitPaymentButton.IsEnabled = !_viewModel.IsSubmitting && _viewModel.CartLines.Count > 0;
        RefreshMessage();
        RefreshProductDetail();
    }

    private void RefreshProductDetail()
    {
        AddSelectedProductButton.IsEnabled = _selectedProduct is not null;
        if (_selectedProduct is null)
        {
            SelectedProductName.Text = "Selecciona um produto";
            SelectedProductCode.Text = "O detalhe do lote FEFO aparece aqui.";
            SelectedProductStock.Text = "Stock vendável: indisponível";
            SelectedProductLot.Text = "Primeiro lote: indisponível";
            return;
        }

        SelectedProductName.Text = _selectedProduct.Name;
        SelectedProductCode.Text = $"{_selectedProduct.Code} · {_selectedProduct.PackageName}";
        SelectedProductStock.Text = $"Stock vendável: {_selectedProduct.AvailableQuantityBase}";
        SelectedProductLot.Text = _selectedProduct.EarliestLotNumber is null
            ? "Primeiro lote: sem lote identificado"
            : $"Primeiro lote: {_selectedProduct.EarliestLotNumber}";
    }

    private void RefreshMessage()
    {
        if (!string.IsNullOrWhiteSpace(_viewModel.ErrorMessage))
        {
            ShowMessage(FriendlyError(_viewModel.ErrorMessage), InfoBarSeverity.Error);
            return;
        }
        SalesMessage.IsOpen = false;
    }

    private void UpdatePaymentSummary()
    {
        long paid = _paymentRows.Sum(row =>
            long.TryParse(row.Amount.Text, NumberStyles.None, CultureInfo.InvariantCulture, out long amount)
                ? amount
                : 0);
        long balance = paid - _viewModel.TotalXofValue;
        PaymentTotalText.Text = _viewModel.TotalText;
        PaymentPaidText.Text = FormatXof(paid);
        PaymentBalanceText.Text = balance >= 0
            ? $"Troco {FormatXof(balance)}"
            : $"Falta {FormatXof(-balance)}";
        PaymentValidationText.Text = string.Join(Environment.NewLine, _viewModel.ValidationErrors.Values);
        PaymentValidationText.Visibility = _viewModel.ValidationErrors.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SubmitPaymentButton.IsEnabled = !_viewModel.IsSubmitting && _viewModel.CartLines.Count > 0;
    }

    private void ShowOnlyPanel(FrameworkElement panel)
    {
        PanelScrim.Visibility = Visibility.Visible;
        PaymentPanel.Visibility = panel == PaymentPanel ? Visibility.Visible : Visibility.Collapsed;
        SuspendPanel.Visibility = panel == SuspendPanel ? Visibility.Visible : Visibility.Collapsed;
        SuspendedSalesPanel.Visibility = panel == SuspendedSalesPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HidePanels()
    {
        PanelScrim.Visibility = Visibility.Collapsed;
        PaymentPanel.Visibility = Visibility.Collapsed;
        SuspendPanel.Visibility = Visibility.Collapsed;
        SuspendedSalesPanel.Visibility = Visibility.Collapsed;
    }

    private void RefocusSearchWhenRequested()
    {
        if (!_viewModel.ShouldRefocusSearch)
        {
            return;
        }
        _viewModel.AcknowledgeSearchRefocus();
        ProductSearchBox.Focus(FocusState.Programmatic);
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        SalesMessage.Message = message;
        SalesMessage.Severity = severity;
        SalesMessage.IsOpen = true;
    }

    private static string FriendlyError(string message)
    {
        if (message.Contains("turno", StringComparison.OrdinalIgnoreCase))
        {
            return "Turno fechado. A venda não pode ser concluída. Abre um turno no módulo Caixa.";
        }
        if (message.Contains("stock", StringComparison.OrdinalIgnoreCase))
        {
            return "Stock alterado. O carrinho foi mantido. Revê as linhas assinaladas.";
        }
        if (message.Contains("licen", StringComparison.OrdinalIgnoreCase))
        {
            return "A licença não permite novas operações. O carrinho foi mantido. Consulta a página Licença.";
        }
        return message;
    }

    private static SaleCartLineViewModel? LineFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as SaleCartLineViewModel;

    private static string FormatXof(long amount) => $"{amount:N0} XOF";

    private sealed record PaymentRow(
        Grid Root,
        ComboBox Method,
        TextBox Amount,
        TextBox Reference);
}
