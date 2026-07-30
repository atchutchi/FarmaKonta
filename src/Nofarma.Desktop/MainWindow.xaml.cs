using Microsoft.UI.Xaml;
using Nofarma.Desktop.Services;
using Nofarma.Infrastructure.Composition;

namespace Nofarma.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly LocalApplicationStartup _startup;
    private readonly NavigationService _navigation;
    private bool _hasStarted;

    public MainWindow(
        LocalApplicationStartup startup,
        NavigationService navigation)
    {
        _startup = startup;
        _navigation = navigation;
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1366, 768));
        _navigation.Initialize(RootFrame);
    }

    private async void OnStartupLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasStarted)
        {
            return;
        }

        _hasStarted = true;
        ApplicationStartDestination destination = await _startup.InitializeAsync(
            CancellationToken.None);
        StartupOverlay.Visibility = Visibility.Collapsed;
        Title = destination == ApplicationStartDestination.Setup
            ? "NôFarma | Configuração inicial"
            : "NôFarma | Acesso local";
        _navigation.NavigateTo(destination);
    }
}
