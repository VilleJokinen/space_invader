// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Threading.Tasks;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Provides login Credentials to the session. This is used between multiple sessions.
    /// </summary>
    public interface ISessionCredentialService
    {
        public readonly struct GuestCredentials
        {
            public readonly string DeviceId;
            public readonly string AuthToken;
            public readonly EntityId PlayerIdHint;

            public GuestCredentials(string deviceId, string authToken, EntityId playerIdHint)
            {
                DeviceId = deviceId;
                AuthToken = authToken;
                PlayerIdHint = playerIdHint;
            }
        }

        public abstract class LoginMethod
        {
            internal LoginMethod() { }
        }

        /// <summary>
        /// A login method where a new Guest Account is created, is saved to device and then used to log in into into the game.
        /// </summary>
        public sealed class NewGuestAccountLoginMethod : LoginMethod
        {
        }

        /// <summary>
        /// A login method where an existing Guest Account is used.
        /// </summary>
        public sealed class GuestAccountLoginMethod : LoginMethod
        {
            public readonly GuestCredentials GuestCredentials;

            public GuestAccountLoginMethod(GuestCredentials guestCredentials)
            {
                GuestCredentials = guestCredentials;
            }
        }

        /// <summary>
        /// A login method for Bots.
        /// </summary>
        public sealed class BotLoginMethod : LoginMethod
        {
            public readonly GuestCredentials BotCredentials;

            public BotLoginMethod(GuestCredentials botCredentials)
            {
                BotCredentials = botCredentials;
            }
        }

        /// <summary>
        /// A login method using a social platform.
        /// </summary>
        public sealed class SocialAuthLoginMethod : LoginMethod
        {
            public readonly SocialAuthenticationClaimBase Claim;
            public readonly EntityId PlayerIdHint;
            public readonly bool IsBot;

            public SocialAuthLoginMethod(SocialAuthenticationClaimBase claim, EntityId playerIdHint, bool isBot)
            {
                Claim = claim;
                PlayerIdHint = playerIdHint;
                IsBot = isBot;
            }
        }

        /// <summary>
        /// A login method using both social platform and DeviceID with implicit binding mechanism.
        /// </summary>
        public sealed class DualSocialAuthLoginMethod : LoginMethod
        {
            /// <summary>
            /// If true, a new guest account is created first.
            /// </summary>
            public readonly bool CreateGuestAccount;
            public readonly SocialAuthenticationClaimBase Claim;
            public readonly EntityId PlayerIdHint;
            public readonly bool IsBot;
            public readonly string DeviceId;
            public readonly string AuthToken;

            public DualSocialAuthLoginMethod(bool createGuestAccount, SocialAuthenticationClaimBase claim, EntityId playerIdHint, bool isBot, string deviceId, string authToken)
            {
                CreateGuestAccount = createGuestAccount;
                Claim = claim;
                PlayerIdHint = playerIdHint;
                IsBot = isBot;
                DeviceId = deviceId;
                AuthToken = authToken;
            }
        }

        /// <summary>
        /// Called before any other method. Connection is not opened until this method completes, making this suitable
        /// for long-running operations such as performing UI actions to negotiate with user and credential service,
        /// and for example making sure no connections are attempted before user has accepted End-User License Agreement.
        ///
        /// Returns the predicted PlayerId, or None if no PlayerId can be predicted.
        ///
        /// Any thrown exception causes session termination with CredentialError error.
        /// </summary>
        /// <remarks> Called on Unity thread on Unity client. </remarks>
        Task<EntityId> InitializeAsync();

        /// <summary>
        /// Called before each login attempt. Returns the Login method client uses to authenticate.
        ///
        /// Any thrown exception causes session termination with CredentialError error.
        /// </summary>
        /// <remarks> Called on Unity thread on Unity client. </remarks>
        Task<LoginMethod> GetCurrentLoginMethodAsync();

        /// <summary>
        /// Called when server generates guest credentials (in response to NewGuestAccountLoginMethod).
        /// </summary>
        /// <remarks> Called on Unity thread on Unity client. </remarks>
        Task OnGuestAccountCreatedAsync(GuestCredentials guestCredentials);

        /// <summary>
        /// Called when account PlayerId changes.
        /// </summary>
        /// <remarks> Called on Unity thread on Unity client. </remarks>
        Task OnPlayerIdUpdatedAsync(AuthenticationPlatform platform, EntityId playerId);
    }
}
