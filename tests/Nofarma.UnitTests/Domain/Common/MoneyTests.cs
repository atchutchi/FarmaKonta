using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Common;

public sealed class MoneyTests
{
    [Fact]
    public void XofPreservesIntegerAmount()
    {
        Money money = Money.Xof(6_250);

        Assert.Equal(6_250, money.Amount);
        Assert.Equal("XOF", money.Currency);
    }

    [Fact]
    public void AddRejectsDifferentCurrency()
    {
        Money xof = Money.Xof(100);
        Money other = new(100, "EUR");

        Assert.Throws<InvalidOperationException>(() => xof.Add(other));
    }

    [Fact]
    public void AddUsesCheckedIntegerArithmetic()
    {
        Money maximum = Money.Xof(long.MaxValue);

        Assert.Throws<OverflowException>(() => maximum.Add(Money.Xof(1)));
    }
}
