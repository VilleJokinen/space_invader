// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// An address of an gateway to the server. This is commonly the address of a load balancer.
    /// </summary>
    [MetaSerializable]
    public class ServerGateway
    {
        /// <summary>
        /// If null or empty, defaults to Primary gateway
        /// </summary>
        [MetaMember(1)] public string   ServerHost  { get; private set; }
        [MetaMember(2)] public int      ServerPort  { get; private set; }
        [MetaMember(3)] public bool     EnableTls   { get; private set; }

        public ServerGateway() { }
        public ServerGateway(string serverHost, int serverPort, bool enableTls)
        {
            ServerHost  = serverHost;
            ServerPort  = serverPort;
            EnableTls   = enableTls;
        }
    }

    /// <summary>
    /// Fully specifies a server endpoint that the client can connect to (including all auxiliary services).
    /// </summary>
    [MetaSerializable]
    public class ServerEndpoint
    {
        // DefaultServerPort is 9380 for WebGL builds, and 9339 otherwise.
#if UNITY_WEBGL && !UNITY_EDITOR
        const int DefaultServerPort = 9380;
#else
        const int DefaultServerPort = 9339;
#endif

        [MetaMember(1)] public string               ServerHost         { get; private set; }
        [MetaMember(2)] public int                  ServerPort         { get; private set; }
        [MetaMember(3)] public bool                 EnableTls          { get; private set; }
        [MetaMember(4)] public string               CdnBaseUrl         { get; private set; }
        // Backup gateway specifications, EnableTLS field not used
        [MetaMember(5)] List<ServerGateway>         BackupGatewaySpecs { get; set; } = new List<ServerGateway>();

        public bool IsOfflineMode => string.IsNullOrEmpty(ServerHost);
        public ServerGateway PrimaryGateway => new ServerGateway(ServerHost, ServerPort, EnableTls);
        public IEnumerable<ServerGateway> BackupGateways => BackupGatewaySpecs.Select(
            x => new ServerGateway(string.IsNullOrEmpty(x.ServerHost) ? ServerHost : x.ServerHost, x.ServerPort, EnableTls));

        public static ServerEndpoint CreateOffline() =>
            new ServerEndpoint();

        public static ServerEndpoint CreateLocalhost(int serverPort = DefaultServerPort) =>
            new ServerEndpoint("localhost", serverPort, enableTls: false, cdnBaseUrl: "http://localhost:5552");

        public static ServerEndpoint CreateCloud(string serverHost, int serverPort = DefaultServerPort)
        {
            int ndx = serverHost.IndexOf(".", StringComparison.Ordinal);
            if (ndx == -1)
                throw new ArgumentException("Invalid server host, must have a dot '.' in it", nameof(serverHost));
            string cdnBaseUrl = "https://" + serverHost.Substring(0, ndx) + "-assets" + serverHost.Substring(ndx);
            DebugLog.Info("CdnBaseUrl: {0}", cdnBaseUrl);

            return new ServerEndpoint(serverHost, serverPort, enableTls: true, cdnBaseUrl);
        }

        public ServerEndpoint() { }
        public ServerEndpoint(string serverHost, int serverPort, bool enableTls, string cdnBaseUrl)
        {
            ServerHost  = serverHost;
            ServerPort  = serverPort;
            EnableTls   = enableTls;
            CdnBaseUrl  = cdnBaseUrl;
        }
        public ServerEndpoint(string serverHost, int serverPort, bool enableTls, string cdnBaseUrl, List<ServerGateway> backupGateways)
        {
            ServerHost         = serverHost;
            ServerPort         = serverPort;
            EnableTls          = enableTls;
            CdnBaseUrl         = cdnBaseUrl;
            BackupGatewaySpecs = backupGateways;
        }

        public static bool operator ==(ServerEndpoint a, ServerEndpoint b)
        {
            if (ReferenceEquals(a, b))
                return true;
            else if (a is null || b is null)
                return false;

            return a.ServerHost == b.ServerHost
                && a.ServerPort == b.ServerPort
                && a.EnableTls == b.EnableTls
                && a.CdnBaseUrl == b.CdnBaseUrl;
        }

        public static bool operator !=(ServerEndpoint a, ServerEndpoint b) => !(a == b);

        public override bool Equals(object obj) => (obj is ServerEndpoint other) ? (this == other) : false;
        public override int GetHashCode() => Util.CombineHashCode(ServerHost.GetHashCode(), ServerPort, EnableTls.GetHashCode(), CdnBaseUrl.GetHashCode());
        public override string ToString() => Invariant($"ServerEndpoint({ServerHost}:{ServerPort}, tls={EnableTls}, cdn={CdnBaseUrl})");
    }
}
