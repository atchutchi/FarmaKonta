using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Inventory;
using Nofarma.Application.Inventory.Import;
using Nofarma.Application.Licensing;
using Nofarma.Application.Purchasing;
using Nofarma.Application.Sales;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Licensing;
using Nofarma.Domain.Sales;
using Nofarma.Infrastructure.Import;
using Nofarma.Infrastructure.Licensing;
using Nofarma.Infrastructure.Persistence;
using Nofarma.IntegrationTests.Inventory;

namespace Nofarma.IntegrationTests.Licensing;

[SupportedOSPlatform("windows")]
public sealed class LicensedOperationServiceTests
{
    private static readonly DateTimeOffset ValidInstant =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiredInstant =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InventoryServiceUsesRealSignedLicenceAndPreservesExpiredRetry()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase database = await StockTestDatabase.CreateAsync();
        SignedRuntime runtime = await CreateRuntimeAsync(database, cancellationToken);
        var service = new InventoryService(
            new SqliteInventoryStore(database.Options),
            new AuthorizationService(runtime.Clock),
            runtime.Policy,
            runtime.Clock);
        LocalSession actor = Session(database.UserId, runtime.Clock);
        var request = new StockEntryRequest(
            database.ProductId,
            5,
            StockMovementType.QuickEntry,
            "LIC-INVENTORY",
            ExpiryDate.ForMonth(2027, 12),
            database.SupplierId,
            50,
            "Entrada licenciada",
            null,
            "licensed-inventory-1");

        await AssertMissingAsync(() => service.ConfirmEntryAsync(
            actor, request, cancellationToken));
        await AssertCountsAsync(database, "stock.quick_entry_confirmed", 0, 0);

        await runtime.ImportAsync(cancellationToken);
        StockConfirmationResult first = await service.ConfirmEntryAsync(
            actor, request, cancellationToken);
        await AssertCountsAsync(database, "stock.quick_entry_confirmed", 1, 1);

        runtime.Clock.Set(ExpiredInstant);
        StockConfirmationResult retry = await service.ConfirmEntryAsync(
            actor, request, cancellationToken);
        Assert.Equal(first, retry);
        await AssertCountsAsync(database, "stock.quick_entry_confirmed", 1, 1);
        await AssertExpiredAsync(() => service.ConfirmEntryAsync(
            actor,
            request with { IdempotencyKey = "licensed-inventory-2" },
            cancellationToken));
        await AssertCountsAsync(database, "stock.quick_entry_confirmed", 1, 1);
    }

    [Fact]
    public async Task InventoryImportServiceUsesRealSignedLicenceAndPreservesExpiredRetry()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase database = await StockTestDatabase.CreateAsync();
        SignedRuntime runtime = await CreateRuntimeAsync(database, cancellationToken);
        var store = new SqliteInventoryImportStore(database.Options);
        var service = new InventoryImportService(
            new InventoryFileReader(),
            store,
            new OpenXmlInventoryImportErrorWriter(),
            new AuthorizationService(runtime.Clock),
            runtime.Policy,
            runtime.Clock);
        LocalSession actor = Session(database.UserId, runtime.Clock);
        EntityId firstImportId = await CreateImportDraftAsync(
            database, store, "licensed-import-a", cancellationToken);

        await AssertMissingAsync(() => service.ConfirmAsync(
            actor, firstImportId, "licensed-import-1", cancellationToken));
        await AssertCountsAsync(database, "stock.opening_inventory_import_confirmed", 0, 0);
        await using (var missingVerification = new NofarmaDbContext(database.Options))
        {
            Assert.Equal(
                (int)InventoryImportStatus.Draft,
                await missingVerification.InventoryImports
                    .Where(record => record.Id == firstImportId.Value)
                    .Select(record => record.Status)
                    .SingleAsync(cancellationToken));
        }

        await runtime.ImportAsync(cancellationToken);
        InventoryImportConfirmationResult first = await service.ConfirmAsync(
            actor, firstImportId, "licensed-import-1", cancellationToken);
        Assert.False(first.AlreadyConfirmed);
        await AssertCountsAsync(database, "stock.opening_inventory_import_confirmed", 1, 1);

        runtime.Clock.Set(ExpiredInstant);
        InventoryImportConfirmationResult retry = await service.ConfirmAsync(
            actor, firstImportId, "licensed-import-1", cancellationToken);
        Assert.Equal(first.ImportId, retry.ImportId);
        Assert.Equal(first.MovementsCreated, retry.MovementsCreated);
        Assert.True(retry.AlreadyConfirmed);
        await AssertCountsAsync(database, "stock.opening_inventory_import_confirmed", 1, 1);

        EntityId secondImportId = await CreateImportDraftAsync(
            database, store, "licensed-import-b", cancellationToken);
        await AssertExpiredAsync(() => service.ConfirmAsync(
            actor, secondImportId, "licensed-import-2", cancellationToken));
        await AssertCountsAsync(database, "stock.opening_inventory_import_confirmed", 1, 1);
    }

    [Fact]
    public async Task PurchaseServiceUsesRealSignedLicenceAndPreservesExpiredRetry()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase database = await StockTestDatabase.CreateAsync();
        SignedRuntime runtime = await CreateRuntimeAsync(database, cancellationToken);
        var service = new PurchaseService(
            new SqlitePurchaseStore(database.Options),
            new AuthorizationService(runtime.Clock),
            runtime.Policy,
            runtime.Clock);
        LocalSession actor = Session(database.UserId, runtime.Clock);
        PurchaseDetails purchase = await service.CreateAsync(
            actor,
            PurchaseDraft(database),
            cancellationToken);
        ConfirmPurchaseReceiptRequest request = ReceiptRequest(
            Assert.Single(purchase.Lines).Id,
            "licensed-receipt-1");

        await AssertMissingAsync(() => service.ConfirmReceiptAsync(
            actor, purchase.Id, request, cancellationToken));
        await AssertCountsAsync(database, "purchase.receipt_confirmed", 0, 0);
        await AssertReceiptCountAsync(database, 0, cancellationToken);

        await runtime.ImportAsync(cancellationToken);
        PurchaseReceiptDetails first = await service.ConfirmReceiptAsync(
            actor, purchase.Id, request, cancellationToken);
        await AssertCountsAsync(database, "purchase.receipt_confirmed", 1, 1);
        await AssertReceiptCountAsync(database, 1, cancellationToken);

        runtime.Clock.Set(ExpiredInstant);
        PurchaseReceiptDetails retry = await service.ConfirmReceiptAsync(
            actor, purchase.Id, request, cancellationToken);
        Assert.Equal(first, retry);
        await AssertCountsAsync(database, "purchase.receipt_confirmed", 1, 1);
        await AssertReceiptCountAsync(database, 1, cancellationToken);

        PurchaseDetails secondPurchase = await service.CreateAsync(
            actor,
            PurchaseDraft(database),
            cancellationToken);
        await AssertExpiredAsync(() => service.ConfirmReceiptAsync(
            actor,
            secondPurchase.Id,
            ReceiptRequest(Assert.Single(secondPurchase.Lines).Id, "licensed-receipt-2"),
            cancellationToken));
        await AssertCountsAsync(database, "purchase.receipt_confirmed", 1, 1);
        await AssertReceiptCountAsync(database, 1, cancellationToken);
    }

    [Fact]
    public async Task CashShiftServiceUsesRealSignedLicencePreservesRetryAndAllowsSafeClose()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase database = await StockTestDatabase.CreateAsync();
        SignedRuntime runtime = await CreateRuntimeAsync(database, cancellationToken);
        var service = new CashShiftService(
            new SqliteCashShiftStore(database.Options),
            new AuthorizationService(runtime.Clock),
            runtime.Policy,
            runtime.Clock);
        LocalSession actor = Session(database.UserId, runtime.Clock);
        var request = new OpenCashShiftRequest(10_000, "licensed-cash-1");

        CashShiftOperationBlockedException missing =
            await Assert.ThrowsAsync<CashShiftOperationBlockedException>(
            () => service.OpenAsync(actor, request, cancellationToken));
        Assert.Equal("LICENSE_MISSING", missing.Code);
        await AssertCountsAsync(database, "cash_shift.opened", 0, 0);
        await AssertCashPersistenceAsync(database, 0, 0, cancellationToken);

        await runtime.ImportAsync(cancellationToken);
        CashShiftSummary first = await service.OpenAsync(actor, request, cancellationToken);
        await AssertCountsAsync(database, "cash_shift.opened", 1, 0);
        await AssertCashPersistenceAsync(database, 1, 1, cancellationToken);

        runtime.Clock.Set(ExpiredInstant);
        CashShiftSummary retry = await service.OpenAsync(actor, request, cancellationToken);
        Assert.Equal(first, retry);
        await AssertCountsAsync(database, "cash_shift.opened", 1, 0);
        await AssertCashPersistenceAsync(database, 1, 1, cancellationToken);
        CashShiftOperationBlockedException blocked = await Assert.ThrowsAsync<CashShiftOperationBlockedException>(
            () => service.RecordManualMovementAsync(
                actor,
                new ManualCashMovementRequest(
                    CashMovementType.ManualEntry,
                    500,
                    "Fundo adicional",
                    "licensed-cash-2"),
                cancellationToken));
        Assert.Equal("LICENSE_EXPIRED_READ_ONLY", blocked.Code);
        await AssertCashPersistenceAsync(database, 1, 1, cancellationToken);

        CashShiftSummary closed = await service.CloseAsync(
            actor,
            new CloseCashShiftRequest(10_000, "licensed-cash-close"),
            cancellationToken);
        Assert.Equal(CashShiftStatus.Closed, closed.Status);
        await AssertCountsAsync(database, "cash_shift.opened", 1, 0);
        await using var verification = new NofarmaDbContext(database.Options);
        Assert.Single(await verification.AuditEvents
            .Where(record => record.Action == "cash_shift.closed")
            .ToArrayAsync(cancellationToken));
        Assert.Equal(2, await verification.CashCommands.CountAsync(cancellationToken));
    }

    private static async Task<EntityId> CreateImportDraftAsync(
        StockTestDatabase database,
        SqliteInventoryImportStore store,
        string fileName,
        CancellationToken cancellationToken)
    {
        var context = new InventoryImportStoreContext(database.PharmacyId, database.DeviceId);
        var row = new InventoryImportDraftRow(
            EntityId.New(),
            2,
            new InventoryImportNormalizedRow(
                "PRD-000001",
                null,
                "Produto",
                null,
                null,
                null,
                null,
                "Geral",
                ProductType.General,
                "Unidade",
                "Unidade",
                1,
                50,
                100,
                10,
                3,
                fileName.ToUpperInvariant(),
                new DateOnly(2027, 12, 31),
                null,
                null,
                false),
            InventoryImportMatchType.InternalCode,
            database.ProductId,
            InventoryImportRowStatus.Valid,
            []);
        InventoryImportDraft draft = await store.SaveDraftAsync(
            context,
            database.UserId,
            $"{fileName}.xlsx",
            new string(fileName.EndsWith('a') ? 'A' : 'B', 64),
            null,
            [row],
            ValidInstant,
            cancellationToken);
        return draft.Id;
    }

    private static CreatePurchaseRequest PurchaseDraft(StockTestDatabase database) => new(
        database.SupplierId,
        null,
        null,
        "Compra licenciada",
        [new(
            database.ProductId,
            database.PackageId,
            10,
            12,
            1_000,
            0,
            null)]);

    private static ConfirmPurchaseReceiptRequest ReceiptRequest(
        EntityId lineId,
        string key) => new(
        "FT-LIC-001",
        new DateOnly(2026, 8, 10),
        "Recepção licenciada",
        key,
        [new(
            lineId,
            4,
            1_000,
            "LIC-RECEIPT",
            ExpiryDate.ForMonth(2027, 12))]);

    private static LocalSession Session(EntityId userId, IUtcClock clock)
    {
        UtcInstant now = clock.GetCurrentInstant();
        return new LocalSession(
            EntityId.New(),
            userId,
            UserRole.Administrator,
            now,
            now,
            null);
    }

    private static async Task AssertMissingAsync(Func<Task> operation)
    {
        StockOperationBlockedException error =
            await Assert.ThrowsAsync<StockOperationBlockedException>(operation);
        Assert.Equal("LICENSE_MISSING", error.Code);
    }

    private static async Task AssertExpiredAsync(Func<Task> operation)
    {
        StockOperationBlockedException error =
            await Assert.ThrowsAsync<StockOperationBlockedException>(operation);
        Assert.Equal("LICENSE_EXPIRED_READ_ONLY", error.Code);
    }

    private static async Task AssertCountsAsync(
        StockTestDatabase database,
        string auditAction,
        int expectedAudits,
        int expectedMovements)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var verification = new NofarmaDbContext(database.Options);
        Assert.Equal(
            expectedAudits,
            await verification.AuditEvents.CountAsync(
                record => record.Action == auditAction,
                cancellationToken));
        Assert.Equal(
            expectedMovements,
            await verification.StockMovements.CountAsync(cancellationToken));
    }

    private static async Task AssertReceiptCountAsync(
        StockTestDatabase database,
        int expected,
        CancellationToken cancellationToken)
    {
        await using var verification = new NofarmaDbContext(database.Options);
        Assert.Equal(
            expected,
            await verification.GoodsReceipts.CountAsync(cancellationToken));
    }

    private static async Task AssertCashPersistenceAsync(
        StockTestDatabase database,
        int expectedShifts,
        int expectedCommands,
        CancellationToken cancellationToken)
    {
        await using var verification = new NofarmaDbContext(database.Options);
        Assert.Equal(expectedShifts, await verification.CashShifts.CountAsync(cancellationToken));
        Assert.Equal(expectedCommands, await verification.CashCommands.CountAsync(cancellationToken));
    }

    private static async Task<SignedRuntime> CreateRuntimeAsync(
        StockTestDatabase database,
        CancellationToken cancellationToken)
    {
        var clock = new MutableClock(ValidInstant);
        string secretsDirectory = Path.Combine(
            Path.GetDirectoryName(database.DatabasePath)!,
            "signed-operation-secrets");
        var identityStore = new WindowsDeviceLicenseIdentityStore(
            secretsDirectory,
            channel: LicenseBuildChannel.Qa);
        var context = new LicenseContext(
            database.PharmacyId,
            database.PharmacyId,
            database.DeviceId);
        DeviceLicenseIdentity identity = identityStore.GetOrCreate(
            context.PharmacyId,
            context.DeviceId);
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var registry = new TrustedLicenseKeyRegistry(
            LicenseBuildChannel.Qa,
            new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                ["qa-ephemeral"] = signingKey.ExportSubjectPublicKeyInfo()
            });
        var service = new LicenseService(
            new SqliteLicenseStore(database.Options),
            new SqliteLicenseContextStore(database.Options),
            new EcdsaLicenseDocumentVerifier(registry),
            identityStore,
            new WindowsLicenseClockCheckpoint(
                secretsDirectory,
                channel: LicenseBuildChannel.Qa),
            clock);
        byte[] document = CreateSignedDocument(
            signingKey,
            context,
            identity.PublicKeyThumbprint);
        return new SignedRuntime(clock, service, new LicenseOperationPolicy(service), document);
    }

    private static byte[] CreateSignedDocument(
        ECDsa signingKey,
        LicenseContext context,
        string thumbprint)
    {
        var unsigned = new SignedLicenseEnvelope(
            EcdsaLicenseDocumentVerifier.CurrentSchemaVersion,
            LicenseBuildChannel.Qa,
            "qa-ephemeral",
            EcdsaLicenseDocumentVerifier.EcdsaP256Sha256P1363Algorithm,
            Guid.NewGuid(),
            context.PharmacyId.Value,
            context.EstablishmentId.Value,
            context.DeviceId.Value,
            thumbprint,
            LicensePlan.Monthly,
            1,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 7, 23, 59, 59, TimeSpan.Zero),
            Array.Empty<int>(),
            ReadOnlyMemory<byte>.Empty);
        byte[] signature = signingKey.SignData(
            CanonicalLicenseJson.SerializePayload(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return CanonicalLicenseJson.SerializeEnvelope(unsigned with { Signature = signature });
    }

    private sealed record SignedRuntime(
        MutableClock Clock,
        LicenseService Service,
        LicenseOperationPolicy Policy,
        byte[] Document)
    {
        public Task<LicenseStatus> ImportAsync(CancellationToken cancellationToken) =>
            Service.ImportAsync(new LicenseImportRequest(Document), cancellationToken);
    }

    private sealed class MutableClock(DateTimeOffset initial) : IUtcClock
    {
        private DateTimeOffset _current = initial;

        public UtcInstant GetCurrentInstant() => UtcInstant.From(_current);

        public void Set(DateTimeOffset value) => _current = value;
    }
}
