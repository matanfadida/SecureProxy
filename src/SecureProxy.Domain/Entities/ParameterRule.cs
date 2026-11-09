using SecureProxy.Domain.Common;
using SecureProxy.Domain.Enums;

namespace SecureProxy.Domain.Entities
{
    // Validation rule for one caller-supplied parameter (section 5.3).
    public class ParameterRule : IAuditable
    {
        public int Id { get; set; }

        public int OperationId { get; set; }

        public Operation Operation { get; set; } = null!;

        public string Name { get; set; } = string.Empty;

        public bool IsRequired { get; set; }

        public ParameterDataType DataType { get; set; }

        public int? MaxLength { get; set; }

        public string? Pattern { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public ParameterLocation Location { get; set; }
    }
}
