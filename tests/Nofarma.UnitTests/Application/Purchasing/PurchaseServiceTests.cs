using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Application.Purchasing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Purchasing;

namespace Nofarma.UnitTests.Application.Purchasing;

public sealed class PurchaseServiceTests
{
    [Fact]
    public async Task ManagerCreatesDraftWithAudit()
    {
        var store = new PurchaseStore();
        PurchaseService service = CreateService(store, allowed: true);

        PurchaseDetails result = await service.CreateAsync(
            Session(UserRole.Manager),
            ValidCreateRequest(),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Draft, result.Status);
        Assert.Equal("purchase.created", store.LastAudit?.Action);
        Assert.Equal(result.Id.Value.ToString("D"), store.LastAudit?.ObjectId);
    }

    [Fact]
    public async Task AuditorReadsPurchaseWithoutCosts()
    {
        var store = new PurchaseStore();
        PurchaseService service = CreateService(store, allowed: true);

        PurchaseDetails details = await service.GetAsync(
            Session(UserRole.Auditor),
            EntityId.New(),
            CancellationToken.None);

        Assert.False(store.LastIncludeCosts);
        Assert.Null(Assert.Single(details.Lines).UnitCostXof);
        Assert.Null(details.TotalXof);
    }

    [Fact]
    public async Task CashierCannotReadPurchases()
    {
        PurchaseService service = CreateService(new PurchaseStore(), allowed: true);

        await Assert.ThrowsAsync<AuthorizationException>(() => service.GetAsync(
            Session(UserRole.Cashier),
            EntityId.New(),
            CancellationToken.None));
    }

    [Fact]
    public async Task AuditorListsPurchasesWithoutCostFields()
    {
        PurchaseService service = CreateService(new PurchaseStore(), allowed: true);

        IReadOnlyList<PurchaseSummary> purchases = await service.SearchAsync(
            Session(UserRole.Auditor),
            "fornecedor",
            CancellationToken.None);

        PurchaseSummary purchase = Assert.Single(purchases);
        Assert.Equal("Fornecedor", purchase.SupplierName);
        Assert.Equal(PurchaseOrderStatus.Draft, purchase.Status);
    }

    [Fact]
    public async Task ReceiptIsBlockedWithoutActiveLicence()
    {
        var store = new PurchaseStore();
        PurchaseService service = CreateService(store, allowed: false);

        StockOperationBlockedException exception = await Assert.ThrowsAsync<StockOperationBlockedException>(
            () => service.ConfirmReceiptAsync(
                Session(UserRole.Manager),
                EntityId.New(),
                ValidReceiptRequest(),
                CancellationToken.None));

        Assert.Equal("LICENSE_REQUIRED", exception.Code);
        Assert.Null(store.LastReceipt);
    }

    [Fact]
    public async Task ReceiptRequiresDocumentAndIdempotencyKey()
    {
        PurchaseService service = CreateService(new PurchaseStore(), allowed: true);
        ConfirmPurchaseReceiptRequest request = ValidReceiptRequest() with
        {
            DocumentNumber = null,
            IdempotencyKey = string.Empty
        };

        await Assert.ThrowsAsync<PurchaseValidationException>(() => service.ConfirmReceiptAsync(
            Session(UserRole.Manager),
            EntityId.New(),
            request,
            CancellationToken.None));
    }

    [Fact]
    public async Task ManagerEditsAndCancelsDraftWithAudit()
    {
        var store = new PurchaseStore();
        PurchaseService service = CreateService(store, allowed: true);
        EntityId purchaseId = EntityId.New();
        var update = new UpdatePurchaseRequest(
            null,
            null,
            "Compra revista",
            [new(
                EntityId.New(),
                EntityId.New(),
                EntityId.New(),
                5,
                12,
                900,
                0,
                null)]);

        await service.UpdateAsync(
            Session(UserRole.Manager),
            purchaseId,
            update,
            CancellationToken.None);
        Assert.Equal("purchase.updated", store.LastAudit?.Action);

        await service.CancelAsync(
            Session(UserRole.Manager),
            purchaseId,
            CancellationToken.None);
        Assert.Equal("purchase.cancelled", store.LastAudit?.Action);
    }

    private static PurchaseService CreateService(IPurchaseStore store, bool allowed)
    {
        var clock = new FixedClock();
        return new PurchaseService(
            store,
            new AuthorizationService(clock),
            new StockPolicy(allowed),
            clock);
    }

    private static CreatePurchaseRequest ValidCreateRequest() => new(
        EntityId.New(),
        null,
        null,
        "Encomenda mensal",
        [new(
            EntityId.New(),
            EntityId.New(),
            10,
            12,
            1_000,
            0,
            null)]);

    private static ConfirmPurchaseReceiptRequest ValidReceiptRequest() => new(
        "FT-2026-001",
        new DateOnly(2026, 7, 28),
        null,
        "receipt:device-1:1",
        [new(
            EntityId.New(),
            5,
            1_000,
            "LOT-001",
            ExpiryDate.ForMonth(2027, 12))]);

    private static LocalSession Session(UserRole role) => new(
        EntityId.New(),
        EntityId.New(),
        role,
        FixedClock.Now,
        FixedClock.Now,
        null);

    private sealed class PurchaseStore : IPurchaseStore
    {
        public AuditEvent? LastAudit { get; private set; }

        public bool LastIncludeCosts { get; private set; }

        public ConfirmPurchaseReceiptCommand? LastReceipt { get; private set; }

        public Task<PurchaseActorContext?> GetContextAsync(
            EntityId actorUserId,
            CancellationToken cancellationToken) => Task.FromResult<PurchaseActorContext?>(
                new(EntityId.New(), EntityId.New(), actorUserId));

        public Task<PurchaseDetails> CreateAsync(
            PurchaseActorContext context,
            EntityId purchaseId,
            IReadOnlyList<EntityId> lineIds,
            EntityId actorUserId,
            CreatePurchaseRequest request,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(Details(purchaseId, includeCosts: true));
        }

        public Task<PurchaseDetails?> GetAsync(
            EntityId pharmacyId,
            EntityId purchaseId,
            bool includeCosts,
            CancellationToken cancellationToken)
        {
            LastIncludeCosts = includeCosts;
            return Task.FromResult<PurchaseDetails?>(Details(purchaseId, includeCosts));
        }

        public Task<IReadOnlyList<PurchaseSummary>> SearchAsync(
            EntityId pharmacyId,
            string query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PurchaseSummary>>(
                [new(
                    EntityId.New(),
                    EntityId.New(),
                    "Fornecedor",
                    PurchaseOrderStatus.Draft,
                    null,
                    null,
                    1)]);

        public Task<PurchaseReceiptDetails> ConfirmReceiptAsync(
            PurchaseActorContext context,
            ConfirmPurchaseReceiptCommand command,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastReceipt = command;
            LastAudit = auditEvent;
            return Task.FromResult(new PurchaseReceiptDetails(
                command.ReceiptId,
                command.PurchaseId,
                command.DocumentNumber,
                FixedClock.Now,
                command.Lines.Sum(line => line.PackageQuantity),
                PurchaseOrderStatus.PartiallyReceived));
        }

        public Task<PurchaseDetails> UpdateAsync(
            PurchaseActorContext context,
            EntityId purchaseId,
            UpdatePurchaseRequest request,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(Details(purchaseId, includeCosts: true));
        }

        public Task CancelAsync(
            PurchaseActorContext context,
            EntityId purchaseId,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.CompletedTask;
        }

        private static PurchaseDetails Details(EntityId purchaseId, bool includeCosts) => new(
            purchaseId,
            EntityId.New(),
            "Fornecedor",
            PurchaseOrderStatus.Draft,
            null,
            null,
            "Encomenda mensal",
            includeCosts ? 10_000 : null,
            [new(
                EntityId.New(),
                EntityId.New(),
                EntityId.New(),
                "Produto",
                "Caixa",
                10,
                0,
                12,
                includeCosts ? 1_000 : null,
                includeCosts ? 0 : null,
                includeCosts ? 10_000 : null)],
            []);
    }

    private sealed class StockPolicy(bool allowed) : IStockOperationPolicy
    {
        public Task<StockOperationPolicyResult> CanConfirmAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StockOperationPolicyResult(allowed, allowed ? null : "LICENSE_REQUIRED"));
    }

    private sealed class FixedClock : IUtcClock
    {
        public static readonly UtcInstant Now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero));

        public UtcInstant GetCurrentInstant() => Now;
    }
}
