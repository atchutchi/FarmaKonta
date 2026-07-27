using Microsoft.UI.Xaml;
using Nofarma.Infrastructure.Composition;

namespace Nofarma.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly LocalApplicationStartup _startup;
    private bool _hasStarted;

    public MainWindow(LocalApplicationStartup startup)
    {
        _startup = startup;
        InitializeComponent();
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
        StartupProgress.IsActive = false;
        StartupProgress.Visibility = Visibility.Collapsed;
        Title = destination == ApplicationStartDestination.Setup
            ? "NôFarma | Configuração inicial"
            : "NôFarma | Acesso local";
    }
}
