using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Idempotency;
using Nofarma.Application.Inventory;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class StockTransactionTests
{
    [Fact]
    public async Task RepeatedIdempotencyKeyReturnsOriginalWithoutDuplication()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation confirmation = fixture.Entry("entry-once", 10);
        AuditEvent audit = fixture.Audit(confirmation.Operation.MovementId);

        StockConfirmationResult first = await store.ConfirmAsync(
            context,
            confirmation,
            audit,
            cancellationToken);
        InventoryConfirmation repeatedConfirmation = confirmation with
        {
            Operation = confirmation.Operation with { MovementId = EntityId.New() }
        };
        StockConfirmationResult repeated = await store.ConfirmAsync(
            context,
            repeatedConfirmation,
            fixture.Audit(repeatedConfirmation.Operation.MovementId),
            cancellationToken);

        Assert.Equal(first, repeated);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Equal(
            confirmation.RequestFingerprint,
            Assert.Single(await verification.StockMovements.ToArrayAsync(cancellationToken))
                .RequestFingerprint);
        Assert.Equal(
            10,
            await verification.StockLots.Select(record => record.AvailableQuantityBase)
                .SingleAsync(cancellationToken));
        Assert.Single(await verification.AuditEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task ReusedIdempotencyKeyWithDifferentStockIntentIsRejected()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation first = fixture.Entry("entry-conflict", 10);
        await store.ConfirmAsync(context, first, fixture.Audit(first.Operation.MovementId), cancellationToken);
        InventoryConfirmation conflicting = first with
        {
            RequestFingerprint = new string('B', 64),
            Operation = first.Operation with
            {
                MovementId = EntityId.New(),
                QuantityBase = 11
            }
        };

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => store.ConfirmAsync(
            context,
            conflicting,
            fixture.Audit(conflicting.Operation.MovementId),
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Single(await verification.StockMovements.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.AuditEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task LegacyMovementWithoutFingerprintConflictsBeforeAndInsideTransaction()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation confirmation = fixture.Entry("entry-legacy", 10);
        await store.ConfirmAsync(
            context,
            confirmation,
            fixture.Audit(confirmation.Operation.MovementId),
            cancellationToken);
        await using (var mutation = new NofarmaDbContext(fixture.Options))
        {
            await mutation.StockMovements.ExecuteUpdateAsync(
                setters => setters.SetProperty(record => record.RequestFingerprint, (string?)null),
                cancellationToken);
        }

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            store.GetIdempotentResultAsync(
                fixture.PharmacyId,
                confirmation.Operation.IdempotencyKey,
                confirmation.RequestFingerprint,
                cancellationToken));
        InventoryConfirmation retry = confirmation with
        {
            Operation = confirmation.Operation with { MovementId = EntityId.New() }
        };
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => store.ConfirmAsync(
            context,
            retry,
            fixture.Audit(retry.Operation.MovementId),
            cancellationToken));
    }

    [Fact]
    public async Task ConcurrentStockIntentsWithSameKeyPersistOnlyOne()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation firstIntent = fixture.Entry("entry-concurrent-conflict", 10);
        InventoryConfirmation secondIntent = fixture.Entry("entry-concurrent-conflict", 11);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<Exception?> first = ConfirmAsync(firstIntent);
        Task<Exception?> second = ConfirmAsync(secondIntent);
        start.SetResult();
        Exception?[] outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, error => error is null);
        Assert.Single(outcomes, error => error is IdempotencyConflictException);
        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Single(await verification.StockMovements.ToArrayAsync(cancellationToken));
        Assert.Single(await verification.AuditEvents.ToArrayAsync(cancellationToken));

        async Task<Exception?> ConfirmAsync(InventoryConfirmation intent)
        {
            await start.Task;
            try
            {
                await store.ConfirmAsync(
                    context,
                    intent,
                    fixture.Audit(intent.Operation.MovementId),
                    cancellationToken);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    [Fact]
    public async Task AuditFailureRollsBackLotMovementAndBalance()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        await using (var connection = new SqliteConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER FailAudit BEFORE INSERT ON AuditEvents
                BEGIN
                    SELECT RAISE(ABORT, 'forced audit failure');
                END;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation confirmation = fixture.Entry("rollback", 10);

        await Assert.ThrowsAnyAsync<Exception>(() => store.ConfirmAsync(
            context,
            confirmation,
            fixture.Audit(confirmation.Operation.MovementId),
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Empty(await verification.StockLots.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.StockMovements.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.AuditEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task CompensationAndStockQueryPreserveCompleteHistory()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation entry = fixture.Entry("entry-to-compensate", 10);
        StockConfirmationResult original = await store.ConfirmAsync(
            context,
            entry,
            fixture.Audit(entry.Operation.MovementId),
            cancellationToken);
        EntityId compensationId = EntityId.New();

        StockConfirmationResult compensation = await store.CompensateAsync(
            context,
            new StockCompensationCommand(
                original.MovementId,
                compensationId,
                fixture.UserId,
                "Entrada no produto errado",
                "compensation-once",
                UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 5, 0, TimeSpan.Zero)),
                new string('C', 64)),
            fixture.Audit(compensationId),
            cancellationToken);
        ProductStockDetails stock = Assert.IsType<ProductStockDetails>(
            await store.GetProductStockAsync(
                fixture.PharmacyId,
                fixture.ProductId,
                new DateOnly(2026, 7, 27),
                cancellationToken));

        Assert.Equal(-10, compensation.QuantityBase);
        Assert.Equal(0, stock.TotalQuantityBase);
        Assert.Equal(StockAlertLevel.OutOfStock, stock.StockAlertLevel);
        Assert.Equal(2, stock.Movements.Count);
        Assert.Equal(0, Assert.Single(stock.Lots).AvailableQuantityBase);
    }

    [Fact]
    public async Task ExpiredLotIsRejectedWithoutPersistingAnything()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation confirmation = fixture.Entry("expired", 10) with
        {
            Lot = new StockLotDefinition(
                EntityId.New(),
                "EXP-01",
                ExpiryDate.ForMonth(2026, 6),
                null,
                50)
        };

        await Assert.ThrowsAsync<InventoryValidationException>(() => store.ConfirmAsync(
            context,
            confirmation,
            fixture.Audit(confirmation.Operation.MovementId),
            cancellationToken));

        await using var verification = new NofarmaDbContext(fixture.Options);
        Assert.Empty(await verification.StockLots.ToArrayAsync(cancellationToken));
        Assert.Empty(await verification.StockMovements.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task FefoAllocationUsesNearestValidExpiryFirst()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using StockTestDatabase fixture = await StockTestDatabase.CreateAsync();
        var store = new SqliteInventoryStore(fixture.Options);
        InventoryActorContext context = Assert.IsType<InventoryActorContext>(
            await store.GetContextAsync(fixture.UserId, cancellationToken));
        InventoryConfirmation later = fixture.Entry("later-lot", 10);
        await store.ConfirmAsync(
            context,
            later,
            fixture.Audit(later.Operation.MovementId),
            cancellationToken);
        EntityId earlierLotId = EntityId.New();
        InventoryConfirmation earlier = fixture.Entry("earlier-lot", 5) with
        {
            Operation = fixture.Entry("unused", 1).Operation with
            {
                MovementId = EntityId.New(),
                LotId = earlierLotId,
                QuantityBase = 5,
                IdempotencyKey = "earlier-lot"
            },
            Lot = new StockLotDefinition(
                earlierLotId,
                "LOT-02",
                ExpiryDate.ForMonth(2027, 6),
                null,
                50)
        };
        await store.ConfirmAsync(
            context,
            earlier,
            fixture.Audit(earlier.Operation.MovementId),
            cancellationToken);

        IReadOnlyList<StockAllocation> allocations = await store.AllocateFefoAsync(
            fixture.PharmacyId,
            fixture.ProductId,
            8,
            new DateOnly(2026, 7, 27),
            cancellationToken);

        Assert.Equal(earlierLotId, allocations[0].LotId);
        Assert.Equal(5, allocations[0].QuantityBase);
        Assert.Equal(3, allocations[1].QuantityBase);
    }
}

internal sealed class StockTestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private StockTestDatabase(
        string directory,
        string connectionString,
        DbContextOptions<NofarmaDbContext> options,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        EntityId productId,
        EntityId packageId,
        EntityId supplierId)
    {
        _directory = directory;
        DatabasePath = Path.Combine(directory, "stock.db");
        ConnectionString = connectionString;
        Options = options;
        PharmacyId = pharmacyId;
        DeviceId = deviceId;
        UserId = userId;
        ProductId = productId;
        PackageId = packageId;
        SupplierId = supplierId;
    }

    public string ConnectionString { get; }

    public string DatabasePath { get; }

    public DbContextOptions<NofarmaDbContext> Options { get; }

    public EntityId PharmacyId { get; }

    public EntityId DeviceId { get; }

    public EntityId UserId { get; }

    public EntityId ProductId { get; }

    public EntityId PackageId { get; }

    public EntityId SupplierId { get; }

    public static async Task<StockTestDatabase> CreateAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nofarma-stock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string connectionString = $"Data Source={Path.Combine(directory, "stock.db")};Default Timeout=15";
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite(connectionString)
            .Options;
        EntityId pharmacyId = EntityId.New();
        EntityId deviceId = EntityId.New();
        EntityId userId = EntityId.New();
        EntityId productId = EntityId.New();
        EntityId packageId = EntityId.New();
        EntityId basePackageId = EntityId.New();
        EntityId supplierId = EntityId.New();
        Guid categoryId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        await using var context = new NofarmaDbContext(options);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        context.Pharmacies.Add(new PharmacyRecord
        {
            Id = pharmacyId.Value,
            Name = "Farmácia de teste",
            TaxIdentifier = $"5{Random.Shared.Next(10000000, 99999999)}",
            Address = "Bissau",
            Contact = string.Empty,
            TimeZoneId = "Africa/Bissau"
        });
        context.Devices.Add(new DeviceRecord
        {
            Id = deviceId.Value,
            PharmacyId = pharmacyId.Value,
            Name = "PC"
        });
        context.LocalUsers.Add(new LocalUserRecord
        {
            Id = userId.Value,
            PharmacyId = pharmacyId.Value,
            DisplayName = "Responsável de stock",
            LoginName = "stock",
            NormalizedLoginName = "STOCK",
            Role = (int)UserRole.StockManager,
            CredentialKind = (int)CredentialKind.Password,
            Status = (int)UserStatus.Active,
            CreatedAtUtc = now
        });
        context.Installations.Add(new InstallationRecord
        {
            Id = Guid.NewGuid(),
            PharmacyId = pharmacyId.Value,
            DeviceId = deviceId.Value,
            PrimaryAdministratorId = userId.Value,
            Status = (int)InstallationStatus.Active,
            CreatedAtUtc = now
        });
        context.ProductCategories.Add(new ProductCategoryRecord
        {
            Id = categoryId,
            PharmacyId = pharmacyId.Value,
            Name = "Geral",
            NormalizedName = "GERAL",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        context.Products.Add(new ProductRecord
        {
            Id = productId.Value,
            PharmacyId = pharmacyId.Value,
            CategoryId = categoryId,
            Code = "PRD-000001",
            NormalizedCode = "PRD-000001",
            Name = "Produto",
            Type = (int)ProductType.General,
            RequiresLot = true,
            RequiresExpiry = true,
            BasePackageId = basePackageId.Value,
            BaseUnit = "Unidade",
            SalePriceXof = 100,
            IndicativePurchasePriceXof = 50,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        context.ProductPackages.Add(new ProductPackageRecord
        {
            Id = basePackageId.Value,
            ProductId = productId.Value,
            Name = "Unidade",
            FactorToBaseUnit = 1,
            IsBaseUnit = true,
            IsActive = true
        });
        context.ProductPackages.Add(new ProductPackageRecord
        {
            Id = packageId.Value,
            ProductId = productId.Value,
            Name = "Caixa",
            FactorToBaseUnit = 12,
            IsBaseUnit = false,
            IsActive = true
        });
        context.Suppliers.Add(new SupplierRecord
        {
            Id = supplierId.Value,
            PharmacyId = pharmacyId.Value,
            Name = "Fornecedor nacional",
            NormalizedName = "FORNECEDOR NACIONAL",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new StockTestDatabase(
            directory,
            connectionString,
            options,
            pharmacyId,
            deviceId,
            userId,
            productId,
            packageId,
            supplierId);
    }

    public InventoryConfirmation Entry(string idempotencyKey, long quantity)
    {
        EntityId lotId = EntityId.New();
        return new InventoryConfirmation(
            new StockOperation(
                EntityId.New(),
                PharmacyId,
                ProductId,
                lotId,
                quantity,
                StockMovementType.QuickEntry,
                "Entrada",
                null,
                UserId,
                UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)),
                idempotencyKey),
            new StockLotDefinition(
                lotId,
                "LOT-01",
                ExpiryDate.ForMonth(2027, 12),
                null,
                50),
            TestFingerprint(quantity));
    }

    public AuditEvent Audit(EntityId movementId) => new(
        EntityId.New(),
        PharmacyId,
        DeviceId,
        UserId,
        "stock.quick_entry_confirmed",
        "StockMovement",
        movementId.Value.ToString("D"),
        UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)),
        AuditOutcome.Success,
        null,
        "{}");

    private static string TestFingerprint(long value) =>
        new("0123456789ABCDEF"[(int)(Math.Abs(value) % 16)], 64);

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
