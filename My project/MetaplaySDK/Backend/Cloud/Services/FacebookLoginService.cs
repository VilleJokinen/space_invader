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
    // \todo: figure out a nice place for this
    // \todo: figure out how AppSecret could come from aws secrets manager.
    [RuntimeOptions("Facebook", isStatic: false, "Configuration options for Facebook integration.")]
    public class FacebookOptions : RuntimeOptionsBase
    {
        [MetaDescription("If enabled, validate social login claims that use Facebook tokens. Otherwise, Facebook social login claims are immediately rejected.")]
        public bool     LoginEnabled    { get; set; }

        [MetaDescription("This game's Facebook project `App ID`.")]
        public string   AppId           { get; set; }

        [MetaDescription("This game's Facebook project `App Secret`.")]
        public string   AppSecret       { get; set; }
    }

    /// <summary>
    /// Service for Facebook Login API.
    /// </summary>
    public class FacebookLoginService
    {
        public static readonly string OpenIdIssuer = "https://www.facebook.com";

        static Lazy<FacebookLoginService> s_instance = new Lazy<FacebookLoginService>(() => new FacebookLoginService());
        public static FacebookLoginService Instance => s_instance.Value;

        /// <summary>
        /// Access Token was created for some other AppId. It is not valid for this App.
        /// </summary>
        public class IncorrectAppIdException : Exception
        {
            public readonly string ClaimAppId;
            public readonly string ExpectedAppId;

            public IncorrectAppIdException(string claimAppId, string expectedAppId)
            {
                ClaimAppId = claimAppId;
                ExpectedAppId = expectedAppId;
            }
        }

        /// <summary>
        /// Login service is disabled in Options.
        /// </summary>
        public class LoginServiceNotEnabledException : Exception
        {
        }

        /// <summary>
        /// Token was not a valid access token.
        /// </summary>
        public class InvalidAccessTokenException : Exception
        {
            public readonly string AccessToken;

            public InvalidAccessTokenException(string accessToken)
            {
                AccessToken = accessToken;
            }
        }

        /// <summary>
        /// Validity of token could not checked due to temporary error.
        /// </summary>
        public class LoginTemporarilyUnavailableException : Exception
        {
            public LoginTemporarilyUnavailableException()
            {
            }
        }

        class InspectTokenResponseJson
        {
            public class Error
            {
                public string   message         = default;
                public string   type            = default;
                public int      code            = default;
                public int      error_subcode   = default;
            }
            public class Data
            {
                public string   user_id     = default;
                public string   app_id      = default;
                public bool     is_valid    = default;
            }

            public Data     data        = default;
            public Error    error       = default;
        }
        class RevokeUserResponseJson
        {
            public bool     success     = false;
        }

        readonly HttpClient                         _httpClient      = HttpUtil.CreateJsonHttpClient();
        readonly Serilog.ILogger                    _log             = Serilog.Log.ForContext<FacebookLoginService>();

        FacebookLoginService()
        {
        }

        string CreateProofString(string userAccessToken, string appSecret)
        {
            byte[] key = Encoding.UTF8.GetBytes(appSecret);
            byte[] content = Encoding.UTF8.GetBytes(userAccessToken);
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                byte[] hash = hmac.ComputeHash(content);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }

        public readonly struct FacebookUserAccessToken
        {
            public readonly string UserId;

            public FacebookUserAccessToken(string userId)
            {
                UserId = userId;
            }
        }

        /// <summary>
        /// Revokes a user login into this Facebook App. This logs the game client out of Facebook. To login with Facebook
        /// into the game, the user must re-authorize this game.
        /// <para>
        /// On success, returns true. On failure, returns false.
        /// The error codes match the underlying API ( https://developers.facebook.com/docs/facebook-login/permissions/requesting-and-revoking#revokelogin )
        /// </para>
        /// </summary>
        public async Task<bool> RevokeUserLoginAsync(string userId)
        {
            FacebookOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<FacebookOptions>();
            if (!options.LoginEnabled)
                return false;

            FacebookAppService.AppAccessToken appAccessToken;
            try
            {
                appAccessToken = await FacebookAppService.Instance.GetAppAccessTokenAsync(options);
            }
            catch
            {
                return false;
            }

            string uri =
                $"https://graph.facebook.com/{userId}/permissions" +
                $"?access_token={HttpUtility.UrlEncode(appAccessToken.AccessToken)}";

            try
            {
                using(HttpResponseMessage reply = await _httpClient.DeleteAsync(uri).ConfigureAwait(false))
                {
                    string                      responsePayload = await reply.Content.ReadAsStringAsync().ConfigureAwait(false);
                    RevokeUserResponseJson      response        = JsonConvert.DeserializeObject<RevokeUserResponseJson>(responsePayload);

                    reply.EnsureSuccessStatusCode();

                    return response.success;
                }
            }
            catch(Exception ex)
            {
                // could not revoke
                _log.Warning("Could not revoke Facebook login: {Cause}", ex);
                return false;
            }
        }

        /// <summary>
        /// Inspects a user access token with Facebook API. On success, returns a <see cref="FacebookUserAccessToken"/>
        /// of the user access token. On Failure, throws <see cref="LoginServiceNotEnabledException"/>,
        /// <see cref="LoginTemporarilyUnavailableException"/>, <see cref="InvalidAccessTokenException"/> or <see cref="IncorrectAppIdException"/>.
        /// </summary>
        public async Task<FacebookUserAccessToken> ValidateUserAccessTokenAsync(string userAccessToken)
        {
            FacebookOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<FacebookOptions>();
            if (!options.LoginEnabled)
                throw new LoginServiceNotEnabledException();

            string  proof       = CreateProofString(userAccessToken, options.AppSecret);
            int     numRetries  = 0;

            for(;;)
            {
                FacebookAppService.AppAccessToken appAccessToken;
                try
                {
                    appAccessToken = await FacebookAppService.Instance.GetAppAccessTokenAsync(options);
                }
                catch
                {
                    // could not get access key. Cannot continue.
                    throw new LoginTemporarilyUnavailableException();
                }

                // Inspect token

                string uri =
                    $"https://graph.facebook.com/debug_token" +
                    $"?input_token={HttpUtility.UrlEncode(userAccessToken)}" +
                    $"&access_token={HttpUtility.UrlEncode(appAccessToken.AccessToken)}" +
                    $"&appsecret_proof={HttpUtility.UrlEncode(proof)}";

                try
                {
                    using(HttpResponseMessage reply = await _httpClient.GetAsync(uri).ConfigureAwait(false))
                    {
                        string                      responsePayload = await reply.Content.ReadAsStringAsync().ConfigureAwait(false);
                        InspectTokenResponseJson    response        = JsonConvert.DeserializeObject<InspectTokenResponseJson>(responsePayload);

                        // check error first for better error messages (and then Status)

                        if (response.error != null)
                        {
                            // If access key is rejected, it could be because our access key is expired. Try again, once, with a new access key
                            bool appAccessKeyIsRejected;
                            if (response.error.code == 190)
                                appAccessKeyIsRejected = true;
                            else if (response.error.code == 102)
                            {
                                switch (response.error.error_subcode)
                                {
                                    case 0:
                                    case 463:
                                    case 467:
                                        appAccessKeyIsRejected = true;
                                        break;

                                    default:
                                        appAccessKeyIsRejected = false;
                                        break;
                                }
                            }
                            else
                                appAccessKeyIsRejected = false;

                            if (appAccessKeyIsRejected && MetaTime.Now >= appAccessToken.AllowRenewAfter && numRetries < 1)
                            {
                                _log.Information("Facebook app access key was rejected, refreshing token.");

                                numRetries++;
                                FacebookAppService.Instance.InvalidateAppAccessToken(appAccessToken);
                                continue;
                            }

                            _log.Debug("Facebook user access token validation attempt was refused: {Message}", response.error.message);
                            throw new LoginTemporarilyUnavailableException();
                        }

                        reply.EnsureSuccessStatusCode();

                        // sanity
                        if (response.data == null)
                            throw new InvalidOperationException("Facebook api response has invalid structure. Missing data.");

                        if (!response.data.is_valid)
                            throw new InvalidAccessTokenException(userAccessToken);
                        if (response.data.app_id != options.AppId)
                            throw new IncorrectAppIdException(response.data.app_id, options.AppId);

                        // sanity
                        if (string.IsNullOrWhiteSpace(response.data.user_id))
                            throw new InvalidOperationException("Facebook api response has invalid user_id");

                        return new FacebookUserAccessToken(userId: response.data.user_id);
                    }
                }
                catch(Exception ex)
                {
                    // could not validate. Side on error.
                    _log.Warning("Could not validate Facebook login token: {Cause}", ex);
                    throw new LoginTemporarilyUnavailableException();
                }

                // unreachable
            }
        }
    }
}
