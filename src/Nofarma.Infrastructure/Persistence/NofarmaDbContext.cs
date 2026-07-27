using Microsoft.EntityFrameworkCore;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class NofarmaDbContext(DbContextOptions<NofarmaDbContext> options)
    : DbContext(options)
{
    public DbSet<InstallationRecord> Installations => Set<InstallationRecord>();

    public DbSet<PharmacyRecord> Pharmacies => Set<PharmacyRecord>();

    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();

    public DbSet<LocalUserRecord> LocalUsers => Set<LocalUserRecord>();

    public DbSet<CredentialRecord> CredentialRecords => Set<CredentialRecord>();

    public DbSet<RecoveryCodeRecord> RecoveryCodes => Set<RecoveryCodeRecord>();

    public DbSet<LocalSessionRecord> LocalSessions => Set<LocalSessionRecord>();

    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NofarmaDbContext).Assembly);
    }
}
