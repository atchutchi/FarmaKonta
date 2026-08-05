using Nofarma.Domain.Sales;

namespace Nofarma.UnitTests.Domain.Sales;

public sealed class SaleNumberTests
{
    [Fact]
    public void CreatesLocalOperationalNumber()
    {
        SaleNumber number = SaleNumber.Create(new DateOnly(2026, 8, 5), 42);

        Assert.Equal("V-20260805-000042", number.Value);
        Assert.Equal(number.Value, number.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000)]
    public void RejectsSequenceOutsideSixDigits(long sequence)
    {
        Assert.Throws<SalesValidationException>(() =>
            SaleNumber.Create(new DateOnly(2026, 8, 5), sequence));
    }
}
