using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<SupplierRecord>
{
    public void Configure(EntityTypeBuilder<SupplierRecord> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Name).HasMaxLength(200).IsRequired();
        builder.Property(record => record.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(record => record.TaxIdentifier).HasMaxLength(64);
        builder.Property(record => record.Phone).HasMaxLength(64);
        builder.Property(record => record.Email).HasMaxLength(254);
        builder.Property(record => record.Address).HasMaxLength(500);
        builder.Property(record => record.Notes).HasMaxLength(2000);
        builder.HasIndex(record => new { record.PharmacyId, record.NormalizedName });
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockLotConfiguration : IEntityTypeConfiguration<StockLotRecord>
{
    public void Configure(EntityTypeBuilder<StockLotRecord> builder)
    {
        builder.ToTable("StockLots");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Number).HasMaxLength(120).IsRequired();
        builder.Property(record => record.NormalizedNumber).HasMaxLength(120).IsRequired();
        builder.Property(record => record.RowVersion).IsConcurrencyToken();
        builder.HasIndex(record => new { record.ProductId, record.NormalizedNumber })
            .IsUnique()
            .HasDatabaseName("IX_StockLots_ProductId_NormalizedNumber");
        builder.HasIndex(record => new { record.ProductId, record.AvailableQuantityBase });
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupplierRecord>().WithMany().HasForeignKey(record => record.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
