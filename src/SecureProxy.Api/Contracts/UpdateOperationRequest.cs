namespace SecureProxy.Api.Contracts;

// Body of PUT /api/admin/operations/{id}. RowVersion travels as Base64 because JSON has no bytes.
public class UpdateOperationRequest
{
    public bool IsActive { get; set; }
    public int MaxRequestSizeBytes { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
