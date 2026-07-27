using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovementRecord>
{
    public void Configure(EntityTypeBuilder<StockMovementRecord> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Reason).HasMaxLength(1000);
        builder.Property(record => record.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("IX_StockMovements_PharmacyId_IdempotencyKey");
        builder.HasIndex(record => new { record.ProductId, record.OccurredAtUtc });
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockLotRecord>().WithMany().HasForeignKey(record => record.StockLotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany().HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovementRecord>().WithMany()
            .HasForeignKey(record => record.CompensatesMovementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryImportConfiguration : IEntityTypeConfiguration<InventoryImportRecord>
{
    public void Configure(EntityTypeBuilder<InventoryImportRecord> builder)
    {
        builder.ToTable("InventoryImports");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.FileName).HasMaxLength(260).IsRequired();
        builder.Property(record => record.SheetName).HasMaxLength(160);
        builder.Property(record => record.ConfirmationIdempotencyKey).HasMaxLength(160);
        builder.HasIndex(record => new { record.PharmacyId, record.FileHash });
        builder.HasIndex(record => new { record.PharmacyId, record.ConfirmationIdempotencyKey })
            .IsUnique()
            .HasFilter("ConfirmationIdempotencyKey IS NOT NULL");
        builder.HasOne<PharmacyRecord>().WithMany().HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>().WithMany().HasForeignKey(record => record.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryImportRowConfiguration : IEntityTypeConfiguration<InventoryImportRowRecord>
{
    public void Configure(EntityTypeBuilder<InventoryImportRowRecord> builder)
    {
        builder.ToTable("InventoryImportRows");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.DataJson).IsRequired();
        builder.HasIndex(record => new { record.InventoryImportId, record.RowNumber }).IsUnique();
        builder.HasOne<InventoryImportRecord>().WithMany().HasForeignKey(record => record.InventoryImportId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(record => record.MatchedProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryImportErrorConfiguration : IEntityTypeConfiguration<InventoryImportErrorRecord>
{
    public void Configure(EntityTypeBuilder<InventoryImportErrorRecord> builder)
    {
        builder.ToTable("InventoryImportErrors");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Field).HasMaxLength(120);
        builder.Property(record => record.ReceivedValue).HasMaxLength(1000);
        builder.Property(record => record.Code).HasMaxLength(80).IsRequired();
        builder.Property(record => record.Message).HasMaxLength(1000).IsRequired();
        builder.HasIndex(record => new { record.InventoryImportId, record.RowNumber });
        builder.HasOne<InventoryImportRecord>().WithMany().HasForeignKey(record => record.InventoryImportId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryImportRowRecord>().WithMany().HasForeignKey(record => record.InventoryImportRowId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
