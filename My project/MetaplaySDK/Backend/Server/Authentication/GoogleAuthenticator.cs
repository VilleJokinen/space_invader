// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication.Authenticators
{
    public abstract class GoogleAuthenticatorBase : SocialPlatformAuthenticatorBase
    {
        readonly static HttpClient s_httpClient = HttpUtil.CreateJsonHttpClient();

        /// <summary>
        /// Decoded parts of of a Google JWT token.
        /// </summary>
        class GoogleTokenResponse
        {
            // Example response:
            // {
            //   "iss": "https://accounts.google.com",
            //   "azp": "123456789012-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com",
            //   "aud": "123456789012-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com",
            //   "sub": "123456789012345678901",
            //   "iat": "1573814708",
            //   "exp": "1573818308",
            //   "alg": "RS256",
            //   "kid": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
            //   "typ": "JWT"
            // }

            [JsonProperty("iss")] public string Issuer      { get; set; }
            [JsonProperty("aud")] public string Audience    { get; set; }
            [JsonProperty("sub")] public string Subject     { get; set; }
            [JsonProperty("exp")] public string ExpiresAt   { get; set; }
            [JsonProperty("iat")] public string IssuedAt    { get; set; }

            [JsonProperty("error_description")] public string ErrorDescription { get; set; }
        }

        static async ValueTask<GoogleTokenResponse> ProcessGoogleJWTToken(string token)
        {
            return await ParseJWTAsync<GoogleTokenResponse>(token, GoogleOAuth2PublicKeyCache.Instance);
        }

        /// <summary>
        /// Check a Google authentication token and (if valid) return its subject.
        /// </summary>
        protected static async Task<string> ValidateGoogleJTWTokenSubjectAsync(string authToken)
        {
            GooglePlayStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();
            if (!storeOpts.EnableGoogleAuthentication)
                throw new AuthenticationError($"Google Play authentication is disabled in {nameof(GooglePlayStoreOptions)}");

            // Validate the token
            GoogleTokenResponse response = await ProcessGoogleJWTToken(authToken).ConfigureAwait(false);

            //DebugLog.Info("ProcessGoogleJWTToken returned: {0}", PrettyPrint.Verbose(response));

            // Check if error is specified
            if (!string.IsNullOrEmpty(response.ErrorDescription))
                throw new AuthenticationError($"Google returned error: {response.ErrorDescription}");

            // Check issuer
            if (response.Issuer != "accounts.google.com" && response.Issuer != "https://accounts.google.com")
                throw new AuthenticationError($"Invalid token issuer {response.Issuer}, excepting '(https://)accounts.google.com'");

            // Check audience
            if (response.Audience != storeOpts.GooglePlayClientId)
                throw new AuthenticationError($"Invalid audience (client id) in claim: {response.Audience}, expecting {storeOpts.GooglePlayClientId}");

            // Check expiration
            const long ExpiryLeniencySeconds = 15 * 60; // accept tokens 15min after expiring
            long curUnixTime = Util.GetUtcUnixTimeSeconds();
            long expiresAt = Convert.ToInt64(response.ExpiresAt, CultureInfo.InvariantCulture);
            if (curUnixTime > expiresAt + ExpiryLeniencySeconds)
                throw new AuthenticationError($"Token expired at {expiresAt} ({curUnixTime - expiresAt}s ago, curTime={curUnixTime}");

            // Check subject
            if (string.IsNullOrEmpty(response.Subject))
                throw new AuthenticationError($"Subject is missing in validated idToken");

            return response.Subject;
        }

        /// <summary>
        /// Returns if the userId is legacy-style Google+ user id. Google+ user IDs are 21-digit numbers.
        /// </summary>
        protected static bool IsGooglePlusAccountId(string userId)
        {
            if (userId.Length != 21)
                return false;
            foreach (char c in userId)
            {
                if (c < '0' || c > '9')
                    return false;
            }
            return true;
        }

        protected readonly struct GoogleOAuth2UserIds
        {
            public readonly string VerifiedPlayerId;
            public readonly string OriginalPlayerIdOrNull;
            public readonly string AlternatePlayerIdOrNull;
            public readonly string PlayerIdOrNull;

            public GoogleOAuth2UserIds(string verifiedPlayerId, string originalPlayerIdOrNull, string alternatePlayerIdOrNull, string playerIdOrNull)
            {
                // canonize empties into nulls
                VerifiedPlayerId        = string.IsNullOrEmpty(verifiedPlayerId)        ? null : verifiedPlayerId;
                OriginalPlayerIdOrNull  = string.IsNullOrEmpty(originalPlayerIdOrNull)  ? null : originalPlayerIdOrNull;
                AlternatePlayerIdOrNull = string.IsNullOrEmpty(alternatePlayerIdOrNull) ? null : alternatePlayerIdOrNull;
                PlayerIdOrNull          = string.IsNullOrEmpty(playerIdOrNull)          ? null : playerIdOrNull;
            }
        }

        struct AppVerifyResponse
        {
            /// <summary>
            /// Must be "games#applicationVerifyResponse"
            /// </summary>
            [JsonProperty("kind")]
            public string Kind { get; set; }

            [JsonProperty("player_id")]
            public string PlayerId { get; set; }

            [JsonProperty("alternate_player_id")]
            public string AlternatePlayerId { get; set; }
        }

        struct PlayerResponse
        {
            /// <summary>
            /// Must be "games#player"
            /// </summary>
            [JsonProperty("kind")]
            public string Kind { get; set; }

            [JsonProperty("playerId")]
            public string PlayerId { get; set; }

            [JsonProperty("originalPlayerId")]
            public string OriginalPlayerId { get; set; }
        }

        struct OAuth2TokenResponse
        {
            [JsonProperty("access_token")] public string AccessToken { get; set; }
            [JsonProperty("token_type")] public string TokenType { get; set; }
        }

        protected static async Task<GoogleOAuth2UserIds> GetGamesOAuth2UserIdsAsync(string oauth2ServerAuthCode)
        {
            GooglePlayStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();
            if (!storeOpts.EnableGoogleAuthentication)
                throw new AuthenticationError($"Google Play authentication is disabled in {nameof(GooglePlayStoreOptions)}");

            // Get AccessToken with the AuthorizationCode
            OAuth2TokenResponse tokenResponse;
            using (HttpRequestMessage tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token"))
            {
                tokenRequest.Content = new FormUrlEncodedContent(new OrderedDictionary<string,string>() {
                    { "grant_type", "authorization_code" },
                    { "code", oauth2ServerAuthCode },
                    { "client_id", storeOpts.GooglePlayClientId },
                    { "client_secret", storeOpts.GooglePlayClientSecret },
                    });
                tokenRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                tokenResponse = await HttpUtil.RequestAsync<OAuth2TokenResponse>(s_httpClient, tokenRequest);
            }

            if (!string.Equals(tokenResponse.TokenType, "bearer", StringComparison.InvariantCultureIgnoreCase))
                throw new InvalidOperationException($"Invalid token type from google player games. Got: {tokenResponse.TokenType}");
            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
                throw new InvalidOperationException($"Invalid access token from google player games. Got: {tokenResponse.AccessToken}");

            // Verify the AccessToken
            AppVerifyResponse verifyResponse;
            using (HttpRequestMessage verifyRequest = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/games/v1/applications/{storeOpts.GooglePlayApplicationId}/verify"))
            {
                verifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
                verifyResponse = await HttpUtil.RequestAsync<AppVerifyResponse>(s_httpClient, verifyRequest);
            }

            if (verifyResponse.Kind != "games#applicationVerifyResponse")
                throw new InvalidOperationException($"Invalid response kind from google player games. Got: {verifyResponse.Kind}");
            if (string.IsNullOrEmpty(verifyResponse.PlayerId))
                throw new InvalidOperationException($"Invalid PlayerID from google player games. Got: {verifyResponse.PlayerId}");

            // Finally, inspect the player itself for originalPlayerId.

            PlayerResponse playerResponse;
            using (HttpRequestMessage playerRequest = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/games/v1/players/me"))
            {
                playerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
                playerResponse = await HttpUtil.RequestAsync<PlayerResponse>(s_httpClient, playerRequest);
            }

            if (playerResponse.Kind != "games#player")
                throw new InvalidOperationException($"Invalid response kind from google player games. Got: {playerResponse.Kind}");

            return new GoogleOAuth2UserIds(
                verifyResponse.PlayerId,
                playerResponse.OriginalPlayerId,
                verifyResponse.AlternatePlayerId,
                playerResponse.PlayerId);
        }
    }

    public class GoogleSignInAuthenticator : GoogleAuthenticatorBase
    {
        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimGoogleSignIn googleSignIn)
        {
            string subject = await ValidateGoogleJTWTokenSubjectAsync(googleSignIn.IdToken);
            return AuthenticatedSocialClaimKeys.FromSingleKey(new AuthenticationKey(AuthenticationPlatform.GoogleSignIn, subject));
        }
    }

    public class GooglePlayV1Authenticator : GoogleAuthenticatorBase
    {
        //static readonly IMetaLogger _log = MetaLogger.ForContext<GooglePlayV1Authenticator>();

        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimGooglePlayV1 googlePlayV1)
        {
            string subject = await ValidateGoogleJTWTokenSubjectAsync(googlePlayV1.IdToken);

            // Legacy path, just Id token is given
            if (string.IsNullOrEmpty(googlePlayV1.ServerAuthCode))
                return AuthenticatedSocialClaimKeys.FromSingleKey(new AuthenticationKey(AuthenticationPlatform.GooglePlay, subject));

            // Upgrade path.
            GoogleOAuth2UserIds userIds = await GetGamesOAuth2UserIdsAsync(googlePlayV1.ServerAuthCode);

            OrderedSet<AuthenticationKey> authKeys = new OrderedSet<AuthenticationKey>();
            authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, subject));
            if (!string.IsNullOrEmpty(userIds.OriginalPlayerIdOrNull))  authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.OriginalPlayerIdOrNull));
                                                                        authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.VerifiedPlayerId));
            if (!string.IsNullOrEmpty(userIds.AlternatePlayerIdOrNull)) authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.AlternatePlayerIdOrNull));
            if (!string.IsNullOrEmpty(userIds.PlayerIdOrNull))          authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.PlayerIdOrNull));

            // Prioritize google+ accounts first, then choose google play games
            AuthenticationKey[] allKeys = authKeys.Where(key => IsGooglePlusAccountId(key.Id)).Concat(authKeys.Where(key => !IsGooglePlusAccountId(key.Id))).ToArray();
            return AuthenticatedSocialClaimKeys.FromPrimaryAndSecondaryKeys(
                primaryAuthenticationKey: allKeys[0],
                secondaryAuthenticationKeys: allKeys[1..]);
        }
    }

    public class GooglePlayV2Authenticator : GoogleAuthenticatorBase
    {
        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimGooglePlayV2 googlePlayV2)
        {
            GoogleOAuth2UserIds userIds = await GetGamesOAuth2UserIdsAsync(googlePlayV2.ServerAuthCode);

            // Combine all possible Ids. First key is the primary, the rest are secondary. Priority is given to the OriginalPlayerId if such is present.

            OrderedSet<AuthenticationKey> authKeys = new OrderedSet<AuthenticationKey>();
            if (!string.IsNullOrEmpty(userIds.OriginalPlayerIdOrNull))  authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.OriginalPlayerIdOrNull));
                                                                        authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.VerifiedPlayerId));
            if (!string.IsNullOrEmpty(userIds.AlternatePlayerIdOrNull)) authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.AlternatePlayerIdOrNull));
            if (!string.IsNullOrEmpty(userIds.PlayerIdOrNull))          authKeys.Add(new AuthenticationKey(AuthenticationPlatform.GooglePlay, userIds.PlayerIdOrNull));

            AuthenticationKey[] allKeys = authKeys.ToArray();
            return AuthenticatedSocialClaimKeys.FromPrimaryAndSecondaryKeys(
                primaryAuthenticationKey: allKeys[0],
                secondaryAuthenticationKeys: allKeys[1..]);
        }
    }
}
