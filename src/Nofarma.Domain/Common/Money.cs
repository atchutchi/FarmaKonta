namespace Nofarma.Domain.Common;

public readonly record struct Money
{
    public Money(long amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public long Amount { get; }

    public string Currency { get; }

    public static Money Xof(long amount) => new(amount, "XOF");

    public Money Add(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Currencies must match.");
        }

        return new Money(checked(Amount + other.Amount), Currency);
    }
}
