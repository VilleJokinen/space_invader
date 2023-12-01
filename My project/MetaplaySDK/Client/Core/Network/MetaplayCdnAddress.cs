// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// The address to the HTTP based Content Delivery Network endpoints. This always
    /// represents a directory (or empty path), i.e. all paths have a trailing '/'.
    /// </summary>
    public readonly struct MetaplayCdnAddress
    {
        private readonly bool _prioritizeIPv4;

        /// <summary> Has trailing / if address is non-empty </summary>
        public readonly string IPv4BaseUrl;
        /// <inheritdoc cref="IPv4BaseUrl"/>
        public readonly string IPv6BaseUrl;

        /// <inheritdoc cref="IPv4BaseUrl"/>
        public string PrimaryBaseUrl => _prioritizeIPv4 ? IPv4BaseUrl : IPv6BaseUrl;
        /// <inheritdoc cref="IPv4BaseUrl"/>
        public string SecondaryBaseUrl => !_prioritizeIPv4 ? IPv4BaseUrl : IPv6BaseUrl;

        public static MetaplayCdnAddress Empty => default;
        public bool IsEmpty => IPv4BaseUrl == null;

        MetaplayCdnAddress(bool prioritizeIPv4, string iPv4BaseUrl, string iPv6BaseUrl)
        {
            _prioritizeIPv4 = prioritizeIPv4;
            IPv4BaseUrl = iPv4BaseUrl;
            IPv6BaseUrl = iPv6BaseUrl;
        }

        /// <summary>
        /// Creates a CdnAddress rooted at the given subdirectory. Base address must not be Empty.
        /// </summary>
        public MetaplayCdnAddress GetSubdirectoryAddress(string directoryName)
        {
            if (IsEmpty)
                throw new InvalidOperationException();

            return new MetaplayCdnAddress(
                prioritizeIPv4: _prioritizeIPv4,
                iPv4BaseUrl:    string.Concat(IPv4BaseUrl, directoryName, "/"),
                iPv6BaseUrl:    string.Concat(IPv6BaseUrl, directoryName, "/"));
        }

        /// <summary>
        /// Creates a CdnAddress rooted at the given subdirectory. If base address is Empty,
        /// this method returns Empty subdirectory
        /// </summary>
        public MetaplayCdnAddress TryGetSubdirectoryAddress(string directoryName)
        {
            if (IsEmpty)
                return Empty;
            else
                return GetSubdirectoryAddress(directoryName);
        }

        /// <summary>
        /// Creates an endpoint address from an IPv4- or IPv6- base url. If <paramref name="prioritizeIPv4"/> is set,
        /// the resulting address prioritizes (attmepts to use first) IPv4 endpoints. Otherwise, IPv6 addresses are
        /// preferred.
        /// </summary>
        public static MetaplayCdnAddress Create(string cdnBaseUrl, bool prioritizeIPv4)
        {
            if (cdnBaseUrl == null)
                throw new ArgumentNullException(nameof(cdnBaseUrl));

            // guarantee always a trailing /
            if (!cdnBaseUrl.EndsWith("/", StringComparison.Ordinal))
                cdnBaseUrl += "/";

            return new MetaplayCdnAddress(
                prioritizeIPv4: prioritizeIPv4,
                iPv4BaseUrl:    GetV4V6SpecificCdn(cdnBaseUrl, isIPv4: true),
                iPv6BaseUrl:    GetV4V6SpecificCdn(cdnBaseUrl, isIPv4: false));
        }

        static string GetV4V6SpecificCdn(string baseUrl, bool isIPv4)
        {
            // offline
            if (baseUrl == null)
                return null;

            Uri parsed = new Uri(baseUrl);
            UriBuilder builder = new UriBuilder(parsed);
            builder.Host = MetaplayHostnameUtil.GetV4V6SpecificHost(builder.Host, isIPv4);
            return builder.Uri.ToString();
        }
    };
}
