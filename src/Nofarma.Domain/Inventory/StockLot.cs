using Nofarma.Domain.Common;

namespace Nofarma.Domain.Inventory;

public sealed class StockLot
{
    private StockLot(
        EntityId id,
        EntityId productId,
        string number,
        ExpiryDate? expiry,
        EntityId? supplierId,
        Money originCost,
        UtcInstant firstEntryUtc)
    {
        Id = id;
        ProductId = productId;
        Number = number;
        Expiry = expiry;
        SupplierId = supplierId;
        OriginCost = originCost;
        FirstEntryUtc = firstEntryUtc;
    }

    public EntityId Id { get; }

    public EntityId ProductId { get; }

    public string Number { get; }

    public ExpiryDate? Expiry { get; }

    public EntityId? SupplierId { get; }

    public Money OriginCost { get; }

    public UtcInstant FirstEntryUtc { get; }

    public static StockLot Create(
        EntityId id,
        EntityId productId,
        string number,
        ExpiryDate? expiry,
        EntityId? supplierId,
        Money originCost,
        UtcInstant firstEntryUtc)
    {
        EnsureIdentifier(id, "O lote deve ter um identificador.");
        EnsureIdentifier(productId, "O produto do lote é obrigatório.");
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new InventoryValidationException("O número do lote é obrigatório.");
        }

        if (!string.Equals(originCost.Currency, "XOF", StringComparison.Ordinal) ||
            originCost.Amount < 0)
        {
            throw new InventoryValidationException(
                "O custo de origem deve ser um valor XOF não negativo.");
        }

        if (supplierId is { Value: var supplierValue } && supplierValue == Guid.Empty)
        {
            throw new InventoryValidationException("O fornecedor do lote não é válido.");
        }

        return new StockLot(
            id,
            productId,
            number.Trim().ToUpperInvariant(),
            expiry,
            supplierId,
            originCost,
            firstEntryUtc);
    }

    public bool ConflictsWith(StockLot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return ProductId == other.ProductId &&
               string.Equals(Number, other.Number, StringComparison.Ordinal) &&
               Expiry != other.Expiry;
    }

    public bool IsBlockedAt(DateOnly businessDate) =>
        Expiry is { } expiry && businessDate >= expiry.BlockingDate;

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new InventoryValidationException(message);
        }
    }
}
