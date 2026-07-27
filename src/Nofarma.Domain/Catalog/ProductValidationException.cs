namespace Nofarma.Domain.Catalog;

public sealed class ProductValidationException(string message) : ArgumentException(message);
