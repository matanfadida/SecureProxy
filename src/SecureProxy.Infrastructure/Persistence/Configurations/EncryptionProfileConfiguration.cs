using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence.Configurations;

public class EncryptionProfileConfiguration : IEntityTypeConfiguration<EncryptionProfile>
{
    public void Configure(EntityTypeBuilder<EncryptionProfile> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.Name).IsUnique();

        // Where the key lives (env var / Key Vault secret name). Never the key itself.
        builder.Property(e => e.KeyReference).HasMaxLength(200).IsRequired();
    }
}
