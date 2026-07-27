using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Nofarma.Desktop.Composition;
using Nofarma.Infrastructure.Composition;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Nofarma.Desktop;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private readonly ServiceProvider _services;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        _services = new ServiceCollection()
            .AddNofarmaLocalIdentity()
            .AddNofarmaDesktop()
            .BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = _services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
