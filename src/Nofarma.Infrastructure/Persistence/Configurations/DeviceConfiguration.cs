using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<DeviceRecord>
{
    public void Configure(EntityTypeBuilder<DeviceRecord> builder)
    {
        builder.ToTable("Devices");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(record => record.PharmacyId).IsUnique();
    }
}
