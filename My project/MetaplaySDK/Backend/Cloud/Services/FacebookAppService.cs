// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Metaplay.Cloud.Services
{
    /// <summary>
    /// Provides Facebook App Access token.
    /// </summary>
    public class FacebookAppService
    {
        static Lazy<FacebookAppService> s_instance = new Lazy<FacebookAppService>(() => new FacebookAppService());
        public static FacebookAppService Instance => s_instance.Value;

        public class CannotGetAppAccessTokenException : Exception
        {
            public CannotGetAppAccessTokenException(string message) : base(message)
            {
            }
            public CannotGetAppAccessTokenException(Exception innerException) : base("error while fetching access token", innerException)
            {
            }
        }

        class AccessTokenResponseJson
        {
            public class Error
            {
                public string   message = default;
                public string   type    = default;
                public int      code    = default;
            }

            public string   access_token    = default;
            public int?     expires_in      = default;
            public Error    error           = default;
        }
        public class AppAccessToken
        {
            public readonly string      AccessToken;
            public readonly MetaTime    AllowRenewAfter;

            public AppAccessToken(string accessToken, MetaTime allowRenewAfter)
            {
                AccessToken = accessToken;
                AllowRenewAfter = allowRenewAfter;
            }
        }
        struct AppAccessFetchResult
        {
            public readonly AppAccessToken                      AccessKey;
            public readonly CannotGetAppAccessTokenException    Error;
            public readonly object                              Options;

            public AppAccessFetchResult(AppAccessToken accessKey, CannotGetAppAccessTokenException error, object options)
            {
                AccessKey = accessKey;
                Error = error;
                Options = options;
            }
        }
        class Box<T>
        {
            public readonly T Value;
            public Box(T value)
            {
                Value = value;
            }
        }

        readonly HttpClient                         _httpClient      = HttpUtil.CreateJsonHttpClient();
        readonly object                             _accessKeyLock   = new object();
        readonly Serilog.ILogger                    _log             = Serilog.Log.ForContext<FacebookLoginService>();

        volatile Box<AppAccessFetchResult>          _appAccessKey;
        Task<AppAccessFetchResult>                  _accessKeyFetcherTask;

        FacebookAppService()
        {
        }

        async Task<AppAccessFetchResult> DoGetAppAccessTokenAsync(FacebookOptions options)
        {
            string appId = options.AppId;
            string appSecret = options.AppSecret;

            CannotGetAppAccessTokenException    resultError;
            AppAccessToken                      resultKey;

            try
            {
                string uri =
                    $"https://graph.facebook.com/oauth/access_token" +
                    $"?client_id={HttpUtility.UrlEncode(appId)}" +
                    $"&client_secret={HttpUtility.UrlEncode(appSecret)}" +
                    $"&grant_type=client_credentials";

                using(HttpResponseMessage reply = await _httpClient.GetAsync(uri).ConfigureAwait(false))
                {
                    string                  responsePayload = await reply.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AccessTokenResponseJson response        = JsonConvert.DeserializeObject<AccessTokenResponseJson>(responsePayload);

                    // check error first for better error messages, and Status code only last

                    if (response.error != null)
                        throw new CannotGetAppAccessTokenException(response.error.message);

                    reply.EnsureSuccessStatusCode();

                    // Allow renew one minute before expires_in, or 5 minutes if no other info is available
                    // If expiration is less than a minute away, allow renew at halfway.
                    MetaTime allowRenewAfter;
                    if (response.expires_in == null || response.expires_in.Value <= 0)
                        allowRenewAfter = MetaTime.Now + MetaDuration.FromMinutes(5);
                    else if (response.expires_in.Value <= 60)
                        allowRenewAfter = MetaTime.Now + MetaDuration.FromMilliseconds(response.expires_in.Value * 1000 / 2);
                    else
                        allowRenewAfter = MetaTime.Now + MetaDuration.FromSeconds(response.expires_in.Value - 60);

                    resultKey = new AppAccessToken(response.access_token, allowRenewAfter);
                    resultError = null;

                    _log.Information("Updated Facebook access token. Earliest possible expiration set to {EarliestExpireAt}.", allowRenewAfter);
                }
            }
            catch (CannotGetAppAccessTokenException ex)
            {
                _log.Error("Failed to get Facebook access token: {Cause}", ex);

                resultKey = null;
                resultError = ex;
            }
            catch (Exception ex)
            {
                _log.Error("Failed to get Facebook access token: {Cause}", ex);

                resultKey = null;
                resultError = new CannotGetAppAccessTokenException(ex);
            }

            AppAccessFetchResult result = new AppAccessFetchResult(resultKey, resultError, options);

            lock (_accessKeyLock)
            {
                _appAccessKey = new Box<AppAccessFetchResult>(result);
            }

            return result;
        }

        /// <summary>
        /// Returns app access token, or throws <see cref="CannotGetAppAccessTokenException"/>.
        /// </summary>
        /// <remarks>The parameter <paramref name="options"/> must be given to avoid potential races on callsite.</remarks>
        public async ValueTask<AppAccessToken> GetAppAccessTokenAsync(FacebookOptions options)
        {
            // opportunistic
            Box<AppAccessFetchResult> oppostunisticResult = _appAccessKey;
            if (oppostunisticResult != null && ReferenceEquals(options, oppostunisticResult.Value.Options))
            {
                if (oppostunisticResult.Value.AccessKey != null)
                    return oppostunisticResult.Value.AccessKey;
                if (oppostunisticResult.Value.Error != null)
                    throw oppostunisticResult.Value.Error;
            }

            // resolve or wait for ongoing resolve

            for (;;)
            {
                Task<AppAccessFetchResult> ongoingTask;
                lock (_accessKeyLock)
                {
                    Box<AppAccessFetchResult> syncedResult = _appAccessKey;
                    if (syncedResult != null && ReferenceEquals(options, syncedResult.Value.Options))
                    {
                        if (syncedResult.Value.AccessKey != null)
                            return syncedResult.Value.AccessKey;
                        if (syncedResult.Value.Error != null)
                            throw syncedResult.Value.Error;
                    }

                    if (_accessKeyFetcherTask == null)
                        _accessKeyFetcherTask = DoGetAppAccessTokenAsync(options);
                    ongoingTask = _accessKeyFetcherTask;
                }

                AppAccessFetchResult result = await ongoingTask;

                // if ongoing result completed, but it was for some other request, try again.
                if (ReferenceEquals(options, result.Options))
                {
                    if (result.AccessKey != null)
                        return result.AccessKey;
                    else
                        throw result.Error;
                }
            }
        }

        /// <summary>
        /// Invalidates cached access token. Next call to <see cref="GetAppAccessTokenAsync"/> will cause refetch if
        /// the current token is <paramref name="oldToken"/>. If current token is different (i.e. already refreshed), this
        /// call does nothing.
        /// This is used to force refresh access token if Facebook API call returns a failure indicating stale Access
        /// Token.
        /// </summary>
        public void InvalidateAppAccessToken(AppAccessToken oldToken)
        {
            lock (_accessKeyLock)
            {
                // already in progress of creating a new
                if (_accessKeyFetcherTask != null)
                    return;

                // already invalidated updated
                Box<AppAccessFetchResult> syncedResult = _appAccessKey;
                if (syncedResult == null || syncedResult.Value.AccessKey != oldToken)
                    return;

                _appAccessKey = null;
            }
        }

        /// <summary>
        /// Starts app access token refresh if there is no cached token.
        /// </summary>
        public void PrefetchAppAccessToken()
        {
            FacebookOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<FacebookOptions>();
            _ = GetAppAccessTokenAsync(options);
        }
    }
}
