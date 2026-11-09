using SecureProxy.Domain.Entities;

namespace SecureProxy.Application.Abstractions;

public interface IOperationRepository
{
    // Returns the operation with its Destination, Parameters and EncryptionProfile, or null.
    Task<Operation?> GetByOperationKeyAsync(string operationKey, CancellationToken cancellationToken);
}
