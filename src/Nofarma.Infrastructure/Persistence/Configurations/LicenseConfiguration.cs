using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class LicenseConfiguration : IEntityTypeConfiguration<LicenseRecord>
{
    public void Configure(EntityTypeBuilder<LicenseRecord> builder)
    {
        builder.ToTable(
            "Licenses",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Licenses_DocumentBytes_Length",
                    "length(\"DocumentBytes\") <= 65536");
                table.HasCheckConstraint(
                    "CK_Licenses_DocumentHash_Length",
                    "length(\"DocumentHash\") = 32");
                table.HasCheckConstraint(
                    "CK_Licenses_Sequence_Positive",
                    "\"Sequence\" >= 1");
            });
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Plan).IsRequired();
        builder.Property(record => record.Sequence).IsRequired();
        builder.Property(record => record.Channel).HasMaxLength(16).IsRequired();
        builder.Property(record => record.KeyId).HasMaxLength(128).IsRequired();
        builder.Property(record => record.DocumentBytes).IsRequired();
        builder.Property(record => record.DocumentHash).IsRequired();
        builder.Property(record => record.IssuedAtUtc).IsRequired();
        builder.Property(record => record.ValidFromUtc).IsRequired();
        builder.Property(record => record.ValidUntilUtc).IsRequired();
        builder.Property(record => record.GraceUntilUtc).IsRequired();
        builder.Property(record => record.ImportedAtUtc).IsRequired();
        builder.HasIndex(record => record.InstallationId)
            .IsUnique()
            .HasDatabaseName("IX_Licenses_InstallationId");
        builder.HasIndex(record => new { record.LicenseId, record.Sequence })
            .IsUnique()
            .HasDatabaseName("IX_Licenses_LicenseId_Sequence");
        builder.HasOne(record => record.Installation)
            .WithOne()
            .HasForeignKey<LicenseRecord>(record => record.InstallationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
