using Nofarma.Domain.Common;

namespace Nofarma.Domain.Sales;

public sealed class SalePayment
{
    private SalePayment(EntityId id, PaymentMethod method, Money amount, string? reference)
    {
        Id = id;
        Method = method;
        Amount = amount;
        Reference = reference;
    }

    public EntityId Id { get; }
    public PaymentMethod Method { get; }
    public Money Amount { get; }
    public string? Reference { get; }

    public static SalePayment Create(
        EntityId id,
        PaymentMethod method,
        Money amount,
        string? reference)
    {
        if (id.Value == Guid.Empty)
        {
            throw new SalesValidationException("O pagamento deve ter um identificador.");
        }
        if (!Enum.IsDefined(method))
        {
            throw new SalesValidationException("O método de pagamento não é válido.");
        }
        if (!string.Equals(amount.Currency, "XOF", StringComparison.Ordinal) || amount.Amount <= 0)
        {
            throw new SalesValidationException("O pagamento deve ser um valor XOF positivo.");
        }

        string? normalizedReference = string.IsNullOrWhiteSpace(reference)
            ? null
            : reference.Trim();
        if (normalizedReference?.Length > 160)
        {
            throw new SalesValidationException(
                "A referência do pagamento não pode exceder 160 caracteres.");
        }

        return new SalePayment(id, method, amount, normalizedReference);
    }
}
