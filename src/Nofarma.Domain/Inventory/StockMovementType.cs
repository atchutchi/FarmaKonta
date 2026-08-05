namespace Nofarma.Domain.Inventory;

public enum StockMovementType
{
    OpeningInventory = 1,
    PurchaseReceipt = 2,
    QuickEntry = 3,
    PositiveAdjustment = 4,
    NegativeAdjustment = 5,
    Loss = 6,
    Damage = 7,
    Expiration = 8,
    SupplierReturn = 9,
    Compensation = 10,
    Sale = 11
}
