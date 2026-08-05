using Nofarma.Domain.Common;

namespace Nofarma.Domain.Sales;

public sealed class SaleLine
{
    private SaleLine(
        EntityId id,
        EntityId productId,
        EntityId packageId,
        string description,
        string unitName,
        long packageFactor,
        long quantityPackages,
        Money unitPrice,
        Money gross,
        Money discount,
        Money net,
        Money capturedCost,
        EntityId? discountAuthorizedByUserId)
    {
        Id = id;
        ProductId = productId;
        PackageId = packageId;
        Description = description;
        UnitName = unitName;
        PackageFactor = packageFactor;
        QuantityPackages = quantityPackages;
        QuantityBase = checked(packageFactor * quantityPackages);
        UnitPrice = unitPrice;
        Gross = gross;
        Discount = discount;
        Net = net;
        CapturedCost = capturedCost;
        DiscountAuthorizedByUserId = discountAuthorizedByUserId;
    }

    public EntityId Id { get; }
    public EntityId ProductId { get; }
    public EntityId PackageId { get; }
    public string Description { get; }
    public string UnitName { get; }
    public long PackageFactor { get; }
    public long QuantityPackages { get; }
    public long QuantityBase { get; }
    public Money UnitPrice { get; }
    public Money Gross { get; }
    public Money Discount { get; }
    public Money Net { get; }
    public Money CapturedCost { get; }
    public EntityId? DiscountAuthorizedByUserId { get; }

    public static SaleLine Create(
        EntityId id,
        EntityId productId,
        EntityId packageId,
        string description,
        string unitName,
        long packageFactor,
        long quantityPackages,
        Money unitPrice,
        Money discount,
        Money capturedCost,
        EntityId? discountAuthorizedByUserId)
    {
        EnsureIdentifier(id, "A linha da venda deve ter um identificador.");
        EnsureIdentifier(productId, "O produto da linha é obrigatório.");
        EnsureIdentifier(packageId, "A embalagem da linha é obrigatória.");
        string normalizedDescription = NormalizeRequired(description, 240, "A descrição do produto");
        string normalizedUnitName = NormalizeRequired(unitName, 80, "A unidade de venda");
        if (packageFactor <= 0)
        {
            throw new SalesValidationException("O factor da embalagem deve ser positivo.");
        }
        if (quantityPackages <= 0)
        {
            throw new SalesValidationException("A quantidade da venda deve ser positiva.");
        }

        EnsureXofNonNegative(unitPrice, "O preço unitário");
        EnsureXofNonNegative(discount, "O desconto da linha");
        EnsureXofNonNegative(capturedCost, "O custo capturado");
        if (discount.Amount > 0)
        {
            if (discountAuthorizedByUserId is null)
            {
                throw new SalesValidationException("O desconto da linha exige autorização.");
            }
            EnsureIdentifier(
                discountAuthorizedByUserId.Value,
                "O utilizador que autorizou o desconto não é válido.");
        }

        try
        {
            _ = checked(packageFactor * quantityPackages);
            long grossAmount = checked(unitPrice.Amount * quantityPackages);
            if (discount.Amount > grossAmount)
            {
                throw new SalesValidationException(
                    "O desconto da linha não pode exceder o valor bruto.");
            }

            return new SaleLine(
                id,
                productId,
                packageId,
                normalizedDescription,
                normalizedUnitName,
                packageFactor,
                quantityPackages,
                unitPrice,
                Money.Xof(grossAmount),
                discount,
                Money.Xof(grossAmount - discount.Amount),
                capturedCost,
                discountAuthorizedByUserId);
        }
        catch (OverflowException exception)
        {
            throw new SalesValidationException(
                $"A quantidade ou o valor da linha excede o limite permitido: {exception.Message}");
        }
    }

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new SalesValidationException(message);
        }
    }

    private static void EnsureXofNonNegative(Money money, string field)
    {
        if (!string.Equals(money.Currency, "XOF", StringComparison.Ordinal) || money.Amount < 0)
        {
            throw new SalesValidationException($"{field} deve ser um valor XOF não negativo.");
        }
    }

    private static string NormalizeRequired(string value, int maxLength, string field)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
        {
            throw new SalesValidationException(
                $"{field} é obrigatório e não pode exceder {maxLength} caracteres.");
        }
        return normalized;
    }
}
