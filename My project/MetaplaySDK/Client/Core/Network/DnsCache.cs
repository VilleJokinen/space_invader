// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

using Metaplay.Core.Tasks;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// DNS resolution cache.
    /// </summary>
    public static class DnsCache
    {
        class DnsCacheEntry
        {
            public DateTime     QueriedAt;
            public IPAddress[]  AddressesIPv4;
            public int          AddressesIPv4Counter;
            public IPAddress[]  AddressesIPv6;
            public int          AddressesIPv6Counter;
        }

#if UNITY_WEBGL_BUILD
        static WebConcurrentDictionary<string, DnsCacheEntry> _cache = new WebConcurrentDictionary<string, DnsCacheEntry>();
#else
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is non-WebGL.
        static ConcurrentDictionary<string, DnsCacheEntry> _cache = new ConcurrentDictionary<string, DnsCacheEntry>();
#pragma warning restore MP_WGL_00
#endif

        static async Task<DnsCacheEntry> TryResolveHostname(string hostname)
        {
            try
            {
                IPHostEntry     dnsEntry    = await Dns.GetHostEntryAsync(hostname).ConfigureAwaitFalse();
                DnsCacheEntry   cacheEntry  = new DnsCacheEntry();

                cacheEntry.QueriedAt = DateTime.UtcNow;
                cacheEntry.AddressesIPv4 = dnsEntry.AddressList.Where(addr => addr.AddressFamily == AddressFamily.InterNetwork).ToArray();
                cacheEntry.AddressesIPv4Counter = 0;
                cacheEntry.AddressesIPv6 = dnsEntry.AddressList.Where(addr => addr.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
                cacheEntry.AddressesIPv6Counter = 0;

                return cacheEntry;
            }
            catch
            {
                return null;
            }
        }

        static async Task<DnsCacheEntry> RefreshEntryAsync(string hostname)
        {
            // \todo: if there is already a query in flight, maybe wait for that.
            DnsCacheEntry entry = await TryResolveHostname(hostname).ConfigureAwaitFalse();
            if (entry == null)
                return null;

            // \todo: should also update for the IPHostEntry.HostName and IPHostEntry.Aliases
            _cache[hostname] = entry;

            return entry;
        }

        /// <summary>
        /// Resolves the IP addresses for the hostname. If the result is cached and newer than <paramref name="maxTimeToLive"/>, the
        /// cached result is returned with entries cycled. Otherwise, a DNS query is made and on success the result is cached. On query
        /// failure an expired record is result from cache as a last resort and a warning is logged. Otherwise if there is no cached
        /// result, an empty array is returned.
        /// </summary>
        public static async Task<IPAddress[]> GetHostAddressesAsync(string hostname, AddressFamily af, TimeSpan maxTimeToLive, IMetaLogger log)
        {
            DateTime        now = DateTime.UtcNow;
            DnsCacheEntry   entry;

            if (!_cache.TryGetValue(hostname, out entry))
            {
                // Not in cache, need to refresh.
                DnsCacheEntry refreshedEntry = await RefreshEntryAsync(hostname).ConfigureAwaitFalse();
                if (refreshedEntry == null)
                    return Array.Empty<IPAddress>();

                entry = refreshedEntry;
            }
            else if (entry.QueriedAt + maxTimeToLive < now)
            {
                // Expired, need to refresh.
                DnsCacheEntry refreshedEntry = await RefreshEntryAsync(hostname).ConfigureAwaitFalse();
                if (refreshedEntry == null)
                {
                    // Cannot refresh. Last resort, use the stale record (entry).
                    log.Warning("DNS cache refresh failed. Using expired record as a last resort. Host: {Hostname}", hostname);
                }
                else
                {
                    // Refreshed
                    entry = refreshedEntry;
                }
            }

            int         offset;
            IPAddress[] addrs;

            switch(af)
            {
                case AddressFamily.InterNetwork:
                    offset = Interlocked.Increment(ref entry.AddressesIPv4Counter);
                    addrs = entry.AddressesIPv4;
                    break;
                case AddressFamily.InterNetworkV6:
                    offset = Interlocked.Increment(ref entry.AddressesIPv6Counter);
                    addrs = entry.AddressesIPv6;
                    break;
                default:
                    return Array.Empty<IPAddress>();

            }

            IPAddress[] cycledArray = new IPAddress[addrs.Length];
            for (int i = 0; i < cycledArray.Length; ++i)
                cycledArray[i] = addrs[(uint)(offset + i) % (uint)addrs.Length];

            return cycledArray;
        }
    }
}
