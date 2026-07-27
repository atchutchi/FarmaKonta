using Nofarma.Application.Abstractions;
using Nofarma.Domain.Common;

namespace Nofarma.Infrastructure.Time;

public sealed class SystemUtcClock : IUtcClock
{
    public UtcInstant GetCurrentInstant() => UtcInstant.From(DateTimeOffset.UtcNow);
}
