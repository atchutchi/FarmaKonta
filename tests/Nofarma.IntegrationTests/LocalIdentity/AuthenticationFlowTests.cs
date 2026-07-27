using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Setup;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.LocalIdentity;

public sealed class AuthenticationFlowTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-auth-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task LoginLockoutAndOneTimeRecoverySurviveSqliteRoundTrips()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<NofarmaDbContext> options = await CreateDatabaseAsync();
        var hasher = new DeterministicHasher();
        var clock = new AdjustableClock();
        var identityStore = new SqliteLocalIdentityStore(options);
        var authenticationStore = new SqliteLocalAuthenticationStore(options);
        var setup = new SetupService(
            identityStore,
            hasher,
            new FixedCodeGenerator("OLD2-CODE-3456-789A-BCDE"),
            clock);
        await setup.ConfigureAsync(ValidRequest(), cancellationToken);

        var currentSession = new CurrentSession();
        var authentication = new AuthenticationService(
            authenticationStore,
            hasher,
            hasher.Hash("timing-only"),
            currentSession,
            clock);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            SignInResult failed = await authentication.SignInAsync(
                new SignInRequest("admin", "wrong"),
                cancellationToken);
            Assert.False(failed.Succeeded);
        }

        SignInResult blocked = await authentication.SignInAsync(
            new SignInRequest("admin", "FarmaKonta!2026"),
            cancellationToken);
        Assert.False(blocked.Succeeded);

        clock.Advance(TimeSpan.FromMinutes(16));
        SignInResult signedIn = await authentication.SignInAsync(
            new SignInRequest("admin", "FarmaKonta!2026"),
            cancellationToken);
        Assert.True(signedIn.Succeeded);
        Assert.NotNull(currentSession.Active);

        var recovery = new RecoveryService(
            authenticationStore,
            hasher,
            new FixedCodeGenerator("NEW2-CODE-3456-789A-BCDE"),
            currentSession,
            clock);
        RecoveryResult recovered = await recovery.RecoverAsync(
            new RecoveryRequest("OLD2-CODE-3456-789A-BCDE", "NovaSenha!2026"),
            cancellationToken);
        Assert.True(recovered.Succeeded);
        Assert.Equal("NEW2-CODE-3456-789A-BCDE", recovered.NewRecoveryCode);
        Assert.Null(currentSession.Active);

        RecoveryResult reused = await recovery.RecoverAsync(
            new RecoveryRequest("OLD2-CODE-3456-789A-BCDE", "OutraSenha!2026"),
            cancellationToken);
        Assert.False(reused.Succeeded);

        SignInResult oldPassword = await authentication.SignInAsync(
            new SignInRequest("admin", "FarmaKonta!2026"),
            cancellationToken);
        Assert.False(oldPassword.Succeeded);
        SignInResult newPassword = await authentication.SignInAsync(
            new SignInRequest("admin", "NovaSenha!2026"),
            cancellationToken);
        Assert.True(newPassword.Succeeded);

        await using var verification = new NofarmaDbContext(options);
        Assert.Equal(2, await verification.RecoveryCodes.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.RecoveryCodes.CountAsync(
            code => code.UsedAtUtc != null,
            cancellationToken));
        Assert.Contains(
            await verification.LocalSessions.ToListAsync(cancellationToken),
            session => session.RevokedAtUtc != null);
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
        string databasePath = Path.Combine(_directory, "nofarma.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var context = new NofarmaDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private static SetupRequest ValidRequest() =>
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
        public CredentialHash Hash(string secret) =>
            new(1, secret, 1, [1], [2]);

        public bool Verify(string secret, CredentialHash credential) =>
            secret == credential.Algorithm;

        public bool NeedsRehash(CredentialHash credential) => false;
    }

    private sealed class FixedCodeGenerator(string code) : IRecoveryCodeGenerator
    {
        public string Generate() => code;
    }

    private sealed class AdjustableClock : IUtcClock
    {
        public UtcInstant Current { get; private set; } =
            UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        public void Advance(TimeSpan duration) =>
            Current = UtcInstant.From(Current.Value.Add(duration));

        public UtcInstant GetCurrentInstant() => Current;
    }
}
