using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence.Configurations;

public class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        // The caller looks operations up by this key, so it must be unique.
        builder.Property(o => o.Key).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => o.Key).IsUnique();

        builder.Property(o => o.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(o => o.PathTemplate).HasMaxLength(500).IsRequired();

        // SQL Server rowversion: changes on every update, used for optimistic concurrency (409).
        builder.Property(o => o.RowVersion).IsRowVersion();

        // A destination that still has operations cannot be deleted.
        builder.HasOne(o => o.Destination)
            .WithMany(d => d.Operations)
            .HasForeignKey(o => o.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.EncryptionProfile)
            .WithMany()
            .HasForeignKey(o => o.EncryptionProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting an operation deletes its parameter rules.
        builder.HasMany(o => o.Parameters)
            .WithOne(p => p.Operation)
            .HasForeignKey(p => p.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
