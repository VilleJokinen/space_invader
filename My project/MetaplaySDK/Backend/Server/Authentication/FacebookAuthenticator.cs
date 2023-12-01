// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication.Authenticators
{
    public class FacebookAuthenticator : SocialPlatformAuthenticatorBase
    {
        /// <summary>
        /// Contents of a Facebook Login OIDC token.
        /// </summary>
        class FacebookLoginOIDCToken
        {
            [JsonProperty("iss")]               public string   Issuer          { get; set; }
            [JsonProperty("aud")]               public string   Audience        { get; set; }
            [JsonProperty("sub")]               public string   Subject         { get; set; }
            [JsonProperty("exp")]               public string   ExpiresAt       { get; set; }
            [JsonProperty("iat")]               public string   IssuedAt        { get; set; }
            [JsonProperty("nonce")]             public string   Nonce           { get; set; }
            [JsonProperty("jti")]               public string   JwtId           { get; set; }
        }

        static async ValueTask<FacebookLoginOIDCToken> TryProcessFacebookLoginOIDCToken(string token)
        {
            try
            {
                return await ParseJWTAsync<FacebookLoginOIDCToken>(token, FacebookLoginPublicKeyCache.Instance);
            }
            catch(InvalidOperationException)
            {
                // token was malformed. So maybe it was not a JWT token at all?
                return null;
            }
        }

        static async ValueTask<string> TryValidateLoginOIDCToken(string token)
        {
            FacebookOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<FacebookOptions>();
            if (!options.LoginEnabled)
                throw new FacebookLoginService.LoginServiceNotEnabledException();

            FacebookLoginOIDCToken oidcToken = await TryProcessFacebookLoginOIDCToken(token).ConfigureAwait(false);
            if (oidcToken == null)
                return null;

            // \todo: would be ideal to give the client a nonce. But see the discussion in AuthenticateSignInWithAppleAsync.

            // Check issuer
            if (oidcToken.Issuer != FacebookLoginService.OpenIdIssuer)
                throw new AuthenticationError($"Invalid token issuer {oidcToken.Issuer}, expecting '{FacebookLoginService.OpenIdIssuer}'");

            // Check audience
            if (oidcToken.Audience != options.AppId)
                throw new AuthenticationError($"Invalid audience (client id) in claim: {oidcToken.Audience}, expecting {options.AppId}");

            // Check expiration
            const long ExpiryLeniencySeconds = 15 * 60; // accept tokens 15min after expiring
            long curUnixTime = Util.GetUtcUnixTimeSeconds();
            long expiresAt = Convert.ToInt64(oidcToken.ExpiresAt, CultureInfo.InvariantCulture);
            if (curUnixTime > expiresAt + ExpiryLeniencySeconds)
                throw new AuthenticationError($"Token expired at {expiresAt} ({curUnixTime - expiresAt}s ago, curTime={curUnixTime}");

            // Check subject
            if (string.IsNullOrEmpty(oidcToken.Subject))
                throw new AuthenticationError($"Subject is missing in validated idToken");

            // \note Subject from response is used as userId instead of claimedUserId
            return oidcToken.Subject;
        }

        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimFacebookLogin facebookLogin)
        {
            try
            {
                // Validate the token as OIDC token. Note that this will throw if token is Well-formed OIDC token, but invalid.
                string oidcTokenUserId = await TryValidateLoginOIDCToken(facebookLogin.AccessTokenOrOIDCToken).ConfigureAwait(false);
                if (oidcTokenUserId != null)
                    return AuthenticatedSocialClaimKeys.FromSingleKey(new AuthenticationKey(AuthenticationPlatform.FacebookLogin, oidcTokenUserId));

                // Not OIDC token, handle as AccessToken
                FacebookLoginService.FacebookUserAccessToken response = await FacebookLoginService.Instance.ValidateUserAccessTokenAsync(facebookLogin.AccessTokenOrOIDCToken);
                return AuthenticatedSocialClaimKeys.FromSingleKey(new AuthenticationKey(AuthenticationPlatform.FacebookLogin, response.UserId));
            }
            catch(FacebookLoginService.LoginServiceNotEnabledException ex)
            {
                throw new AuthenticationError(ex.Message);
            }
            catch(FacebookLoginService.InvalidAccessTokenException)
            {
                // Since token is not valid, it is safe to print it. (No personal information)
                throw new AuthenticationError($"Token is not valid: {facebookLogin.AccessTokenOrOIDCToken}");
            }
            catch(FacebookLoginService.IncorrectAppIdException ex)
            {
                throw new AuthenticationError($"Invalid AppId in claim: {ex.ClaimAppId}, expecting {ex.ExpectedAppId}");
            }
            catch(FacebookLoginService.LoginTemporarilyUnavailableException)
            {
                throw new AuthenticationTemporarilyUnavailable($"Facebook sign in temporarily unavailable");
            }
        }
    }
}
