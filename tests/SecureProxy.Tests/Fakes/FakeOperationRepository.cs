using SecureProxy.Application.Abstractions;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Tests.Fakes;

// Operations live in memory instead of SQL Server.
public class FakeOperationRepository : IOperationRepository
{
    private readonly Dictionary<string, Operation> _operations = new();

    public void Add(Operation operation) => _operations[operation.Key] = operation;

    public Task<Operation?> GetByOperationKeyAsync(string operationKey, CancellationToken cancellationToken) =>
        Task.FromResult(_operations.GetValueOrDefault(operationKey));
}
