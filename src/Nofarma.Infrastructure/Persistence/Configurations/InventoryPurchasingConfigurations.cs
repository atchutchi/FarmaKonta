using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrderRecord>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderRecord> builder)
    {
        builder.ToTable("PurchaseOrders");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.DocumentNumber).HasMaxLength(120);
        builder.Property(record => record.Notes).HasMaxLength(2000);
        builder.HasIndex(record => new { record.PharmacyId, record.Status });
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupplierRecord>().WithMany().HasForeignKey(record => record.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany().HasForeignKey(record => record.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLineRecord>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLineRecord> builder)
    {
        builder.ToTable("PurchaseOrderLines");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Notes).HasMaxLength(1000);
        builder.HasIndex(record => new { record.PurchaseOrderId, record.ProductId });
        builder.HasOne<PurchaseOrderRecord>().WithMany().HasForeignKey(record => record.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductPackageRecord>().WithMany().HasForeignKey(record => record.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceiptRecord>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptRecord> builder)
    {
        builder.ToTable("GoodsReceipts");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.DocumentNumber).HasMaxLength(120);
        builder.Property(record => record.Notes).HasMaxLength(2000);
        builder.Property(record => record.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.IdempotencyKey }).IsUnique();
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseOrderRecord>().WithMany().HasForeignKey(record => record.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupplierRecord>().WithMany().HasForeignKey(record => record.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany().HasForeignKey(record => record.ReceivedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLineRecord>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLineRecord> builder)
    {
        builder.ToTable("GoodsReceiptLines");
        builder.HasKey(record => record.Id);
        builder.HasOne<GoodsReceiptRecord>().WithMany().HasForeignKey(record => record.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseOrderLineRecord>().WithMany().HasForeignKey(record => record.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductPackageRecord>().WithMany().HasForeignKey(record => record.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockLotRecord>().WithMany().HasForeignKey(record => record.StockLotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
