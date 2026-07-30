using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Setup;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.LocalIdentity;

public sealed class SetupTransactionTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-setup-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ValidSetupPersistsCompleteInstallation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<NofarmaDbContext> options = await CreateDatabaseAsync();
        SetupService service = CreateService(options);

        SetupResult result = await service.ConfigureAsync(
            ValidRequest(),
            cancellationToken);

        await using var context = new NofarmaDbContext(options);
        Assert.Equal(result.InstallationId.Value, await context.Installations.Select(x => x.Id).SingleAsync(cancellationToken));
        Assert.Equal(1, await context.Pharmacies.CountAsync(cancellationToken));
        Assert.Equal(1, await context.Devices.CountAsync(cancellationToken));
        Assert.Equal(1, await context.LocalUsers.CountAsync(cancellationToken));
        Assert.Equal(1, await context.CredentialRecords.CountAsync(cancellationToken));
        Assert.Equal(1, await context.RecoveryCodes.CountAsync(cancellationToken));
        Assert.Equal("installation.configured", await context.AuditEvents.Select(x => x.Action).SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task PersistenceFailureRollsBackEverySetupRecord()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<NofarmaDbContext> options = await CreateDatabaseAsync();
        await using (var context = new NofarmaDbContext(options))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_setup_audit
                BEFORE INSERT ON AuditEvents
                BEGIN
                    SELECT RAISE(ABORT, 'controlled setup failure');
                END;
                """,
                cancellationToken);
        }

        SetupService service = CreateService(options);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ConfigureAsync(
            ValidRequest(),
            cancellationToken));

        await using var verification = new NofarmaDbContext(options);
        Assert.Equal(0, await verification.Installations.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.Pharmacies.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.Devices.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.LocalUsers.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.CredentialRecords.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.RecoveryCodes.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.AuditEvents.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task AuditEventsCannotBeChangedAfterInsertion()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<NofarmaDbContext> options = await CreateDatabaseAsync();
        await CreateService(options).ConfigureAsync(
            ValidRequest(),
            cancellationToken);

        await using var context = new NofarmaDbContext(options);
        var auditEvent = await context.AuditEvents.SingleAsync(cancellationToken);
        auditEvent.Action = "changed";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(cancellationToken));
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
        string databasePath = Path.Combine(_directory, $"{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var context = new NofarmaDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private static SetupService CreateService(DbContextOptions<NofarmaDbContext> options) =>
        new(
            new SqliteLocalIdentityStore(options),
            new DeterministicHasher(),
            new DeterministicRecoveryCodeGenerator(),
            new FixedClock());

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
            new(1, "test", 1, [1, 2, 3], [4, 5, 6]);

        public bool Verify(string secret, CredentialHash credential) => true;

        public bool NeedsRehash(CredentialHash credential) => false;
    }

    private sealed class DeterministicRecoveryCodeGenerator : IRecoveryCodeGenerator
    {
        public string Generate() => "ABCD-EFGH-JKLM-NPQR-STUV";
    }

    private sealed class FixedClock : IUtcClock
    {
        public UtcInstant GetCurrentInstant() =>
            UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
    }
}
