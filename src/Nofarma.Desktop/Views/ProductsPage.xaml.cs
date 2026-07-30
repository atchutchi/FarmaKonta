using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Application.Catalog;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

namespace Nofarma.Desktop.Views;

public sealed partial class ProductsPage : Page
{
    private readonly ProductsViewModel _viewModel;
    private bool _loaded;

    public ProductsPage()
    {
        _viewModel = App.Services.GetRequiredService<ProductsViewModel>();
        InitializeComponent();
        ProductTypeBox.ItemsSource = Enum.GetValues<ProductType>();
        ProductTypeBox.SelectedItem = ProductType.General;
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

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await _viewModel.SearchAsync(ProductSearchBox.Text, CancellationToken.None);
        RefreshSurface();
    }

    private void OnShowEditor(object sender, RoutedEventArgs e)
    {
        EditorColumn.Width = new GridLength(360);
        ProductEditor.Visibility = Visibility.Visible;
        ProductNameBox.Focus(FocusState.Programmatic);
    }

    private void OnHideEditor(object sender, RoutedEventArgs e)
    {
        ProductEditor.Visibility = Visibility.Collapsed;
        EditorColumn.Width = new GridLength(0);
    }

    private async void OnSaveProduct(object sender, RoutedEventArgs e)
    {
        SaveProductButton.IsEnabled = false;
        EntityId? categoryId = ProductCategoryBox.SelectedItem is ProductCategorySummary category
            ? category.Id : null;
        bool saved = await _viewModel.CreateAsync(new ProductEditorInput(
            ProductCodeBox.Text, ProductNameBox.Text, categoryId, ProductBaseUnitBox.Text,
            ProductTypeBox.SelectedItem is ProductType type ? type : ProductType.General,
            ProductSalePriceBox.Text, "0", ProductMinimumStockBox.Text,
            ProductRequiresPrescriptionBox.IsChecked == true,
            ProductRequiresLotBox.IsChecked == true,
            ProductRequiresExpiryBox.IsChecked == true), CancellationToken.None);
        SaveProductButton.IsEnabled = true;
        ProductValidationText.Text = string.Join(Environment.NewLine, _viewModel.ValidationErrors.Values);
        ProductValidationText.Visibility = _viewModel.ValidationErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProductMessage.IsOpen = saved || _viewModel.ErrorMessage is not null;
        ProductMessage.Severity = saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        ProductMessage.Message = saved ? "Produto guardado localmente." : _viewModel.ErrorMessage ?? "Revê os campos assinalados.";
        if (saved)
        {
            ProductNameBox.Text = string.Empty;
            ProductCodeBox.Text = string.Empty;
            ProductMinimumStockBox.Text = string.Empty;
            RefreshSurface();
        }
    }

    private void RefreshSurface()
    {
        ProductsList.ItemsSource = _viewModel.Products;
        ProductCategoryBox.ItemsSource = _viewModel.Categories;
        ProductsProgress.IsActive = _viewModel.IsLoading;
        ProductsProgress.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ProductsEmptyState.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        ProductsList.Visibility = _viewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }
}
