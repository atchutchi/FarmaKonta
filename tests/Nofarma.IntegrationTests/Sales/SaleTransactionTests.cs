using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Identity;
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
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
