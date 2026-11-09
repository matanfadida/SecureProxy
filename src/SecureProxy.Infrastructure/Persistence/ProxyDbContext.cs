using Microsoft.EntityFrameworkCore;
using SecureProxy.Domain.Common;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence;

public class ProxyDbContext(DbContextOptions<ProxyDbContext> options) : DbContext(options)
{
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<EncryptionProfile> EncryptionProfiles => Set<EncryptionProfile>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<ParameterRule> ParameterRules => Set<ParameterRule>();

    public override int SaveChanges()
    {
        AddTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AddTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void AddTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var auditableEntity in ChangeTracker.Entries<IAuditable>())
        {
            if (auditableEntity.State == EntityState.Added)
            {
                auditableEntity.Entity.CreatedAt = now;
                auditableEntity.Entity.UpdatedAt = now;
            }

            if (auditableEntity.State == EntityState.Modified)
            {
                auditableEntity.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                auditableEntity.Entity.UpdatedAt = now;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration class in this project (the Configurations folder).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProxyDbContext).Assembly);
    }
}
