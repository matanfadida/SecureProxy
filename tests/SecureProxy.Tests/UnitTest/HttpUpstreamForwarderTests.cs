using System.Net;
using System.Text;
using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Services;
using SecureProxy.Infrastructure.Http;
using SecureProxy.Tests.Fakes;

namespace SecureProxy.Tests.UnitTest
{
    // Tests the REAL HttpUpstreamForwarder. Only the network under HttpClient is fake.
    public class HttpUpstreamForwarderTests
    {
        // Same in every test, so it can be a field.
        private readonly ProxySettings _settings = new ProxySettings { MaxResponse = 100 };

        // Builds a simple GET request. Only the timeout changes between tests.
        private static UpstreamRequest Request(int timeoutSeconds = 5) =>
            new UpstreamRequest(
                "GET",
                new Uri("https://destination.test/users/1"),
                null,                                   // no body
                new Dictionary<string, string>(),       // no headers
                timeoutSeconds);

        // A: the destination answers normally -> Responded, with status and body.
        [Fact]
        public async Task SendAsync_UpstreamResponds_ReturnsResponded()
        {
            // Arrange: how the fake destination behaves in THIS test: answer 200 at once.
            var handler = new StubHttpMessageHandler((request, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}")
                }));

            // A real HttpClient whose "post office" is the fake one, and the real forwarder.
            var forwarder = new HttpUpstreamForwarder(new HttpClient(handler), _settings);

            // Act
            var result = await forwarder.SendAsync(Request(), CancellationToken.None);

            // Assert
            Assert.Equal(UpstreamOutcome.Responded, result.Outcome);
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(result.Body));
            Assert.Single(handler.ReceivedRequests);
        }

        // B (test 13): the destination is too slow -> our timeout stops the wait -> TimedOut.
        [Fact]
        public async Task SendAsync_UpstreamTooSlow_ReturnsTimedOut()
        {
            // The fake destination waits 10 seconds, unless the token is cancelled.
            var handler = new StubHttpMessageHandler(async (request, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return new HttpResponseMessage(HttpStatusCode.OK); // never reached
            });
            var forwarder = new HttpUpstreamForwarder(new HttpClient(handler), _settings);

            // Our timeout is 1 second, so the forwarder should give up after 1 second, not 10.
            var result = await forwarder.SendAsync(Request(timeoutSeconds: 1), CancellationToken.None);

            Assert.Equal(UpstreamOutcome.TimedOut, result.Outcome);
        }

        // C (test 15): the CALLER cancels -> the exception propagates; it is NOT reported as a timeout.
        [Fact]
        public async Task SendAsync_CallerCancels_ThrowsAndDoesNotReportTimeout()
        {
            var handler = new StubHttpMessageHandler(async (request, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var forwarder = new HttpUpstreamForwarder(new HttpClient(handler), _settings);

            // The "caller" gives up after 100 ms. The destination timeout (5 s) is much longer,
            // so if anything stops the wait, it is the caller, not our timer.
            using var callerCancellation = new CancellationTokenSource();
            callerCancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                forwarder.SendAsync(Request(timeoutSeconds: 5), callerCancellation.Token));
        }

        // D: the destination cannot be reached -> ConnectionFailed.
        [Fact]
        public async Task SendAsync_ConnectionFails_ReturnsConnectionFailed()
        {
            var handler = new StubHttpMessageHandler((request, ct) =>
                throw new HttpRequestException("Connection refused"));
            var forwarder = new HttpUpstreamForwarder(new HttpClient(handler), _settings);

            var result = await forwarder.SendAsync(Request(), CancellationToken.None);

            Assert.Equal(UpstreamOutcome.ConnectionFailed, result.Outcome);
        }

        // E: the response is bigger than MaxResponse (100 bytes in these tests) -> ResponseTooLarge.
        // StreamContent sends no Content-Length, so this checks the counting loop, not only the header.
        [Fact]
        public async Task SendAsync_ResponseTooLarge_WithoutContentLength_ReturnsResponseTooLarge()
        {
            var bigBody = new byte[1000]; // 10x the limit
            var handler = new StubHttpMessageHandler((request, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(bigBody)) // no Content-Length header
                }));
            var forwarder = new HttpUpstreamForwarder(new HttpClient(handler), _settings);

            var result = await forwarder.SendAsync(Request(), CancellationToken.None);

            Assert.Equal(UpstreamOutcome.ResponseTooLarge, result.Outcome);
            Assert.Empty(result.Body); // nothing of the big body was kept
        }

        // E2: same, but the destination DOES send Content-Length -> rejected before reading the body.
        [Fact]
        public async Task SendAsync_ResponseTooLarge_WithContentLength_ReturnsResponseTooLarge()
        {
            var handler = new StubHttpMessageHandler((request, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[1000]) // sets Content-Length: 1000
                }));
            var forwarder = new HttpUpstreamForwarder(new HttpClient(handler), _settings);

            var result = await forwarder.SendAsync(Request(), CancellationToken.None);

            Assert.Equal(UpstreamOutcome.ResponseTooLarge, result.Outcome);
        }
    }
}
