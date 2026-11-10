using Microsoft.Extensions.Configuration;
using SecureProxy.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Infrastructure.Encryption
{
    public class ConfigurationKeyProvider : IEncryptionKeyProvider
    {
        private readonly IConfiguration _configuration;

        public ConfigurationKeyProvider(IConfiguration configuration) {
            _configuration = configuration;
        }

        public byte[] GetBytesKey(string keyReference)
        {
            var value = _configuration[keyReference];
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException($"Encryption key '{keyReference}' is not configured.");
            }
            byte[] key;
            try
            {
                key = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"Encryption key '{keyReference}' is not valid Base64.");
            }
            if(key.Length != 32)
            {
                throw new InvalidOperationException("Key length must be 32 bytes for AES-256-GCM.");
            }
            return key;
        }
    }
}
