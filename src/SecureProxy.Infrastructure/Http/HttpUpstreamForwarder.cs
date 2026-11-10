using SecureProxy.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SecureProxy.Infrastructure.Http
{
    public class HttpUpstreamForwarder : IUpstreamForwarder
    {
        private readonly HttpClient _httpClient;

        public HttpUpstreamForwarder(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<UpstreamResult> SendAsync(UpstreamRequest request, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.HttpMethod), request.Url);

            if (request.Body != null)
            {
                message.Content = new ByteArrayContent(request.Body);
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            cts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

            try
            {
                using var response = await _httpClient.SendAsync(message, cts.Token);
                var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
               return new UpstreamResult(UpstreamOutcome.Responded, (int)response.StatusCode, body, response.Content.Headers.ContentType?.ToString());
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The caller did not cancel, so our timeout fired.
                return new UpstreamResult(UpstreamOutcome.TimedOut, 0, Array.Empty<byte>(), null);
            }
            catch (HttpRequestException)
            {
                // DNS failure
                return new UpstreamResult(UpstreamOutcome.ConnectionFailed, 0, Array.Empty<byte>(), null);
            }
        }
    }
}
