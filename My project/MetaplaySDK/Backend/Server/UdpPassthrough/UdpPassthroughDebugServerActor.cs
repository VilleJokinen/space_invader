// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.UdpPassthrough
{
    /// <summary>
    /// Debug server for debugging UDP connectivity. Helper python script:
    /// <code>
    /// import socket
    /// def ask(msg, ip, port):
    ///     try:
    ///         s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    ///         s.sendto(msg.encode("utf-8"), (ip, port))
    ///         s.settimeout(5)
    ///         (reply, addr) = s.recvfrom(4096)
    ///         print("reply: ", reply.decode("utf-8"))
    ///     finally:
    ///         s.close()
    ///
    /// host = input("host:")
    /// port = int(input("port:"))
    /// ipv4 = socket.gethostbyname(host)
    ///
    /// ask("whoami", ipv4, port)
    /// </code>
    /// </summary>
    class UdpPassthroughDebugServerActor : UdpPassthroughHostActorBase
    {
        UdpClient _socket;
        int _port;

        public UdpPassthroughDebugServerActor(EntityId entityId) : base(entityId)
        {
        }

        protected override Task InitializeSocketAsync(int port)
        {
            _socket = new UdpClient(port);
            _port = port;
            return Task.CompletedTask;
        }

        protected override async Task ServeAsync(CancellationToken ct)
        {
            UdpPassthroughOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<UdpPassthroughOptions>();
            byte[] help = Encoding.UTF8.GetBytes("help");
            byte[] whoami = Encoding.UTF8.GetBytes("whoami");
            byte[] datain = Encoding.UTF8.GetBytes("datain");
            byte[] dataout = Encoding.UTF8.GetBytes("dataout");
            byte[] sleep = Encoding.UTF8.GetBytes("sleep");
            byte[] ping = Encoding.UTF8.GetBytes("ping");

            for (;;)
            {
                UdpReceiveResult msg = await _socket.ReceiveAsync(ct);
                byte[] reply;
                Memory<byte> suffix;

                if (help.AsSpan().SequenceEqual(msg.Buffer))
                {
                    reply = Encoding.UTF8.GetBytes("""
                        Metaplay UDP debug server
                        commands:
                            help - print help
                            whoami - print peer information
                            datain|x* - print the number of bytes in the packet for MTU testing. Example: datainxyz => "got 9 bytes"
                            dataout|N - print the number of bytes requested for MTU testing. Example: dataout10 => "xxxxxxxxxx" (10 'x')
                            sleep|N - pause the UDP socket reader for N millisecods, for message buffer testing. Example: sleep100 => ok
                            ping|x* - reply with the pong|x* message
                        """);
                }
                else if (TryMatchPrefix(msg.Buffer, whoami, out suffix) && suffix.Length == 0)
                {
                    reply = Encoding.UTF8.GetBytes(FormattableString.Invariant($"You are {msg.RemoteEndPoint}. I am {Dns.GetHostName()}, entity {_entityId}, local port {_port}. LB is \"{options.PublicFullyQualifiedDomainName}\".\n"));
                }
                else if (TryMatchPrefix(msg.Buffer, datain, out _))
                {
                    reply = Encoding.UTF8.GetBytes(FormattableString.Invariant($"The received packet contains {msg.Buffer.Length} bytes (including the datain prefix).\n"));
                }
                else if (TryMatchPrefix(msg.Buffer, dataout, out suffix))
                {
                    try
                    {
                        string lenStr = Encoding.ASCII.GetString(suffix.Span);
                        int numBytes = int.Parse(lenStr, CultureInfo.InvariantCulture);
                        if (numBytes <= 0)
                            throw new InvalidOperationException();

                        if (numBytes > 4096)
                            reply = Encoding.UTF8.GetBytes("4kb limit.\n");
                        else
                        {
                            // reply with x
                            reply = new byte[numBytes];
                            reply.AsSpan().Fill(120);
                        }
                    }
                    catch
                    {
                        reply = Encoding.UTF8.GetBytes("Bad syntax.\n");
                    }
                }
                else if (TryMatchPrefix(msg.Buffer, sleep, out suffix))
                {
                    try
                    {
                        string lenStr = Encoding.ASCII.GetString(suffix.Span);
                        int milliseconds = int.Parse(lenStr, CultureInfo.InvariantCulture);
                        if (milliseconds <= 0)
                            throw new InvalidOperationException();

                        if (milliseconds > 10000)
                            reply = Encoding.UTF8.GetBytes("10 second limit.\n");
                        else
                        {
                            await Task.Delay(milliseconds);
                            reply = Encoding.UTF8.GetBytes("sleep done.\n");
                        }
                    }
                    catch
                    {
                        reply = Encoding.UTF8.GetBytes("Bad syntax.\n");
                    }
                }
                else if (TryMatchPrefix(msg.Buffer, ping, out suffix))
                {
                    reply = new byte[4 + suffix.Length];
                    Encoding.UTF8.GetBytes("pong").CopyTo(reply, 0);
                    suffix.CopyTo(reply.AsMemory(4));
                }
                else
                {
                    reply = Encoding.UTF8.GetBytes("Metaplay UDP debug server: unknown command: Try 'help'.\n");
                }

                _socket.Send(reply, endPoint: msg.RemoteEndPoint);
            }
        }

        static bool TryMatchPrefix(byte[] buffer, byte[] prefix, out Memory<byte> suffix)
        {
            if (buffer.Length >= prefix.Length && prefix.AsSpan().SequenceEqual(buffer.AsSpan().Slice(0, prefix.Length)))
            {
                suffix = buffer.AsMemory().Slice(prefix.Length);
                return true;
            }
            suffix = default;
            return false;
        }
    }
}
