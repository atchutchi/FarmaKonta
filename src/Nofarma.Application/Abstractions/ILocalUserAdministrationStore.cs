using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Users;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Abstractions;

public interface ILocalUserAdministrationStore
{
    Task<UserAdministrationContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken);

    Task<bool> LoginExistsAsync(
        EntityId pharmacyId,
        string normalizedLogin,
        CancellationToken cancellationToken);

    Task<ManagedUserIdentity?> GetUserAsync(
        EntityId pharmacyId,
        EntityId userId,
        CancellationToken cancellationToken);

    Task CreateUserAsync(
        EntityId pharmacyId,
        LocalUser user,
        CredentialHash credential,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task SaveUserAsync(
        ManagedUserIdentity identity,
        CredentialHash? replacementCredential,
        AuditEvent auditEvent,
        bool revokeSessions,
        CancellationToken cancellationToken);
}
