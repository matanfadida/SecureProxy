using Microsoft.Extensions.Options;
using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Services;
using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly ProxySettings _proxySettings;

        public HttpUpstreamForwarder(HttpClient httpClient, ProxySettings proxySettings)
        {
            _httpClient = httpClient;
            _proxySettings = proxySettings;
        }

        public async Task<UpstreamResult> SendAsync(UpstreamRequest request, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.HttpMethod), request.Url);

            foreach (var header in request.AllowHeaders)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Body != null)
            {
                message.Content = new ByteArrayContent(request.Body);
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            cts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

            try
            {
                using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (response.Content.Headers.ContentLength > _proxySettings.MaxResponse)
                {
                    return new UpstreamResult(UpstreamOutcome.ResponseTooLarge, (int)response.StatusCode, Array.Empty<byte>(), response.Content.Headers.ContentType?.ToString());
                }

                // Content-Length may be missing (chunked) or wrong, so also count while reading.
                await using var responseStream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var bucket = new MemoryStream();   
                var buffer = new byte[8192]; 
                long totalBytes = 0;

                while (true)
                {
                    var bytesRead = await responseStream.ReadAsync(buffer, cts.Token); // read FROM the pipe
                    if (bytesRead == 0)
                    {
                        break; // end of the response
                    }

                    totalBytes += bytesRead;
                    if (totalBytes > _proxySettings.MaxResponse)
                    {
                        // Stop immediately: never hold more than the limit in memory.
                        return new UpstreamResult(UpstreamOutcome.ResponseTooLarge, (int)response.StatusCode, Array.Empty<byte>(), null);
                    }

                    bucket.Write(buffer, 0, bytesRead); // only the bytes actually read
                }

                return new UpstreamResult(UpstreamOutcome.Responded, (int)response.StatusCode, bucket.ToArray(), response.Content.Headers.ContentType?.ToString());
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
