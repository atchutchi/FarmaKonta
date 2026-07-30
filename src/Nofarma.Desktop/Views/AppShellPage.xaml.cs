using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Configuration;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Desktop.Services;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Identity;

namespace Nofarma.Desktop.Views;

public sealed partial class AppShellPage : Page
{
    private readonly CurrentSession _currentSession;
    private readonly NavigationService _navigation;
    private readonly ILocalApplicationInfoStore _applicationInfo;
    private readonly LicensePageOperations _licenseOperations;
    private readonly LicenseViewModel _licenseViewModel;
    private bool _loaded;
    private bool _licenseSubscribed;
    private Button? _selectedNavigationButton;

    public AppShellPage()
    {
        _currentSession = App.Services.GetRequiredService<CurrentSession>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        _applicationInfo = App.Services.GetRequiredService<ILocalApplicationInfoStore>();
        _licenseOperations = App.Services.GetRequiredService<LicensePageOperations>();
        _licenseViewModel = App.Services.GetRequiredService<LicenseViewModel>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_licenseSubscribed)
        {
            _licenseOperations.StatusChanged += OnLicenseStatusChanged;
            _licenseSubscribed = true;
        }

        if (_loaded)
        {
            await RefreshLicenseStatusAsync();
            return;
        }

        _loaded = true;
        LocalSession? session = _currentSession.Active;
        if (session is null)
        {
            _navigation.NavigateToLogin();
            return;
        }

        LocalApplicationInfo? info = await _applicationInfo.GetAsync(CancellationToken.None);
        App.Services.GetRequiredService<MainWindow>().Title = "NôFarma";
        PharmacyNameText.Text = info?.PharmacyName ?? "Farmácia local";
        CurrentUserText.Text = $"Utilizador: {_currentSession.DisplayName}";
        UsersButton.Visibility = RolePermissions.IsAllowed(session.Role, Capability.ManageUsers)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsButton.Visibility = RolePermissions.IsAllowed(session.Role, Capability.ConfigurePharmacy)
            ? Visibility.Visible
            : Visibility.Collapsed;
        AuditButton.Visibility = RolePermissions.IsAllowed(session.Role, Capability.ViewAudit)
            ? Visibility.Visible
            : Visibility.Collapsed;
        InventoryImportButton.Visibility = RolePermissions.IsAllowed(session.Role, Capability.ImportInventory)
            ? Visibility.Visible
            : Visibility.Collapsed;
        var stock = App.Services.GetRequiredService<StockViewModel>();
        await stock.LoadAsync(CancellationToken.None);
        if (stock.ErrorMessage is null)
        {
            StockAlertText.Text = $"Stock: {stock.LowStockProducts} baixo, {stock.OutOfStockProducts} esgotado, {stock.ExpiryAttentionLots} validade";
        }
        await RefreshLicenseStatusAsync();
        SelectNavigationButton(PanelButton);
        ShowEmpty("Painel", "A visão operacional será preenchida apenas com vendas, stock e caixa registados nesta instalação.");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_licenseSubscribed)
        {
            _licenseOperations.StatusChanged -= OnLicenseStatusChanged;
            _licenseSubscribed = false;
        }
    }

    private async void OnLicenseStatusChanged(object? sender, EventArgs e) =>
        await RefreshLicenseStatusAsync();

    private async Task RefreshLicenseStatusAsync()
    {
        await _licenseViewModel.LoadAsync(CancellationToken.None);
        ActivationText.Text = _licenseViewModel.ErrorMessage is null
            ? _licenseViewModel.StatusText
            : "Licença não confirmada";
        QaModeText.Visibility = _licenseViewModel.IsQaMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetName(
            ActivationText,
            $"Estado da licença: {ActivationText.Text}");
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination })
        {
            return;
        }

        SelectNavigationButton((Button)sender);

        switch (destination)
        {
            case "Produtos":
                ModuleContent.Content = new ProductsPage();
                break;
            case "Fornecedores":
                ModuleContent.Content = new SuppliersPage();
                break;
            case "Stock":
                ModuleContent.Content = new StockPage();
                break;
            case "Compras":
                ModuleContent.Content = new PurchasesPage();
                break;
            case "Caixa":
                ModuleContent.Content = new CashPage();
                break;
            case "Importação":
                ModuleContent.Content = new InventoryImportPage();
                break;
            case "Utilizadores":
                ModuleContent.Content = new UsersPage();
                break;
            case "Configurações":
                ModuleContent.Content = new SettingsPage();
                break;
            case "Licença":
                ModuleContent.Content = new LicensePage();
                break;
            default:
                ShowEmpty(destination, DescriptionFor(destination));
                break;
        }
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _currentSession.Clear();
        _navigation.NavigateToLogin();
    }

    private void ShowEmpty(string title, string description) =>
        ModuleContent.Content = new ModuleEmptyPage(title, description);

    private void SelectNavigationButton(Button selected)
    {
        if (_selectedNavigationButton is not null)
        {
            _selectedNavigationButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        selected.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 11, 108, 184));
        _selectedNavigationButton = selected;
    }

    private static string DescriptionFor(string destination) => destination switch
    {
        "Vendas" => "O ponto de venda entra numa fase posterior. Nenhuma venda fictícia é apresentada.",
        "Facturas" => "As facturas aparecerão depois da implementação fiscal e da configuração autorizada pela farmácia.",
        "Produtos" => "O catálogo local ainda não contém produtos.",
        "Stock" => "Os alertas de stock serão calculados a partir de movimentos reais quando o módulo estiver activo.",
        "Compras" => "As compras e recepções de mercadoria ainda não foram registadas.",
        "Fornecedores" => "Ainda não existem fornecedores registados.",
        "Caixa" => "A abertura e o fecho de turnos entram com o módulo de vendas.",
        "Relatórios" => "Os relatórios serão gerados apenas a partir de dados operacionais reais.",
        "Auditoria" => "A auditoria técnica já é guardada localmente. A consulta visual será concluída nesta área.",
        _ => "Este módulo ainda não tem dados locais."
    };
}
