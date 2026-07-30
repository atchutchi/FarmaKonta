using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Sales;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Sales;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteCashShiftStore(
    DbContextOptions<NofarmaDbContext> options) : ICashShiftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<CashShiftActorContext?> GetActorContextAsync(
        EntityId userId,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        var context = await (
            from user in db.LocalUsers.AsNoTracking()
            join installation in db.Installations.AsNoTracking()
                on user.PharmacyId equals installation.PharmacyId
            where user.Id == userId.Value &&
                user.Status == (int)UserStatus.Active &&
                (installation.Status == (int)InstallationStatus.ReadyForActivation ||
                    installation.Status == (int)InstallationStatus.Active)
            select new
            {
                user.PharmacyId,
                installation.DeviceId,
                ActorUserId = user.Id
            }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return context is null
            ? null
            : new CashShiftActorContext(
                new EntityId(context.PharmacyId),
                new EntityId(context.DeviceId),
                new EntityId(context.ActorUserId));
    }

    public async Task<StoredCashShift?> GetCurrentAggregateAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        CashShiftRecord? record = await db.CashShifts.AsNoTracking()
            .SingleOrDefaultAsync(
                shift => shift.PharmacyId == pharmacyId.Value &&
                    shift.DeviceId == deviceId.Value &&
                    shift.Status == (int)CashShiftStatus.Open,
                cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        CashMovementRecord[] movements = await db.CashMovements.AsNoTracking()
            .Where(movement => movement.CashShiftId == record.Id)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        movements = movements
            .OrderBy(movement => movement.Sequence)
            .ToArray();
        return new StoredCashShift(Rebuild(record, movements), record.RowVersion);
    }

    public async Task<CashCommandResult?> GetCommandResultAsync(
        EntityId pharmacyId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        CashCommandRecord? record = await db.CashCommands.AsNoTracking()
            .SingleOrDefaultAsync(
                command => command.PharmacyId == pharmacyId.Value &&
                    command.IdempotencyKey == idempotencyKey,
                cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        CashShiftSummary result = await DeserializeSnapshotAsync(
            db,
            record,
            cancellationToken).ConfigureAwait(false);
        return new CashCommandResult(record.RequestFingerprint, result);
    }

    public async Task<CashShiftSummary> SaveOpenedAsync(
        CashShiftActorContext context,
        CashShift shift,
        CashCommandEnvelope command,
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        ValidateCommon(
            context,
            shift,
            command,
            audit,
            shift.Id,
            "cash_shift.opened",
            "CashShift",
            shift.OpenedAt);
        if (command.Type != CashCommandType.OpenShift ||
            context.ActorUserId != shift.UserId ||
            shift.Status != CashShiftStatus.Open ||
            shift.Movements.Count != 0)
        {
            throw new SalesValidationException("O pedido de abertura do turno não é válido.");
        }

        await using var db = new NofarmaDbContext(options);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        await EnsurePersistedContextAsync(db, context, cancellationToken).ConfigureAwait(false);

        CashShiftSummary? duplicate = await GetDuplicateAsync(
            db,
            context.PharmacyId,
            command,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return duplicate;
        }

        bool alreadyOpen = await db.CashShifts.AsNoTracking().AnyAsync(
            record => record.PharmacyId == context.PharmacyId.Value &&
                record.DeviceId == context.DeviceId.Value &&
                record.Status == (int)CashShiftStatus.Open,
            cancellationToken).ConfigureAwait(false);
        if (alreadyOpen)
        {
            throw new SalesValidationException("Já existe um turno aberto neste posto de caixa.");
        }

        CashShiftSummary result = Map(shift);
        db.CashShifts.Add(MapShift(shift, rowVersion: 1));
        AddCommand(db, context, command, result, shift.OpenedAt);
        db.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(audit));
        db.OutboxEvents.Add(CreateOpenOutbox(context, shift));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CashShiftSummary> SaveMovementAsync(
        CashShiftActorContext context,
        CashShift shift,
        CashMovement movement,
        long expectedVersion,
        CashCommandEnvelope command,
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        CashCommandType expectedCommandType = movement.Type switch
        {
            CashMovementType.ManualEntry => CashCommandType.ManualEntry,
            CashMovementType.ManualExit => CashCommandType.ManualExit,
            _ => throw new SalesValidationException(
                "O movimento indicado não é um movimento manual de caixa.")
        };
        if (command.Type != expectedCommandType)
        {
            throw new SalesValidationException(
                "O tipo do comando não corresponde ao movimento de caixa.");
        }

        string expectedAction = movement.Type == CashMovementType.ManualEntry
            ? "cash_shift.manual_entry_recorded"
            : "cash_shift.manual_exit_recorded";
        ValidateCommon(
            context,
            shift,
            command,
            audit,
            movement.Id,
            expectedAction,
            "CashMovement",
            movement.OccurredAt);
        if (shift.Status != CashShiftStatus.Open ||
            movement.CashShiftId != shift.Id ||
            !shift.Movements.Any(candidate => candidate.Id == movement.Id) ||
            expectedVersion < 1)
        {
            throw new SalesValidationException("O pedido de movimento de caixa não é válido.");
        }

        await using var db = new NofarmaDbContext(options);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        await EnsurePersistedContextAsync(db, context, cancellationToken).ConfigureAwait(false);

        CashShiftSummary? duplicate = await GetDuplicateAsync(
            db,
            context.PharmacyId,
            command,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return duplicate;
        }

        int affected = await db.CashShifts
            .Where(record => record.Id == shift.Id.Value &&
                record.PharmacyId == context.PharmacyId.Value &&
                record.DeviceId == context.DeviceId.Value &&
                record.Status == (int)CashShiftStatus.Open &&
                record.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.ExpectedCashXof, shift.ExpectedCash.Amount)
                    .SetProperty(record => record.RowVersion, record => record.RowVersion + 1),
                cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new CashShiftConcurrencyException();
        }

        CashShiftSummary result = Map(shift);
        db.CashMovements.Add(MapMovement(
            context.PharmacyId,
            movement,
            checked(expectedVersion + 1)));
        AddCommand(db, context, command, result, movement.OccurredAt);
        db.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(audit));
        db.OutboxEvents.Add(CreateMovementOutbox(context, shift, movement));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<CashShiftSummary> SaveClosedAsync(
        CashShiftActorContext context,
        CashShift shift,
        long expectedVersion,
        CashCommandEnvelope command,
        AuditEvent audit,
        CancellationToken cancellationToken)
    {
        ValidateCommon(
            context,
            shift,
            command,
            audit,
            shift.Id,
            "cash_shift.closed",
            "CashShift",
            shift.ClosedAt ?? default);
        if (shift.Status != CashShiftStatus.Closed ||
            shift.CountedCash is null ||
            shift.Difference is null ||
            shift.ClosedAt is null ||
            command.Type != CashCommandType.CloseShift ||
            expectedVersion < 1)
        {
            throw new SalesValidationException("O pedido de fecho do turno não é válido.");
        }

        await using var db = new NofarmaDbContext(options);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        await EnsurePersistedContextAsync(db, context, cancellationToken).ConfigureAwait(false);

        CashShiftSummary? duplicate = await GetDuplicateAsync(
            db,
            context.PharmacyId,
            command,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return duplicate;
        }

        int affected = await db.CashShifts
            .Where(record => record.Id == shift.Id.Value &&
                record.PharmacyId == context.PharmacyId.Value &&
                record.DeviceId == context.DeviceId.Value &&
                record.Status == (int)CashShiftStatus.Open &&
                record.RowVersion == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.Status, (int)CashShiftStatus.Closed)
                    .SetProperty(record => record.ExpectedCashXof, shift.ExpectedCash.Amount)
                    .SetProperty(record => record.CountedCashXof, shift.CountedCash.Value.Amount)
                    .SetProperty(record => record.DifferenceXof, shift.Difference.Value.Amount)
                    .SetProperty(record => record.ClosedAtUtc, shift.ClosedAt.Value.Value)
                    .SetProperty(record => record.RowVersion, record => record.RowVersion + 1),
                cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new CashShiftConcurrencyException();
        }

        CashShiftSummary result = Map(shift);
        AddCommand(db, context, command, result, shift.ClosedAt.Value);
        db.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(audit));
        db.OutboxEvents.Add(CreateCloseOutbox(context, shift));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<CashShiftSummary?> GetDuplicateAsync(
        NofarmaDbContext db,
        EntityId pharmacyId,
        CashCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        CashCommandRecord? existing = await db.CashCommands.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == pharmacyId.Value &&
                    record.IdempotencyKey == command.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        if (existing.OperationType != (int)command.Type ||
            !string.Equals(
                existing.RequestFingerprint,
                command.RequestFingerprint,
                StringComparison.Ordinal))
        {
            throw new CashShiftConflictException();
        }

        return await DeserializeSnapshotAsync(
            db,
            existing,
            cancellationToken).ConfigureAwait(false);
    }

    private static void AddCommand(
        NofarmaDbContext db,
        CashShiftActorContext context,
        CashCommandEnvelope command,
        CashShiftSummary result,
        UtcInstant createdAt) => db.CashCommands.Add(new CashCommandRecord
        {
            Id = Guid.NewGuid(),
            PharmacyId = context.PharmacyId.Value,
            IdempotencyKey = command.IdempotencyKey,
            OperationType = (int)command.Type,
            RequestFingerprint = command.RequestFingerprint,
            CashShiftId = result.Id.Value,
            ResultJson = SerializeSnapshot(result),
            CreatedAtUtc = createdAt.Value
        });

    private static CashShiftRecord MapShift(CashShift shift, long rowVersion) => new()
    {
        Id = shift.Id.Value,
        PharmacyId = shift.PharmacyId.Value,
        DeviceId = shift.DeviceId.Value,
        UserId = shift.UserId.Value,
        Status = (int)shift.Status,
        OpeningCashXof = shift.OpeningCash.Amount,
        ExpectedCashXof = shift.ExpectedCash.Amount,
        CountedCashXof = shift.CountedCash?.Amount,
        DifferenceXof = shift.Difference?.Amount,
        OpenedAtUtc = shift.OpenedAt.Value,
        ClosedAtUtc = shift.ClosedAt?.Value,
        RowVersion = rowVersion
    };

    private static CashMovementRecord MapMovement(
        EntityId pharmacyId,
        CashMovement movement,
        long sequence) => new()
        {
            Id = movement.Id.Value,
            PharmacyId = pharmacyId.Value,
            CashShiftId = movement.CashShiftId.Value,
            Sequence = sequence,
            Type = (int)movement.Type,
            AmountXof = movement.Amount.Amount,
            SourceSaleId = movement.SourceSaleId?.Value,
            Reason = movement.Reason,
            OccurredAtUtc = movement.OccurredAt.Value
        };

    private static CashShift Rebuild(
        CashShiftRecord record,
        IReadOnlyCollection<CashMovementRecord> movements)
    {
        CashShift shift = CashShift.Open(
            new EntityId(record.Id),
            new EntityId(record.PharmacyId),
            new EntityId(record.DeviceId),
            new EntityId(record.UserId),
            Money.Xof(record.OpeningCashXof),
            UtcInstant.From(record.OpenedAtUtc));
        foreach (CashMovementRecord movement in movements)
        {
            shift.RecordMovement(
                new EntityId(movement.Id),
                (CashMovementType)movement.Type,
                Money.Xof(movement.AmountXof),
                movement.SourceSaleId is { } sourceId ? new EntityId(sourceId) : null,
                movement.Reason,
                UtcInstant.From(movement.OccurredAtUtc));
        }

        if (record.Status == (int)CashShiftStatus.Closed)
        {
            shift.Close(
                Money.Xof(record.CountedCashXof!.Value),
                UtcInstant.From(record.ClosedAtUtc!.Value));
        }

        return shift;
    }

    private static CashShiftSummary Map(CashShift shift) => new(
        shift.Id,
        shift.PharmacyId,
        shift.DeviceId,
        shift.UserId,
        shift.Status,
        shift.OpeningCash.Amount,
        shift.TotalEntries.Amount,
        shift.TotalExits.Amount,
        shift.ExpectedCash.Amount,
        shift.CountedCash?.Amount,
        shift.Difference?.Amount,
        shift.OpenedAt,
        shift.ClosedAt,
        shift.Movements.Count);

    private static OutboxEventRecord CreateOpenOutbox(
        CashShiftActorContext context,
        CashShift shift) => CreateOutbox(
            context,
            "cash_shift.opened",
            shift.Id,
            shift.OpenedAt,
            new
            {
                PharmacyId = context.PharmacyId.Value,
                DeviceId = context.DeviceId.Value,
                ShiftId = shift.Id.Value,
                OpeningCashXof = shift.OpeningCash.Amount,
                ExpectedCashXof = shift.ExpectedCash.Amount,
                OpenedAtUtc = shift.OpenedAt.Value
            });

    private static OutboxEventRecord CreateMovementOutbox(
        CashShiftActorContext context,
        CashShift shift,
        CashMovement movement) => CreateOutbox(
            context,
            movement.Type == CashMovementType.ManualEntry
                ? "cash_shift.manual_entry_recorded"
                : "cash_shift.manual_exit_recorded",
            shift.Id,
            movement.OccurredAt,
            new
            {
                PharmacyId = context.PharmacyId.Value,
                DeviceId = context.DeviceId.Value,
                ShiftId = shift.Id.Value,
                MovementId = movement.Id.Value,
                MovementType = (int)movement.Type,
                AmountXof = movement.Amount.Amount,
                ExpectedCashXof = shift.ExpectedCash.Amount,
                OccurredAtUtc = movement.OccurredAt.Value
            });

    private static OutboxEventRecord CreateCloseOutbox(
        CashShiftActorContext context,
        CashShift shift) => CreateOutbox(
            context,
            "cash_shift.closed",
            shift.Id,
            shift.ClosedAt!.Value,
            new
            {
                PharmacyId = context.PharmacyId.Value,
                DeviceId = context.DeviceId.Value,
                ShiftId = shift.Id.Value,
                ExpectedCashXof = shift.ExpectedCash.Amount,
                CountedCashXof = shift.CountedCash!.Value.Amount,
                DifferenceXof = shift.Difference!.Value.Amount,
                ClosedAtUtc = shift.ClosedAt.Value.Value
            });

    private static OutboxEventRecord CreateOutbox(
        CashShiftActorContext context,
        string eventType,
        EntityId aggregateId,
        UtcInstant occurredAt,
        object payload) => new()
        {
            Id = Guid.NewGuid(),
            PharmacyId = context.PharmacyId.Value,
            DeviceId = context.DeviceId.Value,
            EventType = eventType,
            AggregateId = aggregateId.Value,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            OccurredAtUtc = occurredAt.Value
        };

    private static string SerializeSnapshot(CashShiftSummary summary) =>
        JsonSerializer.Serialize(
            new CashShiftSnapshot(
                summary.Id.Value,
                summary.PharmacyId.Value,
                summary.DeviceId.Value,
                summary.UserId.Value,
                (int)summary.Status,
                summary.OpeningCashXof,
                summary.TotalEntriesXof,
                summary.TotalExitsXof,
                summary.ExpectedCashXof,
                summary.CountedCashXof,
                summary.DifferenceXof,
                summary.OpenedAtUtc.Value,
                summary.ClosedAtUtc?.Value,
                summary.MovementCount),
            JsonOptions);

    private static async Task<CashShiftSummary> DeserializeSnapshotAsync(
        NofarmaDbContext db,
        CashCommandRecord command,
        CancellationToken cancellationToken)
    {
        CashShiftSnapshot snapshot = JsonSerializer.Deserialize<CashShiftSnapshot>(
            command.ResultJson,
            JsonOptions) ?? throw new InvalidOperationException(
                "O resultado idempotente do turno de caixa não é válido.");
        if (snapshot.Id != command.CashShiftId ||
            snapshot.PharmacyId != command.PharmacyId ||
            snapshot.TotalEntriesXof.HasValue != snapshot.TotalExitsXof.HasValue)
        {
            throw new InvalidOperationException(
                "O resultado idempotente do turno de caixa não é válido.");
        }

        long totalEntriesXof;
        long totalExitsXof;
        if (snapshot.TotalEntriesXof is { } entries &&
            snapshot.TotalExitsXof is { } exits)
        {
            totalEntriesXof = entries;
            totalExitsXof = exits;
        }
        else
        {
            (totalEntriesXof, totalExitsXof) = await RebuildLegacySnapshotTotalsAsync(
                db,
                snapshot,
                cancellationToken).ConfigureAwait(false);
        }

        return new CashShiftSummary(
            new EntityId(snapshot.Id),
            new EntityId(snapshot.PharmacyId),
            new EntityId(snapshot.DeviceId),
            new EntityId(snapshot.UserId),
            (CashShiftStatus)snapshot.Status,
            snapshot.OpeningCashXof,
            totalEntriesXof,
            totalExitsXof,
            snapshot.ExpectedCashXof,
            snapshot.CountedCashXof,
            snapshot.DifferenceXof,
            UtcInstant.From(snapshot.OpenedAtUtc),
            snapshot.ClosedAtUtc is { } closedAt ? UtcInstant.From(closedAt) : null,
            snapshot.MovementCount);
    }

    private static async Task<(long TotalEntriesXof, long TotalExitsXof)> RebuildLegacySnapshotTotalsAsync(
        NofarmaDbContext db,
        CashShiftSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.MovementCount < 0)
        {
            throw new InvalidOperationException(
                "O resultado idempotente do turno de caixa não é válido.");
        }

        CashShiftRecord? record = await db.CashShifts.AsNoTracking()
            .SingleOrDefaultAsync(
                shift => shift.Id == snapshot.Id,
                cancellationToken).ConfigureAwait(false);
        if (record is null ||
            record.PharmacyId != snapshot.PharmacyId ||
            record.DeviceId != snapshot.DeviceId ||
            record.UserId != snapshot.UserId ||
            record.OpeningCashXof != snapshot.OpeningCashXof ||
            record.OpenedAtUtc != snapshot.OpenedAtUtc)
        {
            throw new InvalidOperationException(
                "O resultado idempotente do turno de caixa não é válido.");
        }

        CashMovementRecord[] movements = await db.CashMovements.AsNoTracking()
            .Where(movement => movement.CashShiftId == snapshot.Id)
            .OrderBy(movement => movement.Sequence)
            .Take(snapshot.MovementCount)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (movements.Length != snapshot.MovementCount)
        {
            throw new InvalidOperationException(
                "O resultado idempotente do turno de caixa não é válido.");
        }

        CashShift historical = CashShift.Open(
            new EntityId(record.Id),
            new EntityId(record.PharmacyId),
            new EntityId(record.DeviceId),
            new EntityId(record.UserId),
            Money.Xof(record.OpeningCashXof),
            UtcInstant.From(record.OpenedAtUtc));
        foreach (CashMovementRecord movement in movements)
        {
            historical.RecordMovement(
                new EntityId(movement.Id),
                (CashMovementType)movement.Type,
                Money.Xof(movement.AmountXof),
                movement.SourceSaleId is { } sourceId ? new EntityId(sourceId) : null,
                movement.Reason,
                UtcInstant.From(movement.OccurredAtUtc));
        }

        if (historical.ExpectedCash.Amount != snapshot.ExpectedCashXof)
        {
            throw new InvalidOperationException(
                "O resultado idempotente do turno de caixa não é válido.");
        }

        return (historical.TotalEntries.Amount, historical.TotalExits.Amount);
    }

    private static void ValidateCommon(
        CashShiftActorContext context,
        CashShift shift,
        CashCommandEnvelope command,
        AuditEvent audit,
        EntityId auditedObjectId,
        string expectedAction,
        string expectedObjectType,
        UtcInstant expectedOccurredAt)
    {
        string key = command.IdempotencyKey?.Trim() ?? string.Empty;
        if (context.PharmacyId != shift.PharmacyId ||
            context.DeviceId != shift.DeviceId ||
            audit.PharmacyId != context.PharmacyId ||
            audit.DeviceId != context.DeviceId ||
            audit.UserId != context.ActorUserId ||
            !string.Equals(audit.Action, expectedAction, StringComparison.Ordinal) ||
            !string.Equals(audit.ObjectType, expectedObjectType, StringComparison.Ordinal) ||
            audit.ObjectId != auditedObjectId.Value.ToString("D") ||
            audit.OccurredAtUtc != expectedOccurredAt ||
            audit.Outcome != AuditOutcome.Success ||
            audit.DiagnosticCode is not null ||
            !string.Equals(audit.DetailsJson, "{}", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(key) ||
            key.Length > 160 ||
            !string.Equals(key, command.IdempotencyKey, StringComparison.Ordinal) ||
            command.RequestFingerprint.Length != 64)
        {
            throw new SalesValidationException("O contexto da operação de caixa não é válido.");
        }
    }

    private static async Task EnsurePersistedContextAsync(
        NofarmaDbContext db,
        CashShiftActorContext context,
        CancellationToken cancellationToken)
    {
        bool deviceBelongsToPharmacy = await db.Devices.AsNoTracking().AnyAsync(
            device => device.Id == context.DeviceId.Value &&
                device.PharmacyId == context.PharmacyId.Value,
            cancellationToken).ConfigureAwait(false);
        bool actorBelongsToPharmacy = await db.LocalUsers.AsNoTracking().AnyAsync(
            user => user.Id == context.ActorUserId.Value &&
                user.PharmacyId == context.PharmacyId.Value &&
                user.Status == (int)UserStatus.Active,
            cancellationToken).ConfigureAwait(false);
        if (!deviceBelongsToPharmacy || !actorBelongsToPharmacy)
        {
            throw new SalesValidationException("O contexto persistido da operação não é válido.");
        }
    }

    private sealed record CashShiftSnapshot(
        Guid Id,
        Guid PharmacyId,
        Guid DeviceId,
        Guid UserId,
        int Status,
        long OpeningCashXof,
        long? TotalEntriesXof,
        long? TotalExitsXof,
        long ExpectedCashXof,
        long? CountedCashXof,
        long? DifferenceXof,
        DateTimeOffset OpenedAtUtc,
        DateTimeOffset? ClosedAtUtc,
        int MovementCount);
}
