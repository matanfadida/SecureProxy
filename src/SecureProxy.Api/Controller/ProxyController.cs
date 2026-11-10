using Microsoft.AspNetCore.Mvc;
using SecureProxy.Application.Proxy;
using SecureProxy.Application.Services;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SecureProxy.Api.Controller
{
    [ApiController]
    [Route("api/proxy")]
    public class ProxyController : ControllerBase
    {
        private readonly ProxyService _proxyService;

        public ProxyController(ProxyService proxyService)
        {
            _proxyService = proxyService;
        }

        [HttpPost("execute")]
        [RequestSizeLimit(1_048_576)]
        public async Task<IActionResult> Execute(ProxyRequest request, CancellationToken ct)
        {
            var traceIdentifier = HttpContext.TraceIdentifier;
            var result = await _proxyService.ExecuteAsync(request, traceIdentifier, ct);

            if (!string.IsNullOrEmpty(result.ErrorCode))
            {
                Response.Headers["X-Proxy-Source"] = "proxy";
                return StatusCode(result.StatusCode, new { error = result.ErrorCode, correlationId = traceIdentifier });
            }
            Response.Headers["X-Proxy-Source"] = "upstream";
            Response.StatusCode = result.StatusCode;
            Response.ContentType = result.ContentType ?? "application/octet-stream";
            await Response.Body.WriteAsync(result.Body, ct);
            return new EmptyResult();
        }
    }
}
