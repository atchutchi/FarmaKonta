using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Inventory.Import;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class InventoryImportConfirmationTests
{
    [Fact]
    public async Task ConfirmCreatesOpeningInventoryAtomicallyAndRetryIsIdempotent()
    {
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryImportStore(fixture.Options);
        var context = new InventoryImportStoreContext(fixture.PharmacyId, fixture.DeviceId);
        EntityId rowId = EntityId.New();
        var row = new InventoryImportDraftRow(
            rowId,
            2,
            new InventoryImportNormalizedRow(
                null, "560999", "Vitamina C", null, null, null, null, "Geral",
                ProductType.General, "Comprimido", "Caixa", 10, 50, 100, 20, 3,
                "VC-01", new DateOnly(2027, 12, 31), null, null, false),
            InventoryImportMatchType.NewProduct,
            null,
            InventoryImportRowStatus.Valid,
            []);
        InventoryImportDraft draft = await store.SaveDraftAsync(
            context, fixture.UserId, "inventory.xlsx", new string('A', 64), "Inventário",
            [row], new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        InventoryImportConfirmationResult first = await store.ConfirmAsync(
            context, fixture.UserId, draft.Id, "opening-1",
            new DateTimeOffset(2026, 7, 28, 12, 1, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        InventoryImportConfirmationResult retry = await store.ConfirmAsync(
            context, fixture.UserId, draft.Id, "opening-1",
            new DateTimeOffset(2026, 7, 28, 12, 2, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(1, first.ProductsCreated);
        Assert.False(first.AlreadyConfirmed);
        Assert.True(retry.AlreadyConfirmed);
        Assert.Equal(1, await verification.StockMovements.CountAsync(
            movement => movement.SourceDocumentId == draft.Id.Value,
            TestContext.Current.CancellationToken));
        Assert.Equal(30, await verification.StockLots.Where(lot => lot.Number == "VC-01")
            .Select(lot => lot.AvailableQuantityBase).SingleAsync(TestContext.Current.CancellationToken));
    }
}
