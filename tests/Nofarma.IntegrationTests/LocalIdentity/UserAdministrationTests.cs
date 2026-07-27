using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Identity.Setup;
using Nofarma.Application.Identity.Users;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.LocalIdentity;

public sealed class UserAdministrationTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-users-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AdministratorManagesCashierWhileCashierIsDenied()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<NofarmaDbContext> options = await CreateDatabaseAsync();
        var hasher = new DeterministicHasher();
        var clock = new FixedClock();
        await new SetupService(
            new SqliteLocalIdentityStore(options),
            hasher,
            new FixedCodeGenerator(),
            clock).ConfigureAsync(ValidSetup(), cancellationToken);

        var authentication = new AuthenticationService(
            new SqliteLocalAuthenticationStore(options),
            hasher,
            hasher.Hash("timing"),
            new CurrentSession(),
            clock);
        LocalSession administrator = Assert.IsType<LocalSession>((await authentication.SignInAsync(
            new SignInRequest("admin", "FarmaKonta!2026"),
            cancellationToken)).Session);
        var users = new UserAdministrationService(
            new SqliteUserAdministrationStore(options),
            new AuthorizationService(clock),
            hasher,
            clock);

        EntityId cashierId = await users.CreateAsync(
            administrator,
            new CreateUserRequest("Caixa Um", "caixa1", UserRole.Cashier, "1234"),
            cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => users.CreateAsync(
            administrator,
            new CreateUserRequest("Duplicado", "CAIXA1", UserRole.Cashier, "5678"),
            cancellationToken));

        LocalSession cashier = Assert.IsType<LocalSession>((await authentication.SignInAsync(
            new SignInRequest("caixa1", "1234"),
            cancellationToken)).Session);
        await Assert.ThrowsAsync<AuthorizationException>(() => users.CreateAsync(
            cashier,
            new CreateUserRequest("Intruso", "intruso", UserRole.Cashier, "9999"),
            cancellationToken));

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await authentication.SignInAsync(
                new SignInRequest("caixa1", "0000"),
                cancellationToken);
        }

        await users.UnlockAsync(administrator, cashierId, cancellationToken);
        Assert.True((await authentication.SignInAsync(
            new SignInRequest("caixa1", "1234"),
            cancellationToken)).Succeeded);

        await users.ChangeRoleAsync(
            administrator,
            cashierId,
            UserRole.Pharmacist,
            "NovaSenha!2026",
            cancellationToken);
        Assert.False((await authentication.SignInAsync(
            new SignInRequest("caixa1", "1234"),
            cancellationToken)).Succeeded);
        Assert.True((await authentication.SignInAsync(
            new SignInRequest("caixa1", "NovaSenha!2026"),
            cancellationToken)).Succeeded);

        await users.DeactivateAsync(administrator, cashierId, cancellationToken);
        Assert.False((await authentication.SignInAsync(
            new SignInRequest("caixa1", "NovaSenha!2026"),
            cancellationToken)).Succeeded);

        await using var verification = new NofarmaDbContext(options);
        string[] actions = await verification.AuditEvents
            .Select(record => record.Action)
            .ToArrayAsync(cancellationToken);
        Assert.Contains("user.created", actions);
        Assert.Contains("user.unlocked", actions);
        Assert.Contains("user.role_changed", actions);
        Assert.Contains("user.deactivated", actions);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private async Task<DbContextOptions<NofarmaDbContext>> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "nofarma.db")}")
            .Options;
        await using var context = new NofarmaDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private static SetupRequest ValidSetup() =>
        new(
            "Farmacia Central",
            "510000001",
            "Avenida principal, Bissau",
            "+245 955 000 000",
            "Africa/Bissau",
            "Caixa principal",
            "Administrador",
            "admin",
            "FarmaKonta!2026");

    private sealed class DeterministicHasher : ICredentialHasher
    {
        public CredentialHash Hash(string secret) => new(1, secret, 1, [1], [2]);

        public bool Verify(string secret, CredentialHash credential) =>
            secret == credential.Algorithm;

        public bool NeedsRehash(CredentialHash credential) => false;
    }

    private sealed class FixedCodeGenerator : IRecoveryCodeGenerator
    {
        public string Generate() => "ABCD-EFGH-JKLM-NPQR-STUV";
    }

    private sealed class FixedClock : IUtcClock
    {
        public UtcInstant GetCurrentInstant() =>
            UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
    }
}
