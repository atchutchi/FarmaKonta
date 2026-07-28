using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Application.Sales;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Sales;

namespace Nofarma.UnitTests.Application.Sales;

public sealed class CashShiftServiceTests
{
    [Fact]
    public async Task CashierOpensShiftWithAuditAndExactXofAmount()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        CashShiftService service = CreateService(store, new StockPolicy(true));

        CashShiftSummary result = await service.OpenAsync(
            actor,
            new OpenCashShiftRequest(50_000, " open:device-1:20260728 "),
            TestContext.Current.CancellationToken);

        Assert.Equal(50_000, result.OpeningCashXof);
        Assert.Equal(50_000, result.ExpectedCashXof);
        Assert.Equal(CashShiftStatus.Open, result.Status);
        Assert.Equal("cash_shift.opened", store.LastAudit?.Action);
        Assert.Equal("open:device-1:20260728", store.LastCommand?.IdempotencyKey);
    }

    [Fact]
    public async Task UserWithoutCashPermissionCannotOpenShift()
    {
        LocalSession actor = Session(UserRole.Pharmacist);
        CashShiftService service = CreateService(
            new CashShiftStore(actor.UserId),
            new StockPolicy(true));

        await Assert.ThrowsAsync<AuthorizationException>(() => service.OpenAsync(
            actor,
            new OpenCashShiftRequest(0, "open:1"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpeningIsBlockedWithoutActiveLicence()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        CashShiftService service = CreateService(store, new StockPolicy(false));

        CashShiftOperationBlockedException exception = await Assert.ThrowsAsync<CashShiftOperationBlockedException>(
            () => service.OpenAsync(
                actor,
                new OpenCashShiftRequest(0, "open:1"),
                TestContext.Current.CancellationToken));

        Assert.Equal("LICENSE_REQUIRED", exception.Code);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task ManualMovementIsBlockedWithoutActiveLicence()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(false));

        await Assert.ThrowsAsync<CashShiftOperationBlockedException>(() =>
            service.RecordManualMovementAsync(
                actor,
                new ManualCashMovementRequest(
                    CashMovementType.ManualEntry,
                    500,
                    "Reforço",
                    "entry:1"),
                TestContext.Current.CancellationToken));

        Assert.Equal(1_000, store.Current!.Shift.ExpectedCash.Amount);
        Assert.Empty(store.Current.Shift.Movements);
    }

    [Fact]
    public async Task CurrentShiftAndSafeCloseRemainAvailableWhenLicenceIsBlocked()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(false));

        CashShiftSummary? current = await service.GetCurrentAsync(
            actor,
            TestContext.Current.CancellationToken);
        CashShiftSummary closed = await service.CloseAsync(
            actor,
            new CloseCashShiftRequest(1_050, "close:1"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(current);
        Assert.Equal(CashShiftStatus.Open, current.Status);
        Assert.Equal(CashShiftStatus.Closed, closed.Status);
        Assert.Equal(50, closed.DifferenceXof);
        Assert.Equal("cash_shift.closed", store.LastAudit?.Action);
    }

    [Fact]
    public async Task SecondOpenShiftOnSameDeviceIsRejected()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        store.Seed(OpenShift(store.Context));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        await Assert.ThrowsAsync<SalesValidationException>(() => service.OpenAsync(
            actor,
            new OpenCashShiftRequest(0, "open:2"),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankIdempotencyKeyIsRejected(string key)
    {
        LocalSession actor = Session(UserRole.Cashier);
        CashShiftService service = CreateService(
            new CashShiftStore(actor.UserId),
            new StockPolicy(true));

        await Assert.ThrowsAsync<SalesValidationException>(() => service.OpenAsync(
            actor,
            new OpenCashShiftRequest(0, key),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OversizedIdempotencyKeyIsRejected()
    {
        LocalSession actor = Session(UserRole.Cashier);
        CashShiftService service = CreateService(
            new CashShiftStore(actor.UserId),
            new StockPolicy(true));

        await Assert.ThrowsAsync<SalesValidationException>(() => service.OpenAsync(
            actor,
            new OpenCashShiftRequest(0, new string('k', 161)),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManualMovementRequiresSupportedTypeAndReason()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        store.Seed(OpenShift(store.Context));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        await Assert.ThrowsAsync<SalesValidationException>(() =>
            service.RecordManualMovementAsync(
                actor,
                new ManualCashMovementRequest(
                    CashMovementType.Sale,
                    100,
                    "Venda",
                    "movement:1"),
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SalesValidationException>(() =>
            service.RecordManualMovementAsync(
                actor,
                new ManualCashMovementRequest(
                    CashMovementType.ManualEntry,
                    100,
                    " ",
                    "movement:2"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdenticalDuplicateReturnsStoredResultAfterLicenceBecomesBlocked()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        var policy = new StockPolicy(true);
        CashShiftService service = CreateService(store, policy);
        var request = new OpenCashShiftRequest(50_000, "open:1");
        CashShiftSummary first = await service.OpenAsync(
            actor,
            request,
            TestContext.Current.CancellationToken);
        policy.Allowed = false;

        CashShiftSummary duplicate = await service.OpenAsync(
            actor,
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(first, duplicate);
        Assert.Equal(1, store.OpenSaveCount);
    }

    [Fact]
    public async Task ConcurrentIdenticalOpenReturnsCommittedIdempotentResult()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            PublishConcurrentDuplicateOnOpenSave = true
        };
        CashShiftService service = CreateService(store, new StockPolicy(true));

        CashShiftSummary result = await service.OpenAsync(
            actor,
            new OpenCashShiftRequest(50_000, "open:concurrent"),
            TestContext.Current.CancellationToken);

        Assert.Equal(CashShiftStatus.Open, result.Status);
        Assert.Equal(50_000, result.OpeningCashXof);
        Assert.Equal(1, store.OpenSaveCount);
    }

    [Fact]
    public async Task ReusingIdempotencyKeyWithDifferentPayloadIsConflict()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId);
        CashShiftService service = CreateService(store, new StockPolicy(true));
        await service.OpenAsync(
            actor,
            new OpenCashShiftRequest(50_000, "open:1"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<CashShiftConflictException>(() => service.OpenAsync(
            actor,
            new OpenCashShiftRequest(60_000, "open:1"),
            TestContext.Current.CancellationToken));
        Assert.Equal(1, store.OpenSaveCount);
    }

    [Fact]
    public async Task SameOpenKeyAndAmountOnAnotherDeviceIsConflict()
    {
        LocalSession actor = Session(UserRole.Cashier);
        EntityId pharmacyId = EntityId.New();
        var commands = new Dictionary<string, CashCommandResult>(StringComparer.Ordinal);
        var firstStore = new CashShiftStore(actor.UserId, commands, pharmacyId, EntityId.New());
        var secondStore = new CashShiftStore(actor.UserId, commands, pharmacyId, EntityId.New());
        await CreateService(firstStore, new StockPolicy(true)).OpenAsync(
            actor,
            new OpenCashShiftRequest(50_000, "open:shared"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<CashShiftConflictException>(() =>
            CreateService(secondStore, new StockPolicy(true)).OpenAsync(
                actor,
                new OpenCashShiftRequest(50_000, "open:shared"),
                TestContext.Current.CancellationToken));

        Assert.Null(secondStore.Current);
    }

    [Fact]
    public async Task SameMovementKeyAndPayloadOnAnotherDeviceIsConflict()
    {
        LocalSession actor = Session(UserRole.Cashier);
        EntityId pharmacyId = EntityId.New();
        var commands = new Dictionary<string, CashCommandResult>(StringComparer.Ordinal);
        var firstStore = new CashShiftStore(actor.UserId, commands, pharmacyId, EntityId.New());
        var secondStore = new CashShiftStore(actor.UserId, commands, pharmacyId, EntityId.New());
        firstStore.Seed(OpenShift(firstStore.Context, 1_000));
        secondStore.Seed(OpenShift(secondStore.Context, 1_000));
        var request = new ManualCashMovementRequest(
            CashMovementType.ManualEntry,
            500,
            "Reforço",
            "entry:shared");
        await CreateService(firstStore, new StockPolicy(true)).RecordManualMovementAsync(
            actor,
            request,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<CashShiftConflictException>(() =>
            CreateService(secondStore, new StockPolicy(true)).RecordManualMovementAsync(
                actor,
                request,
                TestContext.Current.CancellationToken));

        Assert.Empty(secondStore.Current!.Shift.Movements);
    }

    [Fact]
    public async Task SameCloseKeyAndAmountOnAnotherDeviceIsConflict()
    {
        LocalSession actor = Session(UserRole.Cashier);
        EntityId pharmacyId = EntityId.New();
        var commands = new Dictionary<string, CashCommandResult>(StringComparer.Ordinal);
        var firstStore = new CashShiftStore(actor.UserId, commands, pharmacyId, EntityId.New());
        var secondStore = new CashShiftStore(actor.UserId, commands, pharmacyId, EntityId.New());
        firstStore.Seed(OpenShift(firstStore.Context, 1_000));
        secondStore.Seed(OpenShift(secondStore.Context, 1_000));
        var request = new CloseCashShiftRequest(1_000, "close:shared");
        await CreateService(firstStore, new StockPolicy(true)).CloseAsync(
            actor,
            request,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<CashShiftConflictException>(() =>
            CreateService(secondStore, new StockPolicy(true)).CloseAsync(
                actor,
                request,
                TestContext.Current.CancellationToken));

        Assert.Equal(CashShiftStatus.Open, secondStore.Current!.Shift.Status);
    }

    [Fact]
    public async Task ManualMovementReloadsAndRetriesConcurrencyConflict()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            ConcurrencyFailuresRemaining = 2
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        CashShiftSummary result = await service.RecordManualMovementAsync(
            actor,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                500,
                "Reforço",
                "entry:1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1_500, result.ExpectedCashXof);
        Assert.Equal(3, store.MovementSaveAttempts);
        Assert.Single(store.Current!.Shift.Movements);
    }

    [Fact]
    public async Task ConcurrentIdenticalMovementReturnsCommittedIdempotentResult()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            PublishConcurrentDuplicateOnMovementSaveAttempt = 1
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        CashShiftSummary result = await service.RecordManualMovementAsync(
            actor,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                500,
                "Reforço",
                "entry:concurrent"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1_500, result.ExpectedCashXof);
        Assert.Equal(1, store.MovementSaveAttempts);
        Assert.Single(store.Current!.Shift.Movements);
    }

    [Fact]
    public async Task IdenticalMovementCommittedOnThirdAttemptReturnsStoredResult()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            ConcurrencyFailuresRemaining = 2,
            PublishConcurrentDuplicateOnMovementSaveAttempt = 3
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        CashShiftSummary result = await service.RecordManualMovementAsync(
            actor,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                500,
                "Reforço",
                "entry:third"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1_500, result.ExpectedCashXof);
        Assert.Equal(3, store.MovementSaveAttempts);
        Assert.Single(store.Current!.Shift.Movements);
    }

    [Fact]
    public async Task ConcurrentIdenticalCloseReturnsCommittedIdempotentResult()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            PublishConcurrentDuplicateOnCloseSaveAttempt = 1
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        CashShiftSummary result = await service.CloseAsync(
            actor,
            new CloseCashShiftRequest(900, "close:concurrent"),
            TestContext.Current.CancellationToken);

        Assert.Equal(CashShiftStatus.Closed, result.Status);
        Assert.Equal(-100, result.DifferenceXof);
        Assert.Equal(1, store.CloseSaveAttempts);
        Assert.Equal(CashShiftStatus.Closed, store.Current!.Shift.Status);
    }

    [Fact]
    public async Task IdenticalCloseCommittedOnThirdAttemptReturnsStoredResult()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            ConcurrencyFailuresRemaining = 2,
            PublishConcurrentDuplicateOnCloseSaveAttempt = 3
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        CashShiftSummary result = await service.CloseAsync(
            actor,
            new CloseCashShiftRequest(900, "close:third"),
            TestContext.Current.CancellationToken);

        Assert.Equal(CashShiftStatus.Closed, result.Status);
        Assert.Equal(-100, result.DifferenceXof);
        Assert.Equal(3, store.CloseSaveAttempts);
    }

    [Fact]
    public async Task ManualMovementRefreshesTimestampAfterConcurrentLaterMovement()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            PublishLaterMovementOnNextMovementSave = true
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(
            store,
            new StockPolicy(true),
            new RetryClock());

        CashShiftSummary result = await service.RecordManualMovementAsync(
            actor,
            new ManualCashMovementRequest(
                CashMovementType.ManualEntry,
                500,
                "Reforço",
                "entry:later"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1_600, result.ExpectedCashXof);
        Assert.Equal(2, result.MovementCount);
    }

    [Fact]
    public async Task CloseRefreshesTimestampAfterConcurrentLaterMovement()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            PublishLaterMovementOnNextCloseSave = true
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(
            store,
            new StockPolicy(true),
            new RetryClock());

        CashShiftSummary result = await service.CloseAsync(
            actor,
            new CloseCashShiftRequest(900, "close:later"),
            TestContext.Current.CancellationToken);

        Assert.Equal(CashShiftStatus.Closed, result.Status);
        Assert.Equal(-200, result.DifferenceXof);
        Assert.Equal(1, result.MovementCount);
    }

    [Fact]
    public async Task ManualMovementStopsAfterThreeConcurrencyConflicts()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId)
        {
            ConcurrencyFailuresRemaining = 3
        };
        store.Seed(OpenShift(store.Context, openingCashXof: 1_000));
        CashShiftService service = CreateService(store, new StockPolicy(true));

        await Assert.ThrowsAsync<CashShiftConcurrencyException>(() =>
            service.RecordManualMovementAsync(
                actor,
                new ManualCashMovementRequest(
                    CashMovementType.ManualEntry,
                    500,
                    "Reforço",
                    "entry:1"),
                TestContext.Current.CancellationToken));

        Assert.Equal(3, store.MovementSaveAttempts);
        Assert.Empty(store.Current!.Shift.Movements);
        Assert.Equal(1_000, store.Current.Shift.ExpectedCash.Amount);
    }

    [Fact]
    public async Task MissingActorContextInvalidatesSession()
    {
        LocalSession actor = Session(UserRole.Cashier);
        var store = new CashShiftStore(actor.UserId) { ContextAvailable = false };
        CashShiftService service = CreateService(store, new StockPolicy(true));

        await Assert.ThrowsAsync<AuthorizationException>(() => service.GetCurrentAsync(
            actor,
            TestContext.Current.CancellationToken));
    }

    private static CashShiftService CreateService(
        ICashShiftStore store,
        IStockOperationPolicy policy,
        IUtcClock? clock = null)
    {
        clock ??= new FixedClock();
        return new CashShiftService(
            store,
            new AuthorizationService(clock),
            policy,
            clock);
    }

    private static LocalSession Session(UserRole role) => new(
        EntityId.New(),
        EntityId.New(),
        role,
        FixedClock.Now,
        FixedClock.Now,
        null);

    private static CashShift OpenShift(
        CashShiftActorContext context,
        long openingCashXof = 0) => CashShift.Open(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            context.ActorUserId,
            Money.Xof(openingCashXof),
            FixedClock.Now);

    private sealed class CashShiftStore(
        EntityId actorUserId,
        Dictionary<string, CashCommandResult>? commands = null,
        EntityId? pharmacyId = null,
        EntityId? deviceId = null) : ICashShiftStore
    {
        private readonly Dictionary<string, CashCommandResult> _commands =
            commands ?? new(StringComparer.Ordinal);

        public CashShiftActorContext Context { get; } = new(
            pharmacyId ?? EntityId.New(),
            deviceId ?? EntityId.New(),
            actorUserId);

        public StoredCashShift? Current { get; private set; }

        public AuditEvent? LastAudit { get; private set; }

        public CashCommandEnvelope? LastCommand { get; private set; }

        public bool ContextAvailable { get; set; } = true;

        public int ConcurrencyFailuresRemaining { get; set; }

        public int MovementSaveAttempts { get; private set; }

        public int OpenSaveCount { get; private set; }

        public int PublishConcurrentDuplicateOnMovementSaveAttempt { get; set; }

        public int PublishConcurrentDuplicateOnCloseSaveAttempt { get; set; }

        public int CloseSaveAttempts { get; private set; }

        public bool PublishLaterMovementOnNextMovementSave { get; set; }

        public bool PublishLaterMovementOnNextCloseSave { get; set; }

        public bool PublishConcurrentDuplicateOnOpenSave { get; set; }

        public void Seed(CashShift shift, long version = 1) =>
            Current = new StoredCashShift(Clone(shift), version);

        public Task<CashShiftActorContext?> GetActorContextAsync(
            EntityId userId,
            CancellationToken cancellationToken) => Task.FromResult(
                ContextAvailable && userId == Context.ActorUserId ? Context : null);

        public Task<StoredCashShift?> GetCurrentAggregateAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            CancellationToken cancellationToken) => Task.FromResult(
                Current is not null &&
                Current.Shift.PharmacyId == pharmacyId &&
                Current.Shift.DeviceId == deviceId
                    ? new StoredCashShift(Clone(Current.Shift), Current.Version)
                    : null);

        public Task<CashCommandResult?> GetCommandResultAsync(
            EntityId pharmacyId,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult(
                _commands.GetValueOrDefault(idempotencyKey));

        public Task<CashShiftSummary> SaveOpenedAsync(
            CashShiftActorContext context,
            CashShift shift,
            CashCommandEnvelope command,
            AuditEvent audit,
            CancellationToken cancellationToken)
        {
            OpenSaveCount++;
            Current = new StoredCashShift(Clone(shift), 1);
            CashShiftSummary result = StoreResult(shift, command, audit);
            if (PublishConcurrentDuplicateOnOpenSave)
            {
                PublishConcurrentDuplicateOnOpenSave = false;
                throw new CashShiftConcurrencyException();
            }

            return Task.FromResult(result);
        }

        public Task<CashShiftSummary> SaveMovementAsync(
            CashShiftActorContext context,
            CashShift shift,
            CashMovement movement,
            long expectedVersion,
            CashCommandEnvelope command,
            AuditEvent audit,
            CancellationToken cancellationToken)
        {
            MovementSaveAttempts++;
            if (PublishLaterMovementOnNextMovementSave)
            {
                PublishLaterMovementOnNextMovementSave = false;
                PublishConcurrentLaterMovement(expectedVersion);
                throw new CashShiftConcurrencyException();
            }

            if (MovementSaveAttempts == PublishConcurrentDuplicateOnMovementSaveAttempt)
            {
                PublishConcurrentDuplicateOnMovementSaveAttempt = 0;
                Current = new StoredCashShift(Clone(shift), checked(expectedVersion + 1));
                StoreResult(shift, command, audit);
                throw new CashShiftConcurrencyException();
            }

            if (ConcurrencyFailuresRemaining > 0)
            {
                ConcurrencyFailuresRemaining--;
                throw new CashShiftConcurrencyException();
            }

            if (Current is null || Current.Version != expectedVersion)
            {
                throw new CashShiftConcurrencyException();
            }

            Current = new StoredCashShift(Clone(shift), checked(expectedVersion + 1));
            return Task.FromResult(StoreResult(shift, command, audit));
        }

        public Task<CashShiftSummary> SaveClosedAsync(
            CashShiftActorContext context,
            CashShift shift,
            long expectedVersion,
            CashCommandEnvelope command,
            AuditEvent audit,
            CancellationToken cancellationToken)
        {
            CloseSaveAttempts++;
            if (PublishLaterMovementOnNextCloseSave)
            {
                PublishLaterMovementOnNextCloseSave = false;
                PublishConcurrentLaterMovement(expectedVersion);
                throw new CashShiftConcurrencyException();
            }

            if (CloseSaveAttempts == PublishConcurrentDuplicateOnCloseSaveAttempt)
            {
                PublishConcurrentDuplicateOnCloseSaveAttempt = 0;
                Current = new StoredCashShift(Clone(shift), checked(expectedVersion + 1));
                StoreResult(shift, command, audit);
                throw new CashShiftConcurrencyException();
            }

            if (ConcurrencyFailuresRemaining > 0)
            {
                ConcurrencyFailuresRemaining--;
                throw new CashShiftConcurrencyException();
            }

            if (Current is null || Current.Version != expectedVersion)
            {
                throw new CashShiftConcurrencyException();
            }

            Current = new StoredCashShift(Clone(shift), checked(expectedVersion + 1));
            return Task.FromResult(StoreResult(shift, command, audit));
        }

        private CashShiftSummary StoreResult(
            CashShift shift,
            CashCommandEnvelope command,
            AuditEvent audit)
        {
            CashShiftSummary summary = Summary(shift);
            LastAudit = audit;
            LastCommand = command;
            _commands.Add(
                command.IdempotencyKey,
                new CashCommandResult(command.RequestFingerprint, summary));
            return summary;
        }

        private void PublishConcurrentLaterMovement(long expectedVersion)
        {
            CashShift concurrent = Clone(Current!.Shift);
            concurrent.RecordMovement(
                EntityId.New(),
                CashMovementType.ManualEntry,
                Money.Xof(100),
                sourceSaleId: null,
                "Movimento concorrente",
                RetryClock.ConcurrentInstant);
            Current = new StoredCashShift(concurrent, checked(expectedVersion + 1));
        }

        private static CashShift Clone(CashShift source)
        {
            CashShift clone = CashShift.Open(
                source.Id,
                source.PharmacyId,
                source.DeviceId,
                source.UserId,
                source.OpeningCash,
                source.OpenedAt);
            foreach (CashMovement movement in source.Movements)
            {
                clone.RecordMovement(
                    movement.Id,
                    movement.Type,
                    movement.Amount,
                    movement.SourceSaleId,
                    movement.Reason,
                    movement.OccurredAt);
            }

            if (source.Status == CashShiftStatus.Closed)
            {
                clone.Close(source.CountedCash!.Value, source.ClosedAt!.Value);
            }

            return clone;
        }

        private static CashShiftSummary Summary(CashShift shift) => new(
            shift.Id,
            shift.PharmacyId,
            shift.DeviceId,
            shift.UserId,
            shift.Status,
            shift.OpeningCash.Amount,
            shift.ExpectedCash.Amount,
            shift.CountedCash?.Amount,
            shift.Difference?.Amount,
            shift.OpenedAt,
            shift.ClosedAt,
            shift.Movements.Count);
    }

    private sealed class StockPolicy(bool allowed) : IStockOperationPolicy
    {
        public bool Allowed { get; set; } = allowed;

        public Task<StockOperationPolicyResult> CanConfirmAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StockOperationPolicyResult(
                    Allowed,
                    Allowed ? null : "LICENSE_REQUIRED"));
    }

    private sealed class FixedClock : IUtcClock
    {
        public static readonly UtcInstant Now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        public UtcInstant GetCurrentInstant() => Now;
    }

    private sealed class RetryClock : IUtcClock
    {
        private int _calls;

        public static readonly UtcInstant ConcurrentInstant = UtcInstant.From(
            FixedClock.Now.Value.AddMinutes(1));

        private static readonly UtcInstant RetryInstant = UtcInstant.From(
            FixedClock.Now.Value.AddMinutes(2));

        public UtcInstant GetCurrentInstant() => _calls++ < 2
            ? FixedClock.Now
            : RetryInstant;
    }
}
