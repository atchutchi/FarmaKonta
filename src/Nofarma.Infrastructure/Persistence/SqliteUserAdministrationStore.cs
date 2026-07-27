using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Users;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteUserAdministrationStore(
    DbContextOptions<NofarmaDbContext> options) : ILocalUserAdministrationStore
{
    public async Task<UserAdministrationContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        LocalUserRecord? actor = await context.LocalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.Id == actorUserId.Value &&
                    record.Status == (int)UserStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);
        if (actor is null)
        {
            return null;
        }

        Guid deviceId = await context.Installations
            .AsNoTracking()
            .Where(record => record.PharmacyId == actor.PharmacyId)
            .Select(record => record.DeviceId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return new UserAdministrationContext(
            new EntityId(actor.PharmacyId),
            new EntityId(deviceId));
    }

    public async Task<bool> LoginExistsAsync(
        EntityId pharmacyId,
        string normalizedLogin,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        return await context.LocalUsers.AnyAsync(
            record => record.PharmacyId == pharmacyId.Value &&
                record.NormalizedLoginName == normalizedLogin,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManagedUserIdentity?> GetUserAsync(
        EntityId pharmacyId,
        EntityId userId,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        LocalUserRecord? record = await context.LocalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.PharmacyId == pharmacyId.Value &&
                    candidate.Id == userId.Value,
                cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        Guid deviceId = await context.Installations
            .AsNoTracking()
            .Where(candidate => candidate.PharmacyId == pharmacyId.Value)
            .Select(candidate => candidate.DeviceId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ManagedUserIdentity(
            MapUser(record),
            new UserAdministrationContext(pharmacyId, new EntityId(deviceId)));
    }

    public async Task CreateUserAsync(
        EntityId pharmacyId,
        LocalUser user,
        CredentialHash credential,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        context.LocalUsers.Add(MapUser(user, pharmacyId));
        context.CredentialRecords.Add(MapCredential(user.Id, credential));
        context.AuditEvents.Add(MapAudit(auditEvent));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveUserAsync(
        ManagedUserIdentity identity,
        CredentialHash? replacementCredential,
        AuditEvent auditEvent,
        bool revokeSessions,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        LocalUserRecord target = await context.LocalUsers.SingleAsync(
            record => record.Id == identity.User.Id.Value &&
                record.PharmacyId == identity.Context.PharmacyId.Value,
            cancellationToken).ConfigureAwait(false);
        CopyUser(identity.User, target);

        if (replacementCredential is not null)
        {
            CredentialRecord credential = await context.CredentialRecords.SingleAsync(
                record => record.UserId == target.Id,
                cancellationToken).ConfigureAwait(false);
            CopyCredential(replacementCredential, credential);
        }

        if (revokeSessions)
        {
            List<LocalSessionRecord> sessions = await context.LocalSessions
                .Where(record => record.UserId == target.Id && record.RevokedAtUtc == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (LocalSessionRecord session in sessions)
            {
                session.RevokedAtUtc = auditEvent.OccurredAtUtc.Value;
            }
        }

        context.AuditEvents.Add(MapAudit(auditEvent));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LocalUserRecord MapUser(LocalUser user, EntityId pharmacyId) =>
        new()
        {
            Id = user.Id.Value,
            PharmacyId = pharmacyId.Value,
            DisplayName = user.DisplayName,
            LoginName = user.LoginName,
            NormalizedLoginName = user.NormalizedLoginName,
            Role = (int)user.Role,
            CredentialKind = (int)user.CredentialKind,
            IsPrimaryAdministrator = user.IsPrimaryAdministrator,
            Status = (int)user.Status,
            FailedLoginCount = user.FailedLoginCount,
            LockedUntilUtc = user.LockedUntilUtc?.Value,
            LastSuccessfulLoginUtc = user.LastSuccessfulLoginUtc?.Value,
            CreatedAtUtc = user.CreatedAtUtc.Value,
        };

    private static LocalUser MapUser(LocalUserRecord record) =>
        LocalUser.Restore(
            new EntityId(record.Id),
            record.DisplayName,
            record.LoginName,
            (UserRole)record.Role,
            (CredentialKind)record.CredentialKind,
            record.IsPrimaryAdministrator,
            UtcInstant.From(record.CreatedAtUtc),
            (UserStatus)record.Status,
            record.FailedLoginCount,
            record.LockedUntilUtc is { } locked ? UtcInstant.From(locked) : null,
            record.LastSuccessfulLoginUtc is { } last ? UtcInstant.From(last) : null);

    private static CredentialRecord MapCredential(
        EntityId userId,
        CredentialHash credential) =>
        new()
        {
            UserId = userId.Value,
            Version = credential.Version,
            Algorithm = credential.Algorithm,
            WorkFactor = credential.WorkFactor,
            Salt = credential.Salt,
            Hash = credential.Hash,
        };

    private static void CopyUser(LocalUser source, LocalUserRecord target)
    {
        target.Role = (int)source.Role;
        target.CredentialKind = (int)source.CredentialKind;
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
