using Nofarma.Domain.Catalog;

namespace Nofarma.UnitTests.Domain.Catalog;

public sealed class ProductCodeTests
{
    [Fact]
    public void FromSequenceFormatsSixDigits()
    {
        ProductCode code = ProductCode.FromSequence(1);

        Assert.Equal("PRD-000001", code.Value);
    }

    [Fact]
    public void FromSequenceRejectsZero()
    {
        Assert.Throws<ProductValidationException>(() => ProductCode.FromSequence(0));
    }

    [Fact]
    public void ParseTrimsAndNormalizesCode()
    {
        ProductCode code = ProductCode.Parse("  med-42  ");

        Assert.Equal("MED-42", code.Value);
    }
}
