using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventRecord>
{
    public void Configure(EntityTypeBuilder<AuditEventRecord> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Action).HasMaxLength(96).IsRequired();
        builder.Property(record => record.ObjectType).HasMaxLength(96).IsRequired();
        builder.Property(record => record.ObjectId).HasMaxLength(64).IsRequired();
        builder.Property(record => record.OccurredAtUtc).IsRequired();
        builder.Property(record => record.DiagnosticCode).HasMaxLength(64);
        builder.Property(record => record.DetailsJson).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.OccurredAtUtc });

        builder.HasOne<PharmacyRecord>()
            .WithMany()
            .HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceRecord>()
            .WithMany()
            .HasForeignKey(record => record.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalUserRecord>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
