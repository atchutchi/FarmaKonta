using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.IntegrationTests.Licensing;

internal sealed class LicenseDatabase : IAsyncDisposable
{
    private const string DirectoryPrefix = "nofarma-license-";
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory;

    private LicenseDatabase(
        string directory,
        string databasePath,
        DbContextOptions<NofarmaDbContext> options,
        Guid installationId,
        Guid pharmacyId,
        Guid deviceId,
        Guid administratorId)
    {
        _directory = directory;
        DatabasePath = databasePath;
        Options = options;
        InstallationId = installationId;
        PharmacyId = pharmacyId;
        DeviceId = deviceId;
        AdministratorId = administratorId;
        Store = new SqliteLicenseStore(options);
        ContextStore = new SqliteLicenseContextStore(options);
    }

    internal string DatabasePath { get; }

    internal DbContextOptions<NofarmaDbContext> Options { get; }

    internal Guid InstallationId { get; }

    internal Guid PharmacyId { get; }

    internal Guid DeviceId { get; }

    internal Guid AdministratorId { get; }

    internal SqliteLicenseStore Store { get; }

    internal SqliteLicenseContextStore ContextStore { get; }

    internal static async Task<LicenseDatabase> CreateAsync(
        InstallationStatus status = InstallationStatus.ReadyForActivation,
        IInterceptor? interceptor = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"{DirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "licensing.db");
        var optionsBuilder = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite(LocalDatabasePath.BuildConnectionString(databasePath));
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        DbContextOptions<NofarmaDbContext> options = optionsBuilder.Options;
        Guid installationId = Guid.NewGuid();
        Guid pharmacyId = Guid.NewGuid();
        Guid deviceId = Guid.NewGuid();
        Guid administratorId = Guid.NewGuid();
        await using var db = new NofarmaDbContext(options);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        SeedInstallation(
            db,
            installationId,
            pharmacyId,
            deviceId,
            administratorId,
            status,
            "LOCAL");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new LicenseDatabase(
            directory,
            databasePath,
            options,
            installationId,
            pharmacyId,
            deviceId,
            administratorId);
    }

    internal static async Task<LicenseDatabase> CreateWithLicenseAsync(long sequence)
    {
        LicenseDatabase database = await CreateAsync();
        await database.Store.ReplaceAsync(
            database.Verified(sequence),
            database.Audit("license.imported"),
            TestContext.Current.CancellationToken);
        return database;
    }

    internal NofarmaDbContext OpenContext() => new(Options);

    internal VerifiedLicense Verified(
        long sequence,
        byte[]? document = null,
        Guid? licenseId = null,
        LicensePlan plan = LicensePlan.Monthly) =>
        new(
            Grant(sequence, licenseId, plan),
            "QA",
            "qa-test-key",
            document ?? Enumerable.Repeat((byte)sequence, 64).ToArray());

    internal LicenseGrant Grant(
        long sequence,
        Guid? licenseId = null,
        LicensePlan plan = LicensePlan.Monthly) =>
        new(
            new EntityId(licenseId ?? Guid.Parse("44444444-4444-4444-4444-444444444444")),
            new EntityId(PharmacyId),
            new EntityId(PharmacyId),
            new EntityId(DeviceId),
            plan,
            sequence,
            UtcInstant.From(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            UtcInstant.From(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            UtcInstant.From(new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero)),
            UtcInstant.From(new DateTimeOffset(2026, 9, 7, 23, 59, 59, TimeSpan.Zero)));

    internal AuditEvent Audit(string action, Guid? id = null, string detailsJson = "{}") =>
        new(
            new EntityId(id ?? Guid.NewGuid()),
            new EntityId(PharmacyId),
            new EntityId(DeviceId),
            null,
            action,
            "License",
            "44444444-4444-4444-4444-444444444444",
            UtcInstant.From(Now),
            AuditOutcome.Success,
            null,
            detailsJson);

    internal async Task<long?> CurrentSequenceAsync()
    {
        await using NofarmaDbContext db = OpenContext();
        return await db.Licenses.AsNoTracking()
            .Select(record => (long?)record.Sequence)
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    internal async Task AddSecondInstallationAsync()
    {
        await using NofarmaDbContext db = OpenContext();
        await db.Database.ExecuteSqlRawAsync(
            "DROP INDEX IX_Installations_SingletonKey",
            TestContext.Current.CancellationToken);
        SeedInstallation(
            db,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            InstallationStatus.ReadyForActivation,
            "SECOND");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        string fullDirectory = Path.GetFullPath(_directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullTemp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string leaf = Path.GetFileName(fullDirectory);
        if (!fullDirectory.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase)
            || !leaf.StartsWith(DirectoryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The licensing test directory is not safe to remove.");
        }

        if (Directory.Exists(fullDirectory))
        {
            Directory.Delete(fullDirectory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static void SeedInstallation(
        NofarmaDbContext db,
        Guid installationId,
        Guid pharmacyId,
        Guid deviceId,
        Guid administratorId,
        InstallationStatus status,
        string singletonKey)
    {
        db.Pharmacies.Add(new PharmacyRecord
        {
            Id = pharmacyId,
            Name = "Farmácia de teste",
            TaxIdentifier = $"T{pharmacyId:N}",
            Address = "Bissau",
            Contact = string.Empty,
            TimeZoneId = "Africa/Bissau"
        });
        db.Devices.Add(new DeviceRecord
        {
            Id = deviceId,
            PharmacyId = pharmacyId,
            Name = "Posto de teste"
        });
        db.LocalUsers.Add(new LocalUserRecord
        {
            Id = administratorId,
            PharmacyId = pharmacyId,
            DisplayName = "Administrador de teste",
            LoginName = $"admin-{administratorId:N}",
            NormalizedLoginName = $"ADMIN-{administratorId:N}",
            Role = (int)UserRole.Administrator,
            CredentialKind = (int)CredentialKind.Password,
            Status = (int)UserStatus.Active,
            CreatedAtUtc = Now
        });
        db.Installations.Add(new InstallationRecord
        {
            Id = installationId,
            SingletonKey = singletonKey,
            PharmacyId = pharmacyId,
            DeviceId = deviceId,
            PrimaryAdministratorId = administratorId,
            Status = (int)status,
            CreatedAtUtc = Now,
            ReadyForActivationAtUtc = Now
        });
    }
}
