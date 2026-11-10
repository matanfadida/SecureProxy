using SecureProxy.Application.Proxy;
using SecureProxy.Application.Services;
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
    }
}
