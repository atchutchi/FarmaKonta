using Nofarma.Domain.Common;

namespace Nofarma.Domain.Catalog;

public sealed class ProductPackage
{
    private ProductPackage(EntityId id, string name, long factorToBaseUnit, bool isBaseUnit)
    {
        Id = id;
        Name = name;
        FactorToBaseUnit = factorToBaseUnit;
        IsBaseUnit = isBaseUnit;
    }

    public EntityId Id { get; }

    public string Name { get; private set; }

    public long FactorToBaseUnit { get; private set; }

    public bool IsBaseUnit { get; }

    internal static ProductPackage Create(
        EntityId id,
        string name,
        long factorToBaseUnit,
        bool isBaseUnit = false)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ProductValidationException("A embalagem deve ter um identificador.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProductValidationException("O nome da embalagem é obrigatório.");
        }

        if (factorToBaseUnit <= 0)
        {
            throw new ProductValidationException("O factor de conversão deve ser um inteiro positivo.");
        }

        if (isBaseUnit && factorToBaseUnit != 1)
        {
            throw new ProductValidationException("A unidade base deve ter factor um.");
        }

        return new ProductPackage(id, name.Trim(), factorToBaseUnit, isBaseUnit);
    }

    internal void ChangeFactor(long factorToBaseUnit)
    {
        if (IsBaseUnit)
        {
            throw new ProductValidationException("O factor da unidade base não pode ser alterado.");
        }

        if (factorToBaseUnit <= 0)
        {
            throw new ProductValidationException("O factor de conversão deve ser um inteiro positivo.");
        }

        FactorToBaseUnit = factorToBaseUnit;
    }

    internal void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProductValidationException("O nome da embalagem é obrigatório.");
        }

        Name = name.Trim();
    }
}
