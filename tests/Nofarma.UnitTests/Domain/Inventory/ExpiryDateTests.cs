using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Domain.Inventory;

public sealed class ExpiryDateTests
{
    private static readonly TimeZoneInfo BissauTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Africa/Bissau");

    [Fact]
    public void MonthExpiryBlocksAtStartOfFollowingMonth()
    {
        ExpiryDate expiry = ExpiryDate.ForMonth(2028, 2);

        DateTimeOffset blockingInstant = expiry.GetBlockingInstant(BissauTimeZone);

        Assert.Equal(
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero),
            blockingInstant);
    }

    [Fact]
    public void ExactDayBlocksAtStartOfThatDay()
    {
        ExpiryDate expiry = ExpiryDate.ForDay(2026, 8, 15);

        DateTimeOffset blockingInstant = expiry.GetBlockingInstant(BissauTimeZone);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            blockingInstant);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void ForMonthRejectsInvalidMonth(int year, int month)
    {
        Assert.Throws<InventoryValidationException>(() => ExpiryDate.ForMonth(year, month));
    }

    [Fact]
    public void ForDayRejectsInvalidCalendarDate()
    {
        Assert.Throws<InventoryValidationException>(() => ExpiryDate.ForDay(2026, 2, 30));
    }
}
