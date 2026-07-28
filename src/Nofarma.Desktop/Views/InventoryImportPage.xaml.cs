using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nofarma.Application.Inventory.Import;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Catalog;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Nofarma.Desktop.Views;

public sealed partial class InventoryImportPage : Page
{
    private readonly InventoryImportViewModel _viewModel;
    private bool _refreshing;

    public InventoryImportPage()
    {
        _viewModel = App.Services.GetRequiredService<InventoryImportViewModel>();
        InitializeComponent();
        RefreshSurface();
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".csv");
        nint windowHandle = WindowNative.GetWindowHandle(App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, windowHandle);
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        FilePathBox.Text = file.Path;
        await PrepareAsync(null);
    }

    private async void OnPrepareFile(object sender, RoutedEventArgs e) => await PrepareAsync(null);

    private async Task PrepareAsync(string? worksheet)
    {
        SetBusy(true);
        await _viewModel.PrepareFileAsync(FilePathBox.Text, worksheet, CancellationToken.None);
        SetBusy(false);
        RefreshSurface();
    }

    private async void OnWorksheetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || WorksheetBox.SelectedItem is not string worksheet || worksheet == _viewModel.SelectedWorksheet) return;
        await PrepareAsync(worksheet);
    }

    private void OnMappingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _viewModel.SetMapping(ImportColumn.CommercialName, NameColumnBox.SelectedItem as string);
        _viewModel.SetMapping(ImportColumn.BaseUnit, UnitColumnBox.SelectedItem as string);
        _viewModel.SetMapping(ImportColumn.InitialQuantity, QuantityColumnBox.SelectedItem as string);
    }

    private async void OnCreateDraft(object sender, RoutedEventArgs e)
    {
        SaveMappings();
        SetBusy(true);
        await _viewModel.CreateDraftAsync(CancellationToken.None);
        SetBusy(false);
        RefreshSurface();
    }

    private void OnContinueToConfirmation(object sender, RoutedEventArgs e)
    {
        _viewModel.ContinueToConfirmation();
        RefreshSurface();
    }

    private void OnImportRowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ImportRowsList.SelectedItem is not InventoryImportDraftRow row)
        {
            CorrectionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        CorrectionPanel.Visibility = Visibility.Visible;
        CorrectionErrorsText.Text = row.Errors.Count == 0
            ? "A linha é válida. Podes revê-la antes da confirmação."
            : string.Join(Environment.NewLine, row.Errors.Select(error => error.Message));
        CorrectionNameBox.Text = row.Data.CommercialName;
        CorrectionUnitBox.Text = row.Data.BaseUnit;
        CorrectionTypeBox.SelectedIndex = row.Data.ProductType == ProductType.Medicine ? 1 : 0;
        CorrectionQuantityBox.Text = row.Data.InitialQuantity.ToString(CultureInfo.InvariantCulture);
        CorrectionFactorBox.Text = row.Data.ConversionFactor.ToString(CultureInfo.InvariantCulture);
        CorrectionLotBox.Text = row.Data.LotNumber ?? string.Empty;
        CorrectionExpiryPicker.Date = row.Data.ExpiryDate is { } expiry
            ? new DateTimeOffset(expiry.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        CorrectionPurchaseBox.Text = row.Data.PurchasePriceXof.ToString(CultureInfo.InvariantCulture);
        CorrectionSaleBox.Text = row.Data.SalePriceXof.ToString(CultureInfo.InvariantCulture);
        CorrectionMinimumBox.Text = row.Data.MinimumStockBase?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async void OnSaveCorrection(object sender, RoutedEventArgs e)
    {
        if (ImportRowsList.SelectedItem is not InventoryImportDraftRow row) return;
        InventoryImportNormalizedRow corrected = row.Data with
        {
            CommercialName = CorrectionNameBox.Text.Trim(),
            BaseUnit = CorrectionUnitBox.Text.Trim(),
            ProductType = CorrectionTypeBox.SelectedIndex == 1 ? ProductType.Medicine : ProductType.General,
            InitialQuantity = ParseLong(CorrectionQuantityBox.Text, 0),
            ConversionFactor = ParseLong(CorrectionFactorBox.Text, 0),
            LotNumber = EmptyToNull(CorrectionLotBox.Text),
            ExpiryDate = CorrectionExpiryPicker.Date is { } date ? DateOnly.FromDateTime(date.DateTime) : null,
            PurchasePriceXof = ParseLong(CorrectionPurchaseBox.Text, -1),
            SalePriceXof = ParseLong(CorrectionSaleBox.Text, -1),
            MinimumStockBase = string.IsNullOrWhiteSpace(CorrectionMinimumBox.Text)
                ? null
                : ParseLong(CorrectionMinimumBox.Text, -1)
        };
        SetBusy(true);
        bool saved = await _viewModel.CorrectRowAsync(row.Id, corrected, CancellationToken.None);
        SetBusy(false);
        ShowMessage(saved ? "Correcção guardada no rascunho." : _viewModel.ErrorMessage, saved);
        RefreshSurface();
    }

    private async void OnConfirmImport(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        bool confirmed = await _viewModel.ConfirmAsync(ExplicitConfirmationBox.IsChecked == true, CancellationToken.None);
        SetBusy(false);
        ShowMessage(confirmed ? "Inventário aplicado ao stock local." : _viewModel.ErrorMessage, confirmed);
        RefreshSurface();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _viewModel.GoBack();
        RefreshSurface();
    }

    private void RefreshSurface()
    {
        _refreshing = true;
        FilePanel.Visibility = _viewModel.Step == InventoryImportStep.File ? Visibility.Visible : Visibility.Collapsed;
        MappingPanel.Visibility = _viewModel.Step == InventoryImportStep.Mapping ? Visibility.Visible : Visibility.Collapsed;
        ValidationPanel.Visibility = _viewModel.Step == InventoryImportStep.Validation ? Visibility.Visible : Visibility.Collapsed;
        ConfirmationPanel.Visibility = _viewModel.Step == InventoryImportStep.Confirmation ? Visibility.Visible : Visibility.Collapsed;
        Border[] steps = [FileStep, MappingStep, ValidationStep, ConfirmationStep];
        for (int index = 0; index < steps.Length; index++)
        {
            bool active = index + 1 == (int)_viewModel.Step;
            steps[index].Background = new SolidColorBrush(active
                ? Windows.UI.Color.FromArgb(255, 0, 92, 153)
                : Windows.UI.Color.FromArgb(255, 231, 237, 244));
            if (steps[index].Child is TextBlock label) label.Foreground = new SolidColorBrush(active ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(255, 30, 45, 60));
        }

        WorksheetArea.Visibility = _viewModel.Worksheets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WorksheetBox.ItemsSource = _viewModel.Worksheets;
        WorksheetBox.SelectedItem = _viewModel.SelectedWorksheet;
        NameColumnBox.ItemsSource = _viewModel.Headers;
        UnitColumnBox.ItemsSource = _viewModel.Headers;
        QuantityColumnBox.ItemsSource = _viewModel.Headers;
        NameColumnBox.SelectedItem = Mapping(ImportColumn.CommercialName);
        UnitColumnBox.SelectedItem = Mapping(ImportColumn.BaseUnit);
        QuantityColumnBox.SelectedItem = Mapping(ImportColumn.InitialQuantity);
        ImportRowsList.ItemsSource = _viewModel.Draft?.Rows;
        if (_viewModel.Draft is { } draft)
        {
            ValidationSummaryText.Text = $"{draft.ValidRows} linhas válidas e {draft.ErrorRows} bloqueadas em {draft.TotalRows}.";
            ConfirmationSummaryText.Text = $"Serão processadas {draft.ValidRows} linhas do ficheiro {draft.FileName}. Esta operação altera o stock local.";
            ReviewButton.IsEnabled = draft.ErrorRows == 0;
        }
        if (!string.IsNullOrWhiteSpace(_viewModel.ErrorMessage)) ShowMessage(_viewModel.ErrorMessage, false);
        _refreshing = false;
    }

    private string? Mapping(ImportColumn column) => _viewModel.ColumnMappings.GetValueOrDefault(column);

    private void SaveMappings()
    {
        _viewModel.SetMapping(ImportColumn.CommercialName, NameColumnBox.SelectedItem as string);
        _viewModel.SetMapping(ImportColumn.BaseUnit, UnitColumnBox.SelectedItem as string);
        _viewModel.SetMapping(ImportColumn.InitialQuantity, QuantityColumnBox.SelectedItem as string);
    }

    private void SetBusy(bool busy)
    {
        ImportProgress.IsActive = busy;
        ImportProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string? message, bool success)
    {
        ImportMessage.IsOpen = !string.IsNullOrWhiteSpace(message);
        ImportMessage.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        ImportMessage.Message = message ?? string.Empty;
    }

    private static long ParseLong(string value, long fallback) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : fallback;

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
