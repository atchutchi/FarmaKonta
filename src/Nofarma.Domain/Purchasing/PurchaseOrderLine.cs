using Nofarma.Domain.Common;

namespace Nofarma.Domain.Purchasing;

public sealed class PurchaseOrderLine
{
    internal PurchaseOrderLine(
        EntityId id,
        EntityId productId,
        EntityId packageId,
        long orderedPackageQuantity,
        long receivedPackageQuantity,
        long factorToBaseUnit,
        long unitCostXof,
        long discountXof,
        string? notes)
    {
        EnsureIdentifier(id, "A linha deve ter um identificador.");
        EnsureIdentifier(productId, "O produto da linha é obrigatório.");
        EnsureIdentifier(packageId, "A embalagem da linha é obrigatória.");
        ValidateValues(
            orderedPackageQuantity,
            receivedPackageQuantity,
            factorToBaseUnit,
            unitCostXof,
            discountXof);
        Id = id;
        ProductId = productId;
        PackageId = packageId;
        OrderedPackageQuantity = orderedPackageQuantity;
        ReceivedPackageQuantity = receivedPackageQuantity;
        FactorToBaseUnit = factorToBaseUnit;
        UnitCostXof = unitCostXof;
        DiscountXof = discountXof;
        Notes = NormalizeOptional(notes);
    }

    public EntityId Id { get; }

    public EntityId ProductId { get; }

    public EntityId PackageId { get; }

    public long OrderedPackageQuantity { get; private set; }

    public long ReceivedPackageQuantity { get; private set; }

    public long FactorToBaseUnit { get; private set; }

    public long UnitCostXof { get; private set; }

    public long DiscountXof { get; private set; }

    public string? Notes { get; private set; }

    public long RemainingPackageQuantity =>
        checked(OrderedPackageQuantity - ReceivedPackageQuantity);

    public long TotalXof => checked(
        checked(OrderedPackageQuantity * UnitCostXof) - DiscountXof);

    internal void Receive(long packageQuantity)
    {
        if (packageQuantity <= 0 || packageQuantity > RemainingPackageQuantity)
        {
            throw new PurchaseValidationException(
                "A quantidade recebida deve ser positiva e não pode exceder a quantidade pendente.");
        }

        ReceivedPackageQuantity = checked(ReceivedPackageQuantity + packageQuantity);
    }

    internal void Update(
        long orderedPackageQuantity,
        long factorToBaseUnit,
        long unitCostXof,
        long discountXof,
        string? notes)
    {
        ValidateValues(
            orderedPackageQuantity,
            ReceivedPackageQuantity,
            factorToBaseUnit,
            unitCostXof,
            discountXof);
        OrderedPackageQuantity = orderedPackageQuantity;
        FactorToBaseUnit = factorToBaseUnit;
        UnitCostXof = unitCostXof;
        DiscountXof = discountXof;
        Notes = NormalizeOptional(notes);
    }

    private static void ValidateValues(
        long orderedPackageQuantity,
        long receivedPackageQuantity,
        long factorToBaseUnit,
        long unitCostXof,
        long discountXof)
    {
        if (orderedPackageQuantity <= 0 ||
            receivedPackageQuantity < 0 ||
            receivedPackageQuantity > orderedPackageQuantity ||
            factorToBaseUnit <= 0 ||
            unitCostXof <= 0 ||
            discountXof < 0)
        {
            throw new PurchaseValidationException("Os valores da linha de compra não são válidos.");
        }

        long gross = checked(orderedPackageQuantity * unitCostXof);
        if (discountXof > gross)
        {
            throw new PurchaseValidationException("O desconto não pode exceder o valor bruto da linha.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new PurchaseValidationException(message);
        }
    }
}
