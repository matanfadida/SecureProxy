using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SecureProxy.Application.Abstractions;
using SecureProxy.Infrastructure.Encryption;
using SecureProxy.Infrastructure.Http;
using SecureProxy.Infrastructure.Persistence;
using SecureProxy.Infrastructure.Persistence.Repositories;

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

        services.AddScoped<IOperationRepository, OperationRepository>();
        services.AddScoped<IPayloadDecryptor, PayloadDecryptor>();
        services.AddSingleton<IEncryptionKeyProvider, ConfigurationKeyProvider>();
        services.AddHttpClient<IUpstreamForwarder, HttpUpstreamForwarder>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        return services;
    }
}
