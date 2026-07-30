using System.Collections.ObjectModel;
using Nofarma.Domain.Common;

namespace Nofarma.Domain.Sales;

public sealed class CashShift
{
    private readonly List<CashMovement> _movements = [];
    private readonly ReadOnlyCollection<CashMovement> _readOnlyMovements;
    private long _expectedCashXof;
    private long _totalEntriesXof;
    private long _totalExitsXof;

    private CashShift(
        EntityId id,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        Money openingCash,
        UtcInstant openedAt)
    {
        Id = id;
        PharmacyId = pharmacyId;
        DeviceId = deviceId;
        UserId = userId;
        OpeningCash = openingCash;
        OpenedAt = openedAt;
        Status = CashShiftStatus.Open;
        _expectedCashXof = openingCash.Amount;
        _readOnlyMovements = _movements.AsReadOnly();
    }

    public EntityId Id { get; }

    public EntityId PharmacyId { get; }

    public EntityId DeviceId { get; }

    public EntityId UserId { get; }

    public Money OpeningCash { get; }

    public UtcInstant OpenedAt { get; }

    public CashShiftStatus Status { get; private set; }

    public Money ExpectedCash => Money.Xof(_expectedCashXof);

    public Money TotalEntries => Money.Xof(_totalEntriesXof);

    public Money TotalExits => Money.Xof(_totalExitsXof);

    public Money? CountedCash { get; private set; }

    public Money? Difference { get; private set; }

    public UtcInstant? ClosedAt { get; private set; }

    public IReadOnlyList<CashMovement> Movements => _readOnlyMovements;

    public static CashShift Open(
        EntityId id,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        Money openingCash,
        UtcInstant openedAt)
    {
        EnsureIdentifier(id, "O turno de caixa deve ter um identificador.");
        EnsureIdentifier(pharmacyId, "A farmácia do turno é obrigatória.");
        EnsureIdentifier(deviceId, "O posto de caixa é obrigatório.");
        EnsureIdentifier(userId, "O utilizador do turno é obrigatório.");
        EnsureXof(openingCash, "O fundo inicial deve ser registado em XOF.");
        if (openingCash.Amount < 0)
        {
            throw new SalesValidationException("O fundo inicial não pode ser negativo.");
        }

        if (openedAt.Value == default)
        {
            throw new SalesValidationException("A data de abertura do turno é obrigatória.");
        }

        return new CashShift(id, pharmacyId, deviceId, userId, openingCash, openedAt);
    }

    public CashMovement RecordMovement(
        EntityId movementId,
        CashMovementType type,
        Money amount,
        EntityId? sourceSaleId,
        string reason,
        UtcInstant occurredAt)
    {
        EnsureOpen();
        if (occurredAt.Value < OpenedAt.Value)
        {
            throw new SalesValidationException("O movimento não pode ser anterior à abertura do turno.");
        }

        if (_movements.Count > 0 && occurredAt.Value < _movements[^1].OccurredAt.Value)
        {
            throw new SalesValidationException("Os movimentos do turno devem respeitar a ordem temporal.");
        }

        if (_movements.Any(movement => movement.Id == movementId))
        {
            throw new SalesValidationException("O movimento de caixa já existe neste turno.");
        }

        CashMovement movement = CashMovement.Create(
            movementId,
            Id,
            type,
            amount,
            sourceSaleId,
            reason,
            occurredAt);
        long expected;
        long totalEntries = _totalEntriesXof;
        long totalExits = _totalExitsXof;
        try
        {
            bool decreasesCash = type is CashMovementType.ManualExit or CashMovementType.Refund;
            long signedAmount = decreasesCash ? checked(-amount.Amount) : amount.Amount;
            expected = checked(_expectedCashXof + signedAmount);
            if (decreasesCash)
            {
                totalExits = checked(_totalExitsXof + amount.Amount);
            }
            else
            {
                totalEntries = checked(_totalEntriesXof + amount.Amount);
            }
        }
        catch (OverflowException)
        {
            throw new SalesValidationException("O movimento excede o limite monetário suportado.");
        }
        if (expected < 0)
        {
            throw new SalesValidationException("O movimento excede o dinheiro esperado no turno.");
        }

        _movements.Add(movement);
        _expectedCashXof = expected;
        _totalEntriesXof = totalEntries;
        _totalExitsXof = totalExits;
        return movement;
    }

    public void Close(Money countedCash, UtcInstant closedAt)
    {
        EnsureOpen();
        EnsureXof(countedCash, "O valor contado deve ser registado em XOF.");
        if (countedCash.Amount < 0)
        {
            throw new SalesValidationException("O valor contado não pode ser negativo.");
        }

        DateTimeOffset lastActivity = _movements.Count == 0
            ? OpenedAt.Value
            : _movements[^1].OccurredAt.Value;
        if (closedAt.Value == default || closedAt.Value < lastActivity)
        {
            throw new SalesValidationException("O fecho não pode ser anterior à última operação do turno.");
        }

        CountedCash = countedCash;
        Difference = Money.Xof(checked(countedCash.Amount - _expectedCashXof));
        ClosedAt = closedAt;
        Status = CashShiftStatus.Closed;
    }

    private void EnsureOpen()
    {
        if (Status != CashShiftStatus.Open)
        {
            throw new SalesValidationException("O turno de caixa já está fechado.");
        }
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
