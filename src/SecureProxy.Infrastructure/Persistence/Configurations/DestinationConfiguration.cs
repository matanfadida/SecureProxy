using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence.Configurations;

public class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(d => d.Name).IsUnique();

        builder.Property(d => d.BaseUrl).HasMaxLength(500).IsRequired();
    }
}
