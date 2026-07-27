using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Users;

public sealed record ManagedUserSummary(
    EntityId Id,
    string DisplayName,
    string Login,
    UserRole Role,
    CredentialKind CredentialKind,
    UserStatus Status,
    int FailedLoginCount,
    UtcInstant? LockedUntilUtc,
    UtcInstant? LastSuccessfulLoginUtc,
    bool IsPrimaryAdministrator);
