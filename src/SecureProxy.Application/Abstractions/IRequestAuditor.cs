using SecureProxy.Domain.Entities;

namespace SecureProxy.Application.Abstractions;

public interface IRequestAuditor
{
    // Best effort: never throws. If the database write fails, the entry goes to the log instead.
    Task WriteAsync(RequestLog entry);
}
