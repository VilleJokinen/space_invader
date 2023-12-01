// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Client claims to have authenticated using a social platform. Server still needs
    /// to validate the claim before accepting the authentication.
    /// </summary>
    [MetaSerializableDerived(RequestTypeCodes.SocialAuthenticateRequest)]
    public class SocialAuthenticateRequest : MetaRequest
    {
        public SocialAuthenticationClaimBase Claim { get; private set; }

        SocialAuthenticateRequest() { }
        public SocialAuthenticateRequest(SocialAuthenticationClaimBase claim) { Claim = claim; }
    }

    /// <summary>
    /// Represents the results of a completed social authentication request. In case the authentication
    /// was successful, but the social profile was already attached to an existing player state, the
    /// <see cref="ConflictingPlayerIfAvailable"/> contains the information of that other player state, which can
    /// be used to provide the user with a choice of which player profile to continue with. The choice
    /// should be communicated to the server by sending a <see cref="SocialAuthenticateResolveConflict"/>.
    /// <para>
    /// An exception to this is when there was a server-side error which made the other player's state
    /// unavailable (e.g. a deserialization failure), in which case <see cref="ConflictingPlayerIfAvailable"/>
    /// is not available, but <see cref="ConflictingPlayerId"/> is.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(RequestTypeCodes.SocialAuthenticateResponse)]
    public class SocialAuthenticateResult : MetaResponse
    {
        [MetaSerializable]
        public enum ResultCode
        {
            Success = 0,

            /// <summary>
            /// Authentication failed.
            /// </summary>
            AuthError = 1,

            /// <summary>
            /// Authentication could not be performed due to a temporary error.
            /// </summary>
            TemporarilyUnavailable = 2,
        }

        /// <summary>
        /// Social platform for which authentication was done.
        /// </summary>
        public AuthenticationPlatform               Platform                { get; private set; }

        /// <summary>
        /// Success if the platform validation succeeded, error code otherwise.
        /// </summary>
        public ResultCode                           Result                  { get; private set; }

        /// <summary>
        /// The id of a player already mapped to the social authentication. <see cref="EntityId.None"/> if there
        /// was no conflict.
        /// Normally if this is not None then the state of the player is in <see cref="ConflictingPlayerIfAvailable"/>,
        /// but the state might be missing if there was a server-side error.
        /// </summary>
        public EntityId                             ConflictingPlayerId     { get; private set; }

        /// <summary>
        /// The state of a player already mapped to the social authentication. Deserializes to null if there either
        /// was no conflict, or there was a conflict but the other player's state could not be queried due to
        /// a server-side error (in which case the error will have been logged on the server and should
        /// be investigated).
        /// </summary>
        public MetaSerialized<IPlayerModelBase>     ConflictingPlayerIfAvailable { get; private set; }

        /// <summary>
        /// The ID to be used for SocialAuthenticateResolveConflict message if there was a conflict that needs to be resolved.
        /// </summary>
        public int                                  ConflictResolutionId    { get; private set; }

        /// <summary>
        /// Human/Developer readable error message for failure, but only if server has set <see cref="Metaplay.Cloud.Application.EnvironmentOptions.EnableDevelopmentFeatures" />
        /// or the player is marked as a developer. Null otherwise.
        /// </summary>
        public string                               DebugOnlyErrorMessage   { get; private set; }

        SocialAuthenticateResult() { }
        public SocialAuthenticateResult(AuthenticationPlatform platform, ResultCode result, EntityId conflictingPlayerId, MetaSerialized<IPlayerModelBase> conflictingPlayer, int conflictResolutionId, string debugOnlyErrorMessage)
        {
            // conflictingPlayer must always deserialize. No default values here.
            if (conflictingPlayer.IsEmpty)
                throw new ArgumentException("Serialized conflicting player cannot be empty", nameof(conflictingPlayer));

            Platform                        = platform;
            Result                          = result;
            ConflictingPlayerId             = conflictingPlayerId;
            ConflictingPlayerIfAvailable    = conflictingPlayer;
            ConflictResolutionId            = conflictResolutionId;
            DebugOnlyErrorMessage           = debugOnlyErrorMessage;
        }
    }

    /// <summary>
    /// Resolve a social authentication conflict by choosing either our existing player state
    /// or by switching to the player state that is previously attached to the social profile.
    /// In case of switching to the previously attached profile, the server will follow up with
    /// <see cref="SocialAuthenticateForceReconnect"/> to finalize the change.
    /// </summary>
    [MetaMessage(MessageCodesCore.SocialAuthenticateResolveConflict, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class SocialAuthenticateResolveConflict : MetaMessage
    {
        public int      ConflictResolutionId    { get; private set; }
        public bool     UseOther                { get; private set; }

        SocialAuthenticateResolveConflict() { }
        public SocialAuthenticateResolveConflict(int conflictResolutionId, bool useOther)
        {
            ConflictResolutionId = conflictResolutionId;
            UseOther = useOther;
        }
    }

    /// <summary>
    /// A social authentication conflict was resolved by switching to the existing player state.
    /// The client needs to reconnect in order to receive the state of the player to which the
    /// social profile was attached to.
    /// </summary>
    [MetaMessage(MessageCodesCore.SocialAuthenticateForceReconnect, MessageDirection.ServerToClient)]
    public class SocialAuthenticateForceReconnect : MetaMessage
    {
        public EntityId NewPlayerId { get; private set; }

        SocialAuthenticateForceReconnect() { }
        public SocialAuthenticateForceReconnect(EntityId newPlayerId) { NewPlayerId = newPlayerId; }
    }

    /// <summary>
    /// Detach a given <see cref="AuthenticationKey"/> from the player's profile.
    /// </summary>
    [MetaMessage(MessageCodesCore.SocialAuthenticateDetach, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class SocialAuthenticateDetach : MetaMessage
    {
        public AuthenticationKey AuthKey { get; private set; }

        public SocialAuthenticateDetach() { }
        public SocialAuthenticateDetach(AuthenticationKey authKey) { AuthKey = authKey; }
    }

    /// <summary>
    /// Attach a given authentication method to the player profile.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerAttachAuthentication)]
    public class PlayerAttachAuthentication : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public AuthenticationKey    AuthKey     { get; private set; }
        public PlayerAuthEntryBase  AuthEntry   { get; private set; }

        PlayerAttachAuthentication() { }
        public PlayerAttachAuthentication(AuthenticationKey authKey, PlayerAuthEntryBase authEntry)
        {
            AuthKey = authKey;
            AuthEntry = authEntry;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                // \note: we clone the state to avoid sharing the object.
                PlayerAuthEntryBase newEntry = MetaSerialization.CloneTagged(AuthEntry, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);
                player.AttachedAuthMethods.AddOrReplace(AuthKey, newEntry);
                player.ServerListenerCore.AuthMethodAttached(AuthKey);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Detach a given authentication method from the player profile.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerDetachAuthentication)]
    public class PlayerDetachAuthentication : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public AuthenticationKey AuthKey { get; private set; }

        PlayerDetachAuthentication() { }
        public PlayerDetachAuthentication(AuthenticationKey authKey) { AuthKey = authKey; }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                // Remove from attached AuthKeys
                if (player.AttachedAuthMethods.ContainsKey(AuthKey))
                {
                    player.AttachedAuthMethods.Remove(AuthKey);
                    player.ServerListenerCore.AuthMethodDetached(AuthKey);

                    // If account becomes orphaned, clear current push notification tokens
                    if (player.AttachedAuthMethods.Count == 0)
                    {
                        player.PushNotifications.Clear();
                        player.FirebaseMessagingTokensLegacy.Clear();
                    }
                }
            }

            return MetaActionResult.Success;
        }
    }
}
