using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Application.Inventory;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task StockManagerConfirmsQuickEntryWithAuditAndIdempotency()
    {
        var store = new InventoryStore();
        InventoryService service = CreateService(store, allowed: true);
        var request = new StockEntryRequest(
            EntityId.New(),
            20,
            StockMovementType.QuickEntry,
            "LOT-01",
            ExpiryDate.ForMonth(2027, 12),
            null,
            50,
            "Entrada de regularização",
            null,
            "device-1:entry-1");

        StockConfirmationResult result = await service.ConfirmEntryAsync(
            Session(UserRole.StockManager),
            request,
            CancellationToken.None);

        Assert.Equal(20, result.QuantityBase);
        Assert.Equal("device-1:entry-1", store.LastConfirmation?.Operation.IdempotencyKey);
        Assert.Equal("stock.quick_entry_confirmed", store.LastAudit?.Action);
        Assert.Equal(result.MovementId.Value.ToString("D"), store.LastAudit?.ObjectId);
    }

    [Fact]
    public async Task ConfirmationIsBlockedWithoutActiveLicence()
    {
        InventoryService service = CreateService(new InventoryStore(), allowed: false);

        StockOperationBlockedException exception = await Assert.ThrowsAsync<StockOperationBlockedException>(
            () => service.ConfirmEntryAsync(
                Session(UserRole.StockManager),
                ValidEntry(),
                CancellationToken.None));

        Assert.Equal("LICENSE_MISSING", exception.Code);
    }

    [Fact]
    public async Task ExistingEntryResultReturnsBeforeBlockedLicenceAndWithoutAnotherWrite()
    {
        var store = new InventoryStore
        {
            IdempotentResult = new StockConfirmationResult(
                EntityId.New(), EntityId.New(), EntityId.New(), 10, 10,
                StockMovementType.QuickEntry, FixedClock.Now)
        };
        InventoryService service = CreateService(store, allowed: false);

        StockConfirmationResult result = await service.ConfirmEntryAsync(
            Session(UserRole.StockManager), ValidEntry(), CancellationToken.None);

        Assert.Equal(store.IdempotentResult, result);
        Assert.Equal(0, store.ConfirmationWrites);
    }

    [Fact]
    public async Task CashierCannotAdjustStock()
    {
        InventoryService service = CreateService(new InventoryStore(), allowed: true);

        await Assert.ThrowsAsync<AuthorizationException>(() => service.ConfirmAdjustmentAsync(
            Session(UserRole.Cashier),
            new StockAdjustmentRequest(
                EntityId.New(),
                EntityId.New(),
                -1,
                StockMovementType.Loss,
                "Quebra",
                "device-1:loss-1"),
            CancellationToken.None));
    }

    [Fact]
    public async Task AdjustmentIsBlockedWithoutLicenceBeforeTheStoreWrite()
    {
        var store = new InventoryStore();
        InventoryService service = CreateService(store, allowed: false);

        StockOperationBlockedException error = await Assert.ThrowsAsync<StockOperationBlockedException>(
            () => service.ConfirmAdjustmentAsync(
                Session(UserRole.Administrator),
                new StockAdjustmentRequest(
                    EntityId.New(),
                    EntityId.New(),
                    -1,
                    StockMovementType.Loss,
                    "Quebra",
                    "adjustment:1"),
                CancellationToken.None));

        Assert.Equal("LICENSE_MISSING", error.Code);
        Assert.Null(store.LastConfirmation);
    }

    [Fact]
    public async Task ManagerCannotConfirmOpeningInventoryWithoutImportPermission()
    {
        InventoryService service = CreateService(new InventoryStore(), allowed: true);
        StockEntryRequest request = ValidEntry() with
        {
            Type = StockMovementType.OpeningInventory,
            Reason = null
        };

        await Assert.ThrowsAsync<AuthorizationException>(() => service.ConfirmEntryAsync(
            Session(UserRole.Manager),
            request,
            CancellationToken.None));
    }

    [Fact]
    public async Task MedicineEntryRequiresLotAndExpiry()
    {
        var store = new InventoryStore
        {
            Rules = new StockProductRules(ProductType.Medicine, true, true, true)
        };
        InventoryService service = CreateService(store, allowed: true);
        StockEntryRequest request = ValidEntry() with { LotNumber = null, Expiry = null };

        await Assert.ThrowsAsync<InventoryValidationException>(() => service.ConfirmEntryAsync(
            Session(UserRole.StockManager),
            request,
            CancellationToken.None));

        Assert.Null(store.LastConfirmation);
    }

    [Fact]
    public async Task AdjustmentRequiresReasonAndIdempotencyKey()
    {
        InventoryService service = CreateService(new InventoryStore(), allowed: true);
        var request = new StockAdjustmentRequest(
            EntityId.New(),
            EntityId.New(),
            -1,
            StockMovementType.Damage,
            null,
            string.Empty);

        await Assert.ThrowsAsync<InventoryValidationException>(() => service.ConfirmAdjustmentAsync(
            Session(UserRole.Administrator),
            request,
            CancellationToken.None));
    }

    [Fact]
    public async Task AdministratorCompensatesOriginalMovementWithAudit()
    {
        var store = new InventoryStore();
        InventoryService service = CreateService(store, allowed: true);
        EntityId originalMovementId = EntityId.New();

        StockConfirmationResult result = await service.CompensateAsync(
            Session(UserRole.Administrator),
            new StockCompensationRequest(
                originalMovementId,
                "Produto errado",
                "device-1:compensation-1"),
            CancellationToken.None);

        Assert.Equal(originalMovementId, store.LastCompensation?.OriginalMovementId);
        Assert.Equal("stock.compensation_confirmed", store.LastAudit?.Action);
        Assert.Equal(StockMovementType.Compensation, result.Type);
    }

    [Fact]
    public async Task CompensationIsBlockedWithoutLicenceBeforeTheStoreWrite()
    {
        var store = new InventoryStore();
        InventoryService service = CreateService(store, allowed: false);

        StockOperationBlockedException error = await Assert.ThrowsAsync<StockOperationBlockedException>(
            () => service.CompensateAsync(
                Session(UserRole.Administrator),
                new StockCompensationRequest(
                    EntityId.New(),
                    "Produto errado",
                    "compensation:1"),
                CancellationToken.None));

        Assert.Equal("LICENSE_MISSING", error.Code);
        Assert.Null(store.LastCompensation);
    }

    [Fact]
    public async Task CashierCanReadStockWithoutPurchaseCost()
    {
        var store = new InventoryStore();
        var clock = new FixedClock();
        var service = new InventoryQueryService(
            store,
            new AuthorizationService(clock),
            clock);

        ProductStockDetails result = await service.GetProductAsync(
            Session(UserRole.Cashier),
            EntityId.New(),
            CancellationToken.None);

        Assert.Equal(9, result.TotalQuantityBase);
        Assert.Equal(StockAlertLevel.LowStock, result.StockAlertLevel);
        Assert.Single(result.Lots);
    }

    private static InventoryService CreateService(IInventoryStore store, bool allowed)
    {
        var clock = new FixedClock();
        return new InventoryService(
            store,
            new AuthorizationService(clock),
            new StockPolicy(allowed),
            clock);
    }

    private static StockEntryRequest ValidEntry() => new(
        EntityId.New(),
        10,
        StockMovementType.QuickEntry,
        "LOT-01",
        ExpiryDate.ForMonth(2027, 12),
        null,
        50,
        "Entrada",
        null,
        "device-1:entry-1");

    private static LocalSession Session(UserRole role) => new(
        EntityId.New(),
        EntityId.New(),
        role,
        FixedClock.Now,
        FixedClock.Now,
        null);

    private sealed class InventoryStore : IInventoryStore
    {
        public StockProductRules Rules { get; set; } = new(ProductType.General, true, true, true);

        public InventoryConfirmation? LastConfirmation { get; private set; }

        public AuditEvent? LastAudit { get; private set; }

        public StockCompensationCommand? LastCompensation { get; private set; }

        public StockConfirmationResult? IdempotentResult { get; set; }

        public int ConfirmationWrites { get; private set; }

        public Task<InventoryActorContext?> GetContextAsync(
            EntityId actorUserId,
            CancellationToken cancellationToken) => Task.FromResult<InventoryActorContext?>(
                new(EntityId.New(), EntityId.New()));

        public Task<StockProductRules?> GetProductRulesAsync(
            EntityId pharmacyId,
            EntityId productId,
            CancellationToken cancellationToken) => Task.FromResult<StockProductRules?>(Rules);

        public Task<StockConfirmationResult?> GetIdempotentResultAsync(
            EntityId pharmacyId,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult(IdempotentResult);

        public Task<StockConfirmationResult> ConfirmAsync(
            InventoryActorContext context,
            InventoryConfirmation confirmation,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            ConfirmationWrites++;
            LastConfirmation = confirmation;
            LastAudit = auditEvent;
            return Task.FromResult(new StockConfirmationResult(
                confirmation.Operation.MovementId,
                confirmation.Operation.ProductId,
                confirmation.Operation.LotId,
                confirmation.Operation.QuantityBase,
                confirmation.Operation.QuantityBase,
                confirmation.Operation.Type,
                confirmation.Operation.OccurredUtc));
        }

        public Task<StockConfirmationResult> CompensateAsync(
            InventoryActorContext context,
            StockCompensationCommand command,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastCompensation = command;
            LastAudit = auditEvent;
            return Task.FromResult(new StockConfirmationResult(
                command.MovementId,
                EntityId.New(),
                EntityId.New(),
                -1,
                0,
                StockMovementType.Compensation,
                command.OccurredUtc));
        }

        public Task<ProductStockDetails?> GetProductStockAsync(
            EntityId pharmacyId,
            EntityId productId,
            DateOnly businessDate,
            CancellationToken cancellationToken) => Task.FromResult<ProductStockDetails?>(new(
                productId,
                "PRD-000001",
                "Produto",
                9,
                10,
                StockAlertLevel.LowStock,
                [new(
                    EntityId.New(),
                    "LOT-01",
                    9,
                    new DateOnly(2027, 12, 31),
                    StockAlertLevel.Normal)],
                []));

        public Task<IReadOnlyList<StockAllocation>> AllocateFefoAsync(
            EntityId pharmacyId,
            EntityId productId,
            long requiredQuantityBase,
            DateOnly businessDate,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StockAllocation>>(
                [new(EntityId.New(), requiredQuantityBase)]);

        public Task<StockOverview> SearchStockAsync(
            EntityId pharmacyId,
            string query,
            DateOnly businessDate,
            CancellationToken cancellationToken) => Task.FromResult(new StockOverview([], 0, 0, 0));
    }

    private sealed class StockPolicy(bool allowed) : ILicensedOperationPolicy
    {
        public Task<LicensedOperationPolicyResult> CanCreateAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new LicensedOperationPolicyResult(allowed, allowed ? null : "LICENSE_MISSING"));
    }

    private sealed class FixedClock : IUtcClock
    {
        public static readonly UtcInstant Now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        public UtcInstant GetCurrentInstant() => Now;
    }
}
