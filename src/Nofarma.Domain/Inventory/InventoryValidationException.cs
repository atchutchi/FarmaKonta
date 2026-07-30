namespace Nofarma.Domain.Inventory;

public sealed class InventoryValidationException(string message) : ArgumentException(message);
