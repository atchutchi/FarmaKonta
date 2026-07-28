using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Desktop.ViewModels;

namespace Nofarma.Desktop.Views;

public sealed partial class SuppliersPage : Page
{
    private readonly SuppliersViewModel _viewModel;
    private bool _loaded;

    public SuppliersPage()
    {
        _viewModel = App.Services.GetRequiredService<SuppliersViewModel>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await _viewModel.LoadAsync(CancellationToken.None);
        RefreshSurface();
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await _viewModel.SearchAsync(args.QueryText, CancellationToken.None);
        RefreshSurface();
    }

    private void OnShowEditor(object sender, RoutedEventArgs e)
    {
        SupplierEditorColumn.Width = new GridLength(360);
        SupplierEditor.Visibility = Visibility.Visible;
        SupplierNameBox.Focus(FocusState.Programmatic);
    }

    private void OnHideEditor(object sender, RoutedEventArgs e)
    {
        SupplierEditor.Visibility = Visibility.Collapsed;
        SupplierEditorColumn.Width = new GridLength(0);
    }

    private async void OnSaveSupplier(object sender, RoutedEventArgs e)
    {
        SaveSupplierButton.IsEnabled = false;
        bool saved = await _viewModel.CreateAsync(new SupplierEditorInput(
            SupplierNameBox.Text, SupplierTaxBox.Text, SupplierPhoneBox.Text,
            SupplierEmailBox.Text, SupplierAddressBox.Text, SupplierNotesBox.Text), CancellationToken.None);
        SaveSupplierButton.IsEnabled = true;
        SupplierValidationText.Text = string.Join(Environment.NewLine, _viewModel.ValidationErrors.Values);
        SupplierValidationText.Visibility = _viewModel.ValidationErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SupplierMessage.IsOpen = saved || _viewModel.ErrorMessage is not null;
        SupplierMessage.Severity = saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        SupplierMessage.Message = saved ? "Fornecedor guardado localmente." : _viewModel.ErrorMessage ?? "Revê os campos assinalados.";
        if (saved)
        {
            SupplierNameBox.Text = string.Empty;
            SupplierTaxBox.Text = string.Empty;
            SupplierPhoneBox.Text = string.Empty;
            SupplierEmailBox.Text = string.Empty;
            SupplierAddressBox.Text = string.Empty;
            SupplierNotesBox.Text = string.Empty;
            RefreshSurface();
        }
    }

    private void RefreshSurface()
    {
        SuppliersList.ItemsSource = _viewModel.Suppliers;
        SuppliersProgress.IsActive = _viewModel.IsLoading;
        SuppliersProgress.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SuppliersEmptyState.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        SuppliersList.Visibility = _viewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }
}
