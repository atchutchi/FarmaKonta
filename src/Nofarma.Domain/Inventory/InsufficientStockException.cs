namespace Nofarma.Domain.Inventory;

public sealed class InsufficientStockException(string message) : InvalidOperationException(message);
