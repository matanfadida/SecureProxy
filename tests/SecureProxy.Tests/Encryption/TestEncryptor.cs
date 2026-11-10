using SecureProxy.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Tests.Encryption
{
    public class TestEncryptor
    {
        private readonly IEncryptionKeyProvider _encryptionKeyProvider;

        public TestEncryptor(IEncryptionKeyProvider encryptionKeyProvider)
        {
            _encryptionKeyProvider = encryptionKeyProvider;
        }

        public string Encrypt(string keyReference, byte[] plaintext)
        {
            var key = _encryptionKeyProvider.GetBytesKey(keyReference);
            if (key.Length != 32)
            {
                throw new InvalidOperationException("Key length must be 32 bytes for AES-256-GCM.");
            }
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tags = new byte[16];
            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tags);
            }

            byte[] payloadBytes = [.. nonce, .. ciphertext, .. tags];
            return Convert.ToBase64String(payloadBytes);
        }

    }
}
