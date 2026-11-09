using Microsoft.EntityFrameworkCore;
using SecureProxy.Application.Abstractions;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence.Repositories;

public class OperationRepository(ProxyDbContext db) : IOperationRepository
{
    public Task<Operation?> GetByOperationKeyAsync(string operationKey, CancellationToken cancellationToken) =>
        db.Operations
            .AsNoTracking() // read-only: nothing here is updated
            .Include(o => o.Destination)
            .Include(o => o.Parameters)
            .Include(o => o.EncryptionProfile)
            .FirstOrDefaultAsync(o => o.Key == operationKey, cancellationToken);
}
