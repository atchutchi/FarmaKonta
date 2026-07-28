using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Configuration;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Desktop.Services;
using Nofarma.Domain.Identity;

namespace Nofarma.Desktop.Views;

public sealed partial class AppShellPage : Page
{
    private readonly CurrentSession _currentSession;
    private readonly NavigationService _navigation;
    private readonly ILocalApplicationInfoStore _applicationInfo;
    private bool _loaded;

    public AppShellPage()
    {
        _currentSession = App.Services.GetRequiredService<CurrentSession>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        _applicationInfo = App.Services.GetRequiredService<ILocalApplicationInfoStore>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
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
        ShowEmpty("Painel", "A visão operacional será preenchida apenas com vendas, stock e caixa registados nesta instalação.");
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination })
        {
            return;
        }

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
            case "Utilizadores":
                ModuleContent.Content = new UsersPage();
                break;
            case "Configurações":
                ModuleContent.Content = new SettingsPage();
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
