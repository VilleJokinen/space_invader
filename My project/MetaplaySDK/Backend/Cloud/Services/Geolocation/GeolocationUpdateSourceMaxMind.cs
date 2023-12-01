// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services.Geolocation
{
    /// <summary>
    /// Represents the MaxMind-hosted geolocation database as an <see cref="IGeolocationUpdateSource"/>.
    /// Note that the term "origin" is here sometimes used to refer to MaxMind's serving of the database.
    /// </summary>
    internal class GeolocationUpdateSourceMaxMind : IGeolocationUpdateSource
    {
        IMetaLogger     _logger;
        HttpClient      _httpClient = new HttpClient();

        // This is for caching downloads from the origin.
        // Please see GetDatabaseBlobFromOriginAsync for explanation.
        // \note MaxMind's HTTP response doesn't contain Cache-Control,
        //       so we don't bother with that.
        OriginResponse?             _cachedBlobDownloadResponse;
        static readonly TimeSpan    BlobDownloadCacheLifetime = TimeSpan.FromMinutes(30);

        public GeolocationUpdateSourceMaxMind()
        {
            _logger = MetaLogger.ForContext<GeolocationUpdateSourceMaxMind>();
        }

        /// <summary>
        /// The interesting parts of a MaxMind geoip download result, from either a HEAD or GET request.
        /// </summary>
        readonly struct OriginResponse
        {
            /// <summary>
            /// Database build date, from the Last-Modified header in the response.
            /// Null if the response didn't contain Last-Modified (shouldn't happen with MaxMind!).
            /// </summary>
            public readonly DateTimeOffset? BuildDate;
            /// <summary>
            /// The .tar.gz payload downloaded from MaxMind.
            /// The tar contains a folder with the .mmdb and some files we don't need.
            /// Can be null if only build date was requested (using a HEAD request).
            /// </summary>
            public readonly byte[]          Payload;
            /// <summary>
            /// When the download was made. Used for caching the responses.
            /// </summary>
            public readonly DateTime        DownloadedAt;

            public OriginResponse(DateTimeOffset? buildDate, byte[] payload, DateTime downloadedAt)
            {
                BuildDate = buildDate;
                Payload = payload;
                DownloadedAt = downloadedAt;
            }
        }

        #region IGeolocationUpdateSource

        public async Task<GeolocationDatabaseMetadata?> TryFetchMetadataAsync(GeolocationOptions options)
        {
            if (options.MaxMindLicenseKey == null)
                return null;

            DateTimeOffset? originBuildDateDTO = await GetBuildDateFromOriginAsync(options).ConfigureAwait(false);
            if (!originBuildDateDTO.HasValue)
                return null;

            return new GeolocationDatabaseMetadata(MetaTime.FromDateTime(originBuildDateDTO.Value.DateTime));
        }

        public async Task<GeolocationDatabase?> TryFetchDatabaseAsync(GeolocationOptions options)
        {
            if (options.MaxMindLicenseKey == null)
                return null;

            OriginResponse  originResponse  = await GetDatabaseBlobFromOriginAsync(options).ConfigureAwait(false);
            DateTimeOffset  originBuildDate = originResponse.BuildDate
                                              ?? throw new InvalidOperationException($"Cannot create GeolocationDatabase, origin response didn't contain build date");

            return new GeolocationDatabase(
                metadata:   new GeolocationDatabaseMetadata(MetaTime.FromDateTime(originBuildDate.DateTime)),
                payload:    GeolocationExtractionUtil.ExtractGeolite2CountryDatabase(originResponse.Payload));
        }

        #endregion

        /// <summary>
        /// Download the database blob from origin,
        /// or return a cached response if it was pretty recent.
        /// </summary>
        async Task<OriginResponse> GetDatabaseBlobFromOriginAsync(GeolocationOptions options)
        {
            // We keep a cache of the latest successful download.
            // If the latest download was made less than BlobDownloadCacheLifetime ago,
            // then we return that old response instead of doing an actual download.
            //
            // The purpose of this is to limit the download rate, as there's a daily limit
            // to the per-account downloads from MaxMind (2000 downloads per 24h, according to their FAQ).
            //
            // Note that in the happy path this cache should not be hit at all,
            // because under normal circumstances we shouldn't be calling GetDatabaseBlobFromOriginAsync
            // unless we detect there's an update available (or we have no replica available at all),
            // which happens only rarely.
            //
            // So this cache is meant as a failsafe for non-happy scenarios, such as when exception
            // is thrown during TickAsync after the download has been made, but before it has been
            // successfully installed, in which case GetDatabaseBlobFromOriginAsync may be called soon again.
            if (_cachedBlobDownloadResponse.HasValue
                && DateTime.Now < _cachedBlobDownloadResponse.Value.DownloadedAt + BlobDownloadCacheLifetime)
            {
                _logger.Information("Cached download from origin was at {CachedDownloadedAt}, returning cached response instead of downloading", _cachedBlobDownloadResponse.Value.DownloadedAt);
                return _cachedBlobDownloadResponse.Value;
            }

            // Otherwise, do actual download.

            _logger.Information("Downloading database blob from origin");

            OriginResponse response = await DownloadFromOriginAsync(OriginDownloadFlavor.Full, options).ConfigureAwait(false);
            _cachedBlobDownloadResponse = response; // Remember response for cache

            return response;
        }

        /// <summary>
        /// Request just the build date from origin.
        /// May also clear _cachedBlobDownloadResponse if an updated database is detected.
        /// </summary>
        async Task<DateTimeOffset?> GetBuildDateFromOriginAsync(GeolocationOptions options)
        {
            OriginResponse response = await DownloadFromOriginAsync(OriginDownloadFlavor.OnlyBuildDate, options).ConfigureAwait(false);

            // Clear _cachedBlobDownloadResponse if this build date is newer than that in the cached response.
            // That way, GetDatabaseBlobFromOriginAsync won't return an older blob than this build date request indicates.
            if (_cachedBlobDownloadResponse.HasValue
                && _cachedBlobDownloadResponse.Value.BuildDate.HasValue
                && response.BuildDate.HasValue
                && response.BuildDate.Value > _cachedBlobDownloadResponse.Value.BuildDate.Value)
            {
                _logger.Information("Got build date {OriginBuildDate} from origin; newer than cached {CachedBuildDate}. Clearing cache.", response.BuildDate.Value, _cachedBlobDownloadResponse.Value.BuildDate.Value);
                _cachedBlobDownloadResponse = null;
            }

            return response.BuildDate;
        }

        enum OriginDownloadFlavor
        {
            OnlyBuildDate,
            Full,
        }

        /// <summary>
        /// Fetch stuff from origin as indicated by <paramref name="flavor"/>.
        /// Note that this should only be called fairly rarely with <see cref="OriginDownloadFlavor.Full"/>,
        /// as there's a download rate limit in MaxMind. Please see comment in <see cref="GetDatabaseBlobFromOriginAsync"/>.
        /// </summary>
        async Task<OriginResponse> DownloadFromOriginAsync(OriginDownloadFlavor flavor, GeolocationOptions options)
        {
            // \note See https://dev.maxmind.com/geoip/geoip-direct-downloads/ for info.
            //       Basically, the Last-Modified header contains the database build date,
            //       and we use HEAD instead of GET when we only need that build date.

            if (options.MaxMindLicenseKey == null)
                throw new InvalidOperationException("Cannot download from MaxMind without license key");

            string      uri             = $"https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-Country&license_key={options.MaxMindLicenseKey}&suffix=tar.gz";
            HttpMethod  requestMethod   = flavor == OriginDownloadFlavor.OnlyBuildDate
                                          ? HttpMethod.Head
                                          : HttpMethod.Get;

            using (HttpRequestMessage request = new HttpRequestMessage(requestMethod, uri))
            using (HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                DateTimeOffset? buildDate   = response.Content.Headers.LastModified;
                byte[]          payload     = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                return new OriginResponse(
                    buildDate:      buildDate,
                    payload:        payload,
                    downloadedAt:   DateTime.Now);
            }
        }
    }
}
