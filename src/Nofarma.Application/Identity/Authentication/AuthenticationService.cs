using Nofarma.Application.Abstractions;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Authentication;

public sealed class AuthenticationService(
    ILocalAuthenticationStore store,
    ICredentialHasher credentialHasher,
    CredentialHash timingCredential,
    CurrentSession currentSession,
    IUtcClock clock)
{
    public const string GenericFailureMessage =
        "Não foi possível iniciar sessão com os dados indicados.";

    public async Task<SignInResult> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string normalizedLogin = request.Login.Trim().ToUpperInvariant();
        UtcInstant now = clock.GetCurrentInstant();
        AuthenticationIdentity? identity = await store
            .FindByLoginAsync(normalizedLogin, cancellationToken)
            .ConfigureAwait(false);

        if (identity is null)
        {
            _ = credentialHasher.Verify(request.Credential, timingCredential);
            return Failure();
        }

        if (identity.User.Status != UserStatus.Active || identity.User.IsLockedAt(now))
        {
            return Failure(identity.User.LockedUntilUtc);
        }

        if (!credentialHasher.Verify(request.Credential, identity.Credential))
        {
            identity.User.RecordFailedLogin(now);
            AuditEvent audit = CreateAudit(
                identity,
                now,
                "authentication.failed",
                AuditOutcome.Failure,
                "credential_invalid");
            await store
                .SaveFailedSignInAsync(identity, audit, cancellationToken)
                .ConfigureAwait(false);
            return Failure(identity.User.LockedUntilUtc);
        }

        identity.User.RecordSuccessfulLogin(now);
        var session = new LocalSession(
            EntityId.New(),
            identity.User.Id,
            identity.User.Role,
            now,
            now,
            UtcInstant.From(now.Value.AddMinutes(15)));
        AuditEvent successAudit = CreateAudit(
            identity,
            now,
            "authentication.succeeded",
            AuditOutcome.Success,
            null);
        await store
            .SaveSuccessfulSignInAsync(identity, session, successAudit, cancellationToken)
            .ConfigureAwait(false);
        currentSession.Activate(session);

        return new SignInResult(true, string.Empty, session, null);
    }

    private static AuditEvent CreateAudit(
        AuthenticationIdentity identity,
        UtcInstant occurredAtUtc,
        string action,
        AuditOutcome outcome,
        string? diagnosticCode) =>
        new(
            EntityId.New(),
            identity.PharmacyId,
            identity.DeviceId,
            identity.User.Id,
            action,
            "LocalUser",
            identity.User.Id.Value.ToString("D"),
            occurredAtUtc,
            outcome,
            diagnosticCode,
            "{}");

    private static SignInResult Failure(UtcInstant? lockedUntilUtc = null) =>
        new(false, GenericFailureMessage, null, lockedUntilUtc);
}
