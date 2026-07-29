using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Nofarma.Desktop.Composition;
using Nofarma.Desktop.Services;
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
        DesktopLicenseConfiguration licenseConfiguration =
            DesktopLicenseConfiguration.LoadCurrent();
        string dataDirectory = Path.Combine(licenseConfiguration.BaseDirectory, "data");
        string credentialSecretsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABIPTOM",
            "Nofarma",
            "secrets");
        string licensingSecretsDirectory = Path.Combine(
            licenseConfiguration.BaseDirectory,
            "licensing-secrets");
        Directory.CreateDirectory(dataDirectory);
        _services = new ServiceCollection()
            .AddNofarmaLocalIdentity(
                databasePath: Path.Combine(dataDirectory, "nofarma.db"),
                credentialSecretsDirectory: credentialSecretsDirectory,
                licenseChannel: licenseConfiguration.Channel,
                trustedPublicKeys: licenseConfiguration.TrustedPublicKeys,
                licensingSecretsDirectory: licensingSecretsDirectory)
            .AddNofarmaDesktop(licenseConfiguration)
            .BuildServiceProvider(validateScopes: true);
    }

    public static IServiceProvider Services =>
        ((App)Current)._services;

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
