namespace SecureProxy.Tests.Fakes;

// Replaces the network under a REAL HttpClient. HttpUpstreamForwarder stays real;
// only the "post office" is fake. Each test decides how the destination behaves.
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    // Every request that actually went out through HttpClient (with its headers).
    public List<HttpRequestMessage> ReceivedRequests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ReceivedRequests.Add(request);
        return _respond(request, cancellationToken);
    }
}
