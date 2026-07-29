using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.IntegrationTests.Licensing;

public sealed class LicensePersistenceTests
{
    [Fact]
    public async Task ReplacementRequiresTheExactObservedDocumentToken()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateWithLicenseAsync(sequence: 1);
        StoredLicense observed = Assert.IsType<StoredLicense>(
            await db.Store.GetAsync(TestContext.Current.CancellationToken));
        await db.Store.ReplaceAsync(
            db.Verified(2),
            db.Audit("license.renewed", grant: db.Grant(2)),
            LicenseStorePrecondition.Matching(observed.ConcurrencyToken),
            TestContext.Current.CancellationToken);

        LicenseStoreReplaceResult result = await db.Store.ReplaceAsync(
            db.Verified(3),
            db.Audit("license.renewed", grant: db.Grant(3)),
            LicenseStorePrecondition.Matching(observed.ConcurrencyToken),
            TestContext.Current.CancellationToken);

        Assert.Equal(LicenseStoreReplaceResult.Conflict, result);
        Assert.Equal(2, await db.CurrentSequenceAsync());
    }

    [Fact]
    public async Task IdenticalDocumentAfterConcurrentChangeIsAlreadyCurrentWithoutDuplicateAudit()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        VerifiedLicense document = db.Verified(1);
        LicenseStoreReplaceResult first = await db.Store.ReplaceAsync(
            document,
            db.Audit("license.imported", grant: document.Grant),
            LicenseStorePrecondition.ExpectedAbsent(),
            TestContext.Current.CancellationToken);
        LicenseStoreReplaceResult repeated = await db.Store.ReplaceAsync(
            document,
            db.Audit("license.imported", grant: document.Grant),
            LicenseStorePrecondition.ExpectedAbsent(),
            TestContext.Current.CancellationToken);

        Assert.Equal(LicenseStoreReplaceResult.Applied, first);
        Assert.Equal(LicenseStoreReplaceResult.AlreadyCurrent, repeated);
        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(1, await check.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAtomicallyStoresLicenseAuditAndActiveInstallation()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        await db.Store.ReplaceAsync(
            db.Verified(sequence: 1),
            db.Audit("license.imported"),
            TestContext.Current.CancellationToken);

        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(1, await check.Licenses.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            (int)InstallationStatus.Active,
            await check.Installations.Select(record => record.Status)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await check.AuditEvents.CountAsync(
                record => record.Action == "license.imported",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenewalReplacesCurrentRowAndPreservesAuditHistory()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateWithLicenseAsync(sequence: 1);

        await db.Store.ReplaceAsync(
            db.Verified(sequence: 2),
            db.Audit("license.renewed", grant: db.Grant(2)),
            TestContext.Current.CancellationToken);

        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(2, await check.Licenses.Select(record => record.Sequence)
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await check.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoRenewalsProduceCanonicalSafeReconstructableAuditHistory()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        foreach ((long sequence, string action) in new[]
        {
            (1L, "license.imported"),
            (2L, "license.renewed"),
            (3L, "license.renewed")
        })
        {
            await db.Store.ReplaceAsync(
                db.Verified(sequence),
                db.Audit(action, grant: db.Grant(sequence)),
                TestContext.Current.CancellationToken);
        }

        await using NofarmaDbContext check = db.OpenContext();
        var history = new List<(long Sequence, string Plan, string From, string Until)>();
        string[] details = await check.AuditEvents.AsNoTracking()
            .Where(record => record.ObjectType == "License")
            .Select(record => record.DetailsJson)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        foreach (string metadata in details)
        {
            using JsonDocument json = JsonDocument.Parse(metadata);
            JsonElement root = json.RootElement;
            Assert.Equal(4, root.EnumerateObject().Count());
            history.Add((
                root.GetProperty("sequence").GetInt64(),
                root.GetProperty("plan").GetString()!,
                root.GetProperty("validFromUtc").GetString()!,
                root.GetProperty("validUntilUtc").GetString()!));
            Assert.DoesNotContain("signature", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("thumbprint", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("key", metadata, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal([1, 2, 3], history.Select(item => item.Sequence).Order().ToArray());
        Assert.All(history, item => Assert.Equal("Monthly", item.Plan));
        Assert.All(
            history,
            item => Assert.Equal("2026-08-01T00:00:00.0000000+00:00", item.From));
        Assert.All(
            history,
            item => Assert.Equal("2026-08-31T23:59:59.0000000+00:00", item.Until));
    }

    [Fact]
    public async Task FailureBeforeCommitLeavesLicenseAuditAndInstallationUntouched()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        Guid repeatedAuditId = Guid.NewGuid();
        await db.Store.ReplaceAsync(
            db.Verified(sequence: 2),
            db.Audit("license.imported", repeatedAuditId, grant: db.Grant(2)),
            TestContext.Current.CancellationToken);
        await using (NofarmaDbContext status = db.OpenContext())
        {
            status.Installations.Single().Status = (int)InstallationStatus.ReadyForActivation;
            await status.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Store.ReplaceAsync(
            db.Verified(sequence: 3),
            db.Audit("license.renewed", repeatedAuditId, grant: db.Grant(3)),
            TestContext.Current.CancellationToken));

        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(2, await check.Licenses.Select(record => record.Sequence)
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await check.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            (int)InstallationStatus.ReadyForActivation,
            await check.Installations.Select(record => record.Status)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoreAcceptsExactDocumentLimitAndRejectsOneByteMoreWithoutMutation()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        await db.Store.ReplaceAsync(
            db.Verified(1, new byte[65_536]),
            db.Audit("license.imported"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => db.Store.ReplaceAsync(
            db.Verified(2, new byte[65_537]),
            db.Audit("license.renewed", grant: db.Grant(2)),
            TestContext.Current.CancellationToken));

        Assert.Equal(1, await db.CurrentSequenceAsync());
        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(1, await check.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EqualOrLowerSequenceCannotReplaceCurrentLicense(long attemptedSequence)
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        using var signed = SignedLicense.Create(db.PharmacyId, db.DeviceId, sequence: 2);
        VerifiedLicense current = Assert.IsType<VerifiedLicense>(
            signed.Verifier.Verify(signed.Document, signed.Device, signed.Context).License);
        await db.Store.ReplaceAsync(
            current,
            db.Audit("license.imported", grant: current.Grant),
            TestContext.Current.CancellationToken);

        LicenseImportException error = await Assert.ThrowsAsync<LicenseImportException>(() =>
            signed.CreateService(db).ImportAsync(
                new LicenseImportRequest(signed.Issue(attemptedSequence)),
                TestContext.Current.CancellationToken));

        Assert.Equal("LICENSE_ROLLBACK", error.Code);
        Assert.Equal(2, await db.CurrentSequenceAsync());
    }

    [Fact]
    public async Task ConcurrentRecoveryKeepsHighestSignedSequenceWithDeterministicFirstRead()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateWithLicenseAsync(sequence: 1);
        using var signed = SignedLicense.Create(db.PharmacyId, db.DeviceId, sequence: 2);
        var synchronized = new FirstReadsBarrierLicenseStore(db.Store, participants: 2);
        LicenseService lowerService = signed.CreateService(db, synchronized);
        LicenseService higherService = signed.CreateService(db, synchronized);
        Task<LicenseImportException?> lower = CaptureImportErrorAsync(
            lowerService,
            signed.Issue(2));
        Task<LicenseImportException?> higher = CaptureImportErrorAsync(
            higherService,
            signed.Issue(3));

        await Task.WhenAll(lower, higher);

        Assert.Equal(3, await db.CurrentSequenceAsync());
        Assert.Null(await higher);
        LicenseImportException? lowerError = await lower;
        Assert.True(lowerError is null || lowerError.Code == "LICENSE_ROLLBACK");
        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(1, await check.Licenses.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentIdenticalImportsPersistOnlyOneAuditWithDeterministicFirstRead()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        using var signed = SignedLicense.Create(db.PharmacyId, db.DeviceId, sequence: 1);
        var synchronized = new FirstReadsBarrierLicenseStore(db.Store, participants: 2);
        LicenseService firstService = signed.CreateService(db, synchronized);
        LicenseService secondService = signed.CreateService(db, synchronized);

        await Task.WhenAll(
            firstService.ImportAsync(
                new LicenseImportRequest(signed.Document),
                TestContext.Current.CancellationToken),
            secondService.ImportAsync(
                new LicenseImportRequest(signed.Document),
                TestContext.Current.CancellationToken));

        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(1, await check.Licenses.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await check.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PersistedDocumentHasExactSha256AndInputIsCopied()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        byte[] document = [1, 3, 5, 7, 9];
        byte[] expected = SHA256.HashData(document);
        VerifiedLicense verified = db.Verified(1, document);

        Task replace = db.Store.ReplaceAsync(
            verified,
            db.Audit("license.imported"),
            TestContext.Current.CancellationToken);
        document[0] = 99;
        await replace;

        await using NofarmaDbContext check = db.OpenContext();
        LicenseRecord record = await check.Licenses.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1, 3, 5, 7, 9 }, record.DocumentBytes);
        Assert.Equal(expected, record.DocumentHash);
    }

    [Fact]
    public async Task StoreReturnsOnlyDefensivelyCopiedUntrustedDocument()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateWithLicenseAsync(sequence: 1);

        StoredLicense stored = Assert.IsType<StoredLicense>(
            await db.Store.GetAsync(TestContext.Current.CancellationToken));
        byte[] firstRead = stored.Document.ToArray();
        firstRead[0] ^= 0xff;
        StoredLicense reread = Assert.IsType<StoredLicense>(
            await db.Store.GetAsync(TestContext.Current.CancellationToken));

        Assert.NotEqual(firstRead, reread.Document.ToArray());
    }

    [Fact]
    public async Task ContextStoreReturnsNullForZeroAndContextForExactlyOneInstallation()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        LicenseContext context = Assert.IsType<LicenseContext>(
            await db.ContextStore.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(db.PharmacyId, context.PharmacyId.Value);
        Assert.Equal(db.PharmacyId, context.EstablishmentId.Value);
        Assert.Equal(db.DeviceId, context.DeviceId.Value);

        await using (NofarmaDbContext remove = db.OpenContext())
        {
            await remove.Database.ExecuteSqlRawAsync(
                "DELETE FROM Installations",
                TestContext.Current.CancellationToken);
        }

        Assert.Null(await db.ContextStore.GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ContextStoreRejectsMultipleInstallations()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        await db.AddSecondInstallationAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.ContextStore.GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MigrationCreatesLicenseTableConstraintsAndUniqueIndexes()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        await using var connection = new SqliteConnection(
            LocalDatabasePath.BuildConnectionString(db.DatabasePath));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        HashSet<string> indexes = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'index'",
            TestContext.Current.CancellationToken);
        string tableSql = await ReadScalarStringAsync(
            connection,
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Licenses'",
            TestContext.Current.CancellationToken);

        Assert.Contains("IX_Licenses_InstallationId", indexes);
        Assert.Contains("IX_Licenses_LicenseId_Sequence", indexes);
        Assert.Contains("length(\"DocumentBytes\") <= 65536", tableSql, StringComparison.Ordinal);
        Assert.Contains("length(\"DocumentHash\") = 32", tableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupValidationRequiresARealChildOfTempWithTheExpectedPrefix()
    {
        string valid = Path.Combine(
            Path.GetTempPath(),
            $"nofarma-license-{Guid.NewGuid():N}");
        string siblingOfTemp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + $"-outside{Path.DirectorySeparatorChar}nofarma-license-{Guid.NewGuid():N}";

        Assert.True(LicenseDatabase.IsSafeTestDirectory(valid, "nofarma-license-"));
        Assert.False(LicenseDatabase.IsSafeTestDirectory(siblingOfTemp, "nofarma-license-"));
    }

    [Fact]
    public async Task MigrationUpgradesCashShiftDatabaseAndPreservesInstallation()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"nofarma-license-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "upgrade.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite(LocalDatabasePath.BuildConnectionString(databasePath))
            .Options;
        Guid pharmacyId = Guid.NewGuid();
        Guid deviceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid installationId = Guid.NewGuid();
        try
        {
            await using (var previous = new NofarmaDbContext(options))
            {
                IMigrator migrator = previous.Database.GetService<IMigrator>();
                await migrator.MigrateAsync(
                    "20260728120000_AddCashShifts",
                    TestContext.Current.CancellationToken);
                previous.Pharmacies.Add(new PharmacyRecord
                {
                    Id = pharmacyId,
                    Name = "Farmácia antes da migração",
                    TaxIdentifier = $"U{pharmacyId:N}",
                    Address = "Bissau",
                    Contact = string.Empty,
                    TimeZoneId = "Africa/Bissau"
                });
                previous.Devices.Add(new DeviceRecord
                {
                    Id = deviceId,
                    PharmacyId = pharmacyId,
                    Name = "Posto existente"
                });
                previous.LocalUsers.Add(new LocalUserRecord
                {
                    Id = userId,
                    PharmacyId = pharmacyId,
                    DisplayName = "Administrador existente",
                    LoginName = "admin-upgrade-license",
                    NormalizedLoginName = "ADMIN-UPGRADE-LICENSE",
                    Role = (int)UserRole.Administrator,
                    CredentialKind = (int)CredentialKind.Password,
                    Status = (int)UserStatus.Active,
                    CreatedAtUtc = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)
                });
                previous.Installations.Add(new InstallationRecord
                {
                    Id = installationId,
                    SingletonKey = "LOCAL",
                    PharmacyId = pharmacyId,
                    DeviceId = deviceId,
                    PrimaryAdministratorId = userId,
                    Status = (int)InstallationStatus.ReadyForActivation,
                    CreatedAtUtc = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero),
                    ReadyForActivationAtUtc = new DateTimeOffset(
                        2026,
                        7,
                        28,
                        12,
                        0,
                        0,
                        TimeSpan.Zero)
                });
                await previous.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using (var upgraded = new NofarmaDbContext(options))
            {
                await upgraded.Database.MigrateAsync(TestContext.Current.CancellationToken);
                Assert.Equal(0, await upgraded.Licenses.CountAsync(TestContext.Current.CancellationToken));
                Assert.Equal(
                    installationId,
                    await upgraded.Installations.Select(record => record.Id)
                        .SingleAsync(TestContext.Current.CancellationToken));
                string[] appliedMigrations = (await upgraded.Database.GetAppliedMigrationsAsync(
                    TestContext.Current.CancellationToken)).ToArray();
                Assert.Contains(
                    appliedMigrations,
                    migration => migration.Contains(
                        "AddSignedLicensing",
                        StringComparison.Ordinal));
                Assert.Contains(
                    appliedMigrations,
                    migration => migration.Contains(
                        "AddOperationRequestFingerprints",
                        StringComparison.Ordinal));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            string full = Path.GetFullPath(directory);
            if (LicenseDatabase.IsSafeTestDirectory(full, "nofarma-license-upgrade-"))
            {
                Directory.Delete(full, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AuditRejectsUnapprovedOrSensitiveMetadataBeforeMutation()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => db.Store.ReplaceAsync(
            db.Verified(1),
            db.Audit("license.imported", detailsJson: "{\"signature\":\"secret\"}"),
            TestContext.Current.CancellationToken));

        Assert.Null(await db.CurrentSequenceAsync());
        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(0, await check.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationAfterFinalInstallationWriteRollsBackBeforeCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelAfterInstallationUpdateInterceptor(cancellation);
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync(
            interceptor: interceptor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Store.ReplaceAsync(
            db.Verified(1),
            db.Audit("license.imported"),
            cancellation.Token));

        Assert.True(interceptor.WasTriggered);
        await using NofarmaDbContext check = db.OpenContext();
        Assert.Equal(
            0,
            await check.Licenses.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            0,
            await check.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            (int)InstallationStatus.ReadyForActivation,
            await check.Installations.Select(record => record.Status)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TamperedDerivedColumnsNeverChangeSignedDocumentEvaluation()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        using var signed = SignedLicense.Create(db.PharmacyId, db.DeviceId, sequence: 1);
        VerifiedLicense verified = Assert.IsType<VerifiedLicense>(
            signed.Verifier.Verify(signed.Document, signed.Device, signed.Context).License);
        await db.Store.ReplaceAsync(
            verified,
            db.Audit("license.imported"),
            TestContext.Current.CancellationToken);
        await using (NofarmaDbContext mutation = db.OpenContext())
        {
            LicenseRecord record = await mutation.Licenses.SingleAsync(
                TestContext.Current.CancellationToken);
            record.Sequence = 9_999;
            record.Plan = (int)LicensePlan.Annual;
            record.Channel = "Commercial";
            record.KeyId = "untrusted-key";
            record.ValidUntilUtc = new DateTimeOffset(2036, 8, 31, 23, 59, 59, TimeSpan.Zero);
            record.GraceUntilUtc = record.ValidUntilUtc.AddDays(7);
            await mutation.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        LicenseStatus status = await signed.CreateService(
            db,
            now: UtcInstant.From(
                new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)))
            .GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LicenseState.ExpiredReadOnly, status.State);
        Assert.Equal(1, status.Grant!.Sequence);
        Assert.Equal(LicensePlan.Monthly, status.Grant.Plan);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero),
            status.Grant.ValidUntilUtc.Value);
    }

    [Fact]
    public async Task CorruptedDocumentOrHashIsInvalidAndNeverAllowsNewOperations()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        using var signed = SignedLicense.Create(db.PharmacyId, db.DeviceId, sequence: 1);
        VerifiedLicense verified = Assert.IsType<VerifiedLicense>(
            signed.Verifier.Verify(signed.Document, signed.Device, signed.Context).License);
        await db.Store.ReplaceAsync(
            verified,
            db.Audit("license.imported"),
            TestContext.Current.CancellationToken);

        await using (NofarmaDbContext corruptDocument = db.OpenContext())
        {
            LicenseRecord record = await corruptDocument.Licenses.SingleAsync(
                TestContext.Current.CancellationToken);
            record.DocumentBytes[0] ^= 0x01;
            record.DocumentHash = SHA256.HashData(record.DocumentBytes);
            await corruptDocument.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        LicenseStatus documentStatus = await signed.CreateService(db).GetStatusAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(LicenseState.Invalid, documentStatus.State);
        Assert.False(documentStatus.AllowsNewOperations);

        await using (NofarmaDbContext corruptHash = db.OpenContext())
        {
            LicenseRecord record = await corruptHash.Licenses.SingleAsync(
                TestContext.Current.CancellationToken);
            record.DocumentBytes = signed.Document.ToArray();
            record.DocumentHash = new byte[32];
            await corruptHash.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        LicenseStatus hashStatus = await signed.CreateService(db).GetStatusAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(LicenseState.Invalid, hashStatus.State);
        Assert.False(hashStatus.AllowsNewOperations);

        LicenseStatus recovered = await signed.CreateService(db).ImportAsync(
            new LicenseImportRequest(signed.Document),
            TestContext.Current.CancellationToken);
        Assert.Equal(LicenseState.Valid, recovered.State);
        await using NofarmaDbContext repaired = db.OpenContext();
        LicenseRecord repairedRecord = await repaired.Licenses.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SHA256.HashData(repairedRecord.DocumentBytes), repairedRecord.DocumentHash);
        Assert.Equal(1, await repaired.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptedStoredHashNeverPermitsDowngradeOfAValidSignedDocument()
    {
        await using LicenseDatabase db = await LicenseDatabase.CreateAsync();
        using var signed = SignedLicense.Create(db.PharmacyId, db.DeviceId, sequence: 2);
        VerifiedLicense current = Assert.IsType<VerifiedLicense>(
            signed.Verifier.Verify(signed.Document, signed.Device, signed.Context).License);
        await db.Store.ReplaceAsync(
            current,
            db.Audit("license.imported", grant: current.Grant),
            TestContext.Current.CancellationToken);

        await using (NofarmaDbContext corruptHash = db.OpenContext())
        {
            LicenseRecord record = await corruptHash.Licenses.SingleAsync(
                TestContext.Current.CancellationToken);
            record.DocumentHash = new byte[32];
            await corruptHash.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        LicenseImportException error = await Assert.ThrowsAsync<LicenseImportException>(() =>
            signed.CreateService(db).ImportAsync(
                new LicenseImportRequest(signed.Issue(sequence: 1)),
                TestContext.Current.CancellationToken));

        Assert.Equal("LICENSE_ROLLBACK", error.Code);
        Assert.Equal(2, await db.CurrentSequenceAsync());
        LicenseStatus status = await signed.CreateService(db).GetStatusAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(LicenseState.Invalid, status.State);
    }

    private static async Task<LicenseImportException?> CaptureImportErrorAsync(
        LicenseService service,
        byte[] document)
    {
        try
        {
            await service.ImportAsync(
                new LicenseImportRequest(document),
                TestContext.Current.CancellationToken);
            return null;
        }
        catch (LicenseImportException error)
        {
            return error;
        }
    }

    private static async Task<HashSet<string>> ReadNamesAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task<string> ReadScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<string>(await command.ExecuteScalarAsync(cancellationToken));
    }

    private sealed class CancelAfterInstallationUpdateInterceptor(
        CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        internal bool WasTriggered { get; private set; }

        public override int NonQueryExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result)
        {
            CancelIfInstallationUpdate(command);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            CancelIfInstallationUpdate(command);
            return ValueTask.FromResult(result);
        }

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            CancelIfInstallationUpdate(command);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            CancelIfInstallationUpdate(command);
            return ValueTask.FromResult(result);
        }

        private void CancelIfInstallationUpdate(DbCommand command)
        {
            if (command.CommandText.Contains(
                    "UPDATE \"Installations\"",
                    StringComparison.Ordinal))
            {
                WasTriggered = true;
                cancellation.Cancel();
            }
        }
    }

    private sealed class FirstReadsBarrierLicenseStore(
        ILicenseStore inner,
        int participants) : ILicenseStore
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public async Task<StoredLicense?> GetAsync(CancellationToken cancellationToken)
        {
            StoredLicense? current = await inner.GetAsync(cancellationToken);
            int read = Interlocked.Increment(ref _readCount);
            if (read <= participants)
            {
                if (read == participants)
                {
                    _release.TrySetResult();
                }

                await _release.Task.WaitAsync(cancellationToken);
            }

            return current;
        }

        public Task<LicenseStoreReplaceResult> ReplaceAsync(
            VerifiedLicense license,
            AuditEvent audit,
            LicenseStorePrecondition precondition,
            CancellationToken cancellationToken) =>
            inner.ReplaceAsync(license, audit, precondition, cancellationToken);
    }

    private sealed class SignedLicense : IDisposable
    {
        private const string Thumbprint = "SHA256:INTEGRATION-DEVICE";
        private const string KeyId = "qa-integration-key";
        private readonly ECDsa _signingKey;
        private readonly Guid _pharmacyId;
        private readonly Guid _deviceId;

        private SignedLicense(
            ECDsa signingKey,
            EcdsaLicenseDocumentVerifier verifier,
            LicenseContext context,
            DeviceLicenseIdentity device,
            Guid pharmacyId,
            Guid deviceId,
            long sequence)
        {
            _signingKey = signingKey;
            _pharmacyId = pharmacyId;
            _deviceId = deviceId;
            Verifier = verifier;
            Context = context;
            Device = device;
            Document = Issue(sequence);
        }

        internal byte[] Document { get; }

        internal EcdsaLicenseDocumentVerifier Verifier { get; }

        internal LicenseContext Context { get; }

        internal DeviceLicenseIdentity Device { get; }

        internal static SignedLicense Create(Guid pharmacyId, Guid deviceId, long sequence)
        {
            ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var context = new LicenseContext(
                new EntityId(pharmacyId),
                new EntityId(pharmacyId),
                new EntityId(deviceId));
            var device = new DeviceLicenseIdentity(
                new EntityId(pharmacyId),
                new EntityId(deviceId),
                Thumbprint);
            var registry = new TrustedLicenseKeyRegistry(
                LicenseBuildChannel.Qa,
                new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
                {
                    [KeyId] = signingKey.ExportSubjectPublicKeyInfo()
                });
            return new SignedLicense(
                signingKey,
                new EcdsaLicenseDocumentVerifier(registry),
                context,
                device,
                pharmacyId,
                deviceId,
                sequence);
        }

        internal byte[] Issue(long sequence)
        {
            var unsigned = new SignedLicenseEnvelope(
                EcdsaLicenseDocumentVerifier.CurrentSchemaVersion,
                LicenseBuildChannel.Qa,
                KeyId,
                EcdsaLicenseDocumentVerifier.EcdsaP256Sha256P1363Algorithm,
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                _pharmacyId,
                _pharmacyId,
                _deviceId,
                Thumbprint,
                LicensePlan.Monthly,
                sequence,
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 7, 23, 59, 59, TimeSpan.Zero),
                Array.Empty<int>(),
                ReadOnlyMemory<byte>.Empty);
            byte[] signature = _signingKey.SignData(
                CanonicalLicenseJson.SerializePayload(unsigned),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return CanonicalLicenseJson.SerializeEnvelope(
                unsigned with { Signature = signature });
        }

        internal LicenseService CreateService(
            LicenseDatabase database,
            ILicenseStore? store = null,
            UtcInstant? now = null) =>
            new(
                store ?? database.Store,
                database.ContextStore,
                Verifier,
                new FixedIdentityStore(Device),
                new NoRollbackCheckpoint(),
                new FixedClock(now ?? UtcInstant.From(
                    new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero))));

        public void Dispose() => _signingKey.Dispose();

        private sealed class FixedIdentityStore(
            DeviceLicenseIdentity identity) : IDeviceLicenseIdentityStore
        {
            public DeviceLicenseIdentity GetOrCreate(EntityId pharmacyId, EntityId deviceId) => identity;
        }

        private sealed class NoRollbackCheckpoint : ILicenseClockCheckpoint
        {
            public void Initialize(LicenseClockBinding binding, UtcInstant now)
            {
            }

            public LicenseClockCheck CheckAndAdvance(LicenseClockBinding binding, UtcInstant now) =>
                new(false);
        }

        private sealed class FixedClock(UtcInstant now) : IUtcClock
        {
            public UtcInstant GetCurrentInstant() => now;
        }
    }
}
