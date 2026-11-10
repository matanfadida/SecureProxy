using SecureProxy.Application.Abstractions;

namespace SecureProxy.Tests.Fakes;

// The mock destination required by section 12: it records every request it receives.
// Rejection tests assert that ReceivedRequests is EMPTY; a status code alone is not proof.
public class RecordingForwarder : IUpstreamForwarder
{
    public List<UpstreamRequest> ReceivedRequests { get; } = new();

    // What the "destination" answers. Tests can change it (e.g. to a timeout or a 500).
    public UpstreamResult NextResult { get; set; } =
        new(UpstreamOutcome.Responded, 200, "{\"ok\":true}"u8.ToArray(), "application/json");

    public Task<UpstreamResult> SendAsync(UpstreamRequest request, CancellationToken cancellationToken)
    {
        ReceivedRequests.Add(request);
        return Task.FromResult(NextResult);
    }
}
