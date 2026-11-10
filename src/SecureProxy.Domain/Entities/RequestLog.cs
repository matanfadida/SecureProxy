using SecureProxy.Domain.Enums;

namespace SecureProxy.Domain.Entities
{
    // One row per proxy request (assignment section 5.6). Written once, never updated,
    // so it does not implement IAuditable. Never holds keys, headers or bodies.
    public class RequestLog
    {
        public long Id { get; set; }

        public string CorrelationId { get; set; } = string.Empty;

        // What the caller asked for. May not exist in configuration (unknown operation).
        public string? OperationKey { get; set; }

        public int? DestinationId { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime CompletedAt { get; set; }

        public int DurationMs { get; set; }

        public bool Forwarded { get; set; }

        public int? UpstreamStatusCode { get; set; }

        public int ProxyStatusCode { get; set; }

        public RequestOutcome Outcome { get; set; }

        // Internal reason (e.g. "operation_disabled"). Never sent to the caller.
        public string? Reason { get; set; }
    }
}
