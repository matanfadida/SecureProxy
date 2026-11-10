using SecureProxy.Application.Abstractions;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Tests.Fakes;

// Keeps audit rows in a list so tests can check them (test 17).
public class FakeAuditor : IRequestAuditor
{
    public List<RequestLog> Entries { get; } = new();

    public Task WriteAsync(RequestLog entry)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
