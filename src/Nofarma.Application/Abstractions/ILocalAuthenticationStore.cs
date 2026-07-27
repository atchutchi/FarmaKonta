using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Auditing;

namespace Nofarma.Application.Abstractions;

public interface ILocalAuthenticationStore
{
    Task<AuthenticationIdentity?> FindByLoginAsync(
        string normalizedLogin,
        CancellationToken cancellationToken);

    Task SaveFailedSignInAsync(
        AuthenticationIdentity identity,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task SaveSuccessfulSignInAsync(
        AuthenticationIdentity identity,
        LocalSession session,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
