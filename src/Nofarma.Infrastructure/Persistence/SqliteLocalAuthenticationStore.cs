using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteLocalAuthenticationStore(
    DbContextOptions<NofarmaDbContext> options)
    : ILocalAuthenticationStore, ILocalRecoveryStore
{
    public async Task<AuthenticationIdentity?> FindByLoginAsync(
        string normalizedLogin,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        InstallationRecord? installation = await context.Installations
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (installation is null)
        {
            return null;
        }

        LocalUserRecord? user = await context.LocalUsers
            .AsNoTracking()
            .Include(record => record.Credential)
            .SingleOrDefaultAsync(
                record => record.PharmacyId == installation.PharmacyId &&
                    record.NormalizedLoginName == normalizedLogin,
                cancellationToken)
            .ConfigureAwait(false);

        return user is null ? null : MapIdentity(user, installation.DeviceId);
    }

    public async Task SaveFailedSignInAsync(
        AuthenticationIdentity identity,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        LocalUserRecord user = await FindUserAsync(
            context,
            identity.User.Id.Value,
            cancellationToken).ConfigureAwait(false);
        CopyMutableState(identity.User, user);
        context.AuditEvents.Add(MapAudit(auditEvent));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSuccessfulSignInAsync(
        AuthenticationIdentity identity,
        LocalSession session,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        LocalUserRecord user = await FindUserAsync(
            context,
            identity.User.Id.Value,
            cancellationToken).ConfigureAwait(false);
        CopyMutableState(identity.User, user);
        context.LocalSessions.Add(new LocalSessionRecord
        {
            Id = session.Id.Value,
            UserId = session.UserId.Value,
            CreatedAtUtc = session.CreatedAtUtc.Value,
            LastActivityAtUtc = session.LastActivityAtUtc.Value,
            ExpiresAtUtc = session.ExpiresAtUtc?.Value,
        });
        context.AuditEvents.Add(MapAudit(auditEvent));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecoveryIdentity?> GetRecoveryIdentityAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        InstallationRecord? installation = await context.Installations
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (installation is null)
        {
            return null;
        }

        LocalUserRecord? administrator = await context.LocalUsers
            .AsNoTracking()
            .Include(record => record.Credential)
            .SingleOrDefaultAsync(
                record => record.Id == installation.PrimaryAdministratorId,
                cancellationToken)
            .ConfigureAwait(false);
        if (administrator is null)
        {
            return null;
        }

        RecoveryCodeRecord? recovery = await context.RecoveryCodes
            .AsNoTracking()
            .Where(record =>
                record.UserId == administrator.Id && record.UsedAtUtc == null)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (recovery is null)
        {
            return null;
        }

        return new RecoveryIdentity(
            MapIdentity(administrator, installation.DeviceId),
            MapCredential(recovery));
    }

    public async Task CompleteRecoveryAsync(
        RecoveryIdentity identity,
        CredentialHash newPassword,
        CredentialHash newRecoveryCode,
        AuditEvent auditEvent,
        UtcInstant occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        Guid userId = identity.Administrator.User.Id.Value;
        RecoveryCodeRecord currentRecovery = await context.RecoveryCodes
            .Where(record => record.UserId == userId && record.UsedAtUtc == null)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The recovery code is no longer available.");

        if (!Matches(currentRecovery, identity.RecoveryCredential))
        {
            throw new InvalidOperationException("The recovery code changed before completion.");
        }

        currentRecovery.UsedAtUtc = occurredAtUtc.Value;
        CredentialRecord credential = await context.CredentialRecords
            .SingleAsync(record => record.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        CopyCredential(newPassword, credential);
        LocalUserRecord user = await FindUserAsync(context, userId, cancellationToken)
            .ConfigureAwait(false);
        CopyMutableState(identity.Administrator.User, user);

        List<LocalSessionRecord> sessions = await context.LocalSessions
            .Where(record => record.UserId == userId && record.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (LocalSessionRecord session in sessions)
        {
            session.RevokedAtUtc = occurredAtUtc.Value;
        }

        context.RecoveryCodes.Add(new RecoveryCodeRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Version = newRecoveryCode.Version,
            Algorithm = newRecoveryCode.Algorithm,
            WorkFactor = newRecoveryCode.WorkFactor,
            Salt = newRecoveryCode.Salt,
            Hash = newRecoveryCode.Hash,
            CreatedAtUtc = occurredAtUtc.Value,
        });
        context.AuditEvents.Add(MapAudit(auditEvent));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<LocalUserRecord> FindUserAsync(
        NofarmaDbContext context,
        Guid userId,
        CancellationToken cancellationToken) =>
        await context.LocalUsers
            .SingleAsync(record => record.Id == userId, cancellationToken)
            .ConfigureAwait(false);

    private static AuthenticationIdentity MapIdentity(
        LocalUserRecord record,
        Guid deviceId)
    {
        LocalUser user = LocalUser.Restore(
            new EntityId(record.Id),
            record.DisplayName,
            record.LoginName,
            (UserRole)record.Role,
            (CredentialKind)record.CredentialKind,
            record.IsPrimaryAdministrator,
            UtcInstant.From(record.CreatedAtUtc),
            (UserStatus)record.Status,
            record.FailedLoginCount,
            record.LockedUntilUtc is { } locked
                ? UtcInstant.From(locked)
                : null,
            record.LastSuccessfulLoginUtc is { } lastLogin
                ? UtcInstant.From(lastLogin)
                : null);
        return new AuthenticationIdentity(
            user,
            MapCredential(record.Credential),
            new EntityId(record.PharmacyId),
            new EntityId(deviceId));
    }

    private static CredentialHash MapCredential(CredentialRecord record) =>
        new(record.Version, record.Algorithm, record.WorkFactor, record.Salt, record.Hash);

    private static CredentialHash MapCredential(RecoveryCodeRecord record) =>
        new(record.Version, record.Algorithm, record.WorkFactor, record.Salt, record.Hash);

    private static void CopyMutableState(LocalUser source, LocalUserRecord target)
    {
        target.Status = (int)source.Status;
        target.FailedLoginCount = source.FailedLoginCount;
        target.LockedUntilUtc = source.LockedUntilUtc?.Value;
        target.LastSuccessfulLoginUtc = source.LastSuccessfulLoginUtc?.Value;
    }

    private static void CopyCredential(CredentialHash source, CredentialRecord target)
    {
        target.Version = source.Version;
        target.Algorithm = source.Algorithm;
        target.WorkFactor = source.WorkFactor;
        target.Salt = source.Salt;
        target.Hash = source.Hash;
    }

    private static bool Matches(RecoveryCodeRecord record, CredentialHash credential) =>
        record.Version == credential.Version &&
        record.Algorithm == credential.Algorithm &&
        record.WorkFactor == credential.WorkFactor &&
        record.Salt.AsSpan().SequenceEqual(credential.Salt) &&
        record.Hash.AsSpan().SequenceEqual(credential.Hash);

    private static AuditEventRecord MapAudit(AuditEvent audit) =>
        new()
        {
            Id = audit.Id.Value,
            PharmacyId = audit.PharmacyId.Value,
            DeviceId = audit.DeviceId.Value,
            UserId = audit.UserId?.Value,
            Action = audit.Action,
            ObjectType = audit.ObjectType,
            ObjectId = audit.ObjectId,
            OccurredAtUtc = audit.OccurredAtUtc.Value,
            Outcome = (int)audit.Outcome,
            DiagnosticCode = audit.DiagnosticCode,
            DetailsJson = audit.DetailsJson,
        };
}
