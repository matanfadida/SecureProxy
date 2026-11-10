using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Proxy;
using SecureProxy.Application.Services;
using SecureProxy.Domain.Enums;
using SecureProxy.Infrastructure.Encryption;
using SecureProxy.Tests.Encryption;
using SecureProxy.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Tests.UnitTest
{
    public class ProxyServiceTests
    {
        private readonly FakeOperationRepository _repository;
        private readonly RecordingForwarder _forwarder;
        private readonly FakeAuditor _auditor;
        private readonly FakeKeyProvider _keyProvider;
        private readonly ProxyService _service;

        public ProxyServiceTests()
        {
            _repository = new FakeOperationRepository();
            _forwarder = new RecordingForwarder();
            _auditor = new FakeAuditor();
            _keyProvider = new FakeKeyProvider();
            _service = new ProxyService(new PayloadDecryptor(_keyProvider), _repository, _forwarder, _auditor);
        }

        // Test 1: a valid operation is forwarded.
        [Fact]
        public async Task ExecuteAsync_ValidOperation_IsForwarded()
        {
            _repository.Add(TestOperations.CustomerGet());
            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            Assert.True(result.FromUpstream);

            var sent = Assert.Single(_forwarder.ReceivedRequests);
            Assert.Equal("GET", sent.HttpMethod);
            Assert.Equal("https://destination.test/users/1", sent.Url.ToString());
        }

        // Test 2: an unknown operation is rejected.
        [Fact]
        public async Task ExecuteAsync_UnknownOperation_IsRejectedAndNotForwarded()
        {
            // Nothing added to the repository: "customer.get" does not exist.
            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(404, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.OperationNotFound, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 3: a disabled operation is rejected.
        [Fact]
        public async Task ExecuteAsync_DisabledOperation_IsRejectedAndNotForwarded()
        {
            var operation = TestOperations.CustomerGet();
            operation.IsActive = false;
            _repository.Add(operation);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(404, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.OperationNotFound, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 4: a disabled destination is rejected.
        [Fact]
        public async Task ExecuteAsync_DisabledDestination_IsRejectedAndNotForwarded()
        {
            var operation = TestOperations.CustomerGet();
            operation.Destination.IsActive = false;
            _repository.Add(operation);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(404, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.OperationNotFound, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 5: a caller-supplied URL cannot override the configured destination.
        [Fact]
        public async Task ExecuteAsync_CallerUrlIgnored_ForwardsToConfiguredDestination()
        {
            var operation = TestOperations.CustomerGet();
            _repository.Add(operation);

            operation.AllowUnknownParameters = true;

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1", ["baseUrl"] = "https://google.co.il" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            Assert.True(result.FromUpstream);

            var sent = Assert.Single(_forwarder.ReceivedRequests);
            Assert.Equal("GET", sent.HttpMethod);
            Assert.Equal("https://destination.test/users/1", sent.Url.ToString());
        }

        // Test 6: a caller cannot change the HTTP method.
        [Fact]
        public async Task ExecuteAsync_CallerMethodIgnored_ForwardsWithConfiguredMethod()
        {
            var operation = TestOperations.CustomerGet();
            _repository.Add(operation);

            operation.AllowUnknownParameters = true;

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1", ["method"] = "POST" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            Assert.True(result.FromUpstream);

            var sent = Assert.Single(_forwarder.ReceivedRequests);
            Assert.Equal("GET", sent.HttpMethod);
            Assert.Equal("https://destination.test/users/1", sent.Url.ToString());
        }

        // Test 7: a missing required parameter is rejected.
        [Fact]
        public async Task ExecuteAsync_MissingRequiredParameter_IsRejectedAndNotForwarded()
        {
            _repository.Add(TestOperations.CustomerGet());

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.InvalidParameters, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 8a: unknown parameters are rejected when the operation does not allow them (the default).
        [Fact]
        public async Task ExecuteAsync_UnknownParameterNotAllowed_IsRejectedAndNotForwarded()
        {
            var operation = TestOperations.CustomerGet(); // AllowUnknownParameters = false by default
            _repository.Add(operation);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1", ["notFamiliar"] = "x" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.InvalidParameters, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 8b: when the operation allows unknown parameters, the request is forwarded,
        // but the unknown parameter is NOT passed to the destination.
        [Fact]
        public async Task ExecuteAsync_UnknownParameterAllowed_IsForwardedWithoutIt()
        {
            var operation = TestOperations.CustomerGet();
            operation.AllowUnknownParameters = true;
            _repository.Add(operation);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1", ["notFamiliar"] = "x" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            var sent = Assert.Single(_forwarder.ReceivedRequests);
            Assert.Equal("https://destination.test/users/1", sent.Url.ToString());
            Assert.DoesNotContain("notFamiliar", sent.Url.ToString());
        }

        // Test 9: path traversal is rejected.
        // The urlId rule is loosened to a plain string (no pattern, no length, no integer type),
        // so the value reaches the path-traversal check itself and is not stopped by an earlier rule.
        [Theory]
        [InlineData("..")]
        [InlineData(".")]
        [InlineData("../admin")]
        [InlineData("a\\b")]
        public async Task ExecuteAsync_PathTraversal_IsRejectedAndNotForwarded(string urlId)
        {
            var operation = TestOperations.CustomerGet();
            var urlIdRule = operation.Parameters.Single(p => p.Name == "urlId");
            urlIdRule.DataType = ParameterDataType.String;
            urlIdRule.Pattern = null;
            urlIdRule.MaxLength = null;
            _repository.Add(operation);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = urlId }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.InvalidParameters, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }
        // Test 10: .

        // Test 11: an invalid AES-GCM tag is rejected, and nothing reaches the destination.
        [Fact]
        public async Task ExecuteAsync_TamperedEncryptedPayload_IsRejectedAndNotForwarded()
        {
            _repository.Add(TestOperations.InvoiceCreate());

            var encryptor = new TestEncryptor(_keyProvider);
            var validPayload = encryptor.Encrypt(TestOperations.KeyReference, Encoding.UTF8.GetBytes("{\"amount\":100}"));

            var bytes = Convert.FromBase64String(validPayload);
            bytes[^1] ^= 1;
            var tamperedPayload = Convert.ToBase64String(bytes);

            var request = new ProxyRequest
            {
                ClientId = "invoice.create",
                EncryptedPayload = tamperedPayload
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.InvalidEncryptedPayload, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 12: invalid encrypted payloads are never forwarded.
        // Every value here is fixed text, so one [Theory] covers them all.
        [Theory]
        [InlineData(null)]               // required but not sent
        [InlineData("")]                 // required but empty
        [InlineData("not base64!!")]     // not Base64 -> FormatException inside the decryptor
        [InlineData("AAAAAAAAAAAAAA==")] // valid Base64 of 10 bytes, shorter than nonce + tag (28)
        public async Task ExecuteAsync_InvalidEncryptedPayload_IsRejectedAndNotForwarded(string? encryptedPayload)
        {
            _repository.Add(TestOperations.InvoiceCreate());

            var request = new ProxyRequest
            {
                ClientId = "invoice.create",
                EncryptedPayload = encryptedPayload
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.InvalidEncryptedPayload, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 12b: a payload encrypted with a different key is rejected.
        // This one must be created while the test runs, so it cannot be an InlineData value.
        [Fact]
        public async Task ExecuteAsync_PayloadEncryptedWithOtherKey_IsRejectedAndNotForwarded()
        {
            _repository.Add(TestOperations.InvoiceCreate());

            // A second provider = a second random key, unknown to the service.
            var otherEncryptor = new TestEncryptor(new FakeKeyProvider());
            var payloadWithOtherKey = otherEncryptor.Encrypt(TestOperations.KeyReference, Encoding.UTF8.GetBytes("{\"amount\":100}"));

            var request = new ProxyRequest
            {
                ClientId = "invoice.create",
                EncryptedPayload = payloadWithOtherKey
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(400, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.InvalidEncryptedPayload, result.ErrorCode);
            Assert.Empty(_forwarder.ReceivedRequests);
        }

        // Test 13: an upstream timeout returns 504. The request DID reach the destination
        // (the timeout happens after sending), so exactly one request was recorded.
        [Fact]
        public async Task ExecuteAsync_UpstreamTimeout_Returns504()
        {
            _repository.Add(TestOperations.CustomerGet());

            // Arrange BEFORE Act: tell the fake destination to "time out" on the next request.
            _forwarder.NextResult = new UpstreamResult(UpstreamOutcome.TimedOut, 0, Array.Empty<byte>(), null);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(504, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.UpstreamTimeout, result.ErrorCode);
            Assert.False(result.FromUpstream);             // the 504 is the proxy's answer, not the destination's
            Assert.Single(_forwarder.ReceivedRequests);    // it was sent; the destination just didn't answer in time
        }

        // Test 14: an upstream HTTP error is passed through unchanged and marked as coming from upstream.
        // The destination DID answer (with 500), so this is "Responded", not a proxy failure.
        [Fact]
        public async Task ExecuteAsync_UpstreamReturns500_IsPassedThroughAsUpstream()
        {
            _repository.Add(TestOperations.CustomerGet());

            var upstreamBody = "{\"error\":\"internal\"}"u8.ToArray();
            _forwarder.NextResult = new UpstreamResult(UpstreamOutcome.Responded, 500, upstreamBody, "application/json");

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(500, result.StatusCode);       // the destination's status, preserved
            Assert.True(result.FromUpstream);           // -> X-Proxy-Source: upstream
            Assert.Null(result.ErrorCode);              // not a proxy error
            Assert.Equal(upstreamBody, result.Body);    // the destination's body, unchanged
            Assert.Single(_forwarder.ReceivedRequests);
        }

        // Test 14b: the destination could not be reached at all -> 502 from the proxy.
        [Fact]
        public async Task ExecuteAsync_UpstreamConnectionFailed_Returns502()
        {
            _repository.Add(TestOperations.CustomerGet());

            _forwarder.NextResult = new UpstreamResult(UpstreamOutcome.ConnectionFailed, 0, Array.Empty<byte>(), null);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(502, result.StatusCode);
            Assert.Equal(ProxyErrorCodes.UpstreamUnavailable, result.ErrorCode);
            Assert.False(result.FromUpstream);          // the destination never answered; this is the proxy's answer
            Assert.Single(_forwarder.ReceivedRequests);
        }

        // Test 15: .

        // Test 16: .

        // Test 17a: a successful request writes one audit row describing the forward.
        [Fact]
        public async Task ExecuteAsync_SuccessfulRequest_WritesAuditRow()
        {
            _repository.Add(TestOperations.CustomerGet());
            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            var entry = Assert.Single(_auditor.Entries);
            Assert.Equal("test-correlation", entry.CorrelationId);
            Assert.Equal("customer.get", entry.OperationKey);
            Assert.Equal(RequestOutcome.Forwarded, entry.Outcome);
            Assert.True(entry.Forwarded);
            Assert.Equal(200, entry.ProxyStatusCode);
            Assert.Equal(200, entry.UpstreamStatusCode);
            Assert.Null(entry.Reason);
        }

        // Test 17b: a rejected request is audited too, with the internal reason the caller never sees.
        [Fact]
        public async Task ExecuteAsync_RejectedRequest_WritesAuditRowWithReason()
        {
            // Nothing added to the repository: "customer.get" does not exist.
            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            var entry = Assert.Single(_auditor.Entries);
            Assert.Equal(RequestOutcome.Rejected, entry.Outcome);
            Assert.Equal("unknown_operation", entry.Reason);       // internal: exact reason
            Assert.Equal(ProxyErrorCodes.OperationNotFound, result.ErrorCode); // public: generic code
            Assert.False(entry.Forwarded);
            Assert.Equal(404, entry.ProxyStatusCode);
            Assert.Null(entry.UpstreamStatusCode);
        }

        // Test 17c: unknown and disabled operations look the same to the caller,
        // but the audit keeps them apart.
        [Fact]
        public async Task ExecuteAsync_DisabledOperation_AuditReasonDiffersFromUnknown()
        {
            var operation = TestOperations.CustomerGet();
            operation.IsActive = false;
            _repository.Add(operation);

            var request = new ProxyRequest
            {
                ClientId = "customer.get",
                Json = { ["urlId"] = "1" }
            };

            var result = await _service.ExecuteAsync(request, "test-correlation", CancellationToken.None);

            Assert.Equal(ProxyErrorCodes.OperationNotFound, result.ErrorCode);
            var entry = Assert.Single(_auditor.Entries);
            Assert.Equal("operation_disabled", entry.Reason);
        }
    }
}
