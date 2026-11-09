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
    }
}
