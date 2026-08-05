using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.UnitTests.Domain.Sales;

public sealed class SaleTests
{
    [Fact]
    public void LineCalculatesGrossDiscountAndNetAmounts()
    {
        SaleLine line = CreateLine(quantityPackages: 2, unitPriceXof: 1_500, discountXof: 200);

        Assert.Equal(3_000, line.Gross.Amount);
        Assert.Equal(200, line.Discount.Amount);
        Assert.Equal(2_800, line.Net.Amount);
        Assert.Equal(4, line.QuantityBase);
        Assert.Equal(1_100, line.CapturedCost.Amount);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(1, 1, -1, 0)]
    [InlineData(1, 1, 1_001, 0)]
    [InlineData(1, 1, 0, -1)]
    public void LineRejectsInvalidQuantityFactorDiscountAndCost(
        long quantity,
        long factor,
        long discount,
        long capturedCost)
    {
        Assert.Throws<SalesValidationException>(() => CreateLine(
            quantityPackages: quantity,
            packageFactor: factor,
            discountXof: discount,
            capturedCostXof: capturedCost));
    }

    [Fact]
    public void PositiveLineDiscountRequiresAnAuthorizer()
    {
        Assert.Throws<SalesValidationException>(() => SaleLine.Create(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            "Amoxicilina 500 mg",
            "Caixa",
            1,
            1,
            Money.Xof(1_000),
            Money.Xof(100),
            Money.Xof(500),
            discountAuthorizedByUserId: null));
    }

    [Fact]
    public void CashPaymentCanExceedTotalAndProducesChange()
    {
        Sale sale = CreateSale(
            [CreateLine(quantityPackages: 2, unitPriceXof: 1_500, discountXof: 200)],
            [CreatePayment(PaymentMethod.Cash, 3_000)]);

        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(2_800, sale.Total.Amount);
        Assert.Equal(3_000, sale.Paid.Amount);
        Assert.Equal(3_000, sale.CashReceived.Amount);
        Assert.Equal(200, sale.Change.Amount);
    }

    [Fact]
    public void MixedPaymentProducesChangeOnlyFromCashTendered()
    {
        Sale sale = CreateSale(
            [CreateLine(quantityPackages: 2, unitPriceXof: 1_500, discountXof: 200)],
            [
                CreatePayment(PaymentMethod.Card, 1_000, "POS-123"),
                CreatePayment(PaymentMethod.Cash, 2_000)
            ]);

        Assert.Equal(3_000, sale.Paid.Amount);
        Assert.Equal(2_000, sale.CashReceived.Amount);
        Assert.Equal(200, sale.Change.Amount);
    }

    [Fact]
    public void NonCashPaymentCannotExceedTotal()
    {
        Assert.Throws<SalesValidationException>(() => CreateSale(
            [CreateLine(quantityPackages: 2, unitPriceXof: 1_500, discountXof: 200)],
            [CreatePayment(PaymentMethod.Card, 3_000)]));
    }

    [Fact]
    public void PaymentsCannotBeLowerThanTotal()
    {
        Assert.Throws<SalesValidationException>(() => CreateSale(
            [CreateLine(quantityPackages: 2, unitPriceXof: 1_500, discountXof: 200)],
            [CreatePayment(PaymentMethod.Cash, 2_700)]));
    }

    [Fact]
    public void TotalDiscountReducesFinalTotalAndRequiresAuthorizer()
    {
        Sale sale = CreateSale(
            [CreateLine(quantityPackages: 2, unitPriceXof: 1_500, discountXof: 200)],
            [CreatePayment(PaymentMethod.Cash, 2_500)],
            totalDiscountXof: 300,
            totalDiscountAuthorizedByUserId: EntityId.New());

        Assert.Equal(3_000, sale.GrossSubtotal.Amount);
        Assert.Equal(200, sale.LineDiscountTotal.Amount);
        Assert.Equal(300, sale.TotalDiscount.Amount);
        Assert.Equal(2_500, sale.Total.Amount);
    }

    [Fact]
    public void TotalDiscountCannotExceedSubtotalAfterLineDiscounts()
    {
        Assert.Throws<SalesValidationException>(() => CreateSale(
            [CreateLine(unitPriceXof: 1_000)],
            [CreatePayment(PaymentMethod.Cash, 1)],
            totalDiscountXof: 1_001,
            totalDiscountAuthorizedByUserId: EntityId.New()));
    }

    [Fact]
    public void PositiveTotalDiscountRequiresAnAuthorizer()
    {
        Assert.Throws<SalesValidationException>(() => CreateSale(
            [CreateLine(unitPriceXof: 1_000)],
            [CreatePayment(PaymentMethod.Cash, 900)],
            totalDiscountXof: 100));
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, "   ", null)]
    [InlineData(PaymentMethod.Card, "  POS-123  ", "POS-123")]
    public void PaymentNormalizesOptionalReference(
        PaymentMethod method,
        string reference,
        string? expected)
    {
        SalePayment payment = CreatePayment(method, 100, reference);

        Assert.Equal(expected, payment.Reference);
    }

    [Fact]
    public void PaymentRejectsLongReference()
    {
        Assert.Throws<SalesValidationException>(() =>
            CreatePayment(PaymentMethod.MobileMoney, 100, new string('a', 161)));
    }

    [Fact]
    public void SuspendedSaleKeepsCartWithoutOperationalEffects()
    {
        SaleLine line = CreateLine();
        SuspendedSale suspended = SuspendedSale.Create(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            "  Cliente balcão  ",
            [line],
            AtUtc(12));

        Assert.Equal("Cliente balcão", suspended.Name);
        Assert.Single(suspended.Lines);
        Assert.Equal(line.Id, suspended.Lines[0].Id);
    }

    [Fact]
    public void ReceiptCapturesNonFiscalLabelAndNames()
    {
        Sale sale = CreateSale(
            [CreateLine()],
            [CreatePayment(PaymentMethod.Cash, 1_000)]);

        Receipt receipt = Receipt.Create(
            EntityId.New(),
            sale,
            "  Farmácia Central  ",
            "  Maria Caixa  ",
            AtUtc(13));

        Assert.Equal("Recibo interno não fiscal", receipt.DocumentLabel);
        Assert.Equal("Farmácia Central", receipt.PharmacyName);
        Assert.Equal("Maria Caixa", receipt.OperatorName);
        Assert.Equal(sale.Id, receipt.SaleId);
        Assert.Equal(1_000, receipt.Total.Amount);
    }

    private static Sale CreateSale(
        IReadOnlyCollection<SaleLine> lines,
        IReadOnlyCollection<SalePayment> payments,
        long totalDiscountXof = 0,
        EntityId? totalDiscountAuthorizedByUserId = null) => Sale.Create(
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        SaleNumber.Create(new DateOnly(2026, 8, 5), 1),
        lines,
        Money.Xof(totalDiscountXof),
        totalDiscountAuthorizedByUserId,
        payments,
        AtUtc(12));

    private static SaleLine CreateLine(
        long quantityPackages = 1,
        long packageFactor = 2,
        long unitPriceXof = 1_000,
        long discountXof = 0,
        long capturedCostXof = 1_100,
        EntityId? discountAuthorizedByUserId = null) => SaleLine.Create(
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        "Amoxicilina 500 mg",
        "Caixa",
        packageFactor,
        quantityPackages,
        Money.Xof(unitPriceXof),
        Money.Xof(discountXof),
        Money.Xof(capturedCostXof),
        discountXof > 0 ? discountAuthorizedByUserId ?? EntityId.New() : discountAuthorizedByUserId);

    private static SalePayment CreatePayment(
        PaymentMethod method,
        long amountXof,
        string? reference = null) => SalePayment.Create(
        EntityId.New(),
        method,
        Money.Xof(amountXof),
        reference);

    private static UtcInstant AtUtc(int hour) => UtcInstant.From(
        new DateTimeOffset(2026, 8, 5, hour, 0, 0, TimeSpan.Zero));
}
