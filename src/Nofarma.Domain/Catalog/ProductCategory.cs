using Nofarma.Domain.Common;

namespace Nofarma.Domain.Catalog;

public sealed class ProductCategory
{
    private ProductCategory(EntityId id, EntityId pharmacyId, string name)
    {
        Id = id;
        PharmacyId = pharmacyId;
        Name = name;
        IsActive = true;
    }

    public EntityId Id { get; }

    public EntityId PharmacyId { get; }

    public string Name { get; }

    public bool IsActive { get; private set; }

    public static ProductCategory Create(EntityId id, EntityId pharmacyId, string name)
    {
        EnsureIdentifier(id, "A categoria deve ter um identificador.");
        EnsureIdentifier(pharmacyId, "A farmácia deve ter um identificador.");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProductValidationException("O nome da categoria é obrigatório.");
        }

        return new ProductCategory(id, pharmacyId, name.Trim());
    }

    public void Deactivate() => IsActive = false;

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ProductValidationException(message);
        }
    }
}
