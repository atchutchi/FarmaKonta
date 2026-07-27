using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Nofarma.Application.Identity.Setup;
using Nofarma.Infrastructure.Composition;

namespace Nofarma.IntegrationTests.LocalIdentity;

[SupportedOSPlatform("windows")]
public sealed class ApplicationStartupTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-startup-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task NewDatabaseStartsAtSetupAndConfiguredDatabaseStartsAtLogin()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string databasePath = Path.Combine(_directory, "nofarma.db");
        using ServiceProvider provider = new ServiceCollection()
            .AddNofarmaLocalIdentity(databasePath, Path.Combine(_directory, "secrets"))
            .BuildServiceProvider(validateScopes: true);
        var startup = provider.GetRequiredService<LocalApplicationStartup>();

        Assert.Equal(
            ApplicationStartDestination.Setup,
            await startup.InitializeAsync(cancellationToken));

        await provider.GetRequiredService<SetupService>().ConfigureAsync(
            new SetupRequest(
                "Farmacia Central",
                "510000001",
                "Avenida principal, Bissau",
                "+245 955 000 000",
                "Africa/Bissau",
                "Caixa principal",
                "Administrador",
                "admin",
                "FarmaKonta!2026"),
            cancellationToken);

        Assert.Equal(
            ApplicationStartDestination.Login,
            await startup.InitializeAsync(cancellationToken));
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
