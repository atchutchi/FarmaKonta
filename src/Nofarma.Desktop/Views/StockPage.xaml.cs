using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Desktop.ViewModels;

namespace Nofarma.Desktop.Views;

public sealed partial class StockPage : Page
{
    private readonly StockViewModel _viewModel;
    private bool _loaded;

    public StockPage()
    {
        _viewModel = App.Services.GetRequiredService<StockViewModel>();
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

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await _viewModel.SearchAsync(StockSearchBox.Text, CancellationToken.None);
        RefreshSurface();
    }

    private void RefreshSurface()
    {
        StockList.ItemsSource = _viewModel.Items;
        StockProgress.IsActive = _viewModel.IsLoading;
        StockProgress.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        StockEmptyState.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        StockList.Visibility = _viewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        int attention = _viewModel.LowStockProducts + _viewModel.OutOfStockProducts;
        StockAlertBar.IsOpen = attention > 0;
        StockAlertBar.Message = $"{_viewModel.LowStockProducts} com stock baixo e {_viewModel.OutOfStockProducts} esgotados. Revê e repõe o stock para evitar rupturas.";
    }
}
