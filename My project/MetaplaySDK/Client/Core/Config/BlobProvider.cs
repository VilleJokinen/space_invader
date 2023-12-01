// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Network;
using Metaplay.Core.Tasks;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Metaplay.Core.Config
{
    public class BlobProviderError : Exception
    {
        public BlobProviderError(string message) : base(message)
        {
        }

        public BlobProviderError(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Interface for providing versioned binary blobs. Used mainly to control how GameConfig files
    /// are stored and distributed to clients.
    /// </summary>
    public interface IBlobProvider : IDisposable
    {
        Task<byte[]>    GetAsync    (string configName, ContentHash version);

        /// <summary>
        /// Stores the config version into the provider. On failure, throws.
        /// </summary>
        Task            PutAsync    (string configName, ContentHash version, byte[] value);
    }

    /// <summary>
    /// Simple single-value binary blob provider.
    ///
    /// Main use case is to emulate proper providers when wanting to use GameConfig files
    /// without any update flows (eg, in offline mode).
    /// </summary>
    public class StaticBlobProvider : IBlobProvider
    {
        ContentHash _version;
        byte[]      _value;

        public StaticBlobProvider(string configName, ContentHash version, byte[] value)
        {
            _version    = version;
            _value      = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void Dispose()
        {
        }

        public Task<byte[]> GetAsync(string configName, ContentHash version)
        {
            if (version != _version)
                throw new BlobProviderError($"StaticBlobProvider.GetAsync(): version mismatch: querying {version}, but only has {_version}");

            return Task.FromResult(_value);
        }

        public Task PutAsync(string configName, ContentHash version, byte[] value)
        {
            throw new InvalidOperationException($"Not supported");
        }
    }

    /// <summary>
    /// Blob provider which stores its files in a backing <see cref="IBlobStorage"/>.
    ///
    /// Used by the server to store multiple hash-identified versions GameConfigs in persistent
    /// storage (either on-disk or in S3).
    /// </summary>
    public class StorageBlobProvider : IBlobProvider
    {
        IBlobStorage _storage;

        public StorageBlobProvider(IBlobStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public void Dispose()
        {
            // \note we don't own _storage, so not disposing it
        }

        public string GetStorageFileName(string configName, ContentHash version) => $"{configName}/{version}";
        public IBlobStorage GetStorage() => _storage;

        public async Task<byte[]> GetAsync(string configName, ContentHash version)
        {
            return await _storage.GetAsync(GetStorageFileName(configName, version));
        }

        public async Task PutAsync(string configName, ContentHash version, byte[] value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            //DebugLog.Info("RepositoryBlobProvider.PutAsync({0}, setAsCurrent={1})", version, setAsCurrent);

            // Store blob using its version as name
            await _storage.PutAsync(GetStorageFileName(configName, version), value);
        }
    }

    /// <summary>
    /// Binary blob provider which two other <see cref="IBlobProvider"/>s: one base provider and another
    /// provider to use for caching. The cache is always checked first for a file, and if not found, the
    /// base provider is queried (and the resulting blob stored in the cache).
    ///
    /// Only supports reading specific versions of a blob. Files are automatically written to cache when
    /// they are read from the source provider.
    ///
    /// Main use case is for the client to avoid fetching the same GameConfigs multiple times by caching
    /// them on disk.
    /// </summary>
    public class CachingBlobProvider : IBlobProvider
    {
        IBlobProvider _provider;  // Base provider: fetch from here if not in cache
        IBlobProvider _cache;     // Cache provider: store fetched data here for fast retrieval

        public CachingBlobProvider(IBlobProvider provider, IBlobProvider cache)
        {
            _provider   = provider ?? throw new ArgumentNullException(nameof(provider));
            _cache      = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public void Dispose()
        {
        }

        public async Task<byte[]> GetAsync(string configName, ContentHash version)
        {
            // Check if blob in cache
            //DebugLog.Info("CachingBlobProvider.GetAsync({0}): check cache!", version);
            byte[] value = await _cache.GetAsync(configName, version);
            if (value != null)
            {
                //DebugLog.Info("CachingBlobProvider.GetAsync({0}): found in cache", version);
                return value;
            }

            // Fetch blob from provider
            //DebugLog.Info("CachingBlobProvider.GetAsync({0}): fetch from base provider", version);
            value = await _provider.GetAsync(configName, version);
            if (value == null)
            {
                //DebugLog.Info("CachingBlobProvider.GetAsync({0}): not in base provider", version);
                return null;
            }

            // Store in cache
            try
            {
                //DebugLog.Info("CachingBlobProvider.GetAsync({0}): store in cache", version);
                await _cache.PutAsync(configName, version, value);
            }
            catch
            {
            }

            return value;
        }

        public Task PutAsync(string configName, ContentHash version, byte[] value)
        {
            throw new BlobProviderError("CachingBlobProvider.PutAsync(): operation not supported");
        }
    }

    /// <summary>
    /// Provider for fetching binary blobs over HTTP.
    ///
    /// Main use case is for client to fetch GameConfigs from S3-backed storage (over CloudFront).
    /// When running the server locally, it also exposes a HTTP endpoint to emulate the S3-based flow.
    /// </summary>
    public class HttpBlobProvider : IBlobProvider
    {
        MetaHttpClient  _httpClient;
        string          _primaryBaseUrl; // has trailing /
        string          _secondaryBaseUrlMaybe; // null if no such url. Otherwise has trailing /
        string          _uriSuffix;

        public HttpBlobProvider(MetaHttpClient client, MetaplayCdnAddress address, string uriSuffix = null)
        {
            _httpClient = client;
            _primaryBaseUrl = address.PrimaryBaseUrl;
            _secondaryBaseUrlMaybe = address.SecondaryBaseUrl;
            _uriSuffix = uriSuffix;
        }

        public void Dispose()
        {
            // we don't own the _httpClient
        }

        string GetUri(string baseUrl, string configName, string fileName) => $"{baseUrl}{configName}/{fileName}{_uriSuffix}";

        bool IsResponseOkAndHasContent(MetaHttpResponse responseMessage)
        {
            return responseMessage.IsSuccessStatusCode && responseMessage.Content != null;
        }

        async Task<MetaHttpResponse> GetResponseWithContentAsync(string primaryUrl, string secondaryUrlMaybe, int primaryHeadStartMilliseconds = 1000)
        {
            Task<MetaHttpResponse> responseP = _httpClient.GetAsync(primaryUrl);
            Task headStartDelay = MetaTask.Delay(primaryHeadStartMilliseconds);

            // give primary request head start and check if it did complete during that time
            await Task.WhenAny(headStartDelay, responseP).ConfigureAwaitFalse();
            if (responseP.Status == TaskStatus.RanToCompletion && IsResponseOkAndHasContent(responseP.GetCompletedResult()))
                return responseP.GetCompletedResult();

            // start second query in parallel
            Task<MetaHttpResponse> responseS;
            if (secondaryUrlMaybe != null)
                responseS = _httpClient.GetAsync(secondaryUrlMaybe);
            else
                responseS = Task.FromException<MetaHttpResponse>(new NullReferenceException()); // exception does not matter

            // race queries.
            // If one succeeds, return that one and cancel the other.
            // If one fails, wait for the other.
            // If both fails, return the primary error

            Task<MetaHttpResponse> firstResponse = await Task.WhenAny(responseP, responseS).ConfigureAwaitFalse();
            Task<MetaHttpResponse> secondResponse = firstResponse == responseP ? responseS : responseP;

            // first answer
            if (firstResponse.Status == TaskStatus.RanToCompletion && IsResponseOkAndHasContent(firstResponse.GetCompletedResult()))
            {
                secondResponse.ContinueWithDispose();
                return firstResponse.GetCompletedResult();
            }

            // wait for the another
            await Task.WhenAny(secondResponse).ConfigureAwaitFalse();
            if (secondResponse.Status == TaskStatus.RanToCompletion && IsResponseOkAndHasContent(secondResponse.GetCompletedResult()))
            {
                firstResponse.ContinueWithDispose();
                return secondResponse.GetCompletedResult();
            }

            // Both failed. Return primary error if any.
            if (responseP.IsFaulted)
            {
                responseS.ContinueWithDispose();
                throw responseP.Exception;
            }
            else
            {
                responseS.ContinueWithDispose();
                responseP.ContinueWithDispose();
                throw new WebException("did not get suitable content response");
            }
        }

        public async Task<byte[]> GetAsync(string configName, ContentHash version)
        {
            string primaryUri = GetUri(_primaryBaseUrl, configName, version.ToString());
            string secondaryUri = _secondaryBaseUrlMaybe != null ? GetUri(_secondaryBaseUrlMaybe, configName, version.ToString()) : null;
            try
            {
                // DebugLog.Info("HttpBlobProvider.GetAsync({Version}): fetch url {URI} (backup = {URI})", version, primaryUri, secondaryUri);
                using (MetaHttpResponse response = await GetResponseWithContentAsync(primaryUri, secondaryUri).ConfigureAwaitFalse())
                {
                    byte[] bytes = response.Content;
                    //DebugLog.Info("HttpBlobProvider.GetAsync({0}): got {1} bytes", version, bytes.Length);
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                throw new BlobProviderError($"Failed to fetch {primaryUri} (backup = {secondaryUri})", ex);
            }
        }

        Task IBlobProvider.PutAsync(string configName, ContentHash version, byte[] value)
        {
            throw new BlobProviderError($"HttpBlobProvider.PutAsync({configName}): operation not supported");
        }
    }
}
