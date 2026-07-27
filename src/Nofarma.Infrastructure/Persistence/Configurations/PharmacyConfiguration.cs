using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class PharmacyConfiguration : IEntityTypeConfiguration<PharmacyRecord>
{
    public void Configure(EntityTypeBuilder<PharmacyRecord> builder)
    {
        builder.ToTable("Pharmacies");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Name).HasMaxLength(160).IsRequired();
        builder.Property(record => record.TaxIdentifier).HasMaxLength(32).IsRequired();
        builder.HasIndex(record => record.TaxIdentifier).IsUnique();
        builder.Property(record => record.Address).HasMaxLength(320).IsRequired();
        builder.Property(record => record.Contact).HasMaxLength(160);
        builder.Property(record => record.TimeZoneId).HasMaxLength(64).IsRequired();
    }
}
