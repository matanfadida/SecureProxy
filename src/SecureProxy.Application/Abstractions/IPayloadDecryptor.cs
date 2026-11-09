namespace SecureProxy.Application.Abstractions;

public interface IPayloadDecryptor
{
    // False = the caller sent a bad payload. Throws only for server-side problems (bad key config).
    bool TryDecrypt(string keyReference, string base64Payload, out byte[]? plaintext);
}
