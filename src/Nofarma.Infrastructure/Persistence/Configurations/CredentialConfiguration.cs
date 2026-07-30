using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence.Configurations;

public sealed class CredentialConfiguration : IEntityTypeConfiguration<CredentialRecord>
{
    public void Configure(EntityTypeBuilder<CredentialRecord> builder)
    {
        builder.ToTable("CredentialRecords");
        builder.HasKey(record => record.UserId);
        builder.Property(record => record.Algorithm).HasMaxLength(64).IsRequired();
        builder.Property(record => record.Salt).IsRequired();
        builder.Property(record => record.Hash).IsRequired();

        builder.HasOne(record => record.User)
            .WithOne(record => record.Credential)
            .HasForeignKey<CredentialRecord>(record => record.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
