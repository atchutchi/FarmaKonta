using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Setup;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteLocalIdentityStore(
    DbContextOptions<NofarmaDbContext> options) : ILocalIdentityStore
{
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        return await context.Installations
            .AsNoTracking()
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveInitialSetupAsync(
        InitialSetupData data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);

        await using var context = new NofarmaDbContext(options);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (await context.Installations.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("This installation is already configured.");
        }

        var installation = data.Installation;
        var administrator = installation.PrimaryAdministrator;
        var pharmacy = installation.Pharmacy;
        var device = installation.Device;

        var pharmacyRecord = new PharmacyRecord
        {
            Id = pharmacy.Id.Value,
            Name = pharmacy.Name,
            TaxIdentifier = pharmacy.TaxIdentifier,
            Address = pharmacy.Address,
            Contact = pharmacy.Contact,
            TimeZoneId = pharmacy.TimeZoneId,
        };
        var deviceRecord = new DeviceRecord
        {
            Id = device.Id.Value,
            PharmacyId = pharmacy.Id.Value,
            Name = device.Name,
        };
        var administratorRecord = new LocalUserRecord
        {
            Id = administrator.Id.Value,
            PharmacyId = pharmacy.Id.Value,
            DisplayName = administrator.DisplayName,
            LoginName = administrator.LoginName,
            NormalizedLoginName = administrator.NormalizedLoginName,
            Role = (int)administrator.Role,
            CredentialKind = (int)administrator.CredentialKind,
            IsPrimaryAdministrator = administrator.IsPrimaryAdministrator,
            Status = (int)administrator.Status,
            FailedLoginCount = administrator.FailedLoginCount,
            LockedUntilUtc = administrator.LockedUntilUtc?.Value,
            LastSuccessfulLoginUtc = administrator.LastSuccessfulLoginUtc?.Value,
            CreatedAtUtc = administrator.CreatedAtUtc.Value,
        };
        var credentialRecord = CreateCredentialRecord(
            administrator.Id.Value,
            data.AdministratorCredential);
        var installationRecord = new InstallationRecord
        {
            Id = installation.Id.Value,
            Status = (int)installation.Status,
            PharmacyId = pharmacy.Id.Value,
            DeviceId = device.Id.Value,
            PrimaryAdministratorId = administrator.Id.Value,
            CreatedAtUtc = installation.CreatedAtUtc.Value,
            ReadyForActivationAtUtc = installation.ReadyForActivationAtUtc?.Value,
            Pharmacy = pharmacyRecord,
            Device = deviceRecord,
        };
        var recoveryRecord = new RecoveryCodeRecord
        {
            Id = Guid.NewGuid(),
            UserId = administrator.Id.Value,
            Version = data.RecoveryCredential.Version,
            Algorithm = data.RecoveryCredential.Algorithm,
            WorkFactor = data.RecoveryCredential.WorkFactor,
            Salt = data.RecoveryCredential.Salt,
            Hash = data.RecoveryCredential.Hash,
            CreatedAtUtc = installation.CreatedAtUtc.Value,
        };
        var audit = data.AuditEvent;
        var auditRecord = new AuditEventRecord
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

        context.Installations.Add(installationRecord);
        context.LocalUsers.Add(administratorRecord);
        context.CredentialRecords.Add(credentialRecord);
        context.RecoveryCodes.Add(recoveryRecord);
        context.AuditEvents.Add(auditRecord);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CredentialRecord CreateCredentialRecord(
        Guid userId,
        CredentialHash credential) =>
        new()
        {
            UserId = userId,
            Version = credential.Version,
            Algorithm = credential.Algorithm,
            WorkFactor = credential.WorkFactor,
            Salt = credential.Salt,
            Hash = credential.Hash,
        };
}
