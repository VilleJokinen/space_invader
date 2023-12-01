// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#define UNITY_WEBGL_BUILD
#endif

using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    /// <summary>
    /// Detects network (game server) connectivity by accessing a known CDN resource.
    /// </summary>
#if !UNITY_WEBGL_BUILD
    public class NetworkProbe : IDisposable
    {
        enum ResultValue
        {
            HasConnection = 1,
            NoConnection = -1,
        }

#pragma warning disable IDE0052
        Task _task;
#pragma warning restore IDE0052
        CancellationTokenSource _cts;
        volatile int _result; // 1 true, -1 false, 0 no result

        NetworkProbe() { }
        public static NetworkProbe TestConnectivity(IMetaLogger log, ServerEndpoint ep)
        {
            NetworkProbe probe = new NetworkProbe();
            if (ep.IsOfflineMode)
            {
                probe._result = (int)ResultValue.HasConnection;
            }
            else
            {
                MetaplayCdnAddress cdn = MetaplayCdnAddress.Create(ep.CdnBaseUrl, prioritizeIPv4: true);
                string probeUrl = cdn.PrimaryBaseUrl + "Connectivity/connectivity-test";
                probe._cts =  new CancellationTokenSource();
                probe._task = probe.RunNetworkProbeAsync(log, probeUrl, probe._cts.Token);
            }
            return probe;
        }

        /// <summary>
        /// May return different results until settles.
        /// </summary>
        public bool? TryGetConnectivityState()
        {
            int result = _result;
            if (result == (int)ResultValue.HasConnection)
                return true;
            else if (result == (int)ResultValue.NoConnection)
                return false;
            return default;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts = null;
            _task = null;
        }

        async Task RunNetworkProbeAsync(IMetaLogger log, string probeUrl, CancellationToken ct)
        {
            HttpWebRequest networkProbe = null;

            ct.Register(() => networkProbe?.Abort());
            log.Debug("Testing network reachability with a network probe.");

            for (int attemptNdx = 0; attemptNdx < 5; ++attemptNdx)
            {
                Stopwatch sw = Stopwatch.StartNew();
                networkProbe = HttpWebRequest.CreateHttp(probeUrl);
                networkProbe.Method = "GET";
                try
                {
                    log.Debug("Sending network probe {0}", attemptNdx+1);

                    using (WebResponse response = await networkProbe.GetResponseAsync().ConfigureAwaitFalse())
                    {
                        byte[] buffer = new byte[2];
                        try
                        {
                            using (Stream stream = response.GetResponseStream())
                            {
                                int numRead = await stream.ReadAsync(buffer, offset: 0, count: buffer.Length);
                                if (numRead == 1 && buffer[0] == 'y')
                                {
                                    _result = (int)ResultValue.HasConnection;

                                    log.Debug("Network probe {0} completed successfully (took {1}ms).", attemptNdx+1, sw.ElapsedMilliseconds);
                                    return;
                                }

                                log.Warning("Unexpected contents from probe {0}.", attemptNdx+1);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Warning("Network probe {0} failed while reading: {2}", attemptNdx+1, NetworkErrorLoggingUtil.GetMinimalDescription(ex));
                        }
                    }
                }
                catch(WebException webex)
                {
                    log.Warning("Network probe {0} failed after {1}ms: {2}", attemptNdx+1, sw.ElapsedMilliseconds, NetworkErrorLoggingUtil.GetMinimalDescription(webex));
                }

                // error path
                // probe failed. If this was the first failure, we give it the benefit of
                // the doubt and ignore it. For the next failure, we mark the probe as failed
                // but might still change our opinion

                if (attemptNdx == 1)
                    _result = (int)ResultValue.NoConnection;

                int milliseconds;
                if (attemptNdx == 0)
                    milliseconds = 500;
                else
                    milliseconds = 1000;
                await MetaTask.Delay(milliseconds, ct).ConfigureAwaitFalse();
            }

            // ran out of attempts
            log.Warning("All network probes failed. Network is not reachable.");
        }
    }
#else
    // NetworkProbes not implemented in WebGL builds
    public class NetworkProbe : IDisposable
    {
        NetworkProbe() { }
        public static NetworkProbe TestConnectivity(IMetaLogger log, ServerEndpoint ep) => new NetworkProbe();
        public bool? TryGetConnectivityState() => default;
        public void Dispose() { }
    }
#endif
}
