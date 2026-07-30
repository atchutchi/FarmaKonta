using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Domain.Inventory;

public sealed class StockLotTests
{
    [Fact]
    public void CreateRejectsBlankLotNumber()
    {
        Assert.Throws<InventoryValidationException>(() => StockLot.Create(
            EntityId.New(),
            EntityId.New(),
            " ",
            ExpiryDate.ForMonth(2027, 12),
            supplierId: null,
            Money.Xof(100),
            AtUtc(2026, 7, 27)));
    }

    [Fact]
    public void CreateRejectsNegativeOriginCost()
    {
        Assert.Throws<InventoryValidationException>(() => StockLot.Create(
            EntityId.New(),
            EntityId.New(),
            "LOT-01",
            ExpiryDate.ForMonth(2027, 12),
            supplierId: null,
            Money.Xof(-1),
            AtUtc(2026, 7, 27)));
    }

    [Fact]
    public void SameProductAndNumberWithDifferentExpiryIsConflict()
    {
        EntityId productId = EntityId.New();
        StockLot first = CreateLot(productId, "LOT-42", ExpiryDate.ForMonth(2027, 12));
        StockLot second = CreateLot(productId, "LOT-42", ExpiryDate.ForMonth(2028, 1));

        Assert.True(first.ConflictsWith(second));
    }

    [Fact]
    public void SameNumberOnDifferentProductsIsNotConflict()
    {
        StockLot first = CreateLot(EntityId.New(), "LOT-42", ExpiryDate.ForMonth(2027, 12));
        StockLot second = CreateLot(EntityId.New(), "LOT-42", ExpiryDate.ForMonth(2028, 1));

        Assert.False(first.ConflictsWith(second));
    }

    [Fact]
    public void LotIsBlockedOnItsBlockingDate()
    {
        StockLot lot = CreateLot(
            EntityId.New(),
            "LOT-99",
            ExpiryDate.ForDay(2026, 8, 15));

        Assert.False(lot.IsBlockedAt(new DateOnly(2026, 8, 14)));
        Assert.True(lot.IsBlockedAt(new DateOnly(2026, 8, 15)));
    }

    private static StockLot CreateLot(
        EntityId productId,
        string number,
        ExpiryDate expiry) => StockLot.Create(
            EntityId.New(),
            productId,
            number,
            expiry,
            EntityId.New(),
            Money.Xof(100),
            AtUtc(2026, 7, 27));

    private static UtcInstant AtUtc(int year, int month, int day) => UtcInstant.From(
        new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));
}
