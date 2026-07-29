using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Inventory;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class StockConcurrencyTests
{
    [Fact]
    public async Task ConcurrentOutputsNeverCreateNegativeStock()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation opening = fixture.Entry("opening", 10);
        StockConfirmationResult entry = await store.ConfirmAsync(
            context,
            opening,
            fixture.Audit(opening.Operation.MovementId),
            cancellationToken);

        Task<StockConfirmationResult> first = ConfirmLossAsync("loss-1");
        Task<StockConfirmationResult> second = ConfirmLossAsync("loss-2");
        Exception? firstError = await Record.ExceptionAsync(() => first);
        Exception? secondError = await Record.ExceptionAsync(() => second);

        Assert.Equal(1, new[] { firstError, secondError }.Count(error => error is null));
        Assert.Single(new[] { firstError, secondError }.OfType<InsufficientStockException>());
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(
            2,
            await verification.StockMovements.CountAsync(cancellationToken));
        long balance = await verification.StockLots
            .Where(record => record.Id == entry.LotId!.Value.Value)
            .Select(record => record.AvailableQuantityBase)
            .SingleAsync(cancellationToken);
        long movementSum = await verification.StockMovements
            .SumAsync(record => record.QuantityBase, cancellationToken);
        Assert.Equal(2, balance);
        Assert.Equal(movementSum, balance);

        Task<StockConfirmationResult> ConfirmLossAsync(string key)
        {
            EntityId movementId = EntityId.New();
            var confirmation = new InventoryConfirmation(
                new StockOperation(
                    movementId,
                    fixture.PharmacyId,
                    fixture.ProductId,
                    entry.LotId,
                    -8,
                    StockMovementType.Loss,
                    "Perda",
                    null,
                    fixture.UserId,
                    UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 1, 0, TimeSpan.Zero)),
                    key),
                null,
                new string(key.EndsWith('a') ? 'A' : 'B', 64));
            return store.ConfirmAsync(
                context,
                confirmation,
                fixture.Audit(movementId),
                cancellationToken);
        }
    }
}
