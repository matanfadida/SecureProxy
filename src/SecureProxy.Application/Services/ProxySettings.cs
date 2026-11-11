using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureProxy.Application.Services
{
    public class ProxySettings
    {
        public List<string>? AllowedHeaders { get; set; }

        // Maximum upstream response body, in bytes. Default 1 MB if not configured.
        public int MaxResponse { get; set; } = 1_048_576;
    }
}
