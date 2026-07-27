using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategoryRecord>
{
    public void Configure(EntityTypeBuilder<ProductCategoryRecord> builder)
    {
        builder.ToTable("ProductCategories");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Name).HasMaxLength(120).IsRequired();
        builder.Property(record => record.NormalizedName).HasMaxLength(120).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.NormalizedName })
            .IsUnique()
            .HasDatabaseName("IX_ProductCategories_PharmacyId_NormalizedName");
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<ProductRecord>
{
    public void Configure(EntityTypeBuilder<ProductRecord> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Code).HasMaxLength(64).IsRequired();
        builder.Property(record => record.NormalizedCode).HasMaxLength(64).IsRequired();
        builder.Property(record => record.Name).HasMaxLength(200).IsRequired();
        builder.Property(record => record.ActiveIngredient).HasMaxLength(200);
        builder.Property(record => record.Dosage).HasMaxLength(80);
        builder.Property(record => record.PharmaceuticalForm).HasMaxLength(100);
        builder.Property(record => record.Manufacturer).HasMaxLength(160);
        builder.Property(record => record.BaseUnit).HasMaxLength(80).IsRequired();
        builder.Property(record => record.TaxExemptionReason).HasMaxLength(240);
        builder.HasIndex(record => new { record.PharmacyId, record.NormalizedCode })
            .IsUnique()
            .HasDatabaseName("IX_Products_PharmacyId_NormalizedCode");
        builder.HasIndex(record => new { record.PharmacyId, record.GeneratedSequence })
            .IsUnique()
            .HasFilter("GeneratedSequence IS NOT NULL")
            .HasDatabaseName("IX_Products_PharmacyId_GeneratedSequence");
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductCategoryRecord>().WithMany().HasForeignKey(record => record.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductPackageConfiguration : IEntityTypeConfiguration<ProductPackageRecord>
{
    public void Configure(EntityTypeBuilder<ProductPackageRecord> builder)
    {
        builder.ToTable("ProductPackages");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(record => new { record.ProductId, record.Name }).IsUnique();
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcodeRecord>
{
    public void Configure(EntityTypeBuilder<ProductBarcodeRecord> builder)
    {
        builder.ToTable("ProductBarcodes");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Value).HasMaxLength(96).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.Value })
            .IsUnique()
            .HasDatabaseName("IX_ProductBarcodes_PharmacyId_Value");
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductPackageRecord>().WithMany().HasForeignKey(record => record.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
