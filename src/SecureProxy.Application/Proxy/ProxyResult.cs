namespace SecureProxy.Application.Proxy;

// What ProxyService returns. The controller turns it into an HTTP response.
public class ProxyResult
{
    public int StatusCode { get; private init; }

    // Null when the response came from the destination.
    public string? ErrorCode { get; private init; }

    // True = the destination produced this response. False = the proxy did.
    public bool FromUpstream { get; private init; }

    public byte[]? Body { get; private init; }

    public string? ContentType { get; private init; }

    public static ProxyResult Error(int statusCode, string errorCode) =>
        new() { StatusCode = statusCode, ErrorCode = errorCode, FromUpstream = false };

    public static ProxyResult Upstream(int statusCode, byte[] body, string? contentType) =>
        new() { StatusCode = statusCode, Body = body, ContentType = contentType, FromUpstream = true };
}
