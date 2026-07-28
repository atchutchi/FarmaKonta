using Nofarma.Domain.Common;

namespace Nofarma.Domain.Sales;

public sealed class CashMovement
{
    private CashMovement(
        EntityId id,
        EntityId cashShiftId,
        CashMovementType type,
        Money amount,
        EntityId? sourceSaleId,
        string reason,
        UtcInstant occurredAt)
    {
        Id = id;
        CashShiftId = cashShiftId;
        Type = type;
        Amount = amount;
        SourceSaleId = sourceSaleId;
        Reason = reason;
        OccurredAt = occurredAt;
    }

    public EntityId Id { get; }

    public EntityId CashShiftId { get; }

    public CashMovementType Type { get; }

    public Money Amount { get; }

    public EntityId? SourceSaleId { get; }

    public string Reason { get; }

    public UtcInstant OccurredAt { get; }

    internal static CashMovement Create(
        EntityId id,
        EntityId cashShiftId,
        CashMovementType type,
        Money amount,
        EntityId? sourceSaleId,
        string reason,
        UtcInstant occurredAt)
    {
        EnsureIdentifier(id, "O movimento de caixa deve ter um identificador.");
        EnsureIdentifier(cashShiftId, "O turno de caixa é obrigatório.");
        if (!Enum.IsDefined(type))
        {
            throw new SalesValidationException("O tipo de movimento de caixa não é válido.");
        }

        EnsureXof(amount, "O valor do movimento deve ser registado em XOF.");
        if (amount.Amount <= 0)
        {
            throw new SalesValidationException("O valor do movimento de caixa deve ser positivo.");
        }

        bool saleRelated = type is CashMovementType.Sale or CashMovementType.Refund;
        if (saleRelated &&
            (sourceSaleId is not { Value: var saleValue } || saleValue == Guid.Empty))
        {
            throw new SalesValidationException("O movimento deve identificar a venda de origem.");
        }

        if (!saleRelated && sourceSaleId is not null)
        {
            throw new SalesValidationException("Um movimento manual não pode indicar uma venda.");
        }

        if (type is CashMovementType.ManualEntry or CashMovementType.ManualExit &&
            string.IsNullOrWhiteSpace(reason))
        {
            throw new SalesValidationException("O motivo do movimento manual é obrigatório.");
        }

        if (reason is not null && reason.Trim().Length > 500)
        {
            throw new SalesValidationException("O motivo do movimento não pode exceder 500 caracteres.");
        }

        if (occurredAt.Value == default)
        {
            throw new SalesValidationException("A data do movimento de caixa é obrigatória.");
        }

        return new CashMovement(
            id,
            cashShiftId,
            type,
            amount,
            sourceSaleId,
            reason?.Trim() ?? string.Empty,
            occurredAt);
    }

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new SalesValidationException(message);
        }
    }

    private static void EnsureXof(Money amount, string message)
    {
        if (!string.Equals(amount.Currency, "XOF", StringComparison.Ordinal))
        {
            throw new SalesValidationException(message);
        }
    }
}
