// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication.Authenticators
{
    public class SignInWithAppleAuthenticator : SocialPlatformAuthenticatorBase
    {
        /// <summary>
        /// Contents of a Sign in with Apple authentication token.
        /// </summary>
        class SignInWithAppleToken
        {
            [JsonProperty("iss")]               public string   Issuer          { get; set; }
            [JsonProperty("aud")]               public string   Audience        { get; set; }
            [JsonProperty("sub")]               public string   Subject         { get; set; }
            [JsonProperty("exp")]               public string   ExpiresAt       { get; set; }
            [JsonProperty("iat")]               public string   IssuedAt        { get; set; }
            [JsonProperty("nonce")]             public string   Nonce           { get; set; }
            [JsonProperty("nonce_supported")]   public bool     NonceSupported  { get; set; }
            [JsonProperty("email")]             public string   Email           { get; set; }
            [JsonProperty("email_verified")]    public object   EmailVerified   { get; set; } // bool true, or string "true"
            [JsonProperty("is_private_email")]  public object   IsPrivateEmail  { get; set; } // bool, or string "true" or string "false"
            [JsonProperty("real_user_status")]  public int      RealUserStatus  { get; set; }
        }

        static async ValueTask<SignInWithAppleToken> ProcessSignInWithAppleJWTToken(string token)
        {
            return await ParseJWTAsync<SignInWithAppleToken>(token, AppleSignInPublicKeyCache.Instance);
        }

        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimSignInWithApple signInWithApple)
        {
            AppleStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AppleStoreOptions>();
            if (!storeOpts.EnableAppleAuthentication)
                throw new AuthenticationError($"Sign in with Apple is disabled {nameof(AppleStoreOptions)}");

            // Validate the token
            SignInWithAppleToken response = await ProcessSignInWithAppleJWTToken(signInWithApple.IdentityToken).ConfigureAwait(false);

            // \todo: would be ideal to give the client a nonce. But we cannot tie anything to either the player or
            // the session as both can change when we are doing social authentications. We could use some global state
            // like date-time rounded to the second and validate it, but that is pretty weak.
            // Option: What if we give each client a Signed random token in hello, and that or derivative is the nonce.
            // Here we just check the signature? It guarantees the original request came from the server, but a hostile
            // MitM could just replay that as well. But what if we sign clients random in it's hello (mixed with our random)?
            // \note: OpenID's nonces are to protect client side from replay, not us. WTF?
            // \note: Unity's official apple sign in integration does not support setting ASAuthorizationOpenIDRequest.nonce, ignored for now.
            //if (response.NonceSupported)
            //{
            //  Validate(response.Nonce);
            //}

            // Check issuer
            if (response.Issuer != "https://appleid.apple.com")
                throw new AuthenticationError($"Invalid token issuer {response.Issuer}, excepting 'https://appleid.apple.com'");

            // Check audience
            if (response.Audience != storeOpts.IosBundleId)
                throw new AuthenticationError($"Invalid audience (client id) in claim: {response.Audience}, expecting {storeOpts.IosBundleId}");

            // Check expiration
            const long ExpiryLeniencySeconds = 15 * 60; // accept tokens 15min after expiring
            long curUnixTime = Util.GetUtcUnixTimeSeconds();
            long expiresAt = Convert.ToInt64(response.ExpiresAt, CultureInfo.InvariantCulture);
            if (curUnixTime > expiresAt + ExpiryLeniencySeconds)
                throw new AuthenticationError($"Token expired at {expiresAt} ({curUnixTime - expiresAt}s ago, curTime={curUnixTime}");

            // Check subject
            if (string.IsNullOrEmpty(response.Subject))
                throw new AuthenticationError($"Subject is missing in validated idToken");

            // \todo: if account transfer in progress, should also login to "transfer_sub" as a separate platform or convert it to real it and treat it as a secondary auth key

            // \note Subject from response is used as userId instead of claimedUserId
            return AuthenticatedSocialClaimKeys.FromSingleKey(new AuthenticationKey(AuthenticationPlatform.SignInWithApple, response.Subject));
        }
    }
}
