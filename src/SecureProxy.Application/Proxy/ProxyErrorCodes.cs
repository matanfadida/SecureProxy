namespace SecureProxy.Application.Proxy;

// Public error codes. Short and stable; never include internal details.
public static class ProxyErrorCodes
{
    public const string InvalidRequest = "invalid_request";

    // Used for both unknown and disabled operations, so callers cannot tell which keys exist.
    public const string OperationNotFound = "operation_not_found";

    public const string InvalidParameters = "invalid_parameters";

    public const string InvalidEncryptedPayload = "invalid_encrypted_payload";

    public const string RequestTooLarge = "request_too_large";

    public const string UpstreamTimeout = "upstream_timeout";

    public const string UpstreamUnavailable = "upstream_unavailable";
}
