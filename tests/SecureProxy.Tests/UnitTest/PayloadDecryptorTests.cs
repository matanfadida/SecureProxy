using SecureProxy.Infrastructure.Encryption;
using SecureProxy.Tests.Encryption;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Tests.UnitTest
{
    public class PayloadDecryptorTests
    {
        private readonly FakeKeyProvider _fakeKeyProvider = new FakeKeyProvider();
        private readonly TestEncryptor _testEncryptor;
        private readonly PayloadDecryptor _payloadDecryptor;

        public PayloadDecryptorTests()
        {
            _testEncryptor = new TestEncryptor(_fakeKeyProvider);
            _payloadDecryptor = new PayloadDecryptor(_fakeKeyProvider);
        }

        [Fact]
        public void TryDecrypt_ValidPayload_ReturnsOriginalText()
        {
            var encrypt =_testEncryptor.Encrypt("test-profile", Encoding.UTF8.GetBytes("Test Payload"));
            var success = _payloadDecryptor.TryDecrypt("test-profile", encrypt, out var decryptedPayload);

            Assert.True(success);
            Assert.Equal("Test Payload", Encoding.UTF8.GetString(decryptedPayload!));
        }

        [Fact]
        public void TryDecrypt_TamperedTag_ReturnsFalse()
        {
            var encrypt = _testEncryptor.Encrypt("test-profile", Encoding.UTF8.GetBytes("Test Payload"));
            var payloadBytes = Convert.FromBase64String(encrypt);
            payloadBytes[^1] ^= 1;
            var success = _payloadDecryptor.TryDecrypt("test-profile", Convert.ToBase64String(payloadBytes), out var decryptedPayload);

            Assert.False(success);
        }

        // Changing any byte of the ciphertext (not only the tag) breaks authentication.
        [Fact]
        public void TryDecrypt_TamperedCiphertext_ReturnsFalse()
        {
            var encrypt = _testEncryptor.Encrypt("test-profile", Encoding.UTF8.GetBytes("Test Payload"));
            var payloadBytes = Convert.FromBase64String(encrypt);
            payloadBytes[12] ^= 1; // first ciphertext byte (bytes 0-11 are the nonce)

            var success = _payloadDecryptor.TryDecrypt("test-profile", Convert.ToBase64String(payloadBytes), out var plaintext);

            Assert.False(success);
            Assert.Null(plaintext);
        }

        // The nonce is authenticated too: changing it fails decryption.
        [Fact]
        public void TryDecrypt_TamperedNonce_ReturnsFalse()
        {
            var encrypt = _testEncryptor.Encrypt("test-profile", Encoding.UTF8.GetBytes("Test Payload"));
            var payloadBytes = Convert.FromBase64String(encrypt);
            payloadBytes[0] ^= 1;

            var success = _payloadDecryptor.TryDecrypt("test-profile", Convert.ToBase64String(payloadBytes), out _);

            Assert.False(success);
        }

        // Encrypted with a key the decryptor does not have.
        [Fact]
        public void TryDecrypt_WrongKey_ReturnsFalse()
        {
            var otherEncryptor = new TestEncryptor(new FakeKeyProvider()); // new provider = different random key
            var encrypt = otherEncryptor.Encrypt("test-profile", Encoding.UTF8.GetBytes("Test Payload"));

            var success = _payloadDecryptor.TryDecrypt("test-profile", encrypt, out _);

            Assert.False(success);
        }

        // Garbage input from the caller: false, never an exception.
        [Theory]
        [InlineData("")]                 // empty
        [InlineData("   ")]              // whitespace: decodes to 0 bytes -> too short
        [InlineData("not base64!!")]     // FormatException, caught -> false
        [InlineData("AAAAAAAAAAAAAA==")] // 10 bytes: shorter than nonce (12) + tag (16)
        public void TryDecrypt_InvalidInput_ReturnsFalse(string payload)
        {
            var success = _payloadDecryptor.TryDecrypt("test-profile", payload, out var plaintext);

            Assert.False(success);
            Assert.Null(plaintext);
        }

        // Boundary: exactly 28 bytes (empty ciphertext) is valid. Guards against a "<= 28" mistake.
        [Fact]
        public void TryDecrypt_EmptyPlaintext_Succeeds()
        {
            var encrypt = _testEncryptor.Encrypt("test-profile", Array.Empty<byte>());
            Assert.Equal(28, Convert.FromBase64String(encrypt).Length);

            var success = _payloadDecryptor.TryDecrypt("test-profile", encrypt, out var plaintext);

            Assert.True(success);
            Assert.Empty(plaintext!);
        }

        // A key of the wrong length is a server configuration error, not a bad payload:
        // it must THROW (-> 500), not return false (-> 400).
        [Fact]
        public void TryDecrypt_InvalidKeyLength_Throws()
        {
            var validPayload = _testEncryptor.Encrypt("test-profile", Encoding.UTF8.GetBytes("Test Payload"));

            var brokenKeyProvider = new FakeKeyProvider();
            brokenKeyProvider._fakeKey = new byte[10];
            var decryptorWithBrokenKey = new PayloadDecryptor(brokenKeyProvider);

            Assert.Throws<InvalidOperationException>(() =>
                decryptorWithBrokenKey.TryDecrypt("test-profile", validPayload, out _));
        }
    }
}
