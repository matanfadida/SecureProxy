using SecureProxy.Application.Abstractions;
using System.Security.Cryptography;

namespace SecureProxy.Tests.Encryption
{
    public class FakeKeyProvider : IEncryptionKeyProvider
    {
        public byte[] _fakeKey = RandomNumberGenerator.GetBytes(32); 

        public byte[] GetBytesKey(string keyReference)
        {
            return _fakeKey;
        }
    }
}