namespace Nofarma.Domain.Inventory;

public static class StockAlertCalculator
{
    public const long GlobalMinimumStock = 10;

    public static StockAlert CalculateStock(long quantityBase, long? minimumStock)
    {
        if (quantityBase < 0)
        {
            throw new InventoryValidationException("A quantidade de stock não pode ser negativa.");
        }

        if (minimumStock is < 0)
        {
            throw new InventoryValidationException("O stock mínimo não pode ser negativo.");
        }

        long threshold = minimumStock ?? GlobalMinimumStock;
        StockAlertLevel level = quantityBase switch
        {
            0 => StockAlertLevel.OutOfStock,
            _ when quantityBase <= threshold => StockAlertLevel.LowStock,
            _ => StockAlertLevel.Normal
        };

        return new StockAlert(level, quantityBase, threshold, null, null);
    }

    public static StockAlert CalculateExpiry(ExpiryDate? expiry, DateOnly businessDate)
    {
        if (expiry is null)
        {
            return new StockAlert(StockAlertLevel.Normal, null, null, null, null);
        }

        int daysUntilBlock = expiry.Value.BlockingDate.DayNumber - businessDate.DayNumber;
        StockAlertLevel level = daysUntilBlock switch
        {
            <= 0 => StockAlertLevel.Expired,
            <= 30 => StockAlertLevel.ExpiryCritical,
            <= 60 => StockAlertLevel.ExpiryPriority,
            <= 90 => StockAlertLevel.ExpiryAttention,
            _ => StockAlertLevel.Normal
        };

        return new StockAlert(
            level,
            null,
            null,
            daysUntilBlock,
            expiry.Value.BlockingDate);
    }
}
