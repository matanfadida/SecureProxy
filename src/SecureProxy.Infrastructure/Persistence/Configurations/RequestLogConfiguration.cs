using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence.Configurations;

public class RequestLogConfiguration : IEntityTypeConfiguration<RequestLog>
{
    public void Configure(EntityTypeBuilder<RequestLog> builder)
    {
        builder.Property(r => r.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.OperationKey).HasMaxLength(100);
        builder.Property(r => r.Reason).HasMaxLength(200);
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(30);

        // Typical lookups: "what happened to this correlation ID" and "what happened in this time window".
        builder.HasIndex(r => r.CorrelationId);
        builder.HasIndex(r => r.StartedAt);
    }
}
