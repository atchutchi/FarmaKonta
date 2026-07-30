using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class InstallationConfiguration : IEntityTypeConfiguration<InstallationRecord>
{
    public void Configure(EntityTypeBuilder<InstallationRecord> builder)
    {
        builder.ToTable("Installations");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.SingletonKey).HasMaxLength(16).IsRequired();
        builder.HasIndex(record => record.SingletonKey)
            .IsUnique()
            .HasDatabaseName("IX_Installations_SingletonKey");
        builder.Property(record => record.Status).IsRequired();
        builder.Property(record => record.CreatedAtUtc).IsRequired();

        builder.HasOne(record => record.Pharmacy)
            .WithOne()
            .HasForeignKey<InstallationRecord>(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(record => record.Device)
            .WithOne()
            .HasForeignKey<InstallationRecord>(record => record.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
