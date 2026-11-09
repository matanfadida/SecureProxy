using SecureProxy.Domain.Common;

namespace SecureProxy.Domain.Entities
{
    // An explicitly allowed action. Callers choose it by Key (the "clientId" in the request).
    // Destination, method and path always come from here, never from the caller (section 5.2).
    public class Operation : IAuditable
    {
        public int Id { get; set; }

        public string OperationKey { get; set; } = string.Empty; 

        public int DestinationId { get; set; }

        public Destination Destination { get; set; } = null!;

        public string HttpMethod { get; set; } = string.Empty;

        public string PathTemplate { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public int MaxRequestSizeBytes { get; set; }

        public bool RequiresEncryption { get; set; }

        public int? EncryptionProfileId { get; set; }

        public EncryptionProfile? EncryptionProfile { get; set; }

        public bool AllowUnknownParameters { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        // Concurrency token. Configured as SQL Server rowversion in Infrastructure.
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public List<ParameterRule> Parameters { get; set; } = new();
    }
}
