using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nofarma.Application.Inventory.Import;
using Nofarma.Desktop.ViewModels;
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
}
