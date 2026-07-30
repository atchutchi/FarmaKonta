namespace Nofarma.Domain.Inventory;

public enum StockAlertLevel
{
    Normal = 0,
    LowStock = 1,
    OutOfStock = 2,
    ExpiryAttention = 3,
    ExpiryPriority = 4,
    ExpiryCritical = 5,
    Expired = 6
}
