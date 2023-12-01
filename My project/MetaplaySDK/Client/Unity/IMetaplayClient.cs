// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Metaplay.Unity
{
    /// <summary>
    /// A view into <see cref="MetaplayClient"/> which provides
    /// access to the current game session's context (PlayerModel,
    /// action execution, etc).
    /// </summary>
    public interface ISessionContextProvider
    {
        IPlayerClientContext    PlayerContext   { get; }
        MetaplayClientStore     ClientStore     { get; }
    }

    /// <summary>
    /// Interface used to route a game session's parameters (game
    /// config and logic version) from <see cref="MetaplayConnection"/>
    /// to <see cref="MetaplayClient"/>.
    /// </summary>
    public interface ISessionStartHook
    {
        void OnSessionStarted(ClientSessionStartResources startResources);
    }

    /// <summary>
    /// <see cref="IMetaplayConnectionDelegate"/> augmented with things accessed by MetaplayClient.
    /// </summary>
    public interface IMetaplayClientConnectionDelegate : IMetaplayConnectionDelegate
    {
        ISessionStartHook       SessionStartHook    { set; }
        ISessionContextProvider SessionContext      { set; }
    }

    public interface IMetaplayClientAnalyticsDelegate
    {
        /// <summary>
        /// Called by the SDK to route an analytics event to the game client.
        /// This same method is used for entity-specific events (such as those
        /// derived from <see cref="PlayerEventBase"/>) and other events (such
        /// as those derived from <see cref="ClientEventBase"/>).
        /// </summary>
        /// <param name="model">
        /// The entity model associated with the event, if any.
        /// This is <c>null</c> if the event is not associated with an entity,
        /// which is the case with <see cref="ClientEventBase"/> for example.
        /// </param>
        void OnAnalyticsEvent(AnalyticsEventSpec eventSpec, AnalyticsEventBase payload, IModel model);
    }

    public interface IMetaplayClientSocialAuthenticationDelegate
    {
        /// <summary>
        /// Called when Social Authentication request succeeded, and the social account is now associated with this player.
        /// </summary>
        /// <param name="platform">The social platform for which the login attempt succeeded</param>
        void OnSocialAuthenticationSuccess(AuthenticationPlatform platform);

        /// <summary>
        /// Called when attempted Social Authentication request failed.
        /// </summary>
        /// <param name="platform">The social platform for which the login attempt failed</param>
        /// <param name="errorCode">The error category of the failure</param>
        /// <param name="debugOnlyErrorMessage">Error message for the failure. Set only if server is in debug mode or player is a developer, null otherwise.</param>
        void OnSocialAuthenticationFailure(AuthenticationPlatform platform, SocialAuthenticateResult.ResultCode errorCode, string debugOnlyErrorMessage);

        /// <summary>
        /// Called when Social Authentication request succeeded, but the social account is already associated with another player account,
        /// whose state is available for inspection in <paramref name="conflictingPlayer"/>.
        /// <para>
        /// At this point, the social account has *not* been changed to be associated with the currently active player.
        /// </para>
        /// <para>
        /// You should present the user a dialog to choose between the currently active player and <paramref name="conflictingPlayer"/>.
        /// Based on the user's choice, inform the server which player state the user wants to keep using by calling <c>MetaplayClient.SocialAuthManager.ResolveConflict</c>
        /// with <paramref name="conflictResolutionId"/> and the <c>userOther</c> boolean parameter reflecting the user's choice:<br/>
        ///   - <c>useOther == false</c>: keep using the current player state, and attach the social authentication method to this player<br/>
        ///   - <c>useOther == true</c>: keep the social authentication attached to the conflicting player, and switch this device to use that player<br/>
        /// </para>
        /// </summary>
        /// <param name="platform">The social platform for which the conflict happened</param>
        /// <param name="conflictResolutionId">An id that needs to be passed when calling <see cref="SocialAuthManager.ResolveConflict"/></param>
        /// <param name="conflictingPlayer">The state of the other player account already associated with the social account</param>
        void OnSocialAuthenticationConflict(AuthenticationPlatform platform, int conflictResolutionId, IPlayerModelBase conflictingPlayer);

        /// <summary>
        /// Same as <see cref="OnSocialAuthenticationConflict"/>, except that in this case the server failed to get the conflicting player's state,
        /// for example due to a deserialization failure.
        /// In this case only the id of the other player is available, not its full state.
        /// <para>
        /// This likely happens due to a bug which caused the other player's deserialization to fail. The error should be investigated by developers.
        /// The user should be presented with different actions than for <see cref="OnSocialAuthenticationConflict"/>:
        /// Show dialog and let user choose what to do:<br/>
        ///   - Leave the auth conflict unresolved, and possibly contact customer support<br/>
        ///   - Retry, in hopes the failure was transient (could retry by just restarting game)<br/>
        ///   - Resolve the conflict in favor of the current PlayerModel (shouldn't choose the other state because it's possibly broken)<br/>
        /// </para>
        /// </summary>
        /// <param name="platform">The social platform for which the conflict happened</param>
        /// <param name="conflictResolutionId">An id that needs to be passed when calling <see cref="SocialAuthManager.ResolveConflict"/></param>
        /// <param name="conflictingPlayer">The id of the other player account already associated with the social account</param>
        void OnSocialAuthenticationConflictWithFailingOtherPlayer(AuthenticationPlatform platform, int conflictResolutionId, EntityId conflictingPlayerId);
    }

    public interface IMetaplayClientGameConfigDelegate
    {
        void OnSharedGameConfigUpdated(ISharedGameConfig newSharedGameConfig, ContentHash version);
    }
}
