using Nofarma.Application.Abstractions;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Identity.Authentication;

public sealed class RecoveryService(
    ILocalRecoveryStore store,
    ICredentialHasher credentialHasher,
    IRecoveryCodeGenerator recoveryCodeGenerator,
    CurrentSession currentSession,
    IUtcClock clock)
{
    public const string GenericFailureMessage =
        "Não foi possível recuperar o acesso com os dados indicados.";

    public async Task<RecoveryResult> RecoverAsync(
        RecoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateStrongPassword(request.NewAdministratorPassword);

        RecoveryIdentity? identity = await store
            .GetRecoveryIdentityAsync(cancellationToken)
            .ConfigureAwait(false);
        if (identity is null ||
            !credentialHasher.Verify(request.RecoveryCode, identity.RecoveryCredential))
        {
            return new RecoveryResult(false, GenericFailureMessage, null);
        }

        UtcInstant now = clock.GetCurrentInstant();
        string newRecoveryCode = recoveryCodeGenerator.Generate();
        CredentialHash newPassword = credentialHasher.Hash(
            request.NewAdministratorPassword);
        CredentialHash newRecoveryCredential = credentialHasher.Hash(newRecoveryCode);
        AuthenticationIdentity administrator = identity.Administrator;
        administrator.User.Unlock();
        var auditEvent = new AuditEvent(
            EntityId.New(),
            administrator.PharmacyId,
            administrator.DeviceId,
            administrator.User.Id,
            "recovery.completed",
            "LocalUser",
            administrator.User.Id.Value.ToString("D"),
            now,
            AuditOutcome.Success,
            null,
            "{}");

        await store.CompleteRecoveryAsync(
            identity,
            newPassword,
            newRecoveryCredential,
            auditEvent,
            now,
            cancellationToken).ConfigureAwait(false);
        currentSession.Clear();

        return new RecoveryResult(true, string.Empty, newRecoveryCode);
    }

    private static void ValidateStrongPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        bool isStrong = password.Length >= 12 &&
            password.Any(char.IsUpper) &&
            password.Any(char.IsLower) &&
            password.Any(char.IsDigit) &&
            password.Any(character => !char.IsLetterOrDigit(character));

        if (!isStrong)
        {
            throw new ArgumentException(
                "The administrator password does not meet the security requirements.",
                nameof(password));
        }
    }
}
