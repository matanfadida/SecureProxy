using SecureProxy.Domain.Common;

namespace SecureProxy.Domain.Entities
{
    // Points to where a key lives. Never stores the key itself (section 5.5).
    public class EncryptionProfile : IAuditable
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        // Name of the secret in the key source (env var / Key Vault), not the key.
        public string KeyReference { get; set; } = string.Empty;
    }
}
