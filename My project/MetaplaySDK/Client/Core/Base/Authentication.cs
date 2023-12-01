// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.Model;
using System;

namespace Metaplay.Core
{
    /// <summary>
    /// Authentication method type. Either DeviceId/AuthToken pair, or some external method like Google Play or Game Center user.
    /// </summary>
    /// \note: both the names and values of the entries are part of the persisted state. Do not modify them
    [MetaSerializable]
    public enum AuthenticationPlatform
    {
        /// <summary>
        /// Authentication using deviceId and authToken (similar to username/password flow).
        /// </summary>
        DeviceId = 0,

        /// <summary>
        /// Emulated social platform, to help testing/debugging social authentication flow.
        /// </summary>
        Development = 1,

        /// <summary>
        /// Google Play Games
        /// </summary>
        GooglePlay = 2,

        /// <summary>
        /// Apple Game Center
        /// </summary>
        GameCenter = 3,

        /// <summary>
        /// Google Sign-In
        /// </summary>
        GoogleSignIn = 4,

        /// <summary>
        /// Sign in with Apple
        /// </summary>
        SignInWithApple = 5,

        /// <summary>
        /// Facebook Login
        /// </summary>
        FacebookLogin = 6,

        /// <summary>
        /// (allocated, not implemented) Sign in with Apple, in team-migration mode (transfer token)
        /// </summary>
        SignInWithAppleTransfer = 7,

        /// <summary>
        /// Apple Game Center, with WWDC2020 changes. This is a Team-scoped player Id.
        /// </summary>
        GameCenter2020 = 8,

        /// <summary>
        /// Apple Game Center, with WWDC2020 changes, using unauthenticated GamePlayerId for team-migration mode. This is app scoped.
        /// </summary>
        GameCenter2020UAGT = 9,

        /// <summary>
        /// (allocated)
        /// </summary>
        _ReservedDontUse1 = 10,

        /// <summary>
        /// Ethereum account.
        /// </summary>
        Ethereum = 11,

        /// <summary>
        /// ImmutableX account.
        /// </summary>
        ImmutableX = 12,
    }

    /// <summary>
    /// Class for identifying an authentication key fully. Contains the kind (device or platform)
    /// and unique identifier (eg, deviceId or Game Center id).
    /// </summary>
    [MetaSerializable]
    public class AuthenticationKey : IEquatable<AuthenticationKey>
    {
        [MetaMember(1)] public AuthenticationPlatform   Platform;   // Authentication platform (device or social platform).
        [MetaMember(2)] public string                   Id;         // Unique userId for the platform (or deviceId for Development platform).

        public AuthenticationKey() { }
        public AuthenticationKey(AuthenticationPlatform platform, string userId) { Platform = platform; Id = userId; }

        public static bool operator ==(AuthenticationKey a, AuthenticationKey b)
        {
            if (ReferenceEquals(a, b))
                return true;
            else if (a is null || b is null)
                return false;
            return a.Equals(b);
        }

        public static bool operator !=(AuthenticationKey a, AuthenticationKey b) => !(a == b);

        public bool Equals(AuthenticationKey other) => (Platform == other.Platform) && (Id == other.Id);

        public override int GetHashCode() => Platform.GetHashCode() + 17 * Id.GetHashCode();

        public override bool Equals(object obj) => (obj is AuthenticationKey other) ? Equals(other) : false;

        public override string ToString() => $"{Platform}/{Id}";
    }

    /// <summary>
    /// Base class for a social authentication claim. Each platform (Google Play, Game Center, etc.)
    /// has their own implementation of this class with platform-specific information required to
    /// validate the claim.
    /// </summary>
    [MetaSerializable]
    public abstract class SocialAuthenticationClaimBase
    {
        public abstract AuthenticationPlatform Platform { get; }

        public SocialAuthenticationClaimBase() { }
    }

    /// <summary>
    /// Social authentication claim for development platform. Only meant to be used for
    /// easier development of authentication flow, not to be used in production.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class SocialAuthenticationClaimDevelopment : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.Development;

        [MetaMember(2)] public string AuthToken { get; private set; }

        public SocialAuthenticationClaimDevelopment() { }
        public SocialAuthenticationClaimDevelopment(string authToken)
        {
            AuthToken = authToken;
        }
    }

    /// <summary>
    /// Claim for Google Play PlayerId using legacy <i>Google Play - Sign-in in Android Games</i>. See <see cref="SocialAuthenticationClaimGooglePlayV2"/> for the current version.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class SocialAuthenticationClaimGooglePlayV1 : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.GooglePlay;

        [MetaMember(2)] public string IdToken { get; private set; }
        [MetaMember(3)] public string ServerAuthCode { get; private set; }

        SocialAuthenticationClaimGooglePlayV1() { }
        public SocialAuthenticationClaimGooglePlayV1(string idToken, string optionalServerAuthCode = null)
        {
            IdToken = idToken;
            ServerAuthCode = optionalServerAuthCode;
        }
    }

    /// <summary>
    /// Claim for Google Play PlayerId using <i>Play Games Services Sign In v2</i>.
    /// </summary>
    [MetaSerializableDerived(8)]
    public class SocialAuthenticationClaimGooglePlayV2 : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.GooglePlay;

        [MetaMember(2)] public string ServerAuthCode { get; private set; }

        public SocialAuthenticationClaimGooglePlayV2() { }
        public SocialAuthenticationClaimGooglePlayV2(string serverAuthCode)
        {
            ServerAuthCode = serverAuthCode;
        }
    }

    [MetaSerializableDerived(3)]
    public class SocialAuthenticationClaimGameCenter : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.GameCenter;

        [MetaMember(1)] public string   LegacyUserId;
        [MetaMember(2)] public string   PublicKeyUrl;
        [MetaMember(3)] public ulong    Timestamp;
        [MetaMember(4)] public string   Signature;
        [MetaMember(5)] public string   Salt;
        [MetaMember(6)] public string   BundleId;

        /// <summary>
        /// Same claim in GameCenter2020 form to support migrations, or null if not available.
        /// </summary>
        [MetaMember(7)] public SocialAuthenticationClaimGameCenter2020 GameCenter2020MigrationClaim;

        public SocialAuthenticationClaimGameCenter() { }
        public SocialAuthenticationClaimGameCenter(string legacyUserId, string publicKeyUrl, ulong timestamp, string signature, string salt, string bundleId, SocialAuthenticationClaimGameCenter2020 optionalMigrationClaim)
        {
            LegacyUserId                    = legacyUserId;
            PublicKeyUrl                    = publicKeyUrl;
            Timestamp                       = timestamp;
            Signature                       = signature;
            Salt                            = salt;
            BundleId                        = bundleId;
            GameCenter2020MigrationClaim    = optionalMigrationClaim;
        }
    }

    /// <summary>
    /// Claim for Google Account using <i>Google Sign-in</i>.
    /// </summary>
    [MetaSerializableDerived(4)]
    public class SocialAuthenticationClaimGoogleSignIn : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.GoogleSignIn;

        /// <summary>
        /// ID token from Google Sign-In authentication.
        /// As in e.g. (on Android, Java code) <c>GoogleSignInAccount.getIdToken()</c>.
        /// </summary>
        [MetaMember(2)] public string IdToken { get; private set; }

        public SocialAuthenticationClaimGoogleSignIn() { }
        public SocialAuthenticationClaimGoogleSignIn(string idToken)
        {
            IdToken = idToken;
        }
    }

    [MetaSerializableDerived(5)]
    public class SocialAuthenticationClaimSignInWithApple : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.SignInWithApple;

        /// <summary>
        /// Identity token from 'Sign in with Apple' authentication.
        /// As in e.g. (on iOS) <c>ASAuthorizationAppleIDCredential.identityToken</c>.
        /// </summary>
        [MetaMember(2)] public string IdentityToken { get; private set; }

        public SocialAuthenticationClaimSignInWithApple() { }
        public SocialAuthenticationClaimSignInWithApple(string identityToken)
        {
            IdentityToken = identityToken;
        }
    }

    [MetaSerializableDerived(6)]
    public class SocialAuthenticationClaimFacebookLogin : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.FacebookLogin;

        /// <summary>
        /// Access token from Facebook Login authentication, or OpenID Connect token in the case of a Limited Login.
        /// As in e.g. <c>Facebook.Unity.AccessToken.CurrentAccessToken.TokenString</c>.
        /// </summary>
        [MetaMember(2)] public string AccessTokenOrOIDCToken { get; private set; }

        public SocialAuthenticationClaimFacebookLogin() { }
        public SocialAuthenticationClaimFacebookLogin(string accessTokenOrOIDCToken)
        {
            AccessTokenOrOIDCToken = accessTokenOrOIDCToken;
        }
    }

    /// <summary>
    /// Claim for Apple GameCenter using only Team-scoped Ids.
    /// </summary>
    [MetaSerializableDerived(7)]
    public class SocialAuthenticationClaimGameCenter2020 : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.GameCenter2020;

        [MetaMember(1)] public string   TeamPlayerId;
        [MetaMember(2)] public string   GamePlayerId;
        [MetaMember(3)] public string   PublicKeyUrl;
        [MetaMember(4)] public ulong    Timestamp;
        [MetaMember(5)] public string   Signature;
        [MetaMember(6)] public string   Salt;
        [MetaMember(7)] public string   BundleId;

        public SocialAuthenticationClaimGameCenter2020() { }
        public SocialAuthenticationClaimGameCenter2020(string teamPlayerId, string gamePlayerId, string publicKeyUrl, ulong timestamp, string signature, string salt, string bundleId)
        {
            TeamPlayerId    = teamPlayerId;
            GamePlayerId    = gamePlayerId;
            PublicKeyUrl    = publicKeyUrl;
            Timestamp       = timestamp;
            Signature       = signature;
            Salt            = salt;
            BundleId        = bundleId;
        }
    }

    /// <summary>
    /// Social authentication claim for ImmutableX account. ImmutableX also resolves into an Ethreum account.
    /// </summary>
    [MetaSerializableDerived(9)]
    public class SocialAuthenticationClaimImmutableX : SocialAuthenticationClaimBase
    {
        public override AuthenticationPlatform Platform => AuthenticationPlatform.ImmutableX;

        [MetaMember(1)] public string   ClaimedImmutableXAccount    { get; private set; }
        [MetaMember(2)] public string   ClaimedEthereumAccount      { get; private set; }
        [MetaMember(3)] public EntityId ChallengePlayerId           { get; private set; }
        [MetaMember(4)] public MetaTime ChallengeTimestamp          { get; private set; }
        [MetaMember(5)] public string   ChallengeSignature          { get; private set; }

        SocialAuthenticationClaimImmutableX() { }
        public SocialAuthenticationClaimImmutableX(string claimedImmutableXAccount, string claimedEthereumAccount, EntityId challengePlayerId, MetaTime challengeTimestamp, string challengeSignature)
        {
            ClaimedImmutableXAccount = claimedImmutableXAccount;
            ClaimedEthereumAccount = claimedEthereumAccount;
            ChallengePlayerId = challengePlayerId;
            ChallengeTimestamp = challengeTimestamp;
            ChallengeSignature = challengeSignature;
        }
    }

    /// <summary>
    /// Request for server to generate a new login challenge, required for ImmutableX login.
    /// </summary>
    [MetaSerializableDerived(RequestTypeCodes.ImmutableXLoginChallengeRequest)]
    public class ImmutableXLoginChallengeRequest : MetaRequest
    {
        public string ClaimedImmutableXAccount  { get; private set; }
        public string ClaimedEthereumAccount    { get; private set; }

        ImmutableXLoginChallengeRequest() { }
        public ImmutableXLoginChallengeRequest(string claimedImmutableXAccount, string claimedEthereumAccount)
        {
            ClaimedImmutableXAccount = claimedImmutableXAccount;
            ClaimedEthereumAccount = claimedEthereumAccount;
        }
    }

    /// <summary>
    /// Response to <see cref="ImmutableXLoginChallengeRequest"/>.
    /// </summary>
    [MetaSerializableDerived(RequestTypeCodes.ImmutableXLoginChallengeResponse)]
    public class ImmutableXLoginChallengeResponse : MetaResponse
    {
        public string   Message     { get; private set; }
        public string   Description { get; private set; }
        public EntityId PlayerId    { get; private set; }
        public MetaTime Timestamp   { get; private set; }

        ImmutableXLoginChallengeResponse() { }
        public ImmutableXLoginChallengeResponse(string message, string description, EntityId playerId, MetaTime timestamp)
        {
            Message = message;
            Description = description;
            PlayerId = playerId;
            Timestamp = timestamp;
        }
    }
}
