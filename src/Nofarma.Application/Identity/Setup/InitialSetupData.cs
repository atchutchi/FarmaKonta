using Nofarma.Domain.Auditing;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Setup;

public sealed record InitialSetupData(
    Installation Installation,
    CredentialHash AdministratorCredential,
    CredentialHash RecoveryCredential,
    AuditEvent AuditEvent);
