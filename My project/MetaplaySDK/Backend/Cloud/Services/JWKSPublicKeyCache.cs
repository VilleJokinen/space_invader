// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using JWT;
using Metaplay.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services
{
    public class JWKSPublicKeyCache
    {
        public class NoSuchKeyException : Exception
        {
            public readonly string KeyId;
            public NoSuchKeyException(string keyId) : base($"no public key in JWKS: {keyId}")
            {
                KeyId = keyId;
            }
        }
        public class KeyCacheTemporarilyUnavailable : Exception
        {
        }

        class JWKSet
        {
            public JWK[] keys = default;
        }
        class JWK
        {
            public string alg = default;
            public string e = default;
            public string kid = default;
            public string kty = default;
            public string n = default;
            public string use = default;
        }

        readonly Serilog.ILogger        _log;
        readonly HttpClient             _httpClient;
        readonly JwtBase64UrlEncoder    _base64UrlEncoder;
        readonly string                 _jwksUrl;
        readonly object                 _renewLock;
        Task<bool>                      _renewTask;             // protected by the lock
        long                            _cacheStaleAt;          // volatile
        long                            _nextAutoUpdateAt;      // volatile
        Dictionary<string, RSA>         _volatileKeys;          // volatile
        Timer                           _autoRenewTimer;
        MetaTime                        _failureThrottleNextRequestEarliestAt;      // task worker only
        int                             _failureThrottleNumFailedRequestsInARow;    // task worker only

        public string JWKSUrl => _jwksUrl;

        /// <summary>
        /// The product name of the service this Key Cache serves
        /// </summary>
        public readonly string ProductName;

        public JWKSPublicKeyCache(string jwksUrl, string productName)
        {
            _log = Serilog.Log.ForContext("SourceContext", GetType().Name);
            _httpClient = HttpUtil.CreateJsonHttpClient();
            _base64UrlEncoder = new JwtBase64UrlEncoder();
            _jwksUrl = jwksUrl;
            _renewLock = new object();
            _renewTask = null;
            _cacheStaleAt = 0;
            _nextAutoUpdateAt = 0;
            _volatileKeys = new Dictionary<string, RSA>();
            _failureThrottleNextRequestEarliestAt = MetaTime.Epoch;
            _failureThrottleNumFailedRequestsInARow = 0;
            ProductName = productName;
        }

        RSA GetKeyOrNull(string keyId, out object observedCache)
        {
            Dictionary<string, RSA> keys = Volatile.Read(ref _volatileKeys);
            observedCache = keys;
            if (keys.TryGetValue(keyId, out RSA value))
                return value;
            return null;
        }

        class CertFetchResult
        {
            public Dictionary<string, RSA>  Certs;
            public TimeSpan?                CacheMaxAge;
        }

        async Task<CertFetchResult> FetchCertsAsync()
        {
            using(HttpResponseMessage response = await _httpClient.GetAsync(_jwksUrl).ConfigureAwait(false))
            {
                string                      responsePayload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                TimeSpan?                   cacheDuration   = response.Headers.CacheControl?.MaxAge;
                JWKSet                      jwkSet          = JsonConvert.DeserializeObject<JWKSet>(responsePayload);
                Dictionary<string, RSA>     certById        = new Dictionary<string, RSA>();

                foreach (JWK jwk in jwkSet.keys)
                {
                    if (TryCreatePublicSigKeyFromJWK(jwk, out RSA rsa))
                        certById[jwk.kid] = rsa;
                }
                return new CertFetchResult() {  Certs = certById, CacheMaxAge = cacheDuration };
            }
        }

        bool TryCreatePublicSigKeyFromJWK(JWK jwk, out RSA outCert)
        {
            if (jwk.use != "sig" || // signatures
                jwk.kty != "RSA" || // RSA only
                jwk.alg != "RS256") // in RS256 algo
            {
                outCert = null;
                return false;
            }

            try
            {
                RSAParameters parameters = new RSAParameters();
                parameters.Exponent = _base64UrlEncoder.Decode(jwk.e);
                parameters.Modulus = _base64UrlEncoder.Decode(jwk.n);
                outCert = RSACryptoServiceProvider.Create(parameters);
                return true;
            }
            catch(Exception ex)
            {
                _log.Error("Failed to parse JWK: {Cause}", ex);
                outCert = null;
                return false;
            }
        }

        async Task RenewTaskWorkerInner()
        {
            // We fetch the result and inspect the service-reported Cache-control header and we cap it to max
            // 5 minutes. The renew-timer still uses the real cache headers, if available.
            CertFetchResult result                  = await TaskUtil.RetryUntilSuccessAsync(maxNumRetries: 3, FetchCertsAsync);
            TimeSpan?       cacheDuration           = result.CacheMaxAge;
            TimeSpan        validDuration           = Util.Min(cacheDuration ?? TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
            DateTime        cacheStaleAt            = DateTime.UtcNow + validDuration;

            // Schedule next. Next should be a bit before the expiration, but still at least
            // MinRenewPeriodMilliseconds in the future, but no longer than MaxRenewPeriod in
            // the future. The max future creates an upper bound for the updateless period.
            TimeSpan        RenewBeforeExpiration   = TimeSpan.FromMinutes(1);
            TimeSpan        MinRenewPeriod          = TimeSpan.FromSeconds(10);
            TimeSpan        MaxRenewPeriod          = TimeSpan.FromHours(1);
            DateTime        nextAutoUpdateAt;
            if (cacheDuration.HasValue)
            {
                TimeSpan timeToExpiration = cacheDuration.Value;
                TimeSpan timeToRenew = new TimeSpan(ticks: Math.Max(MinRenewPeriod.Ticks, Math.Min(MaxRenewPeriod.Ticks, timeToExpiration.Ticks - RenewBeforeExpiration.Ticks)));
                nextAutoUpdateAt = DateTime.UtcNow + timeToRenew;
            }
            else
                nextAutoUpdateAt = DateTime.UtcNow + TimeSpan.FromMinutes(5);

            _log.Information("JWKS key cache updated for {Product}. Keys in cache: {KeysInCache}. Valid for {Duration}.", ProductName, new List<string>(result.Certs.Keys), validDuration);

            // \note: order matters, see UpdateCacheAsync()
            Volatile.Write(ref _volatileKeys, result.Certs);
            Volatile.Write(ref _cacheStaleAt, cacheStaleAt.Ticks);
            Volatile.Write(ref _nextAutoUpdateAt, nextAutoUpdateAt.Ticks);
        }

        async Task<bool> RenewTaskWorker()
        {
            try
            {
                MetaDuration failureThrottle = _failureThrottleNextRequestEarliestAt - MetaTime.Now;
                if (failureThrottle > MetaDuration.Zero)
                {
                    _log.Warning("Too many failures while trying to fetch JWKS key update. Next query is throttled for {Duration}", failureThrottle);
                    await Task.Delay((int)failureThrottle.Milliseconds);
                }

                await RenewTaskWorkerInner().ConfigureAwait(false);
                _failureThrottleNumFailedRequestsInARow = 0;
                return true;
            }
            catch(Exception ex)
            {
                _log.Error("JWKS key cache update failed from {URL}: {Cause}", _jwksUrl, ex);

                _failureThrottleNumFailedRequestsInARow++;

                // 1 second first, double on each failure until reaching the cap of 64 seconds.
                MetaDuration failureThrottle = MetaDuration.FromSeconds(1 << Util.Clamp<int>(_failureThrottleNumFailedRequestsInARow, 0, 6));
                _failureThrottleNextRequestEarliestAt = MetaTime.Now + failureThrottle;
                return false;
            }
            finally
            {
                lock(_renewLock)
                {
                    _renewTask = null;
                }
            }
        }

        /// <summary>
        /// Fetches _cacheStaleAt such that the memory access is not reordered, and converts it to DateTime of UTC kind.
        /// </summary>
        DateTime FetchCacheStaleAtUtcWithMemoryOrder()
        {
            return new DateTime(ticks: Volatile.Read(ref _cacheStaleAt), DateTimeKind.Utc);
        }

        /// <summary>
        /// Fetches _nextAutoUpdateAt such that the memory access is not reordered, and converts it to DateTime of UTC kind.
        /// </summary>
        DateTime FetchNextAutoUpdateAtUtcWithMemoryOrder()
        {
            return new DateTime(ticks: Volatile.Read(ref _nextAutoUpdateAt), DateTimeKind.Utc);
        }

        enum UpdateReason
        {
            AutoRenew,
            RequestedKeyMissing,
        }
        enum RenewResult
        {
            AlreadyUpToDate,
            Updated,
            CouldNotUpdate
        }

        /// <summary>
        /// If cert cache is still observably up to date (i.e. equal to <see cref="lastObservedCache"/>), returns <see cref="RenewResult.AlreadyUpToDate"/>. Otherwise attempts to update cert
        /// cache. If update is successful, returns <see cref="RenewResult.Updated"/>. Else, returns <see cref="RenewResult.CouldNotUpdate"/>.
        /// </summary>
        async Task<RenewResult> UpdateCacheAsync(UpdateReason reason, object lastObservedCache)
        {
            // For auto-renew, we don't check timer validations. But for key-missing-requests, we
            // need to do some throttling.
            if (reason == UpdateReason.RequestedKeyMissing)
            {
                // \note: use 64bit timer to get atomic reads
                if (DateTime.UtcNow < FetchCacheStaleAtUtcWithMemoryOrder())
                {
                    // it looks like the cache is up-to-date. But did the renew just update
                    // cache? Expires-At is updated last, so if we now check the cache,
                    // it has changed if that was the case.
                    if (!Object.ReferenceEquals(lastObservedCache, Volatile.Read(ref _volatileKeys)))
                        return RenewResult.Updated;
                    else
                        return RenewResult.AlreadyUpToDate;
                }
            }

            Task<bool> renewTask;
            lock(_renewLock)
            {
                // has the renew completed after caller last looked into the cache
                if (!Object.ReferenceEquals(lastObservedCache, Volatile.Read(ref _volatileKeys)))
                    return RenewResult.Updated;

                if (_renewTask == null)
                {
                    // keep locked region small. Dont run the actual work, just enqueue an async task
                    _renewTask = Task.Run(async () => await RenewTaskWorker().ConfigureAwait(false));
                }
                renewTask = _renewTask;
            }

            if (await renewTask.ConfigureAwait(false))
                return RenewResult.Updated;
            else
                return RenewResult.CouldNotUpdate;
        }

        async Task AutoRenewTimerHandler()
        {
            // Renew
            object observedCache = Volatile.Read(ref _volatileKeys);
            _ = await UpdateCacheAsync(UpdateReason.AutoRenew, lastObservedCache: observedCache).ConfigureAwait(false);

            // Note that expiresAt can be in the past
            DateTime nextAutoUpdateAt   = FetchNextAutoUpdateAtUtcWithMemoryOrder();
            TimeSpan timeToAutoUpdate   = nextAutoUpdateAt - DateTime.UtcNow;
            TimeSpan delayToWait        = new TimeSpan(ticks: Math.Max(0, timeToAutoUpdate.Ticks));

            _log.Information("Scheduled JWKS key cache (source {URL}) auto renew in {TimeToRenew}", _jwksUrl, delayToWait);

            _autoRenewTimer.Change(delayToWait, period: TimeSpan.FromMilliseconds(-1));
        }

        /// <summary>
        /// Starts automatic renew timer that updates the key cache a bit before it expires.
        /// </summary>
        public void RenewAutomatically()
        {
            if (_autoRenewTimer == null)
            {
                _autoRenewTimer = new Timer((obj) =>
                {
                    _ = Task.Run(async () => await AutoRenewTimerHandler());
                }, null, dueTime: TimeSpan.Zero, period: TimeSpan.FromMilliseconds(-1));
            }
        }

        /// <summary>
        /// Returns the public key with the given key id. If no such key is found, throws <see cref="NoSuchKeyException"/>.
        /// If cache cannot determine whether a key exists or not, throws <see cref="KeyCacheTemporarilyUnavailable"/>.
        /// </summary>
        public async ValueTask<RSA> GetPublicKeyAsync(string keyId)
        {
            // Fast path, key is in the observable cache
            RSA fastKey = GetKeyOrNull(keyId, out object observedCache);
            if (fastKey != null)
                return fastKey;

            // The key was not in cache. Was this because we had old key in the cache?
            RenewResult renewResult = await UpdateCacheAsync(UpdateReason.RequestedKeyMissing, lastObservedCache: observedCache).ConfigureAwait(false);
            if (renewResult == RenewResult.CouldNotUpdate)
            {
                throw new KeyCacheTemporarilyUnavailable();
            }
            else if (renewResult == RenewResult.Updated)
            {
                RSA key = GetKeyOrNull(keyId, out object _);
                if (key != null)
                    return key;

                // fallthru
            }

            throw new NoSuchKeyException(keyId);
        }
    }
}
