using SecureProxy.Domain.Entities;
using SecureProxy.Domain.Enums;

namespace SecureProxy.Tests.Fakes;

// Ready-made configuration for tests. Each call returns NEW objects,
// so a test can change one (e.g. IsActive = false) without affecting others.
public static class TestOperations
{
    public const string KeyReference = "TEST_KEY";

    public static Destination Destination() => new()
    {
        Id = 1,
        Name = "TestDestination",
        BaseUrl = "https://destination.test",
        IsActive = true,
        TimeoutSeconds = 5
    };

    // GET /users/{urlId}, urlId is a required integer path parameter.
    public static Operation CustomerGet(Destination? destination = null)
    {
        var dest = destination ?? Destination();
        return new Operation
        {
            Id = 1,
            Key = "customer.get",
            DestinationId = dest.Id,
            Destination = dest,
            HttpMethod = "GET",
            PathTemplate = "/users/{urlId}",
            IsActive = true,
            MaxRequestSizeBytes = 4096,
            Parameters =
            {
                new ParameterRule
                {
                    Name = "urlId",
                    IsRequired = true,
                    DataType = ParameterDataType.Integer,
                    MaxLength = 10,
                    Pattern = @"^\d+$",
                    Location = ParameterLocation.Path
                }
            }
        };
    }

    // POST /invoices, requires an encrypted payload, no parameters.
    public static Operation InvoiceCreate(Destination? destination = null)
    {
        var dest = destination ?? Destination();
        return new Operation
        {
            Id = 2,
            Key = "invoice.create",
            DestinationId = dest.Id,
            Destination = dest,
            HttpMethod = "POST",
            PathTemplate = "/invoices",
            IsActive = true,
            MaxRequestSizeBytes = 65536,
            RequiresEncryption = true,
            EncryptionProfileId = 1,
            EncryptionProfile = new EncryptionProfile { Id = 1, Name = "test-key", KeyReference = KeyReference }
        };
    }
}
