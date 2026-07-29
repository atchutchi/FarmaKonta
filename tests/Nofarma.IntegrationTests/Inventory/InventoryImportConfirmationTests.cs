using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Idempotency;
using Nofarma.Application.Inventory.Import;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class InventoryImportConfirmationTests
{
    [Fact]
    public async Task UpdateRowReplacesErrorsAndRecalculatesDraftCounts()
    {
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryImportStore(fixture.Options);
        var context = new InventoryImportStoreContext(fixture.PharmacyId, fixture.DeviceId);
        EntityId rowId = EntityId.New();
        InventoryImportNormalizedRow data = GeneralRow();
        var blocked = new InventoryImportDraftRow(rowId, 2, data with { InitialQuantity = 0 },
            InventoryImportMatchType.NewProduct, null, InventoryImportRowStatus.Blocked,
            [new InventoryImportError(2, "quantidade", "0", "positive_integer_required", "A quantidade deve ser positiva.")]);
        InventoryImportDraft draft = await store.SaveDraftAsync(context, fixture.UserId, "inventory.csv",
            new string('B', 64), null, [blocked], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        var corrected = blocked with { Data = data, Status = InventoryImportRowStatus.Valid, Errors = [] };

        InventoryImportDraft updated = await store.UpdateRowAsync(
            fixture.PharmacyId, draft.Id, corrected, TestContext.Current.CancellationToken);

        Assert.Equal(1, updated.ValidRows);
        Assert.Equal(0, updated.ErrorRows);
        Assert.Empty(Assert.Single(updated.Rows).Errors);
    }

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
            GeneralRow(),
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

    [Fact]
    public async Task ConfirmedImportWithDifferentKeyIsRejected()
    {
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryImportStore(fixture.Options);
        var context = new InventoryImportStoreContext(fixture.PharmacyId, fixture.DeviceId);
        var row = new InventoryImportDraftRow(
            EntityId.New(), 2, GeneralRow(), InventoryImportMatchType.NewProduct,
            null, InventoryImportRowStatus.Valid, []);
        InventoryImportDraft draft = await store.SaveDraftAsync(
            context, fixture.UserId, "inventory.xlsx", new string('A', 64), null,
            [row], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await store.ConfirmAsync(
            context, fixture.UserId, draft.Id, "opening-first", DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => store.ConfirmAsync(
            context, fixture.UserId, draft.Id, "opening-other", DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentConfirmationWithDifferentKeysPersistsOnlyOneIntent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryImportStore(fixture.Options);
        var context = new InventoryImportStoreContext(fixture.PharmacyId, fixture.DeviceId);
        var row = new InventoryImportDraftRow(
            EntityId.New(), 2, GeneralRow(), InventoryImportMatchType.NewProduct,
            null, InventoryImportRowStatus.Valid, []);
        InventoryImportDraft draft = await store.SaveDraftAsync(
            context, fixture.UserId, "inventory.xlsx", new string('A', 64), null,
            [row], DateTimeOffset.UtcNow, cancellationToken);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<(InventoryImportConfirmationResult? Result, Exception? Error)> first =
            ConfirmAsync("opening-concurrent-a");
        Task<(InventoryImportConfirmationResult? Result, Exception? Error)> second =
            ConfirmAsync("opening-concurrent-b");
        start.SetResult();
        (InventoryImportConfirmationResult? Result, Exception? Error)[] outcomes =
            await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome.Result is not null);
        Assert.Single(outcomes, outcome => outcome.Error is IdempotencyConflictException);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Single(await verification.AuditEvents
            .Where(record => record.Action == "stock.opening_inventory_import_confirmed")
            .ToArrayAsync(cancellationToken));
        Assert.Single(await verification.StockMovements
            .Where(record => record.SourceDocumentId == draft.Id.Value)
            .ToArrayAsync(cancellationToken));

        async Task<(InventoryImportConfirmationResult? Result, Exception? Error)> ConfirmAsync(
            string key)
        {
            await start.Task;
            try
            {
                InventoryImportConfirmationResult result = await store.ConfirmAsync(
                    context,
                    fixture.UserId,
                    draft.Id,
                    key,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                return (result, null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }
    }

    private static InventoryImportNormalizedRow GeneralRow() => new(
        null, "560999", "Vitamina C", null, null, null, null, "Geral",
        ProductType.General, "Comprimido", "Caixa", 10, 50, 100, 20, 3,
        "VC-01", new DateOnly(2027, 12, 31), null, null, false);
}
