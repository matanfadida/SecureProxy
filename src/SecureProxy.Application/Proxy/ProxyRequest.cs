namespace SecureProxy.Application.Proxy;

// Body of POST /api/proxy/execute. Field names follow the assignment's contract.
public class ProxyRequest
{
    // The operation key (maps to Operation.Key). Named clientId in the assignment.
    public string ClientId { get; set; } = string.Empty;

    // Caller parameters, including urlId. Values are strings; each ParameterRule checks the type.
    public Dictionary<string, string> Json { get; set; } = new();

    // Base64 of nonce(12) | ciphertext | tag(16). Required only when the operation requires encryption.
    public string? EncryptedPayload { get; set; }
}
