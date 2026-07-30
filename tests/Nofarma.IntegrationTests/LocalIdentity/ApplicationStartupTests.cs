using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Setup;
using Nofarma.Application.Identity.Users;
using Nofarma.Domain.Identity;
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

    [Fact]
    public async Task ExistingCredentialsRemainValidWhenLicensingSecretsUseSeparateDirectory()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string databasePath = Path.Combine(_directory, "upgrade.db");
        string credentialSecretsDirectory = Path.Combine(_directory, "secrets");
        string licensingSecretsDirectory = Path.Combine(_directory, "licensing-secrets");
        string recoveryCode;

        using (ServiceProvider firstStart = new ServiceCollection()
            .AddNofarmaLocalIdentity(
                databasePath,
                credentialSecretsDirectory,
                licensingSecretsDirectory: licensingSecretsDirectory)
            .BuildServiceProvider(validateScopes: true))
        {
            await firstStart.GetRequiredService<LocalApplicationStartup>()
                .InitializeAsync(cancellationToken);
            SetupResult setup = await firstStart.GetRequiredService<SetupService>().ConfigureAsync(
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
            recoveryCode = setup.RecoveryCode;

            SignInResult administrator = await firstStart
                .GetRequiredService<AuthenticationService>()
                .SignInAsync(
                    new SignInRequest("admin", "FarmaKonta!2026"),
                    cancellationToken);
            await firstStart.GetRequiredService<UserAdministrationService>()
                .CreateAsync(
                    Assert.IsType<LocalSession>(administrator.Session),
                    new CreateUserRequest(
                        "Caixa",
                        "caixa1",
                        UserRole.Cashier,
                        "1234"),
                    cancellationToken);

            var context = Assert.IsType<Nofarma.Application.Licensing.LicenseContext>(
                await firstStart.GetRequiredService<ILicenseContextStore>()
                    .GetAsync(cancellationToken));
            _ = firstStart.GetRequiredService<IDeviceLicenseIdentityStore>()
                .GetOrCreate(context.PharmacyId, context.DeviceId);
        }

        Assert.True(File.Exists(Path.Combine(
            credentialSecretsDirectory,
            "credential-pepper.bin")));
        Assert.False(File.Exists(Path.Combine(
            licensingSecretsDirectory,
            "credential-pepper.bin")));
        Assert.True(File.Exists(Path.Combine(
            licensingSecretsDirectory,
            "device-license-key.bin")));
        Assert.False(File.Exists(Path.Combine(
            credentialSecretsDirectory,
            "device-license-key.bin")));

        using ServiceProvider upgradedStart = new ServiceCollection()
            .AddNofarmaLocalIdentity(
                databasePath,
                credentialSecretsDirectory,
                licensingSecretsDirectory: licensingSecretsDirectory)
            .BuildServiceProvider(validateScopes: true);
        SignInResult result = await upgradedStart
            .GetRequiredService<AuthenticationService>()
            .SignInAsync(
                new SignInRequest("admin", "FarmaKonta!2026"),
                cancellationToken);

        Assert.True(result.Succeeded);

        SignInResult cashier = await upgradedStart
            .GetRequiredService<AuthenticationService>()
            .SignInAsync(
                new SignInRequest("caixa1", "1234"),
                cancellationToken);
        Assert.True(cashier.Succeeded);

        RecoveryResult recovery = await upgradedStart
            .GetRequiredService<RecoveryService>()
            .RecoverAsync(
                new RecoveryRequest(recoveryCode, "NovaSenha!2026"),
                cancellationToken);
        Assert.True(recovery.Succeeded);
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
