// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Analytics
{
    public static class AnalyticsEventDocsCore
    {
        // Client events
        public const string ClientConnectionFailure                     = "Client-only event! Client failed to connect to the server, or an existing connection was lost. This can occur for a number of reasons, described by the event parameters.";

        // Player events
        public const string PlayerEventDeserializationFailureSubstitute = "This is a substitute event that is shown if the original serialized event could not be deserialized. This can happen if even data format is changed in an incompatible way.";
        public const string PlayerNameChanged                           = "Player name has changed.";
        public const string PlayerPendingDynamicPurchaseContentAssigned = "Player is about to start an IAP flow in the platform store. The purchase contents are dynamically generated. The event contents describe the purchase the player is attempting to buy.";
        public const string PlayerFacebookAuthenticationRevoked         = "Facebook authentication has been revoked. The event contents describe the reason for revocation (for example: deauthentication on Facebook, or Data Deletion Request on Facebook).";
        public const string PlayerSocialAuthConflictResolved            = "Player has resolved an authentication conflict, and that has affected the player's authentication record. An authentication conflict is situation where multiple authentication sources disagree on which player account is active. The event contents describe the actions done to this player to resolve the conflict.";
        public const string PlayerRegistered                            = "THIS EVENT IS UNUSED";
        public const string PlayerClientConnected                       = "Client has connected to the server.";
        public const string PlayerClientDisconnected                    = "The connection from client to the server has been lost.";
        public const string PlayerSessionStatistics                     = "THIS EVENT IS UNUSED";
        public const string PlayerInAppPurchased                        = "In App Purchase has been completed and the rewards have been granted to the player.";
        public const string PlayerExperimentAssignment                  = "Player has been assigned into an Experiment. The event contents describe the experiment and the variant in it.";
        public const string PlayerDeleted                               = "The account of the player has been deleted. As this might be due to a GDPR Data Deletion Request, analytics pipeline should delete any personal information assosicated with this player.";
        public const string PlayerInAppValidationStarted                = "Server-side IAP validation has started.";
        public const string PlayerInAppValidationComplete               = "Server-side IAP validation was completed. The event contents describe the result of the validation.";
        public const string PlayerPendingStaticPurchaseContextAssigned  = "Player is about to start an IAP flow in the platform store. The purchase contents are specified in the IAP config. The event contents describe the purchase the player is attempting to buy.";
        public const string PlayerInAppPurchaseClientRefused            = "Client-side refusal in the IAP flow in the platform store. This can be either a store failure, or user cancellation. The event contents describe the error code of the refusal.";
        public const string PlayerIAPSubscriptionStateUpdated           = "The state of an IAP subscription was updated. A state update can be a subscription renewal or some other change, such as a change in the auto-renewal status of the subscription. Here, expiration is not considered a state update, as it happens based on time.";
        public const string PlayerIAPSubscriptionDisabledDueToReuse     = "An IAP subscription was disabled for this player because the same purchase was reused on another player account. This can happen due to subscription restoration. The subscription can be enabled again by restoring it again on this player account.";
        public const string PlayerModelSchemaMigrated                   = "The PlayerModel schema was migrated from an old version to the current version (at the time of running the event)";
        public const string PlayerIncidentReported                      = "A player incident occurred. In addition to this event, incidents produce more detailed reports which can be viewed in the LiveOps Dashboard.";

        // Guild events
        public const string GuildEventDeserializationFailureSubstitute  = "This is a substitute event that is shown if the original serialized event could not be deserialized. This can happen if even data format is changed in an incompatible way.";
        public const string GuildCreated                                = "Guild was created. At this point there are 0 members in the guild.";
        public const string GuildFounderJoined                          = "The initial member was added to the guild.";
        public const string GuildMemberJoined                           = "A member (other than the initial member) joined the guild. Initial member is called the Founder, and uses a separate event.";
        public const string GuildMemberLeft                             = "A member left the guild (by their own request).";
        public const string GuildMemberKicked                           = "A member was kicked by a fellow member or an admin.";
        public const string GuildMemberRemovedDueToInconsistency        = "A member was removed from the guild to repair an inconsistency between the player entity and the guild entity, where the player and guild disagree about the player's membership in the guild. This can only happen if DB is an inconsistent state, which may be caused by partial or non-atomically sharded DB rollbacks.";
        public const string GuildNameAndDescriptionChanged              = "The guild's name and/or description were changed by a member or an admin.";
        public const string GuildModelSchemaMigrated                    = "The GuildModel schema was migrated from an old version to the current version (at the time of running the event)";

        // Server events
        public const string ServerExperimentInfo                        = "A metadata update for the Analytics pipeline containing informatation about a Player Experiment. The event is emitted when Experiments change, config changes, and when the server starts.";
        public const string ServerVariantInfo                           = "A metadata update for the Analytics pipeline containing informatation about a Player Experiment Variant. This event is emitted only for event variants that are active. The event is emitted when Experiments change, config changes, and when the server starts.";

        // Division events
        public const string DivisionCreated                             = "Division was created. At this point there are 0 participants in the division.";
        public const string DivisionParticipantJoined                   = "A participant joined the division. Either assigned by the league manager, or by the participant themselves.";
    }
}
