using SecureProxy.Application.Admin;

namespace SecureProxy.Application.Abstractions;

public interface IOperationAdminStore
{
    Task<OperationUpdateResult> UpdateAsync(int operationId, OperationUpdate update, CancellationToken cancellationToken);
}
