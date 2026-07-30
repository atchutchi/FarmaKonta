using Nofarma.Domain.Common;

namespace Nofarma.Domain.Catalog;

public sealed class ProductBarcode
{
    private ProductBarcode(EntityId id, EntityId packageId, string value)
    {
        Id = id;
        PackageId = packageId;
        Value = value;
    }

    public EntityId Id { get; }

    public EntityId PackageId { get; }

    public string Value { get; }

    internal static ProductBarcode Create(EntityId id, EntityId packageId, string value)
    {
        if (id.Value == Guid.Empty || packageId.Value == Guid.Empty)
        {
            throw new ProductValidationException("O código de barras deve identificar a embalagem.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProductValidationException("O código de barras é obrigatório.");
        }

        return new ProductBarcode(id, packageId, value.Trim());
    }
}
