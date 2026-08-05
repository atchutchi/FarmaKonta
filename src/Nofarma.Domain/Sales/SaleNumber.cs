using System.Globalization;

namespace Nofarma.Domain.Sales;

public readonly record struct SaleNumber
{
    private SaleNumber(string value) => Value = value;

    public string Value { get; }

    public static SaleNumber Create(DateOnly businessDate, long sequence)
    {
        if (sequence is < 1 or > 999_999)
        {
            throw new SalesValidationException(
                "A sequência diária da venda deve ficar entre 1 e 999999.");
        }

        string value = string.Create(
            CultureInfo.InvariantCulture,
            $"V-{businessDate:yyyyMMdd}-{sequence:000000}");
        return new SaleNumber(value);
    }

    public override string ToString() => Value;
}
