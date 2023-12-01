// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#define UNITY_WEBGL_BUILD
#endif

using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Core.Network
{
    [MetaSerializable]
    public class SocketProbeStep
    {
        [MetaMember(1)] public bool         IsSuccess   { get; private set; }
        [MetaMember(2)] public MetaDuration Elapsed     { get; private set; }
        [MetaMember(3)] public string       Error       { get; private set; } // \todo [petri] make strongly typed

        public SocketProbeStep() { }
        private SocketProbeStep(bool isSuccess, MetaDuration elapsed, string error) { IsSuccess = isSuccess; Elapsed = elapsed; Error = error; }

        public static SocketProbeStep CreateSuccess(MetaDuration elapsed) => new SocketProbeStep(isSuccess: true, elapsed, error: null);
        public static SocketProbeStep CreateFailure(MetaDuration elapsed, string errorStr) => new SocketProbeStep(isSuccess: false, elapsed, TrimErrorMessage(errorStr));

        public static string TrimErrorMessage(string errorStr) =>
            errorStr.Replace("\u0000", "").Trim(); // \note Unity error messages contain null bytes (up to power-of-two length), so trim them away
    }

    [MetaSerializable]
    public class SocketProbeResult
    {
        [MetaMember(1)] public SocketProbeStep  ResolveDnsStatus    { get; set; }
        [MetaMember(2)] public SocketProbeStep  TcpHandshakeStatus  { get; set; }
        [MetaMember(3)] public SocketProbeStep  TlsHandshakeStatus  { get; set; }
        [MetaMember(4)] public SocketProbeStep  TransferStatus      { get; set; }
        [MetaMember(5)] public SocketProbeStep  ProtocolStatus      { get; set; }

        public SocketProbeResult() { }

        public string ToEncodedString()
        {
            if (ResolveDnsStatus == null)
                return "dp";
            else if (!ResolveDnsStatus.IsSuccess)
                return "df";
            else if (TcpHandshakeStatus == null)
                return "tp";
            else if (!TcpHandshakeStatus.IsSuccess)
                return "tf";
            else if (TlsHandshakeStatus == null)
                return "sp";
            else if (!TlsHandshakeStatus.IsSuccess)
                return "sf";
            else if (TransferStatus == null)
                return "xp";
            else if (!TransferStatus.IsSuccess)
                return "xf";
            else if (ProtocolStatus == null)
                return "pp";
            else if (!ProtocolStatus.IsSuccess)
                return "pf";
            else
                return Invariant($"s{TransferStatus.Elapsed.Milliseconds/100}");
        }

        public string ToPlayerFacingString()
        {
            if (ResolveDnsStatus == null)
                return "dns pending";
            else if (!ResolveDnsStatus.IsSuccess)
                return "dns failed";
            else if (TcpHandshakeStatus == null)
                return "tcp pending";
            else if (!TcpHandshakeStatus.IsSuccess)
                return "tcp failed";
            else if (TlsHandshakeStatus == null)
                return "tls pending";
            else if (!TlsHandshakeStatus.IsSuccess)
                return "tls failed";
            else if (TransferStatus == null)
                return "transfer pending";
            else if (!TransferStatus.IsSuccess)
                return "transfer failed";
            else if (ProtocolStatus == null)
                return "transfer pending";
            else if (!ProtocolStatus.IsSuccess)
                return "protocol failed";
            else
                return Invariant($"success ({ProtocolStatus.Elapsed.Milliseconds}ms)");
        }

        public void AddAnalyticsAttribs(Dictionary<string, object> result, string prefix)
        {
            string statusName = $"{prefix}_status";
            string elapsedName = $"{prefix}_elapsed_ms";

            if (ResolveDnsStatus == null)
                result.Add(statusName, "dns_pending");
            else if (!ResolveDnsStatus.IsSuccess)
            {
                result.Add(statusName, "dns_failed");
                result.Add(elapsedName, (int)ResolveDnsStatus.Elapsed.Milliseconds);
            }
            else if (TcpHandshakeStatus == null)
                result.Add(statusName, "tcp_pending");
            else if (!TcpHandshakeStatus.IsSuccess)
            {
                result.Add(statusName, "tcp_failed");
                result.Add(elapsedName, (int)TcpHandshakeStatus.Elapsed.Milliseconds);
            }
            else if (TlsHandshakeStatus == null)
                result.Add(statusName, "tls_pending");
            else if (!TlsHandshakeStatus.IsSuccess)
            {
                result.Add(statusName, "tls_failed");
                result.Add(elapsedName, (int)TlsHandshakeStatus.Elapsed.Milliseconds);
            }
            else if (TransferStatus == null)
                result.Add(statusName, "transfer_pending");
            else if (!TransferStatus.IsSuccess)
            {
                result.Add(statusName, "transfer_failed");
                result.Add(elapsedName, (int)TransferStatus.Elapsed.Milliseconds);
            }
            else if (ProtocolStatus == null)
                result.Add(statusName, "protocol_pending");
            else if (!ProtocolStatus.IsSuccess)
            {
                result.Add(statusName, "protocol_failed");
                result.Add(elapsedName, (int)ProtocolStatus.Elapsed.Milliseconds);
            }
            else
            {
                result.Add(statusName, "success");
                result.Add(elapsedName, (int)ProtocolStatus.Elapsed.Milliseconds);
            }
        }
    }

    [MetaSerializable]
    public class HttpRequestProbeStatus
    {
        [MetaMember(1)] public bool         IsSuccess   { get; private set; }
        [MetaMember(2)] public MetaDuration Elapsed     { get; private set; }
        [MetaMember(3)] public string       Response    { get; private set; }
        [MetaMember(4)] public string       Error       { get; private set; }

        public HttpRequestProbeStatus() { }
        public HttpRequestProbeStatus(bool isSuccess, MetaDuration elapsed, string response, string error) { IsSuccess = isSuccess; Elapsed = elapsed; Response = response; Error = error; }

        public string ToEncodedString() =>
            IsSuccess ? Invariant($"s{Elapsed.Milliseconds/100}") : Invariant($"f{Elapsed.Milliseconds/100}");
    }

    [MetaSerializable]
    public class HttpRequestProbeResult
    {
        [MetaMember(1)] public HttpRequestProbeStatus Status { get; set; }

        public string ToPlayerFacingString()
        {
            if (Status == null)
                return "pending";
            else if (!Status.IsSuccess)
                return Invariant($"failed ({Status.Elapsed.Milliseconds}ms)");
            else
                return Invariant($"success ({Status.Elapsed.Milliseconds}ms)");
        }

        public string ToEncodedString()
        {
            if (Status == null)
                return "p";
            else
                return Status.ToEncodedString();
        }

        public void AddAnalyticsAttribs(Dictionary<string, object> result, string prefix)
        {
            string statusName = $"{prefix}_status";
            string elapsedName = $"{prefix}_elapsed_ms";

            if (Status == null)
                result.Add(statusName, "pending");
            else
            {
                result.Add(statusName, Status.IsSuccess ? "success" : "failed");
                result.Add(elapsedName, (int)Status.Elapsed.Milliseconds);
            }
        }
    }

    [MetaSerializable]
    public class GameServerEndpointResult
    {
        [MetaMember(1)] public string                                       Hostname;
        [MetaMember(3)] public OrderedDictionary<int, SocketProbeResult>    Gateways;

        public GameServerEndpointResult() { }
        public GameServerEndpointResult(string hostname, List<int> ports)
        {
            Hostname = hostname;
            Gateways = new OrderedDictionary<int, SocketProbeResult>();
            foreach (int port in ports)
                Gateways[port] = new SocketProbeResult();
        }
    }

    [MetaSerializable]
    public class NetworkDiagnosticReport
    {
        [MetaMember(1)] public GameServerEndpointResult GameServerIPv4      { get; private set; }
        [MetaMember(2)] public GameServerEndpointResult GameServerIPv6      { get; private set; }
        [MetaMember(3)] public SocketProbeResult        GameCdnSocketIPv4   { get; private set; }
        [MetaMember(4)] public SocketProbeResult        GameCdnSocketIPv6   { get; private set; }
        [MetaMember(5)] public HttpRequestProbeResult   GameCdnHttpIPv4     { get; private set; }
        [MetaMember(6)] public HttpRequestProbeResult   GameCdnHttpIPv6     { get; private set; }
        [MetaMember(7)] public SocketProbeResult        GoogleComIPv4       { get; private set; }
        [MetaMember(8)] public SocketProbeResult        GoogleComIPv6       { get; private set; }
        [MetaMember(9)] public SocketProbeResult        MicrosoftComIPv4    { get; private set; }
        [MetaMember(10)] public SocketProbeResult       MicrosoftComIPv6    { get; private set; }
        [MetaMember(11)] public SocketProbeResult       AppleComIPv4        { get; private set; }
        [MetaMember(12)] public SocketProbeResult       AppleComIPv6        { get; private set; }

        public NetworkDiagnosticReport() { }
        public NetworkDiagnosticReport(string gameServerHostnameIPv4, string gameServerHostnameIPv6, List<int> gameServerPorts, string cdnHostnameIPv4, string cdnHostnameIPv6)
        {
            GameServerIPv4      = new GameServerEndpointResult(gameServerHostnameIPv4, gameServerPorts);
            GameServerIPv6      = new GameServerEndpointResult(gameServerHostnameIPv6, gameServerPorts);
            GameCdnSocketIPv4   = new SocketProbeResult();
            GameCdnSocketIPv6   = new SocketProbeResult();
            GameCdnHttpIPv4     = new HttpRequestProbeResult();
            GameCdnHttpIPv6     = new HttpRequestProbeResult();
            GoogleComIPv4       = new SocketProbeResult();
            GoogleComIPv6       = new SocketProbeResult();
            MicrosoftComIPv4    = new SocketProbeResult();
            MicrosoftComIPv6    = new SocketProbeResult();
            AppleComIPv4        = new SocketProbeResult();
            AppleComIPv6        = new SocketProbeResult();
        }

        string Decorate(string tag, string input, bool richText) =>
            richText ? $"<{tag}>{input}</{tag}>" : input;

        char GatewayName(int index) => (char)('A' + index);

        public string ToPlayerFacingString(bool richText)
        {
            List<string> lines = new List<string>();

            lines.Add(Decorate("b", "Game Servers:", richText));
            foreach (var ((port, gw), index) in GameServerIPv4.Gateways.Select((i, n) => (i, n)))
                lines.Add(Invariant($"[{GatewayName(index)}/ipv4] {gw.ToPlayerFacingString()}"));
            foreach (var ((port, gw), index) in GameServerIPv6.Gateways.Select((i, n) => (i, n)))
                lines.Add(Invariant($"[{GatewayName(index)}/ipv6] {gw.ToPlayerFacingString()}"));

            lines.Add("");

            lines.Add(Decorate("b", "Game CDN:", richText));
            lines.Add($"[C/ipv4] {GameCdnSocketIPv4.ToPlayerFacingString()}");
            lines.Add($"[C/ipv6] {GameCdnSocketIPv6.ToPlayerFacingString()}");
            lines.Add($"[H/ipv4] {GameCdnHttpIPv4.ToPlayerFacingString()}");
            lines.Add($"[H/ipv6] {GameCdnHttpIPv6.ToPlayerFacingString()}");

            lines.Add("");

            lines.Add(Decorate("b", "Internet:", richText));
            lines.Add($"[G/ipv4] {GoogleComIPv4.ToPlayerFacingString()}");
            lines.Add($"[G/ipv6] {GoogleComIPv6.ToPlayerFacingString()}");
            lines.Add($"[M/ipv4] {MicrosoftComIPv4.ToPlayerFacingString()}");
            lines.Add($"[M/ipv6] {MicrosoftComIPv6.ToPlayerFacingString()}");
            lines.Add($"[A/ipv4] {AppleComIPv4.ToPlayerFacingString()}");
            lines.Add($"[A/ipv6] {AppleComIPv6.ToPlayerFacingString()}");

            return string.Join("\n", lines);
        }

        public string ToEncodedString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("GS:");
            sb.Append(string.Join(".", GameServerIPv4.Gateways.Select((gw, index) => Invariant($"{GatewayName(index)}4{gw.Value.ToEncodedString()}"))));
            sb.Append(".");
            sb.Append(string.Join(".", GameServerIPv6.Gateways.Select((gw, index) => Invariant($"{GatewayName(index)}6{gw.Value.ToEncodedString()}"))));

            sb.Append("\n");

            sb.Append("CDN:");
            sb.Append($"S4{GameCdnSocketIPv4.ToEncodedString()}.");
            sb.Append($"S6{GameCdnSocketIPv6.ToEncodedString()}.");
            sb.Append($"H4{GameCdnHttpIPv4.ToEncodedString()}.");
            sb.Append($"H6{GameCdnHttpIPv6.ToEncodedString()}");

            sb.Append(" ");

            sb.Append("I:");
            sb.Append($"G4{GoogleComIPv4.ToEncodedString()}.");
            sb.Append($"G6{GoogleComIPv6.ToEncodedString()}.");
            sb.Append($"M4{MicrosoftComIPv4.ToEncodedString()}.");
            sb.Append($"M6{MicrosoftComIPv6.ToEncodedString()}.");
            sb.Append($"A4{AppleComIPv4.ToEncodedString()}.");
            sb.Append($"A6{AppleComIPv6.ToEncodedString()}");

            return sb.ToString();
        }

        public Dictionary<string, object> GetAnalyticsParams()
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            foreach ((int port, SocketProbeResult probe) in GameServerIPv4.Gateways)
                probe.AddAnalyticsAttribs(result, Invariant($"gs_ipv4_{port}"));

            foreach ((int port, SocketProbeResult probe) in GameServerIPv6.Gateways)
                probe.AddAnalyticsAttribs(result, Invariant($"gs_ipv6_{port}"));

            GameCdnSocketIPv4.AddAnalyticsAttribs(result, "cdn_socket_ipv4");
            GameCdnSocketIPv6.AddAnalyticsAttribs(result, "cdn_socket_ipv6");
            GameCdnHttpIPv4.AddAnalyticsAttribs(result, "cdn_http_ipv4");
            GameCdnHttpIPv6.AddAnalyticsAttribs(result, "cdn_http_ipv6");

            GoogleComIPv4.AddAnalyticsAttribs(result, "google_ipv4");
            GoogleComIPv6.AddAnalyticsAttribs(result, "google_ipv6");
            MicrosoftComIPv4.AddAnalyticsAttribs(result, "microsoft_ipv4");
            MicrosoftComIPv6.AddAnalyticsAttribs(result, "microsoft_ipv6");
            AppleComIPv4.AddAnalyticsAttribs(result, "apple_ipv4");
            AppleComIPv6.AddAnalyticsAttribs(result, "apple_ipv6");

            return result;
        }
    }

    /// <summary>
    ///
    /// </summary>
    public static class NetworkDiagnostics
    {
        public static NetworkDiagnosticReport CreateDummyReport()
        {
            // Create report & fill in some dummy data
            NetworkDiagnosticReport report = new NetworkDiagnosticReport(
                "prod.game.com",
                "prod-ipv6.game.com",
                new List<int> { 9339, 443, 123 },
                "prod-assets.game.com",
                "prod-assets-ipv6.game.com");

            // First probe successful
            report.GameServerIPv4.Gateways[9339].ResolveDnsStatus      = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(182));
            report.GameServerIPv4.Gateways[9339].TcpHandshakeStatus    = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(221));
            report.GameServerIPv4.Gateways[9339].TlsHandshakeStatus    = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(255));
            report.GameServerIPv4.Gateways[9339].TransferStatus        = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(311));
            report.GameServerIPv4.Gateways[9339].ProtocolStatus        = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(441));

            // Second probe failure
            report.GameServerIPv4.Gateways[443].ResolveDnsStatus      = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(182));
            report.GameServerIPv4.Gateways[443].TcpHandshakeStatus    = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(221));
            report.GameServerIPv4.Gateways[443].TlsHandshakeStatus    = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(255), "Bad bad TLS handshake");
            report.GameServerIPv4.Gateways[443].TransferStatus        = null;
            report.GameServerIPv4.Gateways[443].ProtocolStatus        = null;

            // Third probe still running
            report.GameServerIPv4.Gateways[123].ResolveDnsStatus      = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(182));
            report.GameServerIPv4.Gateways[123].TcpHandshakeStatus    = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(221));
            report.GameServerIPv4.Gateways[123].TlsHandshakeStatus    = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(255));
            report.GameServerIPv4.Gateways[123].TransferStatus        = null;
            report.GameServerIPv4.Gateways[123].ProtocolStatus        = null;

            return report;
        }

        static bool CertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // Always accept certificate, we just want to encrypt the traffic
            // \todo [petri] would be nice to get full certificate check in place as well
            return true;
        }

        static async Task ProbeGameServer(string hostname, int port, AddressFamily addrFamily, bool useTls, SocketProbeResult result, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            MetaTime connectionStartedAt = MetaTime.Now;

            // Resolve DNS (using correct address family)
            IPAddress address;
            try
            {
                IPHostEntry ipHost = await Dns.GetHostEntryAsync(hostname).WithCancelAsync(ct).ConfigureAwaitFalse();
                address = ipHost.AddressList.FirstOrDefault(addr => addr.AddressFamily == addrFamily);
                if (address != null)
                    result.ResolveDnsStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
                else
                {
                    result.ResolveDnsStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), "DNS resolve failed: no matching results");
                    return;
                }
            }
            catch (Exception ex)
            {
                result.ResolveDnsStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            // Open socket to target server
            Socket socket;
            try
            {
                socket = new Socket(addrFamily, SocketType.Stream, ProtocolType.Tcp);
            }
            catch (Exception ex)
            {
                result.TcpHandshakeStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            try
            {
                Task connectTask = socket.ConnectAsync(address, port);
                try
                {
                    await connectTask.WithCancelAsync(ct).ConfigureAwaitFalse();
                }
                catch (OperationCanceledException)
                {
                    // ConnectAsync is not cancelable. To prevent an unobserved exception from accessing a disposed object
                    // let the task finish and only dispose the socket afterwards.
                    if (!connectTask.IsCompleted)
                    {
                        Socket socketToDispose = socket;
                        socket = null;
                        _ = connectTask.ContinueWithCtx(task =>
                        {
                            // Observe any exceptions
                            if (task.IsFaulted)
                                _ = task.Exception;
                            socketToDispose.Dispose();
                        });
                    }
                    throw;
                }
                result.TcpHandshakeStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                if (socket != null)
                    socket.Dispose();
                result.TcpHandshakeStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            // Handshake TLS
            NetworkStream tcpStream = new NetworkStream(socket, ownsSocket: true);
            Stream stream;
            if (useTls)
            {
                SslStream sslStream = new SslStream(tcpStream, leaveInnerStreamOpen: false, CertificateValidationCallback, null);
                try
                {
                    Task authenticateTask = sslStream.AuthenticateAsClientAsync(hostname);
                    try
                    {
                        await authenticateTask.WithCancelAsync(ct).ConfigureAwaitFalse();
                    }
                    catch (OperationCanceledException)
                    {
                        // AuthenticateAsClientAsync is not cancelable (the overload with CancellationToken exists but doesn't work
                        // correctly in the Mono implementation). To prevent an unobserved exception from accessing a disposed object
                        // let the task finish and only dispose the stream afterwards.
                        if (!authenticateTask.IsCompleted)
                        {
                            SslStream streamToDispose = sslStream;
                            sslStream = null;
                            _ = authenticateTask.ContinueWithCtx(task =>
                            {
                                // Observe any exceptions
                                if (task.IsFaulted)
                                    _ = task.Exception;
                                streamToDispose.Dispose();
                            });
                        }
                        throw;

                    }
                    result.TlsHandshakeStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
                    stream = sslStream;
                }
                catch (Exception ex)
                {
                    if (sslStream != null)
                        sslStream.Dispose();
                    result.TlsHandshakeStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                    return;
                }
            }
            else
            {
                result.TlsHandshakeStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds)); // \todo [petri] skipped?
                stream = tcpStream;
            }

            // Read for protocol header
            byte[] protocolHeader = new byte[WireProtocol.ProtocolHeaderSize];
            try
            {
                await stream.ReadAsync(protocolHeader, 0, protocolHeader.Length, ct).ConfigureAwaitFalse();
                result.TransferStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                stream.Dispose();
                result.TransferStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            // Check protocol header & status
            try
            {
                ProtocolStatus status = WireProtocol.ParseProtocolHeader(protocolHeader, 0, MetaplayCore.Options.GameMagic);
                if (status == ProtocolStatus.ClusterRunning)
                    result.ProtocolStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
                else
                    result.ProtocolStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), status.ToString());
            }
            catch (Exception ex)
            {
                stream.Dispose();
                result.ProtocolStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            // Success!

            // Abandon the stream in the background
            _ = MetaTask.Run(async () =>
            {
                MetaTime now = MetaTime.Now;
                Handshake.ClientAbandon abandon = new Handshake.ClientAbandon(
                    connectionStartedAt:    connectionStartedAt,
                    connectionAbandonedAt:  now,
                    abandonedCompletedAt:   now,
                    source:                 Handshake.ClientAbandon.AbandonSource.NetworkProbe);

                byte[] message = MetaSerialization.SerializeTagged<MetaMessage>(abandon, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
                byte[] framed = new byte[WireProtocol.PacketHeaderSize + message.Length];

                WireProtocol.EncodePacketHeader(new WirePacketHeader(WirePacketType.Message, WirePacketCompression.None, message.Length), framed);
                Buffer.BlockCopy(src: message, 0, dst: framed, WireProtocol.PacketHeaderSize, message.Length);

                try
                {
                    await stream.WriteAsync(framed, 0, framed.Length);
                    stream.Close();
                }
                finally
                {
                    stream.Dispose();
                }
            });
        }

        static async Task ProbeHttpServer(string hostname, string path, AddressFamily addrFamily, bool useTls, SocketProbeResult result, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();

            // Resolve DNS (using correct address family)
            IPAddress address;
            try
            {
                IPHostEntry ipHost = await Dns.GetHostEntryAsync(hostname).WithCancelAsync(ct).ConfigureAwaitFalse();
                address = ipHost.AddressList.FirstOrDefault(addr => addr.AddressFamily == addrFamily);
                if (address == null)
                {
                    result.ResolveDnsStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), "DNS resolve failed: no matching results");
                    return;
                }
                result.ResolveDnsStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                result.ResolveDnsStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            // Open socket to target server
            Socket socket = null;
            try
            {
                socket = new Socket(addrFamily, SocketType.Stream, ProtocolType.Tcp);

                // \note[jarkko]: Note that if WithCancelAsync triggers, we pull the rug under the ConnectAsync by disposing the socket
                await socket.ConnectAsync(address, port: 443).WithCancelAsync(ct).ConfigureAwaitFalse();
                result.TcpHandshakeStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                socket?.Dispose();
                result.TcpHandshakeStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            // Handshake TLS
            NetworkStream tcpStream = new NetworkStream(socket, ownsSocket: true);
            Stream stream;
            if (useTls)
            {
                SslStream sslStream = new SslStream(tcpStream, false, CertificateValidationCallback, null);
                try
                {
                    // \note[jarkko]: Note that if AuthenticateAsClientAsync triggers, we pull the rug under the ConnectAsync by disposing the sosslStreamcket
                    await sslStream.AuthenticateAsClientAsync(hostname).WithCancelAsync(ct).ConfigureAwaitFalse();
                    result.TlsHandshakeStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
                    stream = sslStream;
                }
                catch (Exception ex)
                {
                    sslStream.Dispose();
                    result.TlsHandshakeStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                    return;
                }
            }
            else
            {
                result.TlsHandshakeStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds)); // \todo [petri] skipped?
                stream = tcpStream;
            }

            // Send request & receive response
            const int ResponseSize = 5; // "HTTP/"
            byte[] response = new byte[ResponseSize];
            try
            {
                // DEBUG DEBUG: Force timeout
                //await stream.ReadAsync(new byte[4], 0, 4).WithCancelAsync(ct).ConfigureAwaitFalse();

                // Write simple request
                byte[] request = Encoding.UTF8.GetBytes($"HEAD {path} HTTP/1.0\r\n\r\n");
                await stream.WriteAsync(request, 0, request.Length, ct).ConfigureAwaitFalse();

                // Read response
                if (await stream.ReadAsync(response, 0, ResponseSize, ct).ConfigureAwaitFalse() != ResponseSize)
                {
                    stream.Dispose();
                    result.TransferStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), $"Read timeout");
                    return;
                }
                result.TransferStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                stream.Dispose();
                result.TransferStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            try
            {
                string str = Encoding.UTF8.GetString(response);
                if (str != "HTTP/")
                {
                    stream.Dispose();
                    result.ProtocolStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), $"Invalid response: {str}");
                    return;
                }
                result.ProtocolStatus = SocketProbeStep.CreateSuccess(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                stream.Dispose();
                result.ProtocolStatus = SocketProbeStep.CreateFailure(MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), ex.Message);
                return;
            }

            // Success!

            stream.Dispose();
        }

        static async Task ProbeHttpRequest(string hostname, string path, bool useTls, HttpRequestProbeResult result, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
	            string protocol = useTls ? "https" : "http";
	            string uri      = $"{protocol}://{hostname}{path}";

	            using (MetaHttpResponse response = await MetaHttpClient.DefaultInstance.GetAsync(uri, ct).ConfigureAwaitFalse())
                {
	                // \note We don't actually check the contents of the response, just that we get one
	                result.Status = new HttpRequestProbeStatus(isSuccess: true, MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds),
	                                                           response: response.ToString(), error: null);
                }
            }
            catch (Exception ex)
            {
                result.Status = new HttpRequestProbeStatus(isSuccess: false, MetaDuration.FromMilliseconds(sw.ElapsedMilliseconds), response: null, error: ex.Message);
            }
        }
#if !UNITY_WEBGL_BUILD

        public static (NetworkDiagnosticReport, Task) GenerateReport(string gameServerHost4, string gameServerHost6, List<int> gameServerPorts, bool gameServerUseTls, string cdnHostname4, string cdnHostname6, TimeSpan? timeout = null)
        {
            timeout = timeout ?? TimeSpan.FromSeconds(5);
            CancellationTokenSource cts = new CancellationTokenSource();

            NetworkDiagnosticReport report = new NetworkDiagnosticReport(gameServerHost4, gameServerHost6, gameServerPorts, cdnHostname4, cdnHostname6);

            // All probe tasks
            List<Task> tasks = new List<Task>();

            // Game server IPv4 probes
            foreach ((int port, SocketProbeResult gateway) in report.GameServerIPv4.Gateways)
                tasks.Add(MetaTask.Run(() => ProbeGameServer(gameServerHost4, port, AddressFamily.InterNetwork, useTls: gameServerUseTls, gateway, cts.Token), cts.Token));

            // Game server IPv6 probes
            foreach ((int port, SocketProbeResult gateway) in report.GameServerIPv6.Gateways)
                tasks.Add(MetaTask.Run(() => ProbeGameServer(gameServerHost6, port, AddressFamily.InterNetworkV6, useTls: gameServerUseTls, gateway, cts.Token), cts.Token));

            // CDN
            if (!string.IsNullOrEmpty(cdnHostname4))
            {
                tasks.Add(MetaTask.Run(() => ProbeHttpServer(cdnHostname4, "/Connectivity/connectivity-test", AddressFamily.InterNetwork, useTls: true, report.GameCdnSocketIPv4, cts.Token), cts.Token));
                tasks.Add(MetaTask.Run(() => ProbeHttpRequest(cdnHostname4, "/Connectivity/connectivity-test", useTls: true, report.GameCdnHttpIPv4, cts.Token), cts.Token));
            }
            if (!string.IsNullOrEmpty(cdnHostname6))
            {
                tasks.Add(MetaTask.Run(() => ProbeHttpServer(cdnHostname6, "/Connectivity/connectivity-test", AddressFamily.InterNetworkV6, useTls: true, report.GameCdnSocketIPv6, cts.Token), cts.Token));
                tasks.Add(MetaTask.Run(() => ProbeHttpRequest(cdnHostname6, "/Connectivity/connectivity-test", useTls: true, report.GameCdnHttpIPv6, cts.Token), cts.Token));
            }

            // www.google.com
            tasks.Add(MetaTask.Run(() => ProbeHttpServer("www.google.com", "/", AddressFamily.InterNetwork, useTls: true, report.GoogleComIPv4, cts.Token), cts.Token));
            tasks.Add(MetaTask.Run(() => ProbeHttpServer("www.google.com", "/", AddressFamily.InterNetworkV6, useTls: true, report.GoogleComIPv6, cts.Token), cts.Token));

            // www.microsoft.com
            tasks.Add(MetaTask.Run(() => ProbeHttpServer("www.microsoft.com", "/", AddressFamily.InterNetwork, useTls: true, report.MicrosoftComIPv4, cts.Token), cts.Token));
            tasks.Add(MetaTask.Run(() => ProbeHttpServer("www.microsoft.com", "/", AddressFamily.InterNetworkV6, useTls: true, report.MicrosoftComIPv6, cts.Token), cts.Token));

            // www.apple.com
            tasks.Add(MetaTask.Run(() => ProbeHttpServer("www.apple.com", "/", AddressFamily.InterNetwork, useTls: true, report.AppleComIPv4, cts.Token), cts.Token));
            tasks.Add(MetaTask.Run(() => ProbeHttpServer("www.apple.com", "/", AddressFamily.InterNetworkV6, useTls: true, report.AppleComIPv6, cts.Token), cts.Token));

            // Cancel tasks after timeout
            _ = MetaTask.Run(async () =>
            {
                await MetaTask.Delay(timeout.Value);
                cts.Cancel();
            });

            return (report, Task.WhenAll(tasks));
        }
#else
        public static (NetworkDiagnosticReport, Task) GenerateReport(
            string gameServerHost4,
            string gameServerHost6,
            List<int> gameServerPorts,
            bool gameServerUseTls,
            string cdnHostname4,
            string cdnHostname6,
            TimeSpan? timeout = null)
        {
            // \todo [nomi] make this work.
            return (CreateDummyReport(), Task.CompletedTask);
        }
#endif
    }
}
