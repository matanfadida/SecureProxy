using Microsoft.Extensions.Logging;
using SecureProxy.Application.Abstractions;
using SecureProxy.Domain.Entities;

namespace SecureProxy.Infrastructure.Persistence;

// Audit policy: BEST EFFORT. If SQL Server is unavailable, the request still completes and the
// audit entry is written to the structured log instead. Availability of the proxy is preferred over
// a complete audit table (see DESIGN-NOTES).
public class RequestAuditor(ProxyDbContext db, ILogger<RequestAuditor> logger) : IRequestAuditor
{
    public async Task WriteAsync(RequestLog entry)
    {
        try
        {
            db.RequestLogs.Add(entry);

            // CancellationToken.None on purpose: a cancelled request still gets its audit row.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Catching Exception is deliberate here: by policy, an audit failure must never fail the request.
            logger.LogError(ex,
                "Audit write failed. CorrelationId={CorrelationId} Operation={OperationKey} Outcome={Outcome} " +
                "Reason={Reason} ProxyStatus={ProxyStatusCode} UpstreamStatus={UpstreamStatusCode} " +
                "Forwarded={Forwarded} DurationMs={DurationMs}",
                entry.CorrelationId, entry.OperationKey, entry.Outcome, entry.Reason,
                entry.ProxyStatusCode, entry.UpstreamStatusCode, entry.Forwarded, entry.DurationMs);
        }
    }
}
