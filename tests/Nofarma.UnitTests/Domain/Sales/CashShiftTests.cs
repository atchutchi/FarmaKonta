using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.UnitTests.Domain.Sales;

public sealed class CashShiftTests
{
    [Fact]
    public void OpenShiftTracksExpectedCashAndClosingDifference()
    {
        CashShift shift = CreateShift(openingCashXof: 50_000);
        UtcInstant movementTime = AtUtc(2026, 7, 28, 9);

        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.Sale,
            Money.Xof(12_500),
            EntityId.New(),
            "Venda",
            movementTime);
        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(2_000),
            sourceSaleId: null,
            "Reforço",
            movementTime);
        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualExit,
            Money.Xof(500),
            sourceSaleId: null,
            "Transporte",
            movementTime);

        shift.Close(Money.Xof(64_250), AtUtc(2026, 7, 28, 16));

        Assert.Equal(CashShiftStatus.Closed, shift.Status);
        Assert.Equal(64_000, shift.ExpectedCash.Amount);
        Assert.Equal(14_500, shift.TotalEntries.Amount);
        Assert.Equal(500, shift.TotalExits.Amount);
        Assert.Equal(64_250, shift.CountedCash!.Value.Amount);
        Assert.Equal(250, shift.Difference!.Value.Amount);
        Assert.Equal(3, shift.Movements.Count);
    }

    [Fact]
    public void RefundAndManualExitReduceExpectedCash()
    {
        CashShift shift = CreateShift(openingCashXof: 10_000);
        UtcInstant occurredAt = AtUtc(2026, 7, 28, 10);

        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.Refund,
            Money.Xof(1_500),
            EntityId.New(),
            "Reembolso",
            occurredAt);
        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualExit,
            Money.Xof(500),
            sourceSaleId: null,
            "Despesa autorizada",
            occurredAt);

        Assert.Equal(8_000, shift.ExpectedCash.Amount);
        Assert.Equal(0, shift.TotalEntries.Amount);
        Assert.Equal(2_000, shift.TotalExits.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OpenRejectsEveryEmptyContextIdentifier(int emptyIndex)
    {
        EntityId[] identifiers = [EntityId.New(), EntityId.New(), EntityId.New(), EntityId.New()];
        identifiers[emptyIndex] = new EntityId(Guid.Empty);

        Assert.Throws<SalesValidationException>(() => CashShift.Open(
            identifiers[0],
            identifiers[1],
            identifiers[2],
            identifiers[3],
            Money.Xof(0),
            AtUtc(2026, 7, 28, 8)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-50_000)]
    public void OpenRejectsNegativeCash(long amount)
    {
        Assert.Throws<SalesValidationException>(() => CreateShift(amount));
    }

    [Fact]
    public void OpenRejectsCurrencyOtherThanXof()
    {
        Assert.Throws<SalesValidationException>(() => CashShift.Open(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            new Money(100, "EUR"),
            AtUtc(2026, 7, 28, 8)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MovementRejectsNonPositiveAmount(long amount)
    {
        CashShift shift = CreateShift();

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(amount),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 9)));
    }

    [Fact]
    public void MovementRejectsInvalidIdentifierTypeCurrencyAndTimestamp()
    {
        CashShift shift = CreateShift();

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            new EntityId(Guid.Empty),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 9)));
        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            (CashMovementType)999,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 9)));
        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            new Money(1_000, "EUR"),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 9)));
        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            default));

        Assert.Empty(shift.Movements);
        Assert.Equal(0, shift.ExpectedCash.Amount);
    }

    [Theory]
    [InlineData(CashMovementType.ManualEntry)]
    [InlineData(CashMovementType.ManualExit)]
    public void ManualMovementRequiresReason(CashMovementType type)
    {
        CashShift shift = CreateShift();

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            type,
            Money.Xof(1_000),
            sourceSaleId: null,
            " ",
            AtUtc(2026, 7, 28, 9)));
    }

    [Theory]
    [InlineData(CashMovementType.Sale)]
    [InlineData(CashMovementType.Refund)]
    public void SaleRelatedMovementRequiresSourceSale(CashMovementType type)
    {
        CashShift shift = CreateShift();

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            type,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Venda",
            AtUtc(2026, 7, 28, 9)));
    }

    [Theory]
    [InlineData(CashMovementType.Sale)]
    [InlineData(CashMovementType.Refund)]
    public void SaleRelatedMovementRejectsEmptySourceSale(CashMovementType type)
    {
        CashShift shift = CreateShift(openingCashXof: 1_000);

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            type,
            Money.Xof(1_000),
            new EntityId(Guid.Empty),
            "Venda",
            AtUtc(2026, 7, 28, 9)));
    }

    [Fact]
    public void MovementCannotPrecedeShiftOpening()
    {
        CashShift shift = CreateShift();

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 7)));
    }

    [Fact]
    public void DuplicateMovementIdentifierIsRejected()
    {
        CashShift shift = CreateShift();
        EntityId movementId = EntityId.New();
        UtcInstant occurredAt = AtUtc(2026, 7, 28, 9);
        shift.RecordMovement(
            movementId,
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            occurredAt);

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            movementId,
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            occurredAt));
    }

    [Fact]
    public void MovementCannotPrecedePreviousMovement()
    {
        CashShift shift = CreateShift();
        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 12));

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 11)));
        Assert.Single(shift.Movements);
        Assert.Equal(1_000, shift.ExpectedCash.Amount);
    }

    [Fact]
    public void MovementCannotMakeExpectedCashNegative()
    {
        CashShift shift = CreateShift(openingCashXof: 500);

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualExit,
            Money.Xof(501),
            sourceSaleId: null,
            "Saída",
            AtUtc(2026, 7, 28, 9)));
        Assert.Empty(shift.Movements);
        Assert.Equal(500, shift.ExpectedCash.Amount);
    }

    [Fact]
    public void MovementsCannotBeMutatedThroughExposedCollection()
    {
        CashShift shift = CreateShift();
        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 9));
        var exposed = Assert.IsAssignableFrom<IList<CashMovement>>(shift.Movements);

        Assert.Throws<NotSupportedException>(exposed.Clear);
        Assert.Single(shift.Movements);
        Assert.Equal(1_000, shift.ExpectedCash.Amount);
    }

    [Fact]
    public void OverflowIsReportedWithoutMutatingShift()
    {
        CashShift shift = CreateShift(long.MaxValue);

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.Sale,
            Money.Xof(1),
            EntityId.New(),
            "Venda",
            AtUtc(2026, 7, 28, 9)));
        Assert.Empty(shift.Movements);
        Assert.Equal(long.MaxValue, shift.ExpectedCash.Amount);
    }

    [Fact]
    public void ManualMovementRejectsReasonLongerThanFiveHundredCharacters()
    {
        CashShift shift = CreateShift();

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            new string('a', 501),
            AtUtc(2026, 7, 28, 9)));
    }

    [Fact]
    public void ClosedShiftRejectsMovementsAndSecondClose()
    {
        CashShift shift = CreateShift();
        shift.Close(Money.Xof(0), AtUtc(2026, 7, 28, 16));

        Assert.Throws<SalesValidationException>(() => shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 17)));
        Assert.Throws<SalesValidationException>(() =>
            shift.Close(Money.Xof(0), AtUtc(2026, 7, 28, 17)));
    }

    [Fact]
    public void CloseRejectsNegativeCashAndTimeBeforeLastMovement()
    {
        CashShift shift = CreateShift();
        shift.RecordMovement(
            EntityId.New(),
            CashMovementType.ManualEntry,
            Money.Xof(1_000),
            sourceSaleId: null,
            "Reforço",
            AtUtc(2026, 7, 28, 12));

        Assert.Throws<SalesValidationException>(() =>
            shift.Close(Money.Xof(-1), AtUtc(2026, 7, 28, 16)));
        Assert.Throws<SalesValidationException>(() =>
            shift.Close(Money.Xof(1_000), AtUtc(2026, 7, 28, 11)));
    }

    [Fact]
    public void CloseRejectsWrongCurrencyAndDefaultTimestampWithoutMutation()
    {
        CashShift shift = CreateShift(openingCashXof: 1_000);

        Assert.Throws<SalesValidationException>(() =>
            shift.Close(new Money(1_000, "EUR"), AtUtc(2026, 7, 28, 16)));
        Assert.Throws<SalesValidationException>(() =>
            shift.Close(Money.Xof(1_000), default));

        Assert.Equal(CashShiftStatus.Open, shift.Status);
        Assert.Null(shift.CountedCash);
        Assert.Null(shift.Difference);
        Assert.Null(shift.ClosedAt);
    }

    private static CashShift CreateShift(long openingCashXof = 0) => CashShift.Open(
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        Money.Xof(openingCashXof),
        AtUtc(2026, 7, 28, 8));

    private static UtcInstant AtUtc(int year, int month, int day, int hour) => UtcInstant.From(
        new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero));
}
