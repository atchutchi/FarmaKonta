namespace Nofarma.Application.Catalog;

public sealed class CatalogConflictException(string message) : InvalidOperationException(message);
