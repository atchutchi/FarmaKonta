namespace Nofarma.Domain.Catalog;

public readonly record struct ProductCode
{
    private ProductCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ProductCode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProductValidationException("O código interno do produto é obrigatório.");
        }

        return new ProductCode(value.Trim().ToUpperInvariant());
    }

    public static ProductCode FromSequence(long sequence)
    {
        if (sequence <= 0)
        {
            throw new ProductValidationException("A sequência do produto deve ser positiva.");
        }

        return new ProductCode($"PRD-{sequence:D6}");
    }

    public override string ToString() => Value;
}
