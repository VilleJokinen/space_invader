// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services
{
    public class AppleGameCenterPublicKeyCache
    {
        public class InvalidKeyException : Exception
        {
            public InvalidKeyException(string message) : base(message)
            {
            }
        }
        public class KeyCacheTemporarilyUnavailable : Exception
        {
        }

        public class Cert
        {
            public RSA      Rsa;
            public DateTime NotBefore;
            public DateTime NotAfter;

            public bool IsValidTime(DateTime t) => !(t < NotBefore) && !(t > NotAfter);
        }

        static Lazy<AppleGameCenterPublicKeyCache> s_instance = new Lazy<AppleGameCenterPublicKeyCache>(() => new AppleGameCenterPublicKeyCache());
        public static AppleGameCenterPublicKeyCache Instance => s_instance.Value;

        readonly HttpClient                     _httpClient;
        Dictionary<string, Cert>                _volatileKeys;      // volatile

        readonly object                         _fetchLock;
        Dictionary<string, Task<FetchResult>>   _ongoingFetches;    // protected by the lock

        AppleGameCenterPublicKeyCache()
        {
            _httpClient = new HttpClient();
            _volatileKeys = new Dictionary<string, Cert>();

            _fetchLock = new object();
            _ongoingFetches = new Dictionary<string, Task<FetchResult>>();
        }

        Cert GetKeyOrNull(string keyUrl)
        {
            Dictionary<string, Cert> keys = Volatile.Read(ref _volatileKeys);
            if (keys.TryGetValue(keyUrl, out Cert value))
                return value;
            return null;
        }

        void AddKeyToCache(string keyUrl, Cert key)
        {
            for (;;)
            {
                Dictionary<string, Cert> oldKeys = Volatile.Read(ref _volatileKeys);
                Dictionary<string, Cert> newKeys = new Dictionary<string, Cert>(oldKeys);
                newKeys[keyUrl] = key;

                if (Object.ReferenceEquals(oldKeys, Interlocked.CompareExchange(ref _volatileKeys, newKeys, oldKeys)))
                    break;
            }
        }

        static async Task<byte[]> FetchBytes(HttpClient httpClient, string url, int maxNumRetries)
        {
            int numRetriesDone = 0;
            for (;;)
            {
                try
                {
                    using (HttpResponseMessage response = await httpClient.GetAsync(url).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                    if (numRetriesDone >= maxNumRetries)
                        throw;

                    numRetriesDone++;
                }
            }
        }

        async Task<FetchResult> FetchCertAsyncInner(string keyUrl)
        {
            byte[] certBytes;
            try
            {
                certBytes = await FetchBytes(_httpClient, keyUrl, maxNumRetries: 3).ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                Serilog.Log.Error("Apple Game Center key fetch from '{KeyUrl}' failed: {Cause}", keyUrl, ex);
                return FetchResult.CouldNotFetch;
            }

            try
            {
                using (X509Certificate2 rawCert = new X509Certificate2(certBytes))
                {
                    using (X509Chain chain = new X509Chain())
                    {
                        // ignore notafter. key might have expired now, but it could have been valid when the login request
                        // was signed.
                        // allow unknown CAs. The certificate is fetched over HTTPS from the apple servers so we already have
                        // certain level of trust here. This fixes flakiness with various platforms missing/revoking
                        // (legacy?) CAs from their root stores.
                        // \todo: When .NET 5.0 lands, we can use CustomTrustStore to explicitly trust certain CAs without
                        //        polluting the platform store.
                        // \note[jarkko]: allowing the unknown certs is not enough. The Apple's cert might contain only the
                        //                cert chain tail, so the validation never reaches the unkown CAs. As it cannot create
                        //                a complete (let alone a valid) chain, it fails.
                        //if (!chain.Build(rawCert))
                        //    return FetchResult.BadKey;
                    }

                    RSA rsa = rawCert.GetRSAPublicKey();
                    if (rsa == null)
                        return FetchResult.BadKey;

                    // success
                    Cert timedCert = new Cert();
                    timedCert.Rsa = rsa;
                    timedCert.NotAfter = rawCert.NotAfter.ToUniversalTime();
                    timedCert.NotBefore = rawCert.NotBefore.ToUniversalTime();
                    AddKeyToCache(keyUrl, timedCert);
                    return FetchResult.Success;
                }
            }
            catch
            {
                return FetchResult.BadKey;
            }
        }
        async Task<FetchResult> FetchCertAsync(string keyUrl)
        {
            try
            {
                return await FetchCertAsyncInner(keyUrl).ConfigureAwait(false);
            }
            finally
            {
                lock(_fetchLock)
                {
                    _ongoingFetches.Remove(keyUrl);
                }
            }
        }

        enum FetchResult
        {
            Success,
            CouldNotFetch,
            BadKey,
        }

        /// <summary>
        /// Fetches the key from the url, validates it and puts it to the cache. If this is successful, returns <see cref="FetchResult.Success"/>.
        /// If key cannot be (temporarily) fetched, returns <see cref="FetchResult.CouldNotFetch"/>. If fetched key is bad, returns <see cref="FetchResult.BadKey"/>.
        /// </summary>
        Task<FetchResult> FetchKeyToCacheAsync(string keyUrl)
        {
            Task<FetchResult> fetchTask;
            lock(_fetchLock)
            {
                // has the fetch completed already
                if (GetKeyOrNull(keyUrl) != null)
                    return Task.FromResult(FetchResult.Success);

                if (_ongoingFetches.TryGetValue(keyUrl, out var ongoingFetchTask))
                {
                    fetchTask = ongoingFetchTask;
                }
                else
                {
                    // keep locked region small. Dont run the actual work, just enqueue an async task
                    fetchTask = Task.Run(async () => await FetchCertAsync(keyUrl).ConfigureAwait(false));
                    _ongoingFetches[keyUrl] = fetchTask;
                }
            }

            return fetchTask;
        }

        static void EnsureValidUrl(string publicKeyUrl)
        {
            // Validate inputs
            // is should look something like https://static.gc.apple.com/public-key/gc-prod-3.cer
            Uri uri = new Uri(publicKeyUrl);
            if (uri.Scheme != "https")
                throw new InvalidKeyException($"Invalid scheme for Game Center publicKeyUrl '{uri.Scheme}', expecting 'https'");
            if (!uri.Host.EndsWith(".gc.apple.com", StringComparison.Ordinal))
                throw new InvalidKeyException($"Invalid host for Game Center publicKeyUrl '{uri.Host}', expecting host ending with '.gc.apple.com'");
        }

        /// <summary>
        /// Returns the public key with the given url. If the key is not valid, key is not signed by Apple, or the key
        /// is not valid at <paramref name="validationTimeUtc"/>, throws <see cref="InvalidKeyException"/>.
        /// If cache cannot determine whether a key exists or not, throws <see cref="KeyCacheTemporarilyUnavailable"/>.
        /// </summary>
        public async ValueTask<RSA> GetPublicKeyAsync(string keyUrl, DateTime validationTimeUtc)
        {
            // Fast path, key is in the observable cache
            {
                Cert fastKey = GetKeyOrNull(keyUrl);
                if (fastKey != null)
                {
                    if (!fastKey.IsValidTime(validationTimeUtc))
                        throw new InvalidKeyException("key is not valid at the requested time");

                    return fastKey.Rsa;
                }
            }

            // The key was not in cache. Fetch it if needed.
            // \todo: check negative cache first?

            EnsureValidUrl(keyUrl);
            FetchResult result = await FetchKeyToCacheAsync(keyUrl).ConfigureAwait(false);
            if (result == FetchResult.CouldNotFetch)
                throw new KeyCacheTemporarilyUnavailable();
            else if (result == FetchResult.BadKey)
                throw new InvalidKeyException($"bad key: {keyUrl}");
            else // success
            {
                Cert slowKey = GetKeyOrNull(keyUrl);
                if (slowKey == null)
                    throw new InvalidOperationException("internal logic error");
                if (!slowKey.IsValidTime(validationTimeUtc))
                    throw new InvalidKeyException("key is not valid at the requested time");
                return slowKey.Rsa;
            }
        }
    }
}
