namespace Nofarma.Domain.Supply;

public sealed class SupplierValidationException(string message) : ArgumentException(message);
