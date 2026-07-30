using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.LocalIdentity;

public sealed class LocalDatabaseTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-db-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task MigrationsCreateIdentityTablesAndUniqueIndexes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string databasePath = Path.Combine(_directory, "nofarma.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using (var context = new NofarmaDbContext(options))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);

        await using (SqliteCommand versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "SELECT sqlite_version()";
            object? rawVersion = await versionCommand.ExecuteScalarAsync(cancellationToken);
            var sqliteVersion = Version.Parse(Assert.IsType<string>(rawVersion));
            Assert.True(
                sqliteVersion >= new Version(3, 50, 2),
                $"SQLite {sqliteVersion} is older than the required security baseline 3.50.2.");
        }

        HashSet<string> tables = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table'",
            cancellationToken);
        HashSet<string> indexes = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'index'",
            cancellationToken);

        Assert.Contains("Installations", tables);
        Assert.Contains("Pharmacies", tables);
        Assert.Contains("Devices", tables);
        Assert.Contains("LocalUsers", tables);
        Assert.Contains("CredentialRecords", tables);
        Assert.Contains("RecoveryCodes", tables);
        Assert.Contains("LocalSessions", tables);
        Assert.Contains("AuditEvents", tables);
        Assert.Contains("IX_Installations_SingletonKey", indexes);
        Assert.Contains("IX_LocalUsers_PharmacyId_NormalizedLoginName", indexes);
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

    private static async Task<HashSet<string>> ReadNamesAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;

        var names = new HashSet<string>(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
