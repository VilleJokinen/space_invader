// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.EventLog;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Json;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using static System.FormattableString;

namespace Metaplay.Core.Analytics
{
    public static class AnalyticsEventCodesCore
    {
        // 1..999 reserved for game-specific events

        // Client events
        public const int    ClientAppInitialized                        = 1900;
        public const int    ClientConnectedToServer                     = 1901;
        public const int    ClientConnectionFailure                     = 1902;
        // \todo [petri] app pause/resume, connection lost, GameConfig load/parse

        // Player events
        public const int    PlayerEventDeserializationFailureSubstitute = 1099;
        public const int    PlayerNameChanged                           = 1000;
        public const int    PlayerPendingDynamicPurchaseContentAssigned = 1001;
        public const int    PlayerFacebookAuthenticationRevoked         = 1002;
        public const int    PlayerSocialAuthConflictResolved            = 1003;
        public const int    PlayerRegistered                            = 1004;
        public const int    PlayerClientConnected                       = 1005;
        public const int    PlayerClientDisconnected                    = 1006;
        public const int    PlayerSessionStatistics                     = 1007;
        public const int    PlayerInAppPurchased                        = 1008;
        public const int    PlayerExperimentAssignment                  = 1009;
        public const int    PlayerDeleted                               = 1010;
        public const int    PlayerInAppValidationStarted                = 1011;
        public const int    PlayerInAppValidationComplete               = 1012;
        public const int    PlayerPendingStaticPurchaseContextAssigned  = 1013;
        public const int    PlayerInAppPurchaseClientRefused            = 1014;
        public const int    PlayerIAPSubscriptionStateUpdated           = 1015;
        public const int    PlayerIAPSubscriptionDisabledDueToReuse     = 1016;
        public const int    PlayerModelSchemaMigrated                   = 1017;
        public const int    PlayerIncidentRecorded                      = 1018;
        // \todo [petri] ban status update, offer activated/ended, event started/ended, ..
        //public const int    PlayerSocialAuth...                         = 10xx;
        //public const int    PlayerInApp...                              = 10xx;
        //public const int    PlayerGuild...                              = 10xx;

        // Guild events
        public const int    GuildEventDeserializationFailureSubstitute  = 1199;
        public const int    GuildCreated                                = 1100;
        public const int    GuildFounderJoined                          = 1101;
        public const int    GuildMemberJoined                           = 1102;
        public const int    GuildMemberLeft                             = 1103;
        public const int    GuildMemberKicked                           = 1104;
        public const int    GuildMemberRemovedDueToInconsistency        = 1105;
        public const int    GuildNameAndDescriptionChanged              = 1106;
        public const int    GuildModelSchemaMigrated                    = 1107;

        // Server events
        public const int    ServerExperimentInfo                        = 1200;
        public const int    ServerVariantInfo                           = 1201;

        // Division events
        public const int DivisionCreated                                = 1300;
        public const int DivisionParticipantJoined                      = 1301;
    }

    // \todo [petri] do we need a common base class for all event payloads?
    [MetaSerializable]
    public abstract class AnalyticsEventBase
    {
        /// <summary>
        /// A collection of keywords associated to this event, for dashboard search / filter purposes.
        /// </summary>
        [IncludeOnlyInJsonSerializationMode(JsonSerializationMode.AdminApi)]
        public IEnumerable<string> Keywords => AnalyticsEventRegistry.GetEventSpec(GetType()).KeywordsFunc?.Invoke(this) ?? Enumerable.Empty<string>();

        /// <summary>
        /// A collection of keywords associated to this analytics event instance. Should only be used if instance data
        /// is needed for determining keywords, for static keywords per event type use AnalyticsEventKeywordAttribute
        /// on the analytics event class instead.
        /// </summary>
        [JsonIgnore]
        public virtual IEnumerable<string> KeywordsForEventInstance => null;
    }

    // CLIENT EVENTS

    // \todo [petri] should we derive from IMetaEventLogEntryPayload here?
    [AnalyticsEventCategory("Client")]
    public abstract class ClientEventBase : AnalyticsEventBase
    {
    }

    /// <summary>
    /// The base class for the Context data of each analytics event. This contains arbitrary information
    /// from the source entity emitting the event. Customized context not supported in all analytics sinks.
    /// </summary>
    [MetaSerializable]
    public abstract class AnalyticsContextBase
    {
    }

    // \todo [petri] ClientEventApplicationInitialized: app start time

    // \todo [petri] ClientEventConnectedToServer: duration, port, ipv4/v6, ??

    // \todo [petri] ClientEventFailedToConnect: failure reason, etc.

    // \todo [petri] GameConfig load/parse stats?

}
