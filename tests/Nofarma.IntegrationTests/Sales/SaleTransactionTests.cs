using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Licensing;
using Nofarma.Application.Sales;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Sales;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.IntegrationTests.Sales;

public sealed class SaleTransactionTests
{
    [Fact]
    public async Task MigrationUpgradesExistingInventoryWithoutCreatingSales()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"nofarma-sales-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string connectionString = LocalDatabasePath.BuildConnectionString(
            Path.Combine(directory, "upgrade.db"));
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite(connectionString)
            .Options;
        Guid pharmacyId = Guid.NewGuid();
        Guid deviceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid packageId = Guid.NewGuid();
        Guid lotId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
        try
        {
            await using (var previous = new NofarmaDbContext(options))
            {
                IMigrator migrator = previous.Database.GetService<IMigrator>();
                await migrator.MigrateAsync(
                    "20260729103917_AddOperationRequestFingerprints",
                    cancellationToken);
                previous.Pharmacies.Add(new PharmacyRecord
                {
                    Id = pharmacyId,
                    Name = "Farmácia existente",
                    TaxIdentifier = "500000001",
                    Address = "Bissau",
                    Contact = string.Empty,
                    TimeZoneId = "Africa/Bissau"
                });
                previous.Devices.Add(new DeviceRecord
                {
                    Id = deviceId,
                    PharmacyId = pharmacyId,
                    Name = "Caixa existente"
                });
                previous.LocalUsers.Add(new LocalUserRecord
                {
                    Id = userId,
                    PharmacyId = pharmacyId,
                    DisplayName = "Administrador existente",
                    LoginName = "admin-vendas",
                    NormalizedLoginName = "ADMIN-VENDAS",
                    Role = (int)UserRole.Administrator,
                    CredentialKind = (int)CredentialKind.Password,
                    Status = (int)UserStatus.Active,
                    CreatedAtUtc = now
                });
                previous.Installations.Add(new InstallationRecord
                {
                    Id = Guid.NewGuid(),
                    PharmacyId = pharmacyId,
                    DeviceId = deviceId,
                    PrimaryAdministratorId = userId,
                    Status = (int)InstallationStatus.Active,
                    CreatedAtUtc = now
                });
                previous.ProductCategories.Add(new ProductCategoryRecord
                {
                    Id = categoryId,
                    PharmacyId = pharmacyId,
                    Name = "Medicamentos",
                    NormalizedName = "MEDICAMENTOS",
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                previous.Products.Add(new ProductRecord
                {
                    Id = productId,
                    PharmacyId = pharmacyId,
                    CategoryId = categoryId,
                    Code = "MED-1",
                    NormalizedCode = "MED-1",
                    Name = "Amoxicilina",
                    Type = (int)ProductType.Medicine,
                    RequiresLot = true,
                    RequiresExpiry = true,
                    BasePackageId = packageId,
                    BaseUnit = "Comprimido",
                    SalePriceXof = 500,
                    IndicativePurchasePriceXof = 300,
                    IsActive = true,
                    HasMovements = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                previous.ProductPackages.Add(new ProductPackageRecord
                {
                    Id = packageId,
                    ProductId = productId,
                    Name = "Comprimido",
                    FactorToBaseUnit = 1,
                    IsBaseUnit = true,
                    IsActive = true
                });
                previous.StockLots.Add(new StockLotRecord
                {
                    Id = lotId,
                    PharmacyId = pharmacyId,
                    ProductId = productId,
                    Number = "LOTE-1",
                    NormalizedNumber = "LOTE-1",
                    ExpiryYear = 2027,
                    ExpiryMonth = 1,
                    ExpiryDay = 31,
                    OriginCostXof = 300,
                    QuantityReceivedBase = 20,
                    AvailableQuantityBase = 12,
                    FirstEntryAtUtc = now,
                    RowVersion = 4
                });
                await previous.SaveChangesAsync(cancellationToken);
            }

            await using (var upgraded = new NofarmaDbContext(options))
            {
                await upgraded.Database.MigrateAsync(cancellationToken);

                Assert.Equal("Amoxicilina", await upgraded.Products
                    .Where(record => record.Id == productId)
                    .Select(record => record.Name)
                    .SingleAsync(cancellationToken));
                StockLotRecord lot = await upgraded.StockLots
                    .SingleAsync(record => record.Id == lotId, cancellationToken);
                Assert.Equal(12, lot.AvailableQuantityBase);
                Assert.Equal(4, lot.RowVersion);
                Assert.Equal(0, await upgraded.Sales.CountAsync(cancellationToken));
                Assert.Equal(0, await upgraded.SaleLines.CountAsync(cancellationToken));
                Assert.Equal(0, await upgraded.SalePayments.CountAsync(cancellationToken));
                Assert.Equal(0, await upgraded.SaleStockAllocations.CountAsync(cancellationToken));
                Assert.Equal(0, await upgraded.Receipts.CountAsync(cancellationToken));
                Assert.Equal(0, await upgraded.SaleCommands.CountAsync(cancellationToken));
                Assert.Equal(0, await upgraded.SuspendedSales.CountAsync(cancellationToken));
                Assert.Equal(0, await upgraded.SuspendedSaleLines.CountAsync(cancellationToken));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            await DeleteDirectoryWithRetryAsync(directory);
        }
    }

    [Fact]
    public async Task CompleteSalePersistsEveryOperationalEffectOnce()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SalesDatabase fixture = await SalesDatabase.CreateAsync();

        SaleSummary result = await fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(),
            cancellationToken);

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(1, await verification.Sales.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.SaleLines.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.SalePayments.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.SaleStockAllocations.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.StockMovements.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.CashMovements.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.Receipts.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.SaleCommands.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.AuditEvents.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.OutboxEvents.CountAsync(cancellationToken));
        Assert.Equal(
            result.Id.Value,
            await verification.Sales.Select(record => record.Id).SingleAsync(cancellationToken));
        long[] balances = await verification.StockLots
            .OrderBy(record => record.ExpiryMonth)
            .Select(record => record.AvailableQuantityBase)
            .ToArrayAsync(cancellationToken);
        Assert.Equal(
            [0L, 2L],
            balances);
        CashShiftRecord shift = await verification.CashShifts.SingleAsync(cancellationToken);
        Assert.Equal(13_000, shift.ExpectedCashXof);
        Assert.Equal(2, shift.RowVersion);
        CashMovementRecord movement = await verification.CashMovements.SingleAsync(cancellationToken);
        Assert.Equal(3_000, movement.AmountXof);
        Assert.Equal(result.Id.Value, movement.SourceSaleId);
        var store = new SqliteSaleStore(fixture.Options);
        ReceiptDetails receipt = Assert.IsType<ReceiptDetails>(await store.GetReceiptAsync(
            fixture.PharmacyId,
            result.ReceiptId,
            cancellationToken));
        Assert.Equal("Recibo interno não fiscal", receipt.DocumentLabel);
        Assert.Equal("Farmácia Venda", receipt.PharmacyName);
        Assert.Equal("Maria Caixa", receipt.OperatorName);
        Assert.Null(await store.GetReceiptAsync(
            EntityId.New(),
            result.ReceiptId,
            cancellationToken));
    }

    [Fact]
    public async Task RepeatingSameCommandReturnsSameSaleWithoutDuplicateEffects()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SalesDatabase fixture = await SalesDatabase.CreateAsync();
        CompleteSaleRequest request = fixture.Request();

        SaleSummary first = await fixture.Service.CompleteAsync(
            fixture.Session(),
            request,
            cancellationToken);
        SaleSummary second = await fixture.Service.CompleteAsync(
            fixture.Session(),
            request,
            cancellationToken);

        Assert.Equal(first, second);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(1, await verification.Sales.CountAsync(cancellationToken));
        Assert.Equal(2, await verification.StockMovements.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.CashMovements.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.SaleCommands.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.AuditEvents.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.OutboxEvents.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ReusingCommandKeyWithDifferentCartRaisesConflict()
    {
        await using SalesDatabase fixture = await SalesDatabase.CreateAsync();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(),
            cancellationToken);
        var different = new CompleteSaleRequest(
            [new CompleteSaleLineRequest(fixture.ProductId, fixture.PackageId, 1, 0)],
            0,
            [new SalePaymentRequest(PaymentMethod.Cash, 1_000, null)],
            "sale:integration:1");

        await Assert.ThrowsAsync<SaleConflictException>(() => fixture.Service.CompleteAsync(
            fixture.Session(),
            different,
            cancellationToken));
    }

    [Fact]
    public async Task CompletedSaleHistoryIsAppendOnly()
    {
        await using SalesDatabase fixture = await SalesDatabase.CreateAsync();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Service.CompleteAsync(
            fixture.Session(),
            fixture.Request(),
            cancellationToken);
        await using var mutation = new NofarmaDbContext(fixture.Options);
        SaleRecord sale = await mutation.Sales.SingleAsync(cancellationToken);
        sale.TotalXof++;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mutation.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task ConcurrentLotChangeRollsBackEverySaleEffect()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using SalesDatabase fixture = await SalesDatabase.CreateAsync();
        var conflictingStore = new ConflictingSaleStore(
            new SqliteSaleStore(fixture.Options),
            fixture.Options,
            fixture.SecondLotId);
        SaleService service = SalesDatabase.CreateService(conflictingStore);

        SaleConcurrencyException exception = await Assert.ThrowsAsync<SaleConcurrencyException>(
            () => service.CompleteAsync(
                fixture.Session(),
                fixture.Request(),
                cancellationToken));

        Assert.Equal(SaleConcurrencyReason.Stock, exception.Reason);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(0, await verification.Sales.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.SaleLines.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.SalePayments.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.SaleStockAllocations.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.StockMovements.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.CashMovements.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.Receipts.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.SaleCommands.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.AuditEvents.CountAsync(cancellationToken));
        Assert.Equal(0, await verification.OutboxEvents.CountAsync(cancellationToken));
        Assert.Equal(1, await verification.StockLots
            .Where(record => record.Id == fixture.FirstLotId.Value)
            .Select(record => record.AvailableQuantityBase)
            .SingleAsync(cancellationToken));
        Assert.Equal(3, await verification.StockLots
            .Where(record => record.Id == fixture.SecondLotId.Value)
            .Select(record => record.AvailableQuantityBase)
            .SingleAsync(cancellationToken));
        Assert.Equal(10_000, await verification.CashShifts
            .Select(record => record.ExpectedCashXof)
            .SingleAsync(cancellationToken));
    }

    private sealed class SalesDatabase(
        string directory,
        DbContextOptions<NofarmaDbContext> options,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        EntityId productId,
        EntityId packageId,
        EntityId firstLotId,
        EntityId secondLotId)
        : IAsyncDisposable
    {
        private static readonly UtcInstant Now = UtcInstant.From(
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

        public DbContextOptions<NofarmaDbContext> Options { get; } = options;
        public EntityId PharmacyId { get; } = pharmacyId;
        public EntityId DeviceId { get; } = deviceId;
        public EntityId UserId { get; } = userId;
        public EntityId ProductId { get; } = productId;
        public EntityId PackageId { get; } = packageId;
        public EntityId FirstLotId { get; } = firstLotId;
        public EntityId SecondLotId { get; } = secondLotId;
        public SaleService Service { get; private set; } = null!;

        public static async Task<SalesDatabase> CreateAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"nofarma-sales-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string connectionString = LocalDatabasePath.BuildConnectionString(
                Path.Combine(directory, "sales.db"));
            var options = new DbContextOptionsBuilder<NofarmaDbContext>()
                .UseSqlite(connectionString)
                .Options;
            EntityId pharmacyId = EntityId.New();
            EntityId deviceId = EntityId.New();
            EntityId userId = EntityId.New();
            EntityId productId = EntityId.New();
            EntityId packageId = EntityId.New();
            EntityId firstLotId = EntityId.New();
            EntityId secondLotId = EntityId.New();
            Guid categoryId = Guid.NewGuid();
            await using var db = new NofarmaDbContext(options);
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            db.Pharmacies.Add(new PharmacyRecord
            {
                Id = pharmacyId.Value,
                Name = "Farmácia Venda",
                TaxIdentifier = "500000002",
                Address = "Bissau",
                Contact = string.Empty,
                TimeZoneId = "Africa/Bissau"
            });
            db.Devices.Add(new DeviceRecord
            {
                Id = deviceId.Value,
                PharmacyId = pharmacyId.Value,
                Name = "Caixa 1"
            });
            db.LocalUsers.Add(new LocalUserRecord
            {
                Id = userId.Value,
                PharmacyId = pharmacyId.Value,
                DisplayName = "Maria Caixa",
                LoginName = "maria-caixa",
                NormalizedLoginName = "MARIA-CAIXA",
                Role = (int)UserRole.Cashier,
                CredentialKind = (int)CredentialKind.Pin,
                Status = (int)UserStatus.Active,
                CreatedAtUtc = Now.Value
            });
            db.Installations.Add(new InstallationRecord
            {
                Id = Guid.NewGuid(),
                PharmacyId = pharmacyId.Value,
                DeviceId = deviceId.Value,
                PrimaryAdministratorId = userId.Value,
                Status = (int)InstallationStatus.Active,
                CreatedAtUtc = Now.Value
            });
            db.ProductCategories.Add(new ProductCategoryRecord
            {
                Id = categoryId,
                PharmacyId = pharmacyId.Value,
                Name = "Medicamentos",
                NormalizedName = "MEDICAMENTOS",
                IsActive = true,
                CreatedAtUtc = Now.Value,
                UpdatedAtUtc = Now.Value
            });
            db.Products.Add(new ProductRecord
            {
                Id = productId.Value,
                PharmacyId = pharmacyId.Value,
                CategoryId = categoryId,
                Code = "MED-1",
                NormalizedCode = "MED-1",
                Name = "Amoxicilina",
                Type = (int)ProductType.Medicine,
                RequiresLot = true,
                RequiresExpiry = true,
                BasePackageId = packageId.Value,
                BaseUnit = "Comprimido",
                SalePriceXof = 1_000,
                IndicativePurchasePriceXof = 500,
                IsActive = true,
                HasMovements = true,
                CreatedAtUtc = Now.Value,
                UpdatedAtUtc = Now.Value
            });
            db.ProductPackages.Add(new ProductPackageRecord
            {
                Id = packageId.Value,
                ProductId = productId.Value,
                Name = "Comprimido",
                FactorToBaseUnit = 1,
                IsBaseUnit = true,
                IsActive = true
            });
            db.StockLots.AddRange(
                Lot(firstLotId, pharmacyId, productId, "LOTE-1", 9, 1, 2, 400),
                Lot(secondLotId, pharmacyId, productId, "LOTE-2", 10, 4, 3, 500));
            db.CashShifts.Add(new CashShiftRecord
            {
                Id = Guid.NewGuid(),
                PharmacyId = pharmacyId.Value,
                DeviceId = deviceId.Value,
                UserId = userId.Value,
                Status = (int)CashShiftStatus.Open,
                OpeningCashXof = 10_000,
                ExpectedCashXof = 10_000,
                OpenedAtUtc = Now.Value.AddHours(-4),
                RowVersion = 1
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var fixture = new SalesDatabase(
                directory,
                options,
                pharmacyId,
                deviceId,
                userId,
                productId,
                packageId,
                firstLotId,
                secondLotId);
            fixture.Service = CreateService(new SqliteSaleStore(options));
            return fixture;
        }

        public static SaleService CreateService(ISaleStore store)
        {
            var clock = new FixedClock();
            return new SaleService(
                store,
                new AuthorizationService(clock),
                new AllowedPolicy(),
                clock);
        }

        public LocalSession Session() => new(
            EntityId.New(),
            UserId,
            UserRole.Cashier,
            Now,
            Now,
            null);

        public CompleteSaleRequest Request() => new(
            [new CompleteSaleLineRequest(ProductId, PackageId, 3, 0)],
            0,
            [new SalePaymentRequest(PaymentMethod.Cash, 3_500, null)],
            "sale:integration:1");

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            await DeleteDirectoryWithRetryAsync(directory);
        }

        private static StockLotRecord Lot(
            EntityId id,
            EntityId pharmacyId,
            EntityId productId,
            string number,
            int expiryMonth,
            long available,
            long version,
            long cost) => new()
            {
                Id = id.Value,
                PharmacyId = pharmacyId.Value,
                ProductId = productId.Value,
                Number = number,
                NormalizedNumber = number,
                ExpiryYear = 2026,
                ExpiryMonth = expiryMonth,
                ExpiryDay = 30,
                OriginCostXof = cost,
                QuantityReceivedBase = available,
                AvailableQuantityBase = available,
                FirstEntryAtUtc = Now.Value.AddDays(-10 + expiryMonth),
                RowVersion = version
            };

        private sealed class FixedClock : IUtcClock
        {
            public UtcInstant GetCurrentInstant() => Now;
        }

        private sealed class AllowedPolicy : ILicensedOperationPolicy
        {
            public Task<LicensedOperationPolicyResult> CanCreateAsync(
                CancellationToken cancellationToken) => Task.FromResult(
                new LicensedOperationPolicyResult(true, null));
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    private sealed class ConflictingSaleStore(
        ISaleStore inner,
        DbContextOptions<NofarmaDbContext> options,
        EntityId lotId) : ISaleStore
    {
        public Task<SaleActorContext?> GetActorContextAsync(EntityId userId, CancellationToken token) =>
            inner.GetActorContextAsync(userId, token);

        public Task<IReadOnlyList<SaleProductResult>> SearchProductsAsync(
            EntityId pharmacyId,
            string query,
            DateOnly date,
            int limit,
            CancellationToken token) => inner.SearchProductsAsync(pharmacyId, query, date, limit, token);

        public Task<StoredCashShift?> GetOpenShiftAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            CancellationToken token) => inner.GetOpenShiftAsync(pharmacyId, deviceId, token);

        public Task<SaleProductSnapshot?> GetProductSnapshotAsync(
            EntityId pharmacyId,
            EntityId productId,
            EntityId packageId,
            DateOnly date,
            CancellationToken token) => inner.GetProductSnapshotAsync(
                pharmacyId, productId, packageId, date, token);

        public Task<SaleCommandResult?> GetCommandResultAsync(
            EntityId pharmacyId,
            string key,
            CancellationToken token) => inner.GetCommandResultAsync(pharmacyId, key, token);

        public Task<long> GetNextSaleSequenceAsync(
            EntityId pharmacyId,
            DateOnly date,
            CancellationToken token) => inner.GetNextSaleSequenceAsync(pharmacyId, date, token);

        public async Task<SaleSummary> CompleteAsync(
            SaleCompletion completion,
            CancellationToken token)
        {
            await using (var conflict = new NofarmaDbContext(options))
            {
                StockLotRecord lot = await conflict.StockLots.SingleAsync(
                    record => record.Id == lotId.Value,
                    token);
                lot.AvailableQuantityBase--;
                lot.RowVersion++;
                await conflict.SaveChangesAsync(token);
            }
            return await inner.CompleteAsync(completion, token);
        }

        public Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            CancellationToken token) => inner.GetSuspendedAsync(pharmacyId, deviceId, token);

        public Task<SuspendedSaleDetails?> GetSuspendedDetailsAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            EntityId suspendedSaleId,
            CancellationToken token) => inner.GetSuspendedDetailsAsync(
                pharmacyId, deviceId, suspendedSaleId, token);

        public Task<SuspendedSaleSummary> SaveSuspendedAsync(
            SuspendedSale sale,
            Nofarma.Domain.Auditing.AuditEvent audit,
            CancellationToken token) => inner.SaveSuspendedAsync(sale, audit, token);

        public Task<bool> DeleteSuspendedAsync(
            EntityId pharmacyId,
            EntityId deviceId,
            EntityId suspendedSaleId,
            Nofarma.Domain.Auditing.AuditEvent audit,
            CancellationToken token) => inner.DeleteSuspendedAsync(
                pharmacyId, deviceId, suspendedSaleId, audit, token);

        public Task<ReceiptDetails?> GetReceiptAsync(
            EntityId pharmacyId,
            EntityId receiptId,
            CancellationToken token) => inner.GetReceiptAsync(pharmacyId, receiptId, token);
    }
}
