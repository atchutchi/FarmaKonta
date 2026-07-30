using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Domain.Sales;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class CashShiftConfiguration : IEntityTypeConfiguration<CashShiftRecord>
{
    public void Configure(EntityTypeBuilder<CashShiftRecord> builder)
    {
        builder.ToTable("CashShifts");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.OpenedAtUtc).IsRequired();
        builder.Property(record => record.RowVersion).IsConcurrencyToken();
        builder.HasIndex(record => new { record.PharmacyId, record.DeviceId })
            .IsUnique()
            .HasFilter($"Status = {(int)CashShiftStatus.Open}");
        builder.HasIndex(record => new { record.PharmacyId, record.OpenedAtUtc });
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

public sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovementRecord>
{
    public void Configure(EntityTypeBuilder<CashMovementRecord> builder)
    {
        builder.ToTable("CashMovements");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Reason).HasMaxLength(500).IsRequired();
        builder.Property(record => record.OccurredAtUtc).IsRequired();
        builder.HasIndex(record => new { record.CashShiftId, record.Sequence }).IsUnique();
        builder.HasIndex(record => new { record.PharmacyId, record.OccurredAtUtc });
        builder.HasOne<CashShiftRecord>()
            .WithMany()
            .HasForeignKey(record => record.CashShiftId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PharmacyRecord>()
            .WithMany()
            .HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CashCommandConfiguration : IEntityTypeConfiguration<CashCommandRecord>
{
    public void Configure(EntityTypeBuilder<CashCommandRecord> builder)
    {
        builder.ToTable("CashCommands");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(record => record.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ResultJson).IsRequired();
        builder.Property(record => record.CreatedAtUtc).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.IdempotencyKey })
            .IsUnique();
        builder.HasOne<CashShiftRecord>()
            .WithMany()
            .HasForeignKey(record => record.CashShiftId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PharmacyRecord>()
            .WithMany()
            .HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEventRecord>
{
    public void Configure(EntityTypeBuilder<OutboxEventRecord> builder)
    {
        builder.ToTable("OutboxEvents");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.EventType).HasMaxLength(96).IsRequired();
        builder.Property(record => record.PayloadJson).IsRequired();
        builder.Property(record => record.OccurredAtUtc).IsRequired();
        builder.HasIndex(record => new { record.PharmacyId, record.OccurredAtUtc });
        builder.HasOne<PharmacyRecord>()
            .WithMany()
            .HasForeignKey(record => record.PharmacyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceRecord>()
            .WithMany()
            .HasForeignKey(record => record.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
