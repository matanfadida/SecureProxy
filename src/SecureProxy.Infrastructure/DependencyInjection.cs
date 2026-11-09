using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureProxy.Infrastructure.Persistence;

namespace SecureProxy.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers everything Infrastructure provides. Called once from Program.cs.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProxyDbContext>(options =>
        {
            // Read when the DbContext is first used, not at startup,
            var connectionString = configuration.GetConnectionString("SqlServer");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'SqlServer' is missing. Set it with User Secrets (see README).");
            }

            options.UseSqlServer(connectionString);
        });

        return services;
    }
}
