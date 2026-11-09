using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Proxy;
using SecureProxy.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

            if (!ValidateParameters(operationDb, request.Json))
                return ProxyResult.Error(400, ProxyErrorCodes.InvalidParameters);

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

        private bool ValidateParameters(Operation operation, Dictionary<string, string> json)
        {
            // Step 4: urlId must be sent exactly when the path has a {urlId} placeholder.
            var pathNeedsUrlId = operation.PathTemplate.Contains("{urlId}");
            var urlIdSent = json.ContainsKey("urlId");

            if (pathNeedsUrlId != urlIdSent)
            {
                return false;
            }

            // Step 5: everything that is defined must be valid (and present if required).
            foreach (var rule in operation.Parameters)
            {
                if (!json.TryGetValue(rule.Name, out var value))
                {
                    if (rule.IsRequired) return false; // missing and required
                    continue;                          // missing and optional
                }

                if (rule.MaxLength.HasValue && value.Length > rule.MaxLength.Value)
                {
                    return false;
                }

                switch (rule.DataType)
                {
                    case Domain.Enums.ParameterDataType.String:
                        break;
                    case Domain.Enums.ParameterDataType.Integer:
                        if (!long.TryParse(value, out _)) return false;
                        break;
                    case Domain.Enums.ParameterDataType.Guid:
                        if (!Guid.TryParse(value, out _)) return false;
                        break;
                    case Domain.Enums.ParameterDataType.Boolean:
                        if (!bool.TryParse(value, out _)) return false;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown parameter data type: {rule.DataType}");
                }

                if (rule.Pattern != null &&
                    !Regex.IsMatch(value, rule.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                {
                    return false;
                }

                // Path traversal: a path value must stay a single path segment.
                if (rule.Location == Domain.Enums.ParameterLocation.Path &&
                    (value.Contains('/') || value.Contains('\\') || value == "." || value == ".."))
                {
                    return false;
                }
            }

            // Step 6: everything that was sent must be defined (unless the operation allows unknowns).
            if (!operation.AllowUnknownParameters)
            {
                foreach (var key in json.Keys)
                {
                    var hasRule = operation.Parameters.Any(p => string.Equals(p.Name, key, StringComparison.Ordinal));
                    if (!hasRule) return false;
                }
            }

            return true;
        }
    }
}
