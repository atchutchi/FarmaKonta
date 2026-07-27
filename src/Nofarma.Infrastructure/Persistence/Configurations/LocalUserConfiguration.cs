using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class LocalUserConfiguration : IEntityTypeConfiguration<LocalUserRecord>
{
    public void Configure(EntityTypeBuilder<LocalUserRecord> builder)
    {
        builder.ToTable("LocalUsers");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(record => record.LoginName).HasMaxLength(80).IsRequired();
        builder.Property(record => record.NormalizedLoginName).HasMaxLength(80).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.NormalizedLoginName })
            .IsUnique()
            .HasDatabaseName("IX_LocalUsers_PharmacyId_NormalizedLoginName");
        builder.Property(record => record.CreatedAtUtc).IsRequired();

        builder.HasOne<PharmacyRecord>()
            .WithMany()
            .HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
