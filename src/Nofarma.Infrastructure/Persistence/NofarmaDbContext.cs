using Microsoft.EntityFrameworkCore;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class NofarmaDbContext(DbContextOptions<NofarmaDbContext> options)
    : DbContext(options)
{
    public DbSet<InstallationRecord> Installations => Set<InstallationRecord>();

    public DbSet<PharmacyRecord> Pharmacies => Set<PharmacyRecord>();

    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();

    public DbSet<LocalUserRecord> LocalUsers => Set<LocalUserRecord>();

    public DbSet<CredentialRecord> CredentialRecords => Set<CredentialRecord>();

    public DbSet<RecoveryCodeRecord> RecoveryCodes => Set<RecoveryCodeRecord>();

    public DbSet<LocalSessionRecord> LocalSessions => Set<LocalSessionRecord>();

    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();

    public DbSet<CashShiftRecord> CashShifts => Set<CashShiftRecord>();

    public DbSet<CashMovementRecord> CashMovements => Set<CashMovementRecord>();

    public DbSet<CashCommandRecord> CashCommands => Set<CashCommandRecord>();

    public DbSet<OutboxEventRecord> OutboxEvents => Set<OutboxEventRecord>();

    public DbSet<ProductCategoryRecord> ProductCategories => Set<ProductCategoryRecord>();

    public DbSet<ProductRecord> Products => Set<ProductRecord>();

    public DbSet<ProductPackageRecord> ProductPackages => Set<ProductPackageRecord>();

    public DbSet<ProductBarcodeRecord> ProductBarcodes => Set<ProductBarcodeRecord>();

    public DbSet<SupplierRecord> Suppliers => Set<SupplierRecord>();

    public DbSet<PurchaseOrderRecord> PurchaseOrders => Set<PurchaseOrderRecord>();

    public DbSet<PurchaseOrderLineRecord> PurchaseOrderLines => Set<PurchaseOrderLineRecord>();

    public DbSet<GoodsReceiptRecord> GoodsReceipts => Set<GoodsReceiptRecord>();

    public DbSet<GoodsReceiptLineRecord> GoodsReceiptLines => Set<GoodsReceiptLineRecord>();

    public DbSet<StockLotRecord> StockLots => Set<StockLotRecord>();

    public DbSet<StockMovementRecord> StockMovements => Set<StockMovementRecord>();

    public DbSet<InventoryImportRecord> InventoryImports => Set<InventoryImportRecord>();

    public DbSet<InventoryImportRowRecord> InventoryImportRows => Set<InventoryImportRowRecord>();

    public DbSet<InventoryImportErrorRecord> InventoryImportErrors => Set<InventoryImportErrorRecord>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAppendOnlyRecords();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAppendOnlyRecords();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NofarmaDbContext).Assembly);
    }

    private void EnsureAppendOnlyRecords()
    {
        bool hasAuditMutation = ChangeTracker
            .Entries<AuditEventRecord>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (hasAuditMutation)
        {
            throw new InvalidOperationException("Audit events are append-only.");
        }

        bool hasStockMovementMutation = ChangeTracker
            .Entries<StockMovementRecord>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (hasStockMovementMutation)
        {
            throw new InvalidOperationException("Stock movements are append-only.");
        }

        bool hasCashMovementMutation = ChangeTracker
            .Entries<CashMovementRecord>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (hasCashMovementMutation)
        {
            throw new InvalidOperationException("Cash movements are append-only.");
        }

        bool hasCashCommandMutation = ChangeTracker
            .Entries<CashCommandRecord>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (hasCashCommandMutation)
        {
            throw new InvalidOperationException("Cash commands are append-only.");
        }
    }
}
