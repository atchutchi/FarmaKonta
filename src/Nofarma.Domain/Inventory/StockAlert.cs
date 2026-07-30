namespace Nofarma.Domain.Inventory;

public sealed record StockAlert(
    StockAlertLevel Level,
    long? QuantityBase,
    long? Threshold,
    int? DaysUntilBlock,
    DateOnly? BlockingDate);
