using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class InventoryDatabaseTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-inventory-db-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task MigrationCreatesInventoryTablesAndUniqueIndexes()
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
        HashSet<string> tables = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table'",
            cancellationToken);
        HashSet<string> indexes = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'index'",
            cancellationToken);

        string[] expectedTables =
        [
            "ProductCategories",
            "Products",
            "ProductPackages",
            "ProductBarcodes",
            "Suppliers",
            "PurchaseOrders",
            "PurchaseOrderLines",
            "GoodsReceipts",
            "GoodsReceiptLines",
            "StockLots",
            "StockMovements",
            "InventoryImports",
            "InventoryImportRows",
            "InventoryImportErrors"
        ];
        foreach (string table in expectedTables)
        {
            Assert.Contains(table, tables);
        }

        Assert.Contains("IX_Products_PharmacyId_NormalizedCode", indexes);
        Assert.Contains("IX_ProductBarcodes_PharmacyId_Value", indexes);
        Assert.Contains("IX_StockLots_ProductId_NormalizedNumber", indexes);
        Assert.Contains("IX_StockMovements_PharmacyId_IdempotencyKey", indexes);
    }

    [Fact]
    public async Task HistoricalRelationshipsDoNotCascadeDeletes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string databasePath = Path.Combine(_directory, "history.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using (var context = new NofarmaDbContext(options))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_list('StockMovements')";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        int relationships = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            relationships++;
            string deleteAction = reader.GetString(reader.GetOrdinal("on_delete"));
            Assert.NotEqual("CASCADE", deleteAction);
        }

        Assert.True(relationships >= 3);
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task ConfirmedStockMovementsAreAppendOnly(EntityState state)
    {
        string databasePath = Path.Combine(_directory, $"append-only-{state}.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var context = new NofarmaDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var movement = new StockMovementRecord { Id = Guid.NewGuid() };
        context.Attach(movement);
        context.Entry(movement).State = state;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
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
