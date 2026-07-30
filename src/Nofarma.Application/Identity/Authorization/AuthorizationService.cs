using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Authorization;

public sealed class AuthorizationService(IUtcClock clock)
{
    public void EnsureAllowed(LocalSession session, Capability capability)
    {
        ArgumentNullException.ThrowIfNull(session);
        UtcInstant currentInstant = clock.GetCurrentInstant();

        if (session.IsExpiredAt(currentInstant) ||
            !RolePermissions.IsAllowed(session.Role, capability))
        {
            throw new AuthorizationException(
                "A sessão actual não tem permissão para esta operação.");
        }
    }
}
