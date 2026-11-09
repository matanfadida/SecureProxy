using SecureProxy.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Infrastructure.Encryption
{
    public class PayloadDecryptor : IPayloadDecryptor
    {
        private readonly IEncryptionKeyProvider _encryptionKeyProvider;

        public PayloadDecryptor(IEncryptionKeyProvider encryptionKeyProvider)
        {
            _encryptionKeyProvider = encryptionKeyProvider;
        }

        public bool TryDecrypt(string keyReference, string base64Payload, out byte[]? plaintext)
        {
            try
            {
                if (string.IsNullOrEmpty(base64Payload))
                {
                    plaintext = null;
                    return false;
                }
                var payloadBytes = Convert.FromBase64String(base64Payload);

                if(payloadBytes.Length < 28)
                {
                    plaintext = null;
                    return false;
                }

                var key = _encryptionKeyProvider.GetBytesKey(keyReference);
                if(key.Length != 32)
                {
                    plaintext = null;
                    throw new InvalidOperationException("Key length must be 32 bytes for AES-256-GCM.");
                }
                var nonce = payloadBytes[0..12];
                var tags = payloadBytes[^16..];
                var ciphertext = payloadBytes[12..^16];
                using var aes = new AesGcm(key, 16);
                plaintext = new byte[ciphertext.Length];
                aes.Decrypt(nonce, ciphertext, tags, plaintext);
                
            }
            catch (Exception ex) when (ex is FormatException or AuthenticationTagMismatchException)
            {
                plaintext = null;
                return false;
            }
            return true;
        }
    }
}
