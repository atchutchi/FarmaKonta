using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Identity;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Licensing;

public sealed class SqliteLicenseStore(
    DbContextOptions<NofarmaDbContext> options) : ILicenseStore
{
    private const int MaximumDocumentBytes = 65_536;

    public async Task<StoredLicense?> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        var record = await db.Licenses.AsNoTracking()
            .Select(candidate => new
            {
                candidate.DocumentBytes,
                candidate.DocumentHash
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        byte[] document = record.DocumentBytes.ToArray();
        byte[] expectedHash = SHA256.HashData(document);
        bool validHash = record.DocumentHash.Length == expectedHash.Length
            && CryptographicOperations.FixedTimeEquals(record.DocumentHash, expectedHash);
        CryptographicOperations.ZeroMemory(expectedHash);
        if (!validHash)
        {
            CryptographicOperations.ZeroMemory(document);
            throw new LicensePersistenceIntegrityException();
        }

        return new StoredLicense(document);
    }

    public async Task ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(audit);
        byte[] document = license.Document.ToArray();
        ValidateBeforeTransaction(license, audit, document);
        byte[] hash = SHA256.HashData(document);

        await using var db = new NofarmaDbContext(options);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);

        InstallationRecord installation = await db.Installations
            .SingleOrDefaultAsync(
                record => record.PharmacyId == license.Grant.PharmacyId.Value
                    && record.DeviceId == license.Grant.DeviceId.Value,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "A instalação da licença não existe neste dispositivo.");
        LicenseRecord? current = await db.Licenses
            .SingleOrDefaultAsync(
                record => record.InstallationId == installation.Id,
                cancellationToken)
            .ConfigureAwait(false);

        if (current is not null
            && string.Equals(audit.Action, "license.renewed", StringComparison.Ordinal)
            && license.Grant.Sequence <= current.Sequence)
        {
            throw new LicenseImportException("LICENSE_ROLLBACK");
        }

        if (current is null)
        {
            current = new LicenseRecord
            {
                Id = Guid.NewGuid(),
                InstallationId = installation.Id
            };
            db.Licenses.Add(current);
        }

        Map(current, license, audit, document, hash);
        db.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(audit));
        installation.Status = (int)InstallationStatus.Active;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateBeforeTransaction(
        VerifiedLicense license,
        AuditEvent audit,
        byte[] document)
    {
        if (document.Length > MaximumDocumentBytes)
        {
            throw new ArgumentException(
                "The license document exceeds the supported size.",
                nameof(license));
        }

        if (string.IsNullOrWhiteSpace(license.Channel) || license.Channel.Length > 16
            || string.IsNullOrWhiteSpace(license.KeyId) || license.KeyId.Length > 128)
        {
            throw new ArgumentException("The verified license metadata is invalid.", nameof(license));
        }

        bool validAudit = audit.PharmacyId == license.Grant.PharmacyId
            && audit.DeviceId == license.Grant.DeviceId
            && audit.UserId is null
            && audit.Action is "license.imported" or "license.renewed"
            && string.Equals(audit.ObjectType, "License", StringComparison.Ordinal)
            && string.Equals(
                audit.ObjectId,
                license.Grant.Id.Value.ToString("D"),
                StringComparison.Ordinal)
            && audit.Outcome == AuditOutcome.Success
            && audit.DiagnosticCode is null
            && string.Equals(audit.DetailsJson, "{}", StringComparison.Ordinal);
        if (!validAudit)
        {
            throw new ArgumentException(
                "The license audit metadata is invalid.",
                nameof(audit));
        }
    }

    private static void Map(
        LicenseRecord record,
        VerifiedLicense license,
        AuditEvent audit,
        byte[] document,
        byte[] hash)
    {
        record.LicenseId = license.Grant.Id.Value;
        record.Plan = (int)license.Grant.Plan;
        record.Sequence = license.Grant.Sequence;
        record.Channel = license.Channel;
        record.KeyId = license.KeyId;
        record.DocumentBytes = document;
        record.DocumentHash = hash;
        record.IssuedAtUtc = license.Grant.IssuedAtUtc.Value;
        record.ValidFromUtc = license.Grant.ValidFromUtc.Value;
        record.ValidUntilUtc = license.Grant.ValidUntilUtc.Value;
        record.GraceUntilUtc = license.Grant.GraceUntilUtc.Value;
        record.ImportedAtUtc = audit.OccurredAtUtc.Value;
    }
}
