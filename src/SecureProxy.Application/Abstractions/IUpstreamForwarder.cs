namespace SecureProxy.Application.Abstractions;

public interface IUpstreamForwarder
{
    Task<UpstreamResult> SendAsync(UpstreamRequest request, CancellationToken cancellationToken);
}

// Everything here comes from trusted configuration plus validated values.
public record UpstreamRequest(
    string HttpMethod,
    Uri Url,
    byte[]? Body,
    Dictionary<string, string> AllowHeaders,
    int TimeoutSeconds);

public enum UpstreamOutcome
{
    Responded,
    TimedOut,
    ConnectionFailed,
    ResponseTooLarge
}

public record UpstreamResult(
    UpstreamOutcome Outcome,
    int StatusCode,
    byte[] Body,
    string? ContentType);
