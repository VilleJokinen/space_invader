// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Netlink;
using Metaplay.Cloud.Netlink.Messages.SockDiag;
using Metaplay.Cloud.ProcFs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Metaplay.Cloud.Metrics
{
    partial class SocketCollector
    {
        // This collector reports all values from the current process `netstat`, and
        // the sum of all pending sockets in listen backlogs.
        //
        // For aggregate results we parse /proc/ pseudo filesystem's text files over
        // and over. Instead of opening the file over and over, we only open it once
        // and seek to the beginning.
        //
        // For socket-specific information we could use /proc/*/net/tcp but enumerating
        // all sockets and priting them has a considerable overhead. Instead, we use
        // sock_diag(7) which allows for very specific filtering.
        sealed class Linux : SocketCollector
        {
            class Exporter
            {
                public readonly string  Name;
                public readonly string  Description;
                Prometheus.Gauge        _gauge;
                bool                    _isPublished;

                public Exporter(string name, string description)
                {
                    Name = name;
                    Description = description;
                    _gauge = null;
                    _isPublished = false;
                }

                public void Update(long value)
                {
                    // Intentionally lazy. Metrics become only present after first measurement completes.
                    if (_gauge == null)
                    {
                        _gauge = Prometheus.Metrics.CreateGauge(Name, Description);
                        _isPublished = true;
                    }
                    _gauge.Set((double)value);
                    if (!_isPublished)
                    {
                        _gauge.Publish();
                        _isPublished = true;
                    }
                }
                public void SetUnavailable()
                {
                    if (_isPublished)
                    {
                        _gauge.Unpublish();
                        _isPublished = false;
                    }
                }
            }
            class ProcFsMetric
            {
                Exporter            _exporter;
                ProcFsNetstatReader _sourceFile;
                (string, string)    _sourceId;
                ProcFsSnmp6Reader   _sourceFileIpv6;
                string              _sourceIdIpv6;

                public ProcFsMetric(string name, string description, ProcFsNetstatReader sourceFile, (string, string) sourceId, ProcFsSnmp6Reader sourceFileIpv6 = null, string sourceIdIpv6 = null)
                {
                    _exporter = new Exporter(name, description);
                    _sourceFile = sourceFile;
                    _sourceId = sourceId;
                    _sourceFileIpv6 = sourceFileIpv6;
                    _sourceIdIpv6 = sourceIdIpv6;
                }

                public void Collect()
                {
                    bool hasValue = false;
                    long sum = 0;

                    // Sum both IPv4 source (if available)..
                    if (_sourceFile != null && _sourceFile.TryGetValue(_sourceId.Item1, _sourceId.Item2, out long source1))
                    {
                        sum += source1;
                        hasValue = true;
                    }

                    // .. and the IPv6 source
                    if (_sourceFileIpv6 != null && _sourceFileIpv6.TryGetValue(_sourceIdIpv6, out long source2))
                    {
                        sum += source2;
                        hasValue = true;
                    }

                    if (hasValue)
                        _exporter.Update(sum);
                    else
                        _exporter.SetUnavailable();
                }
            }

            ProcFsNetstatReader _netstatFile;
            ProcFsNetstatReader _snmpFile;
            ProcFsSnmp6Reader   _snmp6File;
            SockDiag            _sockDiag;

            List<ProcFsMetric>  _procMetrics = new List<ProcFsMetric>();
            Exporter            _socketBacklogExporter;

            public Linux()
            {
                try
                {
                    _netstatFile = new ProcFsNetstatReader("/proc/self/net/netstat");
                }
                catch(Exception)
                {
                }
                try
                {
                    _snmpFile = new ProcFsNetstatReader("/proc/self/net/snmp");
                }
                catch(Exception)
                {
                }
                try
                {
                    _snmp6File = new ProcFsSnmp6Reader("/proc/self/net/snmp6");
                }
                catch(Exception)
                {
                }
                try
                {
                    _sockDiag = new SockDiag();
                }
                catch(Exception)
                {
                }

                // Netstat
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_listen_overflows_total",
                    "TcpExt:ListenOverflows",
                    _netstatFile, ("TcpExt", "ListenOverflows")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_listen_drops_total",
                    "TcpExt:ListenDrops",
                    _netstatFile, ("TcpExt", "ListenDrops")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_ondata_resets_total",
                    "TcpExt:TCPAbortOnData. Sent RSTs due to receiving data to shutdown socket.",
                    _netstatFile, ("TcpExt", "TCPAbortOnData")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_close_timed_waits_total",
                    "TcpExt:TW. Number of host-initiated cleanly closing TCP connections, i.e. TCP TIME-WAIT.",
                    _netstatFile, ("TcpExt", "TW")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_syn_retransmits_total",
                    "TcpExt:TCPSynRetrans. Number of SYN retransmits.",
                    _netstatFile,
                    ("TcpExt", "TCPSynRetrans")));

                // Netstat + SNMP6
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_received_bytes_total",
                    "Sum of IpExt:InOctets and Ip6InOctets.",
                    _netstatFile, ("IpExt", "InOctets"),
                    _snmp6File, "Ip6InOctets"));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_sent_bytes_total",
                    "Sum of IpExt:OutOctets and Ip6OutOctets.",
                    _netstatFile, ("IpExt", "OutOctets"),
                    _snmp6File, "Ip6OutOctets"));

                // SNMP
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_connects_total",
                    "Tcp:ActiveOpens. Number of connect()s on TCP connections.",
                    _netstatFile, ("Tcp", "ActiveOpens")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_listen_accepts_total",
                    "Tcp:PassiveOpens. Number of TCP connections accepted.",
                    _netstatFile, ("Tcp", "PassiveOpens")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_connections",
                    "Tcp:CurrEstab. Number of ESTABLISHED TCP sockets.",
                    _netstatFile, ("Tcp", "CurrEstab")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_sent_resets_total",
                    "Tcp:OutRsts. Sent RST packets.",
                    _netstatFile, ("Tcp", "OutRsts")));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_tcp_established_resets_total",
                    "Tcp:EstabResets. TCP Transitions from ESTABLISHED to RST.",
                    _netstatFile, ("Tcp", "EstabResets")));

                // SNMP + SNMP6
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_udp_in_errors_total",
                    "Sum of Udp:InErrors and Udp6InErrors. Number of errors on UDP packet receiving path.",
                    _snmpFile, ("Udp", "InErrors"),
                    _snmp6File, "Udp6InErrors"));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_udp_rcvbuf_errors_total",
                    "Sum of Udp:RcvbufErrors and Udp6RcvbufErrors. Number of UDP packets dropped because application's receive buffer was full.",
                    _snmpFile, ("Udp", "RcvbufErrors"),
                    _snmp6File, "Udp6RcvbufErrors"));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_udp_in_datagrams_total",
                    "Sum of Udp:InDatagrams and Udp6InDatagrams. Number of received UDP messages.",
                    _snmpFile, ("Udp", "InDatagrams"),
                    _snmp6File, "Udp6InDatagrams"));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_udp_out_datagrams_total",
                    "Sum of Udp:OutDatagrams and Udp6OutDatagrams. Number of sent UDP messages.",
                    _snmpFile, ("Udp", "OutDatagrams"),
                    _snmp6File, "Udp6OutDatagrams"));
                _procMetrics.Add(new ProcFsMetric(
                    "netstat_udp_sndbuf_errors_total",
                    "Sum of Udp:SndbufErrors and Udp6SndbufErrors. Number of UDP packets dropped due to send buffer being full.",
                    _snmpFile, ("Udp", "SndbufErrors"),
                    _snmp6File, "Udp6SndbufErrors"));

                // sock_diag
                _socketBacklogExporter = new Exporter("socket_accept_backlog_connections", "Number of ready sockets in TCP accept queues.");
            }

            protected override void CollectImpl()
            {
                _netstatFile?.UpdateFromProcFs();
                _snmpFile?.UpdateFromProcFs();
                _snmp6File?.UpdateFromProcFs();

                foreach (ProcFsMetric metric in _procMetrics)
                    metric.Collect();

                if (_sockDiag != null)
                    CollectSockDiag();
            }
            protected override void DisposeImpl()
            {
                _netstatFile?.Dispose();
                _netstatFile = null;
                _snmpFile?.Dispose();
                _snmpFile = null;
                _snmp6File?.Dispose();
                _snmp6File = null;
                _sockDiag?.Dispose();
                _sockDiag = null;
            }

            void CollectSockDiag()
            {
                List<InetDiagMsg> socks = TryGetTCPListenSocks();
                if (socks != null)
                {
                    // \todo: Filter by our listen ports. this now reports queues size of all processes in this ns
                    int numSocketsInQueue = (int)socks.Sum(sock => sock.Rqueue);
                    _socketBacklogExporter.Update((long)numSocketsInQueue);
                }
                else
                {
                    // If cannot measure, don't print wrong results
                    _socketBacklogExporter.SetUnavailable();
                }
            }

            List<InetDiagMsg> TryGetTCPListenSocks()
            {
                if (_sockDiag == null)
                    return null;

                try
                {
                    SockDiag.Filter filter = new SockDiag.Filter()
                    {
                        Protocol = InetProtocol.TCP,
                        States = InetSocketStateFlags.LISTEN,
                    };

                    List<InetDiagMsg> listensocks4;
                    List<InetDiagMsg> listensocks6;

                    try
                    {
                        filter.Family = InetFamily.IPv4;
                        listensocks4 = _sockDiag.GetAll(filter);

                        filter.Family = InetFamily.IPv6;
                        listensocks6 = _sockDiag.GetAll(filter);
                    }
                    catch (SockDiagException)
                    {
                        _sockDiag.Dispose();
                        _sockDiag = null;
                        return null;
                    }

                    listensocks4.AddRange(listensocks6);
                    return listensocks4;
                }
                catch(Exception)
                {
                }
                return null;
            }
        }
    }
}
