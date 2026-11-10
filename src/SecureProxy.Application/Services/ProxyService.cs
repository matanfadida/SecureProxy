using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Proxy;
using SecureProxy.Domain.Entities;
using SecureProxy.Domain.Enums;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SecureProxy.Application.Services
{
    public class ProxyService
    {
        private readonly IPayloadDecryptor _payloadDecryptor;
        private readonly IOperationRepository _operationRepository;
        private readonly IUpstreamForwarder _forwarder;
        private readonly IRequestAuditor _auditor;

        public ProxyService(IPayloadDecryptor payloadDecryptor, IOperationRepository operationRepository, IUpstreamForwarder forwarder, IRequestAuditor auditor)
        {
            _operationRepository = operationRepository;
            _payloadDecryptor = payloadDecryptor;
            _forwarder = forwarder;
            _auditor = auditor;
        }

        public async Task<ProxyResult> ExecuteAsync(ProxyRequest request, string correlationId, CancellationToken ct)
        {
            var log = new RequestLog
            {
                CorrelationId = correlationId,
                OperationKey = request.ClientId,
                StartedAt = DateTime.UtcNow
            };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await ProcessAsync(request, log, ct);
                log.ProxyStatusCode = result.StatusCode;
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller went away. 499 = "client closed request"; it is only recorded, never sent.
                log.Outcome = RequestOutcome.Cancelled;
                log.ProxyStatusCode = 499;
                throw;
            }
            catch
            {
                // Unexpected error (e.g. broken key configuration). The exception handler returns 500.
                log.Outcome = RequestOutcome.Failed;
                log.ProxyStatusCode = 500;
                throw;
            }
            finally
            {
                log.CompletedAt = DateTime.UtcNow;
                log.DurationMs = (int)stopwatch.ElapsedMilliseconds;
                await _auditor.WriteAsync(log);
            }
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

        private Uri BuildUrl(Operation operation, Dictionary<string, string> json)
        {
            var path = operation.PathTemplate;
            if (path.Contains("{urlId}"))
            {
                path = path.Replace("{urlId}", Uri.EscapeDataString(json["urlId"]));
            }

            var queryParts = operation.Parameters
                .Where(p => p.Location == Domain.Enums.ParameterLocation.Query && json.ContainsKey(p.Name))
                .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(json[p.Name])}");

            var query = string.Join("&", queryParts);
            var relative = path.TrimStart('/') + (query.Length > 0 ? "?" + query : "");

            // BaseUrl gets a trailing slash so a base path (e.g. /api) is kept, not replaced.
            var baseUri = new Uri(operation.Destination.BaseUrl.TrimEnd('/') + "/");
            return new Uri(baseUri, relative);
        }

        private async Task<ProxyResult> ProcessAsync(ProxyRequest request, RequestLog log, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.ClientId))
            {
                return Reject(log, 400, ProxyErrorCodes.InvalidRequest, "missing_client_id");
            }

            var operationDb = await _operationRepository.GetByOperationKeyAsync(request.ClientId, cancellationToken);

            if (operationDb == null)
            {
                return Reject(log, 404, ProxyErrorCodes.OperationNotFound, "unknown_operation");
            }

            log.DestinationId = operationDb.DestinationId;

            // Same public answer as an unknown operation (no enumeration), different internal reason.
            if (!operationDb.IsActive)
            {
                return Reject(log, 404, ProxyErrorCodes.OperationNotFound, "operation_disabled");
            }

            if (!operationDb.Destination.IsActive)
            {
                return Reject(log, 404, ProxyErrorCodes.OperationNotFound, "destination_disabled");
            }

            if (!ValidateParameters(operationDb, request.Json))
                return Reject(log, 400, ProxyErrorCodes.InvalidParameters, "invalid_parameters");

            byte[]? plaintext = null;

            if (operationDb.RequiresEncryption)
            {
                if (operationDb.EncryptionProfile == null)
                {
                    throw new InvalidOperationException($"Operation {operationDb.Id} requires encryption but has no encryption profile.");
                }

                if (string.IsNullOrEmpty(request.EncryptedPayload))
                {
                    return Reject(log, 400, ProxyErrorCodes.InvalidEncryptedPayload, "missing_payload");
                }

                if (!_payloadDecryptor.TryDecrypt(operationDb.EncryptionProfile.KeyReference, request.EncryptedPayload, out plaintext))
                    return Reject(log, 400, ProxyErrorCodes.InvalidEncryptedPayload, "decryption_failed");
            }
            else if (!string.IsNullOrEmpty(request.EncryptedPayload))
            {
                return Reject(log, 400, ProxyErrorCodes.InvalidEncryptedPayload, "unexpected_payload");
            }

            if (plaintext != null && plaintext.Length > operationDb.MaxRequestSizeBytes)
            {
                return Reject(log, 413, ProxyErrorCodes.RequestTooLarge, "request_too_large");
            }

            var url = BuildUrl(operationDb, request.Json);

            var upstreamRequest = new UpstreamRequest(operationDb.HttpMethod, url, plaintext, operationDb.Destination.TimeoutSeconds);

            // From here on the request has left the proxy.
            log.Forwarded = true;
            var result = await _forwarder.SendAsync(upstreamRequest, cancellationToken);

            switch (result.Outcome)
            {
                case UpstreamOutcome.TimedOut:
                    log.Outcome = RequestOutcome.UpstreamTimeout;
                    log.Reason = "upstream_timeout";
                    return ProxyResult.Error(504, ProxyErrorCodes.UpstreamTimeout);

                case UpstreamOutcome.ConnectionFailed:
                    log.Outcome = RequestOutcome.UpstreamUnavailable;
                    log.Reason = "upstream_connection_failed";
                    return ProxyResult.Error(502, ProxyErrorCodes.UpstreamUnavailable);

                default:
                    log.Outcome = RequestOutcome.Forwarded;
                    log.UpstreamStatusCode = result.StatusCode;
                    return ProxyResult.Upstream(result.StatusCode, result.Body, result.ContentType);
            }
        }

        // Rejection before forwarding: records the internal reason for the audit,
        // returns only the public error code to the caller.
        private static ProxyResult Reject(RequestLog log, int statusCode, string errorCode, string reason)
        {
            log.Outcome = RequestOutcome.Rejected;
            log.Reason = reason;
            return ProxyResult.Error(statusCode, errorCode);
        }
    }
}
