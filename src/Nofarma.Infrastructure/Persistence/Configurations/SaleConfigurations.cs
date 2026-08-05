using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class SaleConfiguration : IEntityTypeConfiguration<SaleRecord>
{
    public void Configure(EntityTypeBuilder<SaleRecord> builder)
    {
        builder.ToTable("Sales");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Number).HasMaxLength(32).IsRequired();
        builder.Property(record => record.BusinessDate).IsRequired();
        builder.Property(record => record.CompletedAtUtc).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.BusinessDate, record.DailySequence })
            .IsUnique();
        builder.HasIndex(record => new { record.PharmacyId, record.Number }).IsUnique();
        builder.HasIndex(record => new { record.PharmacyId, record.CompletedAtUtc });
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceRecord>().WithMany().HasForeignKey(record => record.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany().HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashShiftRecord>().WithMany().HasForeignKey(record => record.CashShiftId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany()
            .HasForeignKey(record => record.TotalDiscountAuthorizedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLineRecord>
{
    public void Configure(EntityTypeBuilder<SaleLineRecord> builder)
    {
        builder.ToTable("SaleLines");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Description).HasMaxLength(240).IsRequired();
        builder.Property(record => record.UnitName).HasMaxLength(80).IsRequired();
        builder.HasIndex(record => new { record.SaleId, record.Sequence }).IsUnique();
        builder.HasOne<SaleRecord>().WithMany().HasForeignKey(record => record.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductPackageRecord>().WithMany().HasForeignKey(record => record.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany()
            .HasForeignKey(record => record.DiscountAuthorizedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalePaymentConfiguration : IEntityTypeConfiguration<SalePaymentRecord>
{
    public void Configure(EntityTypeBuilder<SalePaymentRecord> builder)
    {
        builder.ToTable("SalePayments");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Reference).HasMaxLength(160);
        builder.HasIndex(record => new { record.SaleId, record.Sequence }).IsUnique();
        builder.HasOne<SaleRecord>().WithMany().HasForeignKey(record => record.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SaleStockAllocationConfiguration
    : IEntityTypeConfiguration<SaleStockAllocationRecord>
{
    public void Configure(EntityTypeBuilder<SaleStockAllocationRecord> builder)
    {
        builder.ToTable("SaleStockAllocations");
        builder.HasKey(record => record.Id);
        builder.HasIndex(record => new
        {
            record.SaleId,
            record.SaleLineId,
            record.StockLotId
        }).IsUnique();
        builder.HasIndex(record => record.StockMovementId).IsUnique();
        builder.HasOne<SaleRecord>().WithMany().HasForeignKey(record => record.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleLineRecord>().WithMany().HasForeignKey(record => record.SaleLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockLotRecord>().WithMany().HasForeignKey(record => record.StockLotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovementRecord>().WithMany()
            .HasForeignKey(record => record.StockMovementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SuspendedSaleConfiguration : IEntityTypeConfiguration<SuspendedSaleRecord>
{
    public void Configure(EntityTypeBuilder<SuspendedSaleRecord> builder)
    {
        builder.ToTable("SuspendedSales");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Name).HasMaxLength(120);
        builder.Property(record => record.SuspendedAtUtc).IsRequired();
        builder.Property(record => record.RowVersion).IsConcurrencyToken();
        builder.HasIndex(record => new { record.PharmacyId, record.DeviceId, record.SuspendedAtUtc });
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceRecord>().WithMany().HasForeignKey(record => record.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany().HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SuspendedSaleLineConfiguration
    : IEntityTypeConfiguration<SuspendedSaleLineRecord>
{
    public void Configure(EntityTypeBuilder<SuspendedSaleLineRecord> builder)
    {
        builder.ToTable("SuspendedSaleLines");
        builder.HasKey(record => record.Id);
        builder.HasIndex(record => new { record.SuspendedSaleId, record.Sequence }).IsUnique();
        builder.HasOne<SuspendedSaleRecord>().WithMany()
            .HasForeignKey(record => record.SuspendedSaleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductPackageRecord>().WithMany().HasForeignKey(record => record.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReceiptConfiguration : IEntityTypeConfiguration<ReceiptRecord>
{
    public void Configure(EntityTypeBuilder<ReceiptRecord> builder)
    {
        builder.ToTable("Receipts");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Number).HasMaxLength(32).IsRequired();
        builder.Property(record => record.ContentJson).IsRequired();
        builder.Property(record => record.CreatedAtUtc).IsRequired();
        builder.HasIndex(record => record.SaleId).IsUnique();
        builder.HasIndex(record => new { record.PharmacyId, record.Number }).IsUnique();
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleRecord>().WithMany().HasForeignKey(record => record.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SaleCommandConfiguration : IEntityTypeConfiguration<SaleCommandRecord>
{
    public void Configure(EntityTypeBuilder<SaleCommandRecord> builder)
    {
        builder.ToTable("SaleCommands");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(record => record.RequestFingerprint)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(record => record.ResultJson).IsRequired();
        builder.Property(record => record.CreatedAtUtc).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.IdempotencyKey }).IsUnique();
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleRecord>().WithMany().HasForeignKey(record => record.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
