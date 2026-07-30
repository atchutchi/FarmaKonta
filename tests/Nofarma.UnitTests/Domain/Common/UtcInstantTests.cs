using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Common;

public sealed class UtcInstantTests
{
    [Fact]
    public void FromNormalizesOffsetToUtc()
    {
        UtcInstant instant = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.FromHours(1)));

        Assert.Equal(
            new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero),
            instant.Value);
    }
}
