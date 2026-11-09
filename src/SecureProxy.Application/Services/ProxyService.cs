using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Proxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;

namespace SecureProxy.Application.Services
{
    public class ProxyService
    {
        private readonly IPayloadDecryptor _payloadDecryptor;
        private readonly IOperationRepository _operationRepository;

        public ProxyService(IPayloadDecryptor payloadDecryptor, IOperationRepository operationRepository)
        {
            _operationRepository = operationRepository;
            _payloadDecryptor = payloadDecryptor;
        }

        public async Task<ProxyResult> ExecuteAsync(ProxyRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.ClientId))
            {
                return ProxyResult.Error(400, ProxyErrorCodes.InvalidRequest);
            }

            var operationDb = await _operationRepository.GetByOperationKeyAsync(request.ClientId, cancellationToken);

            if (operationDb == null)
            {
                return ProxyResult.Error(404, ProxyErrorCodes.OperationNotFound);
            }

            if (!operationDb.IsActive || !operationDb.Destination.IsActive)
            {
                return ProxyResult.Error(404, ProxyErrorCodes.OperationNotFound);
            }

            // 4-6. Parameters → TODO (next session)

            byte[]? plaintext = null;

            if (operationDb.RequiresEncryption)
            {
                if (operationDb.EncryptionProfile == null)
                {
                    throw new InvalidOperationException($"Operation {operationDb.Id} requires encryption but has no encryption profile.");
                }

                if (string.IsNullOrEmpty(request.EncryptedPayload))
                {
                    return ProxyResult.Error(400, ProxyErrorCodes.InvalidEncryptedPayload);
                }

                if (!_payloadDecryptor.TryDecrypt(operationDb.EncryptionProfile.KeyReference, request.EncryptedPayload, out plaintext))
                    return ProxyResult.Error(400, ProxyErrorCodes.InvalidEncryptedPayload);
            }
            else if (!string.IsNullOrEmpty(request.EncryptedPayload))
            {
                return ProxyResult.Error(400, ProxyErrorCodes.InvalidEncryptedPayload);
            }


            if (plaintext != null && plaintext.Length > operationDb.MaxRequestSizeBytes)
            {
                return ProxyResult.Error(413, ProxyErrorCodes.RequestTooLarge);
            }

            //9.Forward → TODO(next session)

            return ProxyResult.Error(501, "not_implemented");
        }
    }
}
