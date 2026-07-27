using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Authentication;

public sealed record LocalProfile(
    EntityId UserId,
    string DisplayName,
    string Login,
    UserRole Role,
    CredentialKind CredentialKind,
    UserStatus Status,
    UtcInstant? LockedUntilUtc);
