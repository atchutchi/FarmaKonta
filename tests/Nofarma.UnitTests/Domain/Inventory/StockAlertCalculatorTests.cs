using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Domain.Inventory;

public sealed class StockAlertCalculatorTests
{
    [Theory]
    [InlineData(11, StockAlertLevel.Normal)]
    [InlineData(10, StockAlertLevel.LowStock)]
    [InlineData(1, StockAlertLevel.LowStock)]
    [InlineData(0, StockAlertLevel.OutOfStock)]
    public void StockUsesGlobalThresholdOfTen(long quantity, StockAlertLevel expected)
    {
        StockAlert alert = StockAlertCalculator.CalculateStock(quantity, minimumStock: null);

        Assert.Equal(expected, alert.Level);
        Assert.Equal(10, alert.Threshold);
    }

    [Fact]
    public void ProductThresholdOverridesGlobalThreshold()
    {
        StockAlert alert = StockAlertCalculator.CalculateStock(15, minimumStock: 20);

        Assert.Equal(StockAlertLevel.LowStock, alert.Level);
        Assert.Equal(20, alert.Threshold);
    }

    [Theory]
    [InlineData(91, StockAlertLevel.Normal)]
    [InlineData(90, StockAlertLevel.ExpiryAttention)]
    [InlineData(60, StockAlertLevel.ExpiryPriority)]
    [InlineData(30, StockAlertLevel.ExpiryCritical)]
    [InlineData(0, StockAlertLevel.Expired)]
    public void ExpiryUsesExactAlertBoundaries(int daysUntilBlock, StockAlertLevel expected)
    {
        DateOnly today = new(2026, 7, 27);
        ExpiryDate expiry = ExpiryDate.ForDay(
            today.AddDays(daysUntilBlock).Year,
            today.AddDays(daysUntilBlock).Month,
            today.AddDays(daysUntilBlock).Day);

        StockAlert alert = StockAlertCalculator.CalculateExpiry(expiry, today);

        Assert.Equal(expected, alert.Level);
        Assert.Equal(daysUntilBlock, alert.DaysUntilBlock);
    }

    [Fact]
    public void StockCalculationRejectsNegativeQuantity()
    {
        Assert.Throws<InventoryValidationException>(() =>
            StockAlertCalculator.CalculateStock(-1, minimumStock: null));
    }

    [Fact]
    public void StockCalculationRejectsNegativeThreshold()
    {
        Assert.Throws<InventoryValidationException>(() =>
            StockAlertCalculator.CalculateStock(10, minimumStock: -1));
    }

    [Fact]
    public void ProductWithoutExpiryHasNoExpiryAlert()
    {
        StockAlert alert = StockAlertCalculator.CalculateExpiry(
            expiry: null,
            new DateOnly(2026, 7, 27));

        Assert.Equal(StockAlertLevel.Normal, alert.Level);
        Assert.Null(alert.DaysUntilBlock);
    }
}
