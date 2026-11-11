namespace SecureProxy.Application.Admin;

public enum OperationUpdateStatus
{
    Updated,   // -> 200
    NotFound,  // -> 404
    Conflict   // -> 409: someone changed the operation since ExpectedRowVersion was read
}

// NewRowVersion is set only when Status is Updated.
public record OperationUpdateResult(OperationUpdateStatus Status, byte[]? NewRowVersion = null);
