using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface ILocalRecoveryStore
{
    Task<RecoveryIdentity?> GetRecoveryIdentityAsync(
        CancellationToken cancellationToken);

    Task CompleteRecoveryAsync(
        RecoveryIdentity identity,
        CredentialHash newPassword,
        CredentialHash newRecoveryCode,
        AuditEvent auditEvent,
        UtcInstant occurredAtUtc,
        CancellationToken cancellationToken);
}
