using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Licensing;
using Nofarma.Application.Sales;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Sales;

namespace Nofarma.UnitTests.Application.Sales;

public sealed class SaleServiceTests
{
    private static readonly UtcInstant Now = UtcInstant.From(
        new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CompletionRejectsAClosedLicenseAndPreservesItsCode()
    {
        var fixture = new Fixture { LicenseAllowed = false, LicenseCode = "LICENSE_EXPIRED" };

        SaleOperationBlockedException exception = await Assert.ThrowsAsync<SaleOperationBlockedException>(
            () => fixture.Service.CompleteAsync(
                fixture.Session(),
                fixture.Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("LICENSE_EXPIRED", exception.Code);
        Assert.Null(fixture.Store.SavedCompletion);
    }

    [Fact]
    public async Task CompletionRequiresAnOpenShiftOwnedByTheCurrentUserAndDevice()
    {
        var fixture = new Fixture();
        fixture.Store.OpenShift = fixture.OpenShift(userId: EntityId.New());

        SalesValidationException exception = await Assert.ThrowsAsync<SalesValidationException>(
            () => fixture.Service.CompleteAsync(
                fixture.Session(),
                fixture.Request(),
                TestContext.Current.CancellationToken));

        Assert.Contains("utilizador", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Store.SavedCompletion);
    }

    [Fact]
    public async Task CashierCannotApplyLineOrTotalDiscount()
    {
        var fixture = new Fixture();
        CompleteSaleRequest request = fixture.Request(
            lineDiscountXof: 100,
            totalDiscountXof: 50,
            cashXof: 1_850);

        await Assert.ThrowsAsync<AuthorizationException>(() => fixture.Service.CompleteAsync(
            fixture.Session(UserRole.Cashier),
            request,
            TestContext.Current.CancellationToken));

        Assert.Null(fixture.Store.SavedCompletion);
    }

    [Fact]
    public async Task ManagerBecomesTheRecordedDiscountAuthorizer()
    {
        var fixture = new Fixture();

        await fixture.Service.CompleteAsync(
            fixture.Session(UserRole.Manager),
            fixture.Request(lineDiscountXof: 100, totalDiscountXof: 50, cashXof: 1_850),
            TestContext.Current.CancellationToken);

        SaleCompletion completion = Assert.IsType<SaleCompletion>(fixture.Store.SavedCompletion);
        Assert.Equal(fixture.UserId, Assert.Single(completion.Sale.Lines).DiscountAuthorizedByUserId);
        Assert.Equal(fixture.UserId, completion.Sale.TotalDiscountAuthorizedByUserId);
    }

    [Fact]
    public async Task CompletionAllocatesFefoAcrossLotsAndCapturesOperationalEffects()
    {
        var fixture = new Fixture();
        fixture.Store.ProductSnapshot = fixture.Product(
            factor: 2,
            lotQuantities: [3, 5],
            lotCosts: [400, 500]);

        SaleSummary result = await fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(quantityPackages: 3, cashXof: 6_500),
            TestContext.Current.CancellationToken);

        SaleCompletion completion = Assert.IsType<SaleCompletion>(fixture.Store.SavedCompletion);
        Assert.Equal(result.Id, completion.Sale.Id);
        Assert.Equal(6_000, completion.Sale.Total.Amount);
        Assert.Equal(500, completion.Sale.Change.Amount);
        Assert.Collection(
            completion.Allocations,
            first =>
            {
                Assert.Equal(3, first.QuantityBase);
                Assert.Equal(0, first.ResultingLotBalance);
                Assert.Equal(400, first.OriginUnitCostXof);
            },
            second =>
            {
                Assert.Equal(3, second.QuantityBase);
                Assert.Equal(2, second.ResultingLotBalance);
                Assert.Equal(500, second.OriginUnitCostXof);
            });
        Assert.Equal([-3L, -3L], completion.StockMovements.Select(item => item.QuantityBase));
        Assert.Equal(2_700, Assert.Single(completion.Sale.Lines).CapturedCost.Amount);
        Assert.Equal(6_000, Assert.IsType<CashMovement>(completion.CashMovement).Amount.Amount);
        Assert.Equal("sale.completed", completion.Audit.Action);
        Assert.Equal("sale.completed.v1", completion.Outbox.EventType);
        Assert.Equal("Recibo interno não fiscal", completion.Receipt.DocumentLabel);
    }

    [Fact]
    public async Task NonCashSaleDoesNotCreateCashMovement()
    {
        var fixture = new Fixture();

        await fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(cashXof: 0, cardXof: 2_000),
            TestContext.Current.CancellationToken);

        SaleCompletion completion = Assert.IsType<SaleCompletion>(fixture.Store.SavedCompletion);
        Assert.Null(completion.CashMovement);
        Assert.Empty(completion.Shift.Shift.Movements);
    }

    [Fact]
    public async Task DifferentPackagesOfTheSameProductCreateUniqueStockCommands()
    {
        var fixture = new Fixture();
        EntityId secondPackageId = EntityId.New();
        SaleProductSnapshot firstPackage = fixture.Product();
        fixture.Store.ProductSnapshots[fixture.PackageId] = firstPackage;
        fixture.Store.ProductSnapshots[secondPackageId] = firstPackage with
        {
            PackageId = secondPackageId,
            PackageName = "Blister"
        };
        var request = new CompleteSaleRequest(
            [
                new CompleteSaleLineRequest(fixture.ProductId, fixture.PackageId, 1, 0),
                new CompleteSaleLineRequest(fixture.ProductId, secondPackageId, 1, 0)
            ],
            0,
            [new SalePaymentRequest(PaymentMethod.Cash, 4_000, null)],
            "sale:two-packages");

        await fixture.Service.CompleteAsync(
            fixture.Session(),
            request,
            TestContext.Current.CancellationToken);

        SaleCompletion completion = Assert.IsType<SaleCompletion>(fixture.Store.SavedCompletion);
        Assert.Equal(
            2,
            completion.StockMovements.Select(item => item.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public async Task InsufficientStockDoesNotReachPersistence()
    {
        var fixture = new Fixture();
        fixture.Store.ProductSnapshot = fixture.Product(lotQuantities: [1]);

        await Assert.ThrowsAsync<InsufficientStockException>(() => fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(quantityPackages: 2, cashXof: 4_000),
            TestContext.Current.CancellationToken));

        Assert.Null(fixture.Store.SavedCompletion);
    }

    [Fact]
    public async Task MatchingIdempotentCommandReturnsStoredResultBeforeLicenseCheck()
    {
        var fixture = new Fixture { LicenseAllowed = false };
        CompleteSaleRequest request = fixture.Request();
        string fingerprint = SaleCommandFingerprint.ForComplete(
            fixture.Store.Context,
            request);
        SaleSummary expected = Fixture.Summary();
        fixture.Store.CommandResult = new SaleCommandResult(fingerprint, expected);

        SaleSummary actual = await fixture.Service.CompleteAsync(
            fixture.Session(),
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
        Assert.Equal(0, fixture.PolicyCalls);
        Assert.Null(fixture.Store.SavedCompletion);
    }

    [Fact]
    public async Task ReusedKeyWithDifferentRequestRaisesConflict()
    {
        var fixture = new Fixture();
        fixture.Store.CommandResult = new SaleCommandResult(new string('f', 64), Fixture.Summary());

        await Assert.ThrowsAsync<SaleConflictException>(() => fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompletionRejectsBlankIdempotencyKey(string key)
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<SalesValidationException>(() => fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(idempotencyKey: key),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProductSearchReturnsFirstFefoLotAndLocalAvailability()
    {
        var fixture = new Fixture();
        fixture.Store.SearchResults =
        [
            new SaleProductResult(
                fixture.ProductId,
                fixture.PackageId,
                "MED-1",
                "Amoxicilina",
                "Caixa",
                1,
                8,
                2_000,
                "LOTE-A",
                new DateOnly(2026, 9, 1),
                true)
        ];

        IReadOnlyList<SaleProductResult> results = await fixture.Service.SearchProductsAsync(
            fixture.Session(),
            " amox ",
            TestContext.Current.CancellationToken);

        SaleProductResult result = Assert.Single(results);
        Assert.Equal("LOTE-A", result.EarliestLotNumber);
        Assert.Equal(8, result.AvailableQuantityBase);
        Assert.Equal("amox", fixture.Store.LastSearchQuery);
    }

    private sealed class Fixture
    {
        public EntityId PharmacyId { get; } = EntityId.New();
        public EntityId DeviceId { get; } = EntityId.New();
        public EntityId UserId { get; } = EntityId.New();
        public EntityId ProductId { get; } = EntityId.New();
        public EntityId PackageId { get; } = EntityId.New();
        public bool LicenseAllowed { get; init; } = true;
        public string? LicenseCode { get; init; }
        public int PolicyCalls { get; private set; }
        public FakeSaleStore Store { get; }
        public SaleService Service { get; }

        public Fixture()
        {
            Store = new FakeSaleStore
            {
                Context = new SaleActorContext(
                    PharmacyId,
                    "Farmácia Central",
                    "Africa/Bissau",
                    DeviceId,
                    UserId,
                    "Maria Caixa")
            };
            Store.OpenShift = OpenShift();
            Store.ProductSnapshot = Product();
            Store.NextSequence = 1;
            var clock = new FixedClock();
            Service = new SaleService(
                Store,
                new AuthorizationService(clock),
                new LicensePolicy(this),
                clock);
        }

        public LocalSession Session(UserRole role = UserRole.Cashier) => new(
            EntityId.New(),
            UserId,
            role,
            Now,
            Now,
            null);

        public CompleteSaleRequest Request(
            long quantityPackages = 1,
            long lineDiscountXof = 0,
            long totalDiscountXof = 0,
            long cashXof = 2_000,
            long cardXof = 0,
            string idempotencyKey = "sale:test:1")
        {
            var payments = new List<SalePaymentRequest>();
            if (cardXof > 0)
            {
                payments.Add(new SalePaymentRequest(PaymentMethod.Card, cardXof, "POS-1"));
            }
            if (cashXof > 0)
            {
                payments.Add(new SalePaymentRequest(PaymentMethod.Cash, cashXof, null));
            }
            return new CompleteSaleRequest(
                [new CompleteSaleLineRequest(ProductId, PackageId, quantityPackages, lineDiscountXof)],
                totalDiscountXof,
                payments,
                idempotencyKey);
        }

        public StoredCashShift OpenShift(EntityId? userId = null) => new(
            CashShift.Open(
                EntityId.New(),
                PharmacyId,
                DeviceId,
                userId ?? UserId,
                Money.Xof(10_000),
                UtcInstant.From(Now.Value.AddHours(-4))),
            3);

        public SaleProductSnapshot Product(
            long factor = 1,
            long[]? lotQuantities = null,
            long[]? lotCosts = null,
            EntityId? packageId = null)
        {
            long[] quantities = lotQuantities ?? [10];
            long[] costs = lotCosts ?? Enumerable.Repeat(500L, quantities.Length).ToArray();
            var lots = new List<SaleLotSnapshot>();
            for (int index = 0; index < quantities.Length; index++)
            {
                StockLot lot = StockLot.Create(
                    EntityId.New(),
                    ProductId,
                    $"LOTE-{index + 1}",
                    ExpiryDate.ForDay(2026, 9 + index, 1),
                    supplierId: null,
                    Money.Xof(costs[index]),
                    UtcInstant.From(Now.Value.AddDays(-10 + index)));
                lots.Add(new SaleLotSnapshot(lot, quantities[index], index + 1));
            }
            return new SaleProductSnapshot(
                ProductId,
                packageId ?? PackageId,
                "MED-1",
                "Amoxicilina",
                "Caixa",
                factor,
                2_000,
                false,
                lots);
        }

        public static SaleSummary Summary() => new(
            EntityId.New(),
            "V-20260805-000001",
            2_000,
            2_000,
            0,
            Now,
            EntityId.New());

        private sealed class FixedClock : IUtcClock
        {
            public UtcInstant GetCurrentInstant() => Now;
        }

        private sealed class LicensePolicy(Fixture fixture) : ILicensedOperationPolicy
        {
            public Task<LicensedOperationPolicyResult> CanCreateAsync(
                CancellationToken cancellationToken)
            {
                fixture.PolicyCalls++;
                return Task.FromResult(new LicensedOperationPolicyResult(
                    fixture.LicenseAllowed,
                    fixture.LicenseCode));
            }
        }
    }

    private sealed class FakeSaleStore : ISaleStore
    {
        public required SaleActorContext Context { get; init; }
        public StoredCashShift? OpenShift { get; set; }
        public SaleProductSnapshot? ProductSnapshot { get; set; }
        public Dictionary<EntityId, SaleProductSnapshot> ProductSnapshots { get; } = [];
        public SaleCommandResult? CommandResult { get; set; }
        public long NextSequence { get; set; }
        public SaleCompletion? SavedCompletion { get; private set; }
        public IReadOnlyList<SaleProductResult> SearchResults { get; set; } = [];
        public string? LastSearchQuery { get; private set; }

        public Task<SaleActorContext?> GetActorContextAsync(
            EntityId userId,
            CancellationToken cancellationToken) => Task.FromResult<SaleActorContext?>(Context);

        public Task<IReadOnlyList<SaleProductResult>> SearchProductsAsync(
            EntityId pharmacyId,
            string query,
            DateOnly businessDate,
            int limit,
            CancellationToken cancellationToken)
        {
            LastSearchQuery = query;
            return Task.FromResult(SearchResults);
        }

        public Task<StoredCashShift?> GetOpenShiftAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            CancellationToken cancellationToken) => Task.FromResult(OpenShift);

        public Task<SaleProductSnapshot?> GetProductSnapshotAsync(
            EntityId pharmacyId,
            EntityId productId,
            EntityId packageId,
            DateOnly businessDate,
            CancellationToken cancellationToken) => Task.FromResult(
                ProductSnapshots.TryGetValue(packageId, out SaleProductSnapshot? snapshot)
                    ? snapshot
                    : ProductSnapshot);

        public Task<SaleCommandResult?> GetCommandResultAsync(
            EntityId pharmacyId,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult(CommandResult);

        public Task<long> GetNextSaleSequenceAsync(
            EntityId pharmacyId,
            DateOnly businessDate,
            CancellationToken cancellationToken) => Task.FromResult(NextSequence);

        public Task<SaleSummary> CompleteAsync(
            SaleCompletion completion,
            CancellationToken cancellationToken)
        {
            if (completion.StockMovements
                    .Select(item => item.IdempotencyKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != completion.StockMovements.Count)
            {
                throw new InvalidOperationException("Existem comandos de stock repetidos.");
            }
            SavedCompletion = completion;
            return Task.FromResult(new SaleSummary(
                completion.Sale.Id,
                completion.Sale.Number.Value,
                completion.Sale.Total.Amount,
                completion.Sale.Paid.Amount,
                completion.Sale.Change.Amount,
                completion.Sale.CompletedAt,
                completion.Receipt.Id));
        }

        public Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SuspendedSaleSummary>>([]);

        public Task<SuspendedSaleDetails?> GetSuspendedDetailsAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            EntityId suspendedSaleId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SuspendedSaleDetails?>(null);

        public Task<SuspendedSaleSummary> SaveSuspendedAsync(
            SuspendedSale sale,
            AuditEvent auditEvent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteSuspendedAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            EntityId suspendedSaleId,
            AuditEvent auditEvent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReceiptDetails?> GetReceiptAsync(
            EntityId pharmacyId,
            EntityId receiptId,
            CancellationToken cancellationToken) => Task.FromResult<ReceiptDetails?>(null);
    }
}
