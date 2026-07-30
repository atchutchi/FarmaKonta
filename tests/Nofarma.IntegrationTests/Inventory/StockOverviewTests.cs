using Nofarma.Application.Inventory;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class StockOverviewTests
{
    [Fact]
    public async Task SearchStockReturnsLotAndRealAlertCounts()
    {
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var inventory = new SqliteInventoryStore(fixture.Options);
        var entry = fixture.Entry("overview-entry", 2);
        await inventory.ConfirmAsync(
            new(fixture.PharmacyId, fixture.DeviceId),
            entry,
            fixture.Audit(entry.Operation.MovementId),
            TestContext.Current.CancellationToken);

        StockOverview overview = await inventory.SearchStockAsync(
            fixture.PharmacyId,
            string.Empty,
            new DateOnly(2026, 7, 28),
            TestContext.Current.CancellationToken);

        StockOverviewItem item = Assert.Single(overview.Items);
        Assert.Equal("Produto", item.ProductName);
        Assert.Equal("LOT-01", item.LotNumber);
        Assert.Equal(2, item.AvailableQuantityBase);
        Assert.Equal(1, overview.LowStockProducts);
        Assert.Equal(0, overview.OutOfStockProducts);
    }
}
