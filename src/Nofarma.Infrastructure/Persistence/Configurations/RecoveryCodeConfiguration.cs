using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCodeRecord>
{
    public void Configure(EntityTypeBuilder<RecoveryCodeRecord> builder)
    {
        builder.ToTable("RecoveryCodes");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Algorithm).HasMaxLength(64).IsRequired();
        builder.Property(record => record.Salt).IsRequired();
        builder.Property(record => record.Hash).IsRequired();
        builder.Property(record => record.CreatedAtUtc).IsRequired();
        builder.HasIndex(record => record.UserId)
            .IsUnique()
            .HasFilter("\"UsedAtUtc\" IS NULL")
            .HasDatabaseName("IX_RecoveryCodes_OneActivePerUser");

        builder.HasOne<LocalUserRecord>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
