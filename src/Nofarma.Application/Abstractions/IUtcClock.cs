using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface IUtcClock
{
    UtcInstant GetCurrentInstant();
}
