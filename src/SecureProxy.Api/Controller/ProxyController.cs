using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using SecureProxy.Application.Proxy;
using SecureProxy.Application.Services;
using System.Threading.Tasks;

namespace SecureProxy.Api.Controller
{
    [Route("/api")]
    public class ProxyController : ControllerBase
    {
        private readonly ProxyService _proxyService;

        public ProxyController(ProxyService proxyService)
        {
            _proxyService = proxyService;
        }

        [Route("/proxy/execute")]
        [HttpPost]
        public async Task<ProxyResult> Index(ProxyRequest request, CancellationToken ct)
        {
            return await _proxyService.ExecuteAsync(request, ct);
        }
    }
}
