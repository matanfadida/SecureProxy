using Microsoft.EntityFrameworkCore;
using SecureProxy.Domain.Entities;
using SecureProxy.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Infrastructure.Persistence
{
    public static class DevelopmentSeeder
    {
        public async static Task SeedAsync(ProxyDbContext db, CancellationToken ct)
        {
            if (await db.Destinations.AnyAsync(ct)) return;

            var encryptionProfile = new EncryptionProfile
            {
                KeyReference = "ENCRYPTION_KEY_DEMO",
                Name = "demo-key"
            };
            var destinations = new List<Destination>
            {
                new Destination
                {
                    Name = "JsonPlaceholder",
                    BaseUrl = "https://jsonplaceholder.typicode.com",
                    IsActive = true,
                    TimeoutSeconds = 5,
                    Operations = new List<Operation>
                    {
                        new Operation
                        {
                            Key = "customer.get",
                            HttpMethod = "GET",
                            PathTemplate = "/users/{urlId}",
                            IsActive = true,
                            MaxRequestSizeBytes = 4096,
                            RequiresEncryption = false,
                            Parameters = new List<ParameterRule>
                            {
                                new ParameterRule
                                {
                                    Name = "urlId",
                                    IsRequired = true,
                                    MaxLength = 10,
                                    Pattern = @"^\d+$",
                                    DataType = ParameterDataType.Integer,
                                    Location = ParameterLocation.Path
                                }
                            }
                        },
                        new Operation
                        {
                            Key = "invoice.create",
                            HttpMethod = "POST",
                            PathTemplate = "/posts",
                            IsActive = true,
                            MaxRequestSizeBytes = 65536,
                            RequiresEncryption = true,
                            EncryptionProfile = encryptionProfile,
                        },
                        new Operation
                        {
                            Key = "customer.disabled",
                            HttpMethod = "GET",
                            PathTemplate = "/users/{urlId}",
                            IsActive = false,
                            MaxRequestSizeBytes = 4096,
                            RequiresEncryption = false,
                            Parameters = new List<ParameterRule>
                            {
                                new ParameterRule
                                {
                                    Name = "urlId",
                                    IsRequired = true,
                                    MaxLength = 10,
                                    Pattern = @"^\d+$",
                                    DataType = ParameterDataType.Integer,
                                    Location = ParameterLocation.Path
                                }
                            }
                        }
                    }
                },
            };
            db.EncryptionProfiles.Add(encryptionProfile);
            db.Destinations.AddRange(destinations);

            await db.SaveChangesAsync(ct);
        }
    }
}
