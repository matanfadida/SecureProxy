using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence.Configurations;

public class ParameterRuleConfiguration : IEntityTypeConfiguration<ParameterRule>
{
    public void Configure(EntityTypeBuilder<ParameterRule> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();

        // The same parameter cannot be defined twice for one operation.
        builder.HasIndex(p => new { p.OperationId, p.Name }).IsUnique();

        builder.Property(p => p.Pattern).HasMaxLength(500);

        // Stored as text ("Path", "Integer") so the table is readable.
        builder.Property(p => p.DataType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Location).HasConversion<string>().HasMaxLength(20);
    }
}
