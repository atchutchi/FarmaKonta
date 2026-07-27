using Nofarma.Infrastructure.Time;

namespace Nofarma.UnitTests.Infrastructure.Time;

public sealed class SystemUtcClockTests
{
    [Fact]
    public void GetCurrentInstantReturnsZeroOffsetAndCurrentTime()
    {
        SystemUtcClock clock = new();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        DateTimeOffset value = clock.GetCurrentInstant().Value;

        DateTimeOffset after = DateTimeOffset.UtcNow;
        Assert.Equal(TimeSpan.Zero, value.Offset);
        Assert.InRange(value, before, after);
    }
}
