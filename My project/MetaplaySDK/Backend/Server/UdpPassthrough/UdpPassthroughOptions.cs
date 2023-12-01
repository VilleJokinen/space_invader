// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Metaplay.Server.UdpPassthrough
{
    [RuntimeOptions("UdpPassthrough", isStatic: true, "UDP packet passthrough configuration options.")]
    class UdpPassthroughOptions : RuntimeOptionsBase
    {
        [MetaDescription("Is UDP passthrough enabled.")]
        public bool Enabled { get; private set; } = false;

        [MetaDescription("The domain of the publicly visible gateway to the server. This is the server (in direct connect mode) or a loadbalancer (in LB mode). Chosen automatically.")]
        public string PublicFullyQualifiedDomainName { get; private set; }

        [MetaDescription("First port in the the public gateway port range. Only used in cloud.")]
        public int GatewayPortRangeStart { get; private set; }

        [MetaDescription("Last (inclusive) port in the the public gateway port range. Only used in cloud.")]
        public int GatewayPortRangeEnd { get; private set; }

        [MetaDescription("Server port where the server listens locally.")]
        public int LocalServerPort { get; private set; }

        [MetaDescription("If set, a debug server is bound to the port instead of the game actor. Send 'help' for more details.")]
        public bool UseDebugServer { get; private set; }

        public override Task OnLoadedAsync()
        {
            if (Enabled)
            {
                if (IsLocalEnvironment)
                {
                    // On local environment, default to local hostname but allow overriding
                    if (string.IsNullOrEmpty(PublicFullyQualifiedDomainName))
                        PublicFullyQualifiedDomainName = GetLocalhostAddress();

                    if (LocalServerPort == 0)
                        throw new InvalidOperationException("UdpPassthrough:LocalServerPort must be set manually in Local environments if passthrough is enabled.");
                }
                else
                {
                    // On cloud, the infra injects the fields
                    if (string.IsNullOrEmpty(PublicFullyQualifiedDomainName))
                        throw new InvalidOperationException("UdpPassthrough:PublicFullyQualifiedDomainName must be defined by if passthrough is enabled. Is udp passthrough enabled in infra modules?");

                    // Cloud infra should allocate and set port range too.
                    if (GatewayPortRangeStart == 0)
                        throw new InvalidOperationException("UdpPassthrough:GatewayPortRangeStart must be set if passthrough is enabled. Is udp passthrough enabled in infra modules?");
                    if (GatewayPortRangeEnd == 0)
                        throw new InvalidOperationException("UdpPassthrough:GatewayPortRangeEnd must be set if passthrough is enabled. Is udp passthrough enabled in infra modules?");
                    if (LocalServerPort == 0)
                        throw new InvalidOperationException("UdpPassthrough:LocalServerPort must be set if passthrough is enabled. Is udp passthrough enabled in infra modules?");
                }
            }
            return Task.CompletedTask;
        }

        static string GetLocalhostAddress()
        {
            // First IPv4 address, or 127.0.0.1
            IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress addr in entry.AddressList)
            {
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.ToString();
            }
            return "127.0.0.1";
        }
    }
}
