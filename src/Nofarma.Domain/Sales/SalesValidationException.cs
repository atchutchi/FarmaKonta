namespace Nofarma.Domain.Sales;

public sealed class SalesValidationException(string message) : Exception(message);
