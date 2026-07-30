using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Domain.Inventory;

public sealed class FefoAllocatorTests
{
    [Fact]
    public void AllocatesEarlierExpiryFirst()
    {
        EntityId productId = EntityId.New();
        StockLot later = CreateLot(productId, "LATER", ExpiryDate.ForMonth(2027, 12), AtUtc(2026, 1, 1));
        StockLot earlier = CreateLot(productId, "EARLIER", ExpiryDate.ForMonth(2027, 6), AtUtc(2026, 2, 1));

        IReadOnlyList<StockAllocation> result = FefoAllocator.Allocate(
            7,
            [new(later, 10), new(earlier, 5)],
            new DateOnly(2026, 7, 27));

        Assert.Equal(2, result.Count);
        Assert.Equal(earlier.Id, result[0].LotId);
        Assert.Equal(5, result[0].QuantityBase);
        Assert.Equal(later.Id, result[1].LotId);
        Assert.Equal(2, result[1].QuantityBase);
    }

    [Fact]
    public void ExcludesBlockedLots()
    {
        EntityId productId = EntityId.New();
        StockLot expired = CreateLot(productId, "OLD", ExpiryDate.ForDay(2026, 7, 27), AtUtc(2026, 1, 1));
        StockLot valid = CreateLot(productId, "VALID", ExpiryDate.ForDay(2026, 8, 1), AtUtc(2026, 2, 1));

        IReadOnlyList<StockAllocation> result = FefoAllocator.Allocate(
            4,
            [new(expired, 10), new(valid, 5)],
            new DateOnly(2026, 7, 27));

        StockAllocation allocation = Assert.Single(result);
        Assert.Equal(valid.Id, allocation.LotId);
        Assert.Equal(4, allocation.QuantityBase);
    }

    [Fact]
    public void SameExpiryUsesFirstEntry()
    {
        EntityId productId = EntityId.New();
        ExpiryDate expiry = ExpiryDate.ForMonth(2027, 12);
        StockLot newer = CreateLot(productId, "NEW", expiry, AtUtc(2026, 2, 1));
        StockLot older = CreateLot(productId, "OLD", expiry, AtUtc(2026, 1, 1));

        IReadOnlyList<StockAllocation> result = FefoAllocator.Allocate(
            2,
            [new(newer, 5), new(older, 5)],
            new DateOnly(2026, 7, 27));

        Assert.Equal(older.Id, Assert.Single(result).LotId);
    }

    [Fact]
    public void LotsWithoutExpiryUseFirstEntry()
    {
        EntityId productId = EntityId.New();
        StockLot newer = CreateLot(productId, "NEW", expiry: null, AtUtc(2026, 2, 1));
        StockLot older = CreateLot(productId, "OLD", expiry: null, AtUtc(2026, 1, 1));

        IReadOnlyList<StockAllocation> result = FefoAllocator.Allocate(
            2,
            [new(newer, 5), new(older, 5)],
            new DateOnly(2026, 7, 27));

        Assert.Equal(older.Id, Assert.Single(result).LotId);
    }

    [Fact]
    public void InsufficientValidStockRejectsWholeAllocation()
    {
        StockLot lot = CreateLot(
            EntityId.New(),
            "LOT-01",
            ExpiryDate.ForMonth(2027, 12),
            AtUtc(2026, 1, 1));

        Assert.Throws<InsufficientStockException>(() => FefoAllocator.Allocate(
            6,
            [new(lot, 5)],
            new DateOnly(2026, 7, 27)));
    }

    [Fact]
    public void RejectsLotsFromDifferentProducts()
    {
        StockLot first = CreateLot(
            EntityId.New(),
            "LOT-01",
            ExpiryDate.ForMonth(2027, 12),
            AtUtc(2026, 1, 1));
        StockLot second = CreateLot(
            EntityId.New(),
            "LOT-02",
            ExpiryDate.ForMonth(2027, 12),
            AtUtc(2026, 1, 2));

        Assert.Throws<InventoryValidationException>(() => FefoAllocator.Allocate(
            1,
            [new(first, 5), new(second, 5)],
            new DateOnly(2026, 7, 27)));
    }

    private static StockLot CreateLot(
        EntityId productId,
        string number,
        ExpiryDate? expiry,
        UtcInstant firstEntry) => StockLot.Create(
            EntityId.New(),
            productId,
            number,
            expiry,
            supplierId: null,
            Money.Xof(100),
            firstEntry);

    private static UtcInstant AtUtc(int year, int month, int day) => UtcInstant.From(
        new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));
}
