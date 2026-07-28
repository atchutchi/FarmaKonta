using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Application.Sales;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Sales;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.IntegrationTests.Sales;

public sealed class CashShiftTransactionTests
{
    [Fact]
    public async Task DifferentDevicesCanOpenIndependentShifts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift first = fixture.OpenShift(fixture.DeviceId, 50_000);
        CashShift second = fixture.OpenShift(fixture.SecondDeviceId, 75_000);

        CashShiftSummary firstResult = await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            first,
            CashTestDatabase.Command("open:first", CashCommandType.OpenShift, 'A'),
            fixture.Audit(first.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        CashShiftSummary secondResult = await store.SaveOpenedAsync(
            fixture.Context(fixture.SecondDeviceId),
            second,
            CashTestDatabase.Command("open:second", CashCommandType.OpenShift, 'B'),
            fixture.Audit(second.Id, fixture.SecondDeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);

        Assert.NotEqual(firstResult.Id, secondResult.Id);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(2, await verification.CashShifts.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.CashCommands.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.OutboxEvents.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task SameDeviceCannotOpenSecondShift()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift first = fixture.OpenShift(fixture.DeviceId, 50_000);
        await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            first,
            CashTestDatabase.Command("open:first", CashCommandType.OpenShift, 'A'),
            fixture.Audit(first.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        CashShift second = fixture.OpenShift(fixture.DeviceId, 60_000);

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            second,
            CashTestDatabase.Command("open:second", CashCommandType.OpenShift, 'B'),
            fixture.Audit(second.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Single(await verification.CashShifts.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.CashCommands.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task IdenticalDuplicateReturnsOriginalImmutableSnapshot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashCommandEnvelope command = CashTestDatabase.Command(
            "open:duplicate",
            CashCommandType.OpenShift,
            'A');
        CashShift original = fixture.OpenShift(fixture.DeviceId, 50_000);
        CashShiftSummary first = await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            original,
            command,
            fixture.Audit(original.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        CashShift repeatedAggregate = fixture.OpenShift(fixture.DeviceId, 50_000);

        CashShiftSummary repeated = await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            repeatedAggregate,
            command,
            fixture.Audit(
                repeatedAggregate.Id,
                fixture.DeviceId,
                "cash_shift.opened",
                "CashShift"),
            cancellationToken);

        Assert.Equal(first, repeated);
        Assert.NotEqual(repeatedAggregate.Id, repeated.Id);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Single(await verification.CashShifts.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.CashCommands.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.AuditEvents.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.OutboxEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task ReusedKeyWithDifferentFingerprintIsRejected()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift original = fixture.OpenShift(fixture.DeviceId, 50_000);
        await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            original,
            CashTestDatabase.Command("open:conflict", CashCommandType.OpenShift, 'A'),
            fixture.Audit(original.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        CashShift conflicting = fixture.OpenShift(fixture.DeviceId, 50_000);

        await Assert.ThrowsAsync<CashShiftConflictException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            conflicting,
            CashTestDatabase.Command("open:conflict", CashCommandType.OpenShift, 'B'),
            fixture.Audit(conflicting.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken));
    }

    [Fact]
    public async Task ConcurrentIdenticalOpenStoresOneCommandResult()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashCommandEnvelope command = CashTestDatabase.Command(
            "open:concurrent",
            CashCommandType.OpenShift,
            'A');
        CashShift firstShift = fixture.OpenShift(fixture.DeviceId, 50_000);
        CashShift secondShift = fixture.OpenShift(fixture.DeviceId, 50_000);

        Task<CashShiftSummary> first = store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            firstShift,
            command,
            fixture.Audit(firstShift.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        Task<CashShiftSummary> second = store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            secondShift,
            command,
            fixture.Audit(secondShift.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        CashShiftSummary[] results = await Task.WhenAll(first, second);

        Assert.Equal(results[0], results[1]);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Single(await verification.CashShifts.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.CashCommands.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.AuditEvents.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.OutboxEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task ConcurrentDifferentMovementsPreserveBothAmounts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShiftService service = CashTestDatabase.CreateService(store);
        LocalSession session = fixture.Session();
        await service.OpenAsync(
            session,
            new OpenCashShiftRequest(1_000, "open:movements"),
            cancellationToken);

        Task<CashShiftSummary> first = service.RecordManualMovementAsync(
            session,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                100,
                "Reforço um",
                "entry:one"),
            cancellationToken);
        Task<CashShiftSummary> second = service.RecordManualMovementAsync(
            session,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                200,
                "Reforço dois",
                "entry:two"),
            cancellationToken);
        await Task.WhenAll(first, second);

        CashShiftSummary current = Assert.IsType<CashShiftSummary>(
            await service.GetCurrentAsync(session, cancellationToken));
        Assert.Equal(1_300, current.ExpectedCashXof);
        Assert.Equal(2, current.MovementCount);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(2, await verification.CashMovements.CountAsync(cancellationToken));
        Assert.Equal(3, await verification.CashCommands.CountAsync(cancellationToken));
        Assert.Equal(3, await verification.AuditEvents.CountAsync(cancellationToken));
        Assert.Equal(3, await verification.OutboxEvents.CountAsync(cancellationToken));
        string[] payloads = await verification.OutboxEvents
            .Select(record => record.PayloadJson)
            .ToArrayAsync(cancellationToken);
        Assert.DoesNotContain(payloads, payload => payload.Contains("Reforço", StringComparison.Ordinal));
        Assert.DoesNotContain(
            payloads,
            payload => payload.Contains(
                fixture.UserId.Value.ToString("D"),
                StringComparison.OrdinalIgnoreCase));
        string[] forbiddenProperties =
        [
            "userId",
            "reason",
            "displayName",
            "loginName",
            "contact",
            "credential"
        ];
        foreach (string payload in payloads)
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            string[] propertyNames = document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
            Assert.DoesNotContain(
                propertyNames,
                name => forbiddenProperties.Contains(name, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task AuditDatabaseFailureRollsBackShiftCommandAndOutbox()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        await using (var connection = new SqliteConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand trigger = connection.CreateCommand();
            trigger.CommandText = """
                CREATE TRIGGER FailCashAudit BEFORE INSERT ON AuditEvents
                BEGIN
                    SELECT RAISE(ABORT, 'forced cash audit failure');
                END;
                """;
            await trigger.ExecuteNonQueryAsync(cancellationToken);
        }

        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.DeviceId, 50_000);
        AuditEvent audit = fixture.Audit(
            shift.Id,
            fixture.DeviceId,
            "cash_shift.opened",
            "CashShift");

        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            shift,
            CashTestDatabase.Command("open:rollback", CashCommandType.OpenShift, 'A'),
            audit,
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Empty(await verification.CashShifts.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.CashCommands.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.OutboxEvents.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.AuditEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task AuditActorMustMatchCashShiftActor()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.DeviceId, 50_000);
        AuditEvent wrongActor = fixture.Audit(
            shift.Id,
            fixture.DeviceId,
            "cash_shift.opened",
            "CashShift") with
        {
            UserId = fixture.SecondUserId
        };

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            shift,
            CashTestDatabase.Command("open:wrong-audit", CashCommandType.OpenShift, 'A'),
            wrongActor,
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Empty(await verification.CashShifts.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.AuditEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task AuditMetadataMustMatchCashOperation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.DeviceId, 50_000);
        AuditEvent wrongAction = fixture.Audit(
            shift.Id,
            fixture.DeviceId,
            "cash_shift.closed",
            "CashShift");

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            shift,
            CashTestDatabase.Command("open:wrong-action", CashCommandType.OpenShift, 'A'),
            wrongAction,
            cancellationToken));
    }

    [Fact]
    public async Task AuditTimestampMustMatchCashOperationTimestamp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.DeviceId, 50_000);
        AuditEvent wrongTimestamp = fixture.Audit(
            shift.Id,
            fixture.DeviceId,
            "cash_shift.opened",
            "CashShift") with
        {
            OccurredAtUtc = UtcInstant.From(shift.OpenedAt.Value.AddMinutes(1))
        };

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            shift,
            CashTestDatabase.Command("open:wrong-time", CashCommandType.OpenShift, 'A'),
            wrongTimestamp,
            cancellationToken));
    }

    [Theory]
    [InlineData(CashMovementType.ManualEntry, CashCommandType.ManualExit)]
    [InlineData(CashMovementType.ManualExit, CashCommandType.ManualEntry)]
    public async Task CommandTypeMustMatchManualMovementType(
        CashMovementType movementType,
        CashCommandType wrongCommandType)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.DeviceId, 100);
        await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            shift,
            CashTestDatabase.Command("open:type-match", CashCommandType.OpenShift, 'A'),
            fixture.Audit(shift.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        StoredCashShift stored = Assert.IsType<StoredCashShift>(
            await store.GetCurrentAggregateAsync(
                fixture.PharmacyId,
                fixture.DeviceId,
                cancellationToken));
        CashMovement movement = stored.Shift.RecordMovement(
            EntityId.New(),
            movementType,
            Money.Xof(50),
            sourceSaleId: null,
            "Correcção",
            stored.Shift.OpenedAt);
        string wrongAction = wrongCommandType == CashCommandType.ManualEntry
            ? "cash_shift.manual_entry_recorded"
            : "cash_shift.manual_exit_recorded";

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveMovementAsync(
            fixture.Context(fixture.DeviceId),
            stored.Shift,
            movement,
            stored.Version,
            CashTestDatabase.Command("movement:wrong-type", wrongCommandType, 'B'),
            fixture.Audit(movement.Id, fixture.DeviceId, wrongAction, "CashMovement"),
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Empty(await verification.CashMovements.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.CashCommands.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task AuthorizedColleagueCanCloseShiftOpenedByAnotherCashier()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShiftService service = CashTestDatabase.CreateService(store);
        await service.OpenAsync(
            fixture.Session(),
            new OpenCashShiftRequest(1_000, "open:colleague"),
            cancellationToken);

        CashShiftSummary closed = await service.CloseAsync(
            fixture.Session(fixture.SecondUserId, UserRole.Manager),
            new CloseCashShiftRequest(1_000, "close:colleague"),
            cancellationToken);

        Assert.Equal(CashShiftStatus.Closed, closed.Status);
        await using var verification = new NofarmaDbContext(fixture.Options);
        AuditEventRecord closeAudit = await verification.AuditEvents.AsNoTracking()
            .SingleAsync(
                record => record.Action == "cash_shift.closed",
                cancellationToken);
        Assert.Equal(fixture.SecondUserId.Value, closeAudit.UserId);
    }

    [Fact]
    public async Task DeviceMustBelongToCashShiftPharmacy()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.ForeignDeviceId, 50_000);

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.ForeignDeviceId),
            shift,
            CashTestDatabase.Command("open:foreign-device", CashCommandType.OpenShift, 'A'),
            fixture.Audit(
                shift.Id,
                fixture.ForeignDeviceId,
                "cash_shift.opened",
                "CashShift"),
            cancellationToken));
    }

    [Fact]
    public async Task ActorFromAnotherPharmacyCannotOpenShift()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(
            fixture.DeviceId,
            50_000,
            fixture.ForeignUserId);

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId, fixture.ForeignUserId),
            shift,
            CashTestDatabase.Command("open:foreign-actor", CashCommandType.OpenShift, 'A'),
            fixture.Audit(
                shift.Id,
                fixture.DeviceId,
                "cash_shift.opened",
                "CashShift",
                fixture.ForeignUserId),
            cancellationToken));
    }

    [Fact]
    public async Task InactiveActorCannotOpenShift()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(
            fixture.DeviceId,
            50_000,
            fixture.InactiveUserId);

        await Assert.ThrowsAsync<SalesValidationException>(() => store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId, fixture.InactiveUserId),
            shift,
            CashTestDatabase.Command("open:inactive-actor", CashCommandType.OpenShift, 'A'),
            fixture.Audit(
                shift.Id,
                fixture.DeviceId,
                "cash_shift.opened",
                "CashShift",
                fixture.InactiveUserId),
            cancellationToken));
    }

    [Fact]
    public async Task ClosingPersistsReconciliationAndAllowsNextShift()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShiftService service = CashTestDatabase.CreateService(store);
        LocalSession session = fixture.Session();
        await service.OpenAsync(
            session,
            new OpenCashShiftRequest(1_000, "open:closing"),
            cancellationToken);
        await service.RecordManualMovementAsync(
            session,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                200,
                "Reforço",
                "entry:closing"),
            cancellationToken);

        CashShiftSummary closed = await service.CloseAsync(
            session,
            new CloseCashShiftRequest(1_150, "close:closing"),
            cancellationToken);
        CashShiftSummary next = await service.OpenAsync(
            session,
            new OpenCashShiftRequest(500, "open:next"),
            cancellationToken);

        Assert.Equal(CashShiftStatus.Closed, closed.Status);
        Assert.Equal(-50, closed.DifferenceXof);
        Assert.Equal(CashShiftStatus.Open, next.Status);
        Assert.NotEqual(closed.Id, next.Id);
        await using var verification = new NofarmaDbContext(fixture.Options);
        CashShiftRecord persisted = await verification.CashShifts.AsNoTracking()
            .SingleAsync(record => record.Id == closed.Id.Value, cancellationToken);
        Assert.Equal((int)CashShiftStatus.Closed, persisted.Status);
        Assert.Equal(1_150, persisted.CountedCashXof);
        Assert.Equal(-50, persisted.DifferenceXof);
        Assert.Equal(3, persisted.RowVersion);
        Assert.Equal(4, await verification.CashCommands.CountAsync(cancellationToken));
        Assert.Equal(4, await verification.OutboxEvents.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task IdempotentCommandSnapshotIsAppendOnly()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.DeviceId, 50_000);
        await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            shift,
            CashTestDatabase.Command("open:append-only", CashCommandType.OpenShift, 'A'),
            fixture.Audit(shift.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        await using var mutation = new NofarmaDbContext(fixture.Options);
        CashCommandRecord command = await mutation.CashCommands.SingleAsync(cancellationToken);
        command.ResultJson = "{}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mutation.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task CashMovementHistoryIsAppendOnly()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShiftService service = CashTestDatabase.CreateService(store);
        LocalSession session = fixture.Session();
        await service.OpenAsync(
            session,
            new OpenCashShiftRequest(1_000, "open:append-only"),
            cancellationToken);
        await service.RecordManualMovementAsync(
            session,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                100,
                "Original",
                "entry:append-only"),
            cancellationToken);
        await using var mutation = new NofarmaDbContext(fixture.Options);
        CashMovementRecord movement = await mutation.CashMovements.SingleAsync(cancellationToken);
        movement.Reason = "Alterado";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mutation.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task EqualTimestampMovementsRebuildInPersistedSequence()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CashTestDatabase fixture = await CashTestDatabase.CreateAsync();
        var store = new SqliteCashShiftStore(fixture.Options);
        CashShift shift = fixture.OpenShift(fixture.DeviceId, 0);
        await store.SaveOpenedAsync(
            fixture.Context(fixture.DeviceId),
            shift,
            CashTestDatabase.Command("open:sequence", CashCommandType.OpenShift, 'A'),
            fixture.Audit(shift.Id, fixture.DeviceId, "cash_shift.opened", "CashShift"),
            cancellationToken);
        StoredCashShift firstVersion = Assert.IsType<StoredCashShift>(
            await store.GetCurrentAggregateAsync(
                fixture.PharmacyId,
                fixture.DeviceId,
                cancellationToken));
        CashMovement entry = firstVersion.Shift.RecordMovement(
            new EntityId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
            CashMovementType.ManualEntry,
            Money.Xof(100),
            sourceSaleId: null,
            "Entrada",
            firstVersion.Shift.OpenedAt);
        await store.SaveMovementAsync(
            fixture.Context(fixture.DeviceId),
            firstVersion.Shift,
            entry,
            firstVersion.Version,
            CashTestDatabase.Command("entry:sequence", CashCommandType.ManualEntry, 'B'),
            fixture.Audit(
                entry.Id,
                fixture.DeviceId,
                "cash_shift.manual_entry_recorded",
                "CashMovement"),
            cancellationToken);
        StoredCashShift secondVersion = Assert.IsType<StoredCashShift>(
            await store.GetCurrentAggregateAsync(
                fixture.PharmacyId,
                fixture.DeviceId,
                cancellationToken));
        CashMovement exit = secondVersion.Shift.RecordMovement(
            new EntityId(Guid.Parse("00000000-0000-0000-0000-000000000001")),
            CashMovementType.ManualExit,
            Money.Xof(100),
            sourceSaleId: null,
            "Saída",
            secondVersion.Shift.OpenedAt);
        await store.SaveMovementAsync(
            fixture.Context(fixture.DeviceId),
            secondVersion.Shift,
            exit,
            secondVersion.Version,
            CashTestDatabase.Command("exit:sequence", CashCommandType.ManualExit, 'C'),
            fixture.Audit(
                exit.Id,
                fixture.DeviceId,
                "cash_shift.manual_exit_recorded",
                "CashMovement"),
            cancellationToken);

        StoredCashShift rebuilt = Assert.IsType<StoredCashShift>(
            await store.GetCurrentAggregateAsync(
                fixture.PharmacyId,
                fixture.DeviceId,
                cancellationToken));
        Assert.Equal(0, rebuilt.Shift.ExpectedCash.Amount);
        Assert.Collection(
            rebuilt.Shift.Movements,
            movement => Assert.Equal(CashMovementType.ManualEntry, movement.Type),
            movement => Assert.Equal(CashMovementType.ManualExit, movement.Type));
    }

    [Fact]
    public async Task MigrationUpgradesExistingInstallationAndPreservesItsData()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"nofarma-cash-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string connectionString = LocalDatabasePath.BuildConnectionString(
            Path.Combine(directory, "upgrade.db"));
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite(connectionString)
            .Options;
        Guid pharmacyId = Guid.NewGuid();
        Guid deviceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        try
        {
            await using (var previous = new NofarmaDbContext(options))
            {
                IMigrator migrator = previous.Database.GetService<IMigrator>();
                await migrator.MigrateAsync(
                    "20260727190819_AddInventory",
                    cancellationToken);
                previous.Pharmacies.Add(new PharmacyRecord
                {
                    Id = pharmacyId,
                    Name = "Farmácia existente",
                    TaxIdentifier = $"8{Random.Shared.Next(10000000, 99999999)}",
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
                    LoginName = "admin-existente",
                    NormalizedLoginName = "ADMIN-EXISTENTE",
                    Role = (int)UserRole.Administrator,
                    CredentialKind = (int)CredentialKind.Password,
                    Status = (int)UserStatus.Active,
                    CreatedAtUtc = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)
                });
                previous.Installations.Add(new InstallationRecord
                {
                    Id = Guid.NewGuid(),
                    PharmacyId = pharmacyId,
                    DeviceId = deviceId,
                    PrimaryAdministratorId = userId,
                    Status = (int)InstallationStatus.Active,
                    CreatedAtUtc = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)
                });
                await previous.SaveChangesAsync(cancellationToken);
            }

            await using (var upgraded = new NofarmaDbContext(options))
            {
                await upgraded.Database.MigrateAsync(cancellationToken);
                Assert.Equal(
                    deviceId,
                    await upgraded.Installations.AsNoTracking()
                        .Select(record => record.DeviceId)
                        .SingleAsync(cancellationToken));
                upgraded.Devices.Add(new DeviceRecord
                {
                    Id = Guid.NewGuid(),
                    PharmacyId = pharmacyId,
                    Name = "Segundo posto"
                });
                await upgraded.SaveChangesAsync(cancellationToken);
                Assert.Equal(
                    2,
                    await upgraded.Devices.CountAsync(
                        record => record.PharmacyId == pharmacyId,
                        cancellationToken));
                Assert.Equal(0, await upgraded.CashShifts.CountAsync(cancellationToken));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class CashTestDatabase(
        string directory,
        string connectionString,
        DbContextOptions<NofarmaDbContext> options,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId secondDeviceId,
        EntityId userId,
        EntityId secondUserId,
        EntityId foreignDeviceId,
        EntityId foreignUserId,
        EntityId inactiveUserId) : IAsyncDisposable
    {
        private static readonly UtcInstant Now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        public DbContextOptions<NofarmaDbContext> Options { get; } = options;

        public string ConnectionString { get; } = connectionString;

        public EntityId PharmacyId { get; } = pharmacyId;

        public EntityId DeviceId { get; } = deviceId;

        public EntityId SecondDeviceId { get; } = secondDeviceId;

        public EntityId UserId { get; } = userId;

        public EntityId SecondUserId { get; } = secondUserId;

        public EntityId ForeignDeviceId { get; } = foreignDeviceId;

        public EntityId ForeignUserId { get; } = foreignUserId;

        public EntityId InactiveUserId { get; } = inactiveUserId;

        public static async Task<CashTestDatabase> CreateAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"nofarma-cash-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string connectionString = LocalDatabasePath.BuildConnectionString(
                Path.Combine(directory, "cash.db"));
            var options = new DbContextOptionsBuilder<NofarmaDbContext>()
                .UseSqlite(connectionString)
                .Options;
            EntityId pharmacyId = EntityId.New();
            EntityId deviceId = EntityId.New();
            EntityId secondDeviceId = EntityId.New();
            EntityId userId = EntityId.New();
            EntityId secondUserId = EntityId.New();
            EntityId foreignPharmacyId = EntityId.New();
            EntityId foreignDeviceId = EntityId.New();
            EntityId foreignUserId = EntityId.New();
            EntityId inactiveUserId = EntityId.New();
            await using var context = new NofarmaDbContext(options);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            context.Pharmacies.Add(new PharmacyRecord
            {
                Id = pharmacyId.Value,
                Name = "Farmácia Caixa",
                TaxIdentifier = $"6{Random.Shared.Next(10000000, 99999999)}",
                Address = "Bissau",
                Contact = string.Empty,
                TimeZoneId = "Africa/Bissau"
            });
            context.Pharmacies.Add(new PharmacyRecord
            {
                Id = foreignPharmacyId.Value,
                Name = "Outra Farmácia",
                TaxIdentifier = $"7{Random.Shared.Next(10000000, 99999999)}",
                Address = "Bissau",
                Contact = string.Empty,
                TimeZoneId = "Africa/Bissau"
            });
            context.Devices.AddRange(
                new DeviceRecord
                {
                    Id = deviceId.Value,
                    PharmacyId = pharmacyId.Value,
                    Name = "Caixa 1"
                },
                new DeviceRecord
                {
                    Id = secondDeviceId.Value,
                    PharmacyId = pharmacyId.Value,
                    Name = "Caixa 2"
                },
                new DeviceRecord
                {
                    Id = foreignDeviceId.Value,
                    PharmacyId = foreignPharmacyId.Value,
                    Name = "Caixa externo"
                });
            context.LocalUsers.Add(new LocalUserRecord
            {
                Id = userId.Value,
                PharmacyId = pharmacyId.Value,
                DisplayName = "Operador de caixa",
                LoginName = "caixa",
                NormalizedLoginName = "CAIXA",
                Role = (int)UserRole.Cashier,
                CredentialKind = (int)CredentialKind.Pin,
                Status = (int)UserStatus.Active,
                CreatedAtUtc = Now.Value
            });
            context.LocalUsers.AddRange(
                new LocalUserRecord
                {
                    Id = foreignUserId.Value,
                    PharmacyId = foreignPharmacyId.Value,
                    DisplayName = "Utilizador externo",
                    LoginName = "externo",
                    NormalizedLoginName = "EXTERNO",
                    Role = (int)UserRole.Manager,
                    CredentialKind = (int)CredentialKind.Password,
                    Status = (int)UserStatus.Active,
                    CreatedAtUtc = Now.Value
                },
                new LocalUserRecord
                {
                    Id = inactiveUserId.Value,
                    PharmacyId = pharmacyId.Value,
                    DisplayName = "Utilizador inactivo",
                    LoginName = "inactivo",
                    NormalizedLoginName = "INACTIVO",
                    Role = (int)UserRole.Manager,
                    CredentialKind = (int)CredentialKind.Password,
                    Status = (int)UserStatus.Disabled,
                    CreatedAtUtc = Now.Value
                });
            context.LocalUsers.Add(new LocalUserRecord
            {
                Id = secondUserId.Value,
                PharmacyId = pharmacyId.Value,
                DisplayName = "Gerente",
                LoginName = "gerente",
                NormalizedLoginName = "GERENTE",
                Role = (int)UserRole.Manager,
                CredentialKind = (int)CredentialKind.Password,
                Status = (int)UserStatus.Active,
                CreatedAtUtc = Now.Value
            });
            context.Installations.Add(new InstallationRecord
            {
                Id = Guid.NewGuid(),
                PharmacyId = pharmacyId.Value,
                DeviceId = deviceId.Value,
                PrimaryAdministratorId = userId.Value,
                Status = (int)InstallationStatus.Active,
                CreatedAtUtc = Now.Value
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            return new CashTestDatabase(
                directory,
                connectionString,
                options,
                pharmacyId,
                deviceId,
                secondDeviceId,
                userId,
                secondUserId,
                foreignDeviceId,
                foreignUserId,
                inactiveUserId);
        }

        public CashShiftActorContext Context(
            EntityId targetDeviceId,
            EntityId? actorUserId = null) => new(
            PharmacyId,
            targetDeviceId,
            actorUserId ?? UserId);

        public CashShift OpenShift(
            EntityId targetDeviceId,
            long openingCashXof,
            EntityId? openerUserId = null) =>
            CashShift.Open(
                EntityId.New(),
                PharmacyId,
                targetDeviceId,
                openerUserId ?? UserId,
                Money.Xof(openingCashXof),
                Now);

        public static CashCommandEnvelope Command(
            string key,
            CashCommandType type,
            char fingerprintCharacter) => new(
                key,
                type,
                new string(fingerprintCharacter, 64));

        public AuditEvent Audit(
            EntityId objectId,
            EntityId targetDeviceId,
            string action,
            string objectType,
            EntityId? actorUserId = null) => new(
                EntityId.New(),
                PharmacyId,
                targetDeviceId,
                actorUserId ?? UserId,
                action,
                objectType,
                objectId.Value.ToString("D"),
                Now,
                AuditOutcome.Success,
                null,
                "{}");

        public LocalSession Session(
            EntityId? targetUserId = null,
            UserRole role = UserRole.Cashier) => new(
            EntityId.New(),
            targetUserId ?? UserId,
            role,
            Now,
            Now,
            null);

        public static CashShiftService CreateService(ICashShiftStore store)
        {
            var clock = new FixedClock();
            return new CashShiftService(
                store,
                new AuthorizationService(clock),
                new AllowedStockPolicy(),
                clock);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private sealed class FixedClock : IUtcClock
        {
            public UtcInstant GetCurrentInstant() => Now;
        }

        private sealed class AllowedStockPolicy : IStockOperationPolicy
        {
            public Task<StockOperationPolicyResult> CanConfirmAsync(
                CancellationToken cancellationToken) => Task.FromResult(
                    new StockOperationPolicyResult(true, null));
        }
    }
}
