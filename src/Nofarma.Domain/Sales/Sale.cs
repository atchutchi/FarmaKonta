using Nofarma.Domain.Common;

namespace Nofarma.Domain.Sales;

public sealed class Sale
{
    private Sale(
        EntityId id,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        EntityId cashShiftId,
        SaleNumber number,
        IReadOnlyList<SaleLine> lines,
        Money grossSubtotal,
        Money lineDiscountTotal,
        Money totalDiscount,
        EntityId? totalDiscountAuthorizedByUserId,
        IReadOnlyList<SalePayment> payments,
        Money total,
        Money paid,
        Money cashReceived,
        Money change,
        UtcInstant completedAt)
    {
        Id = id;
        PharmacyId = pharmacyId;
        DeviceId = deviceId;
        UserId = userId;
        CashShiftId = cashShiftId;
        Number = number;
        Lines = lines;
        GrossSubtotal = grossSubtotal;
        LineDiscountTotal = lineDiscountTotal;
        TotalDiscount = totalDiscount;
        TotalDiscountAuthorizedByUserId = totalDiscountAuthorizedByUserId;
        Payments = payments;
        Total = total;
        Paid = paid;
        CashReceived = cashReceived;
        Change = change;
        CompletedAt = completedAt;
        Status = SaleStatus.Completed;
    }

    public EntityId Id { get; }
    public EntityId PharmacyId { get; }
    public EntityId DeviceId { get; }
    public EntityId UserId { get; }
    public EntityId CashShiftId { get; }
    public SaleNumber Number { get; }
    public SaleStatus Status { get; }
    public IReadOnlyList<SaleLine> Lines { get; }
    public Money GrossSubtotal { get; }
    public Money LineDiscountTotal { get; }
    public Money TotalDiscount { get; }
    public EntityId? TotalDiscountAuthorizedByUserId { get; }
    public IReadOnlyList<SalePayment> Payments { get; }
    public Money Total { get; }
    public Money Paid { get; }
    public Money CashReceived { get; }
    public Money Change { get; }
    public UtcInstant CompletedAt { get; }

    public static Sale Create(
        EntityId id,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        EntityId cashShiftId,
        SaleNumber number,
        IReadOnlyCollection<SaleLine> lines,
        Money totalDiscount,
        EntityId? totalDiscountAuthorizedByUserId,
        IReadOnlyCollection<SalePayment> payments,
        UtcInstant completedAt)
    {
        EnsureIdentifier(id, "A venda deve ter um identificador.");
        EnsureIdentifier(pharmacyId, "A farmácia da venda é obrigatória.");
        EnsureIdentifier(deviceId, "O dispositivo da venda é obrigatório.");
        EnsureIdentifier(userId, "O utilizador da venda é obrigatório.");
        EnsureIdentifier(cashShiftId, "O turno de caixa da venda é obrigatório.");
        if (string.IsNullOrWhiteSpace(number.Value))
        {
            throw new SalesValidationException("O número operacional da venda é obrigatório.");
        }
        if (completedAt == default)
        {
            throw new SalesValidationException("A data de conclusão da venda é obrigatória.");
        }
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(payments);
        if (lines.Count == 0)
        {
            throw new SalesValidationException("A venda deve ter pelo menos uma linha.");
        }
        if (payments.Count == 0)
        {
            throw new SalesValidationException("A venda deve ter pelo menos um pagamento.");
        }
        if (lines.Select(line => line.Id).Distinct().Count() != lines.Count)
        {
            throw new SalesValidationException("As linhas da venda devem ter identificadores únicos.");
        }
        if (payments.Select(payment => payment.Id).Distinct().Count() != payments.Count)
        {
            throw new SalesValidationException("Os pagamentos devem ter identificadores únicos.");
        }
        EnsureXofNonNegative(totalDiscount, "O desconto total");
        if (totalDiscount.Amount > 0)
        {
            if (totalDiscountAuthorizedByUserId is null)
            {
                throw new SalesValidationException("O desconto total exige autorização.");
            }
            EnsureIdentifier(
                totalDiscountAuthorizedByUserId.Value,
                "O utilizador que autorizou o desconto total não é válido.");
        }

        try
        {
            long grossSubtotal = lines.Sum(line => line.Gross.Amount);
            long lineDiscountTotal = lines.Sum(line => line.Discount.Amount);
            long afterLineDiscounts = checked(grossSubtotal - lineDiscountTotal);
            if (totalDiscount.Amount > afterLineDiscounts)
            {
                throw new SalesValidationException(
                    "O desconto total não pode exceder o subtotal da venda.");
            }
            long total = checked(afterLineDiscounts - totalDiscount.Amount);
            long paid = payments.Sum(payment => payment.Amount.Amount);
            if (paid < total)
            {
                throw new SalesValidationException("A soma dos pagamentos é inferior ao total da venda.");
            }
            long cashReceived = payments
                .Where(payment => payment.Method == PaymentMethod.Cash)
                .Sum(payment => payment.Amount.Amount);
            long change = checked(paid - total);
            if (change > cashReceived)
            {
                throw new SalesValidationException(
                    "O valor pago sem dinheiro não pode exceder o total da venda.");
            }

            return new Sale(
                id,
                pharmacyId,
                deviceId,
                userId,
                cashShiftId,
                number,
                Array.AsReadOnly(lines.ToArray()),
                Money.Xof(grossSubtotal),
                Money.Xof(lineDiscountTotal),
                totalDiscount,
                totalDiscountAuthorizedByUserId,
                Array.AsReadOnly(payments.ToArray()),
                Money.Xof(total),
                Money.Xof(paid),
                Money.Xof(cashReceived),
                Money.Xof(change),
                completedAt);
        }
        catch (OverflowException exception)
        {
            throw new SalesValidationException(
                $"O total da venda excede o limite permitido: {exception.Message}");
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
}
