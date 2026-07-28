using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Purchasing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Purchasing;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class PurchaseReceiptTests
{
    [Fact]
    public async Task PartialAndFinalReceiptsUpdatePurchaseAndStockAtomically()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqlitePurchaseStore(fixture.Options);
        PurchaseActorContext context = Assert.IsType<PurchaseActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        PurchaseDetails purchase = await CreatePurchaseAsync(store, fixture, context);
        EntityId orderLineId = Assert.Single(purchase.Lines).Id;
        ConfirmPurchaseReceiptCommand partialCommand = ReceiptCommand(
            fixture,
            purchase.Id,
            orderLineId,
            packageQuantity: 4,
            "receipt:partial");

        PurchaseReceiptDetails partial = await store.ConfirmReceiptAsync(
            context,
            partialCommand,
            Audit(fixture, partialCommand.ReceiptId, "purchase.receipt_confirmed", "GoodsReceipt"),
            cancellationToken);
        ConfirmPurchaseReceiptCommand finalCommand = ReceiptCommand(
            fixture,
            purchase.Id,
            orderLineId,
            packageQuantity: 6,
            "receipt:final");
        PurchaseReceiptDetails final = await store.ConfirmReceiptAsync(
            context,
            finalCommand,
            Audit(fixture, finalCommand.ReceiptId, "purchase.receipt_confirmed", "GoodsReceipt"),
            cancellationToken);
        PurchaseDetails history = Assert.IsType<PurchaseDetails>(
            await store.GetAsync(
                fixture.PharmacyId,
                purchase.Id,
                includeCosts: true,
                cancellationToken));

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, partial.PurchaseStatus);
        Assert.Equal(PurchaseOrderStatus.Received, final.PurchaseStatus);
        Assert.Equal(2, history.Receipts.Count);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(2, await verification.GoodsReceipts.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.StockMovements.CountAsync(cancellationToken));
        Assert.Equal(
            120,
            await verification.StockLots.Select(record => record.AvailableQuantityBase)
                .SingleAsync(cancellationToken));
        Assert.Equal(
            10,
            await verification.PurchaseOrderLines
                .Select(record => record.ReceivedPackageQuantity)
                .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task RepeatedReceiptKeyReturnsOriginalWithoutDuplicatingStock()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqlitePurchaseStore(fixture.Options);
        PurchaseActorContext context = Assert.IsType<PurchaseActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        PurchaseDetails purchase = await CreatePurchaseAsync(store, fixture, context);
        EntityId orderLineId = Assert.Single(purchase.Lines).Id;
        ConfirmPurchaseReceiptCommand command = ReceiptCommand(
            fixture,
            purchase.Id,
            orderLineId,
            4,
            "receipt:once");
        PurchaseReceiptDetails first = await store.ConfirmReceiptAsync(
            context,
            command,
            Audit(fixture, command.ReceiptId, "purchase.receipt_confirmed", "GoodsReceipt"),
            cancellationToken);
        ConfirmPurchaseReceiptCommand retry = ReceiptCommand(
            fixture,
            purchase.Id,
            orderLineId,
            4,
            "receipt:once");

        PurchaseReceiptDetails repeated = await store.ConfirmReceiptAsync(
            context,
            retry,
            Audit(fixture, retry.ReceiptId, "purchase.receipt_confirmed", "GoodsReceipt"),
            cancellationToken);

        Assert.Equal(first, repeated);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Single(await verification.GoodsReceipts.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.StockMovements.ToArrayAsync(cancellationToken));
        Assert.Equal(
            48,
            await verification.StockLots.Select(record => record.AvailableQuantityBase)
                .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task AuditFailureRollsBackReceiptMovementBalanceAndPurchaseState()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqlitePurchaseStore(fixture.Options);
        PurchaseActorContext context = Assert.IsType<PurchaseActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        PurchaseDetails purchase = await CreatePurchaseAsync(store, fixture, context);
        await using (var connection = new SqliteConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand trigger = connection.CreateCommand();
            trigger.CommandText = """
                CREATE TRIGGER FailReceiptAudit BEFORE INSERT ON AuditEvents
                WHEN NEW.Action = 'purchase.receipt_confirmed'
                BEGIN
                    SELECT RAISE(ABORT, 'forced receipt audit failure');
                END;
                """;
            await trigger.ExecuteNonQueryAsync(cancellationToken);
        }

        EntityId orderLineId = Assert.Single(purchase.Lines).Id;
        ConfirmPurchaseReceiptCommand command = ReceiptCommand(
            fixture,
            purchase.Id,
            orderLineId,
            4,
            "receipt:rollback");

        await Assert.ThrowsAnyAsync<Exception>(() => store.ConfirmReceiptAsync(
            context,
            command,
            Audit(fixture, command.ReceiptId, "purchase.receipt_confirmed", "GoodsReceipt"),
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Empty(await verification.GoodsReceipts.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.StockMovements.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.StockLots.ToArrayAsync(cancellationToken));
        Assert.Equal(
            0,
            await verification.PurchaseOrderLines
                .Select(record => record.ReceivedPackageQuantity)
                .SingleAsync(cancellationToken));
        Assert.Equal(
            (int)PurchaseOrderStatus.Draft,
            await verification.PurchaseOrders.Select(record => record.Status)
                .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task MissingRequiredLotIsRejected()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqlitePurchaseStore(fixture.Options);
        PurchaseActorContext context = Assert.IsType<PurchaseActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        PurchaseDetails purchase = await CreatePurchaseAsync(store, fixture, context);
        EntityId lineId = Assert.Single(purchase.Lines).Id;
        ConfirmPurchaseReceiptCommand command = ReceiptCommand(
            fixture,
            purchase.Id,
            lineId,
            1,
            "receipt:no-lot") with
        {
            Lines =
            [
                ReceiptCommand(fixture, purchase.Id, lineId, 1, "unused")
                    .Lines.Single() with { LotNumber = null }
            ]
        };

        await Assert.ThrowsAsync<PurchaseValidationException>(() => store.ConfirmReceiptAsync(
            context,
            command,
            Audit(fixture, command.ReceiptId, "purchase.receipt_confirmed", "GoodsReceipt"),
            cancellationToken));
    }

    [Fact]
    public async Task ExpiredLotCannotBeReceived()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqlitePurchaseStore(fixture.Options);
        PurchaseActorContext context = Assert.IsType<PurchaseActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        PurchaseDetails purchase = await CreatePurchaseAsync(store, fixture, context);
        EntityId lineId = Assert.Single(purchase.Lines).Id;
        ConfirmPurchaseReceiptCommand command = ReceiptCommand(
            fixture,
            purchase.Id,
            lineId,
            1,
            "receipt:expired");
        command = command with
        {
            Lines = [command.Lines.Single() with { Expiry = ExpiryDate.ForMonth(2026, 6) }]
        };

        await Assert.ThrowsAsync<PurchaseValidationException>(() => store.ConfirmReceiptAsync(
            context,
            command,
            Audit(fixture, command.ReceiptId, "purchase.receipt_confirmed", "GoodsReceipt"),
            cancellationToken));
    }

    [Fact]
    public async Task ReceiptRejectsActorDifferentFromAuthenticatedContext()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqlitePurchaseStore(fixture.Options);
        PurchaseActorContext context = Assert.IsType<PurchaseActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        PurchaseDetails purchase = await CreatePurchaseAsync(store, fixture, context);
        EntityId lineId = Assert.Single(purchase.Lines).Id;
        EntityId forgedUserId = EntityId.New();
        ConfirmPurchaseReceiptCommand command = ReceiptCommand(
            fixture,
            purchase.Id,
            lineId,
            1,
            "receipt:forged") with
        { ActorUserId = forgedUserId };
        AuditEvent forgedAudit = new(
            EntityId.New(),
            fixture.PharmacyId,
            fixture.DeviceId,
            forgedUserId,
            "purchase.receipt_confirmed",
            "GoodsReceipt",
            command.ReceiptId.Value.ToString("D"),
            command.OccurredUtc,
            AuditOutcome.Success,
            null,
            "{}");

        await Assert.ThrowsAsync<PurchaseValidationException>(() => store.ConfirmReceiptAsync(
            context,
            command,
            forgedAudit,
            cancellationToken));
    }

    [Fact]
    public async Task DraftCanBeEditedThenCancelledWithoutCreatingStock()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqlitePurchaseStore(fixture.Options);
        PurchaseActorContext context = Assert.IsType<PurchaseActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        PurchaseDetails purchase = await CreatePurchaseAsync(store, fixture, context);
        PurchaseLineDetails line = Assert.Single(purchase.Lines);
        var update = new UpdatePurchaseRequest(
            "PRÓ-FORMA-01",
            new DateOnly(2026, 7, 28),
            "Quantidade revista",
            [new(
                line.Id,
                line.ProductId,
                line.PackageId,
                5,
                line.FactorToBaseUnit,
                900,
                0,
                null)]);

        PurchaseDetails updated = await store.UpdateAsync(
            context,
            purchase.Id,
            update,
            Audit(fixture, purchase.Id, "purchase.updated", "PurchaseOrder"),
            cancellationToken);
        await store.CancelAsync(
            context,
            purchase.Id,
            Audit(fixture, purchase.Id, "purchase.cancelled", "PurchaseOrder"),
            cancellationToken);
        PurchaseDetails cancelled = Assert.IsType<PurchaseDetails>(
            await store.GetAsync(
                fixture.PharmacyId,
                purchase.Id,
                includeCosts: true,
                cancellationToken));
        IReadOnlyList<PurchaseSummary> found = await store.SearchAsync(
            fixture.PharmacyId,
            "fornecedor",
            cancellationToken);

        Assert.Equal(5, Assert.Single(updated.Lines).OrderedPackageQuantity);
        Assert.Equal(4_500, updated.TotalXof);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(purchase.Id, Assert.Single(found).Id);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Empty(await verification.StockMovements.ToArrayAsync(cancellationToken));
    }

    private static async Task<PurchaseDetails> CreatePurchaseAsync(
        SqlitePurchaseStore store,
        StockTestDatabase fixture,
        PurchaseActorContext context)
    {
        EntityId purchaseId = EntityId.New();
        var request = new CreatePurchaseRequest(
            fixture.SupplierId,
            null,
            null,
            "Compra mensal",
            [new(
                fixture.ProductId,
                fixture.PackageId,
                10,
                12,
                1_000,
                0,
                null)]);
        return await store.CreateAsync(
            context,
            purchaseId,
            [EntityId.New()],
            fixture.UserId,
            request,
            Audit(fixture, purchaseId, "purchase.created", "PurchaseOrder"),
            TestContext.Current.CancellationToken);
    }

    private static ConfirmPurchaseReceiptCommand ReceiptCommand(
        StockTestDatabase fixture,
        EntityId purchaseId,
        EntityId orderLineId,
        long packageQuantity,
        string idempotencyKey) => new(
            EntityId.New(),
            purchaseId,
            fixture.UserId,
            "FT-2026-001",
            new DateOnly(2026, 7, 28),
            null,
            idempotencyKey,
            UtcInstant.From(new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero)),
            [new(
                EntityId.New(),
                orderLineId,
                EntityId.New(),
                EntityId.New(),
                packageQuantity,
                1_000,
                "LOT-001",
                ExpiryDate.ForMonth(2027, 12),
                $"{idempotencyKey}:line:1")]);

    private static AuditEvent Audit(
        StockTestDatabase fixture,
        EntityId objectId,
        string action,
        string objectType) => new(
            EntityId.New(),
            fixture.PharmacyId,
            fixture.DeviceId,
            fixture.UserId,
            action,
            objectType,
            objectId.Value.ToString("D"),
            UtcInstant.From(new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero)),
            AuditOutcome.Success,
            null,
            "{}");
}
