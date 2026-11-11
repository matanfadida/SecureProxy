namespace SecureProxy.Application.Admin;

// The fields an admin may change, plus the version the admin saw when loading the operation.
public record OperationUpdate(
    bool IsActive,
    int MaxRequestSizeBytes,
    byte[] ExpectedRowVersion);
