using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Users;

public sealed class UserAdministrationService(
    ILocalUserAdministrationStore store,
    AuthorizationService authorization,
    ICredentialHasher credentialHasher,
    IUtcClock clock)
{
    public async Task<EntityId> CreateAsync(
        LocalSession actor,
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageUsers);
        ArgumentNullException.ThrowIfNull(request);
        ValidateCredential(request.Role, request.Credential);

        UserAdministrationContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        UtcInstant now = clock.GetCurrentInstant();
        LocalUser user = LocalUser.Create(
            EntityId.New(),
            request.DisplayName,
            request.Login,
            request.Role,
            now);
        if (await store.LoginExistsAsync(
            context.PharmacyId,
            user.NormalizedLoginName,
            cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("O identificador de acesso já está em uso.");
        }

        CredentialHash credential = credentialHasher.Hash(request.Credential);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            user.Id,
            "user.created",
            now);
        await store.CreateUserAsync(
            context.PharmacyId,
            user,
            credential,
            audit,
            cancellationToken).ConfigureAwait(false);
        return user.Id;
    }

    public async Task DeactivateAsync(
        LocalSession actor,
        EntityId userId,
        CancellationToken cancellationToken)
    {
        ManagedUserIdentity identity = await GetAuthorizedUserAsync(
            actor,
            userId,
            cancellationToken).ConfigureAwait(false);
        identity.User.Deactivate();
        await SaveAsync(
            actor,
            identity,
            null,
            "user.deactivated",
            revokeSessions: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UnlockAsync(
        LocalSession actor,
        EntityId userId,
        CancellationToken cancellationToken)
    {
        ManagedUserIdentity identity = await GetAuthorizedUserAsync(
            actor,
            userId,
            cancellationToken).ConfigureAwait(false);
        identity.User.Unlock();
        await SaveAsync(
            actor,
            identity,
            null,
            "user.unlocked",
            revokeSessions: false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeRoleAsync(
        LocalSession actor,
        EntityId userId,
        UserRole role,
        string newCredential,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManagePermissions);
        ValidateCredential(role, newCredential);
        ManagedUserIdentity identity = await GetAuthorizedUserAsync(
            actor,
            userId,
            cancellationToken).ConfigureAwait(false);
        identity.User.ChangeRole(role);
        CredentialHash credential = credentialHasher.Hash(newCredential);
        await SaveAsync(
            actor,
            identity,
            credential,
            "user.role_changed",
            revokeSessions: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedUserIdentity> GetAuthorizedUserAsync(
        LocalSession actor,
        EntityId userId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageUsers);
        UserAdministrationContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.GetUserAsync(context.PharmacyId, userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("O utilizador indicado não existe.");
    }

    private async Task<UserAdministrationContext> GetContextAsync(
        LocalSession actor,
        CancellationToken cancellationToken) =>
        await store.GetContextAsync(actor.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");

    private async Task SaveAsync(
        LocalSession actor,
        ManagedUserIdentity identity,
        CredentialHash? credential,
        string action,
        bool revokeSessions,
        CancellationToken cancellationToken)
    {
        AuditEvent audit = CreateAudit(
            identity.Context,
            actor.UserId,
            identity.User.Id,
            action,
            clock.GetCurrentInstant());
        await store.SaveUserAsync(
            identity,
            credential,
            audit,
            revokeSessions,
            cancellationToken).ConfigureAwait(false);
    }

    private static AuditEvent CreateAudit(
        UserAdministrationContext context,
        EntityId actorUserId,
        EntityId targetUserId,
        string action,
        UtcInstant occurredAtUtc) =>
        new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actorUserId,
            action,
            "LocalUser",
            targetUserId.Value.ToString("D"),
            occurredAtUtc,
            AuditOutcome.Success,
            null,
            "{}");

    private static void ValidateCredential(UserRole role, string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        if (role == UserRole.Cashier)
        {
            bool validPin = credential.Length is >= 4 and <= 6 &&
                credential.All(char.IsAsciiDigit);
            if (!validPin)
            {
                throw new ArgumentException(
                    "O PIN do Caixa deve ter entre quatro e seis algarismos.",
                    nameof(credential));
            }

            return;
        }

        bool strongPassword = credential.Length >= 12 &&
            credential.Any(char.IsUpper) &&
            credential.Any(char.IsLower) &&
            credential.Any(char.IsDigit) &&
            credential.Any(character => !char.IsLetterOrDigit(character));
        if (!strongPassword)
        {
            throw new ArgumentException(
                "A palavra-passe não cumpre os requisitos de segurança.",
                nameof(credential));
        }
    }
}
