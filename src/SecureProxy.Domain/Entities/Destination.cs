using SecureProxy.Domain.Common;

namespace SecureProxy.Domain.Entities
{
    // An upstream API the proxy is allowed to call (assignment section 5.1).
    public class Destination : IAuditable
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public int TimeoutSeconds { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public List<Operation> Operations { get; set; } = new();
    }
}
