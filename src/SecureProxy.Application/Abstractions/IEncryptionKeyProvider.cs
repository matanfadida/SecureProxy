using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Application.Abstractions
{
    public interface IEncryptionKeyProvider
    {
        public byte[] GetBytesKey(string keyReference);
    }
}
