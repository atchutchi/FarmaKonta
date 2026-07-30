using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Authentication;

public sealed record LocalSession(
    EntityId Id,
    EntityId UserId,
    UserRole Role,
    UtcInstant CreatedAtUtc,
    UtcInstant LastActivityAtUtc,
    UtcInstant? ExpiresAtUtc)
{
    public bool IsExpiredAt(UtcInstant instant) =>
        ExpiresAtUtc is { } expiry && expiry.Value <= instant.Value;
}
