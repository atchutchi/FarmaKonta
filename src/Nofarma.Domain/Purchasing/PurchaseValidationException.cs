namespace Nofarma.Domain.Purchasing;

public sealed class PurchaseValidationException(string message) : Exception(message);
