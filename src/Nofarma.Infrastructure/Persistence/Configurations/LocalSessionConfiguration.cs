using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class LocalSessionConfiguration : IEntityTypeConfiguration<LocalSessionRecord>
{
    public void Configure(EntityTypeBuilder<LocalSessionRecord> builder)
    {
        builder.ToTable("LocalSessions");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.CreatedAtUtc).IsRequired();
        builder.Property(record => record.LastActivityAtUtc).IsRequired();
        builder.HasIndex(record => new { record.UserId, record.RevokedAtUtc });

        builder.HasOne<LocalUserRecord>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
