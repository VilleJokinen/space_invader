// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Client;
using Metaplay.Server;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Session protocol messages.
    /// </summary>
    public static class SessionProtocol
    {
        /// <summary>
        /// The client's proposal for session resources. Server may accept these
        /// or issue a correction (e.g. <see cref="SessionResourceCorrection"/>).
        /// </summary>
        [MetaSerializable]
        public struct SessionResourceProposal
        {
            [MetaMember(1)] public OrderedDictionary<ClientSlot, ContentHash>   ConfigVersions              { get; private set; }
            [MetaMember(2)] public OrderedDictionary<ClientSlot, ContentHash>   PatchVersions               { get; private set; }
            [MetaMember(10)] public LanguageId                                  ClientActiveLanguage        { get; private set; }
            [MetaMember(11)] public ContentHash                                 ClientLocalizationVersion   { get; private set; }

            public SessionResourceProposal(OrderedDictionary<ClientSlot, ContentHash> configVersions, OrderedDictionary<ClientSlot, ContentHash> patchVersions, LanguageId clientActiveLanguage, ContentHash clientLocalizationVersion)
            {
                ConfigVersions = configVersions;
                PatchVersions = patchVersions;
                ClientActiveLanguage = clientActiveLanguage;
                ClientLocalizationVersion = clientLocalizationVersion;
            }
        }

        /// <summary>
        /// The set of corrections for session resources. Issued by server if the proposed
        /// resources (in <see cref="SessionResourceProposal"/>) are not satisfactory.
        /// </summary>
        [MetaSerializable]
        public class SessionResourceCorrection
        {
            [MetaSerializable]
            public struct ConfigArchiveUpdateInfo
            {
                /// <summary>
                /// The config version the client should update to
                /// </summary>
                [MetaMember(1)] public ContentHash      SharedGameConfigVersion;

                /// <summary>
                /// The URL suffix for the download resource. May be null which means empty or no suffix.
                /// </summary>
                [MetaMember(2)] public string           UrlSuffix;

                public ConfigArchiveUpdateInfo(ContentHash sharedGameConfigVersion, string urlSuffix)
                {
                    if (sharedGameConfigVersion == ContentHash.None)
                        throw new ArgumentException("version must be set", nameof(sharedGameConfigVersion));

                    SharedGameConfigVersion = sharedGameConfigVersion;
                    UrlSuffix = urlSuffix;
                }
            }

            [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
            [MetaImplicitMembersRange(1, 100)]
            public struct LanguageUpdateInfo
            {
                /// <summary>
                /// The language the client should use.
                /// </summary>
                public LanguageId       ActiveLanguage;

                /// <summary>
                /// The localization version the client should use.
                /// </summary>
                public ContentHash      LocalizationVersion;

                public LanguageUpdateInfo(LanguageId activeLanguage, ContentHash localizationVersion)
                {
                    ActiveLanguage = activeLanguage;
                    LocalizationVersion = localizationVersion;
                }
            }

            [MetaSerializable]
            public struct ConfigPatchesUpdateInfo
            {
                /// <summary>
                /// The patch set version the client should update to.
                /// </summary>
                [MetaMember(1)] public ContentHash PatchesVersion;

                public ConfigPatchesUpdateInfo(ContentHash patchesVersion)
                {
                    PatchesVersion = patchesVersion;
                }
            }

            /// <summary>
            /// If set, the config the client should use
            /// </summary>
            [MetaMember(1)] public OrderedDictionary<ClientSlot, ConfigArchiveUpdateInfo> ConfigUpdates;

            /// <summary>
            /// If set, the config patches each client should use
            /// </summary>
            [MetaMember(2)] public OrderedDictionary<ClientSlot, ConfigPatchesUpdateInfo> PatchUpdates;

            /// <summary>
            /// If set, the language the client should use.
            /// </summary>
            [MetaMember(3)] public LanguageUpdateInfo? LanguageUpdate;

            public SessionResourceCorrection()
            {
                ConfigUpdates = new OrderedDictionary<ClientSlot, ConfigArchiveUpdateInfo>();
                PatchUpdates = new OrderedDictionary<ClientSlot, ConfigPatchesUpdateInfo>();
                LanguageUpdate = null;
            }

            public bool HasAnyCorrection()
            {
                if (ConfigUpdates.Count > 0)
                    return true;
                if (PatchUpdates.Count > 0)
                    return true;
                if (LanguageUpdate.HasValue)
                    return true;
                return false;
            }

            public static SessionResourceCorrection Combine(SessionResourceCorrection a, SessionResourceCorrection b)
            {
                SessionResourceCorrection combined = new SessionResourceCorrection();

                foreach ((ClientSlot slot, ConfigArchiveUpdateInfo update) in a.ConfigUpdates)
                    combined.ConfigUpdates.Add(slot, update);
                foreach ((ClientSlot slot, ConfigArchiveUpdateInfo update) in b.ConfigUpdates)
                    combined.ConfigUpdates.Add(slot, update);

                foreach ((ClientSlot slot, ConfigPatchesUpdateInfo update) in a.PatchUpdates)
                    combined.PatchUpdates.Add(slot, update);
                foreach ((ClientSlot slot, ConfigPatchesUpdateInfo update) in b.PatchUpdates)
                    combined.PatchUpdates.Add(slot, update);

                combined.LanguageUpdate = a.LanguageUpdate ?? b.LanguageUpdate;
                return combined;
            }
        }

        /// <summary>
        /// Request to create a new session.
        /// </summary>
        [MetaMessage(MessageCodesCore.SessionStartRequest, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public class SessionStartRequest : MetaMessage
        {
            [MetaMember(1)] public int                              QueryId                         { get; private set; }
            [MetaMember(2)] public string                           DeviceGuid                      { get; private set; }   // Device unique identifier (MetaplaySDK.DeviceGuid). If not given or rejected by server due to not
                                                                                                                            // matching existing per-device info a new device guid is generated and communicated to client via
                                                                                                                            // `SessionStartSuccess.CorrectedDeviceGuid`.
            [MetaMember(3)] public string                           DeviceModel                     { get; private set; }   // Human-readable device model (SystemInfo.deviceModel). Clamped to 256 characters on receive.
            [MetaMember(4)] public PlayerTimeZoneInfo               TimeZoneInfo                    { get; private set; }
            [MetaMember(5)] public SessionResourceProposal          ResourceProposal                { get; private set; }
            [MetaMember(6)] public bool                             IsDryRun                        { get; private set; }   // If set, start will fail, but not before resource corrections are made
            [MetaMember(7)] public ISessionStartRequestGamePayload  GamePayload                     { get; private set; }
            [MetaMember(8)] public CompressionAlgorithmSet          SupportedArchiveCompressions    { get; private set; }   // Supported archive compression modes
            [MetaMember(9)] public ClientAppPauseStatus             ClientAppPauseStatus            { get; private set; }

            SessionStartRequest() { }
            public SessionStartRequest(int queryId, string deviceGuid, string deviceModel, PlayerTimeZoneInfo timeZoneInfo, SessionResourceProposal resourceProposal, bool isDryRun,
                ISessionStartRequestGamePayload gamePayload, CompressionAlgorithmSet supportedArchiveCompressions, ClientAppPauseStatus clientAppPauseStatus)
            {
                QueryId = queryId;
                DeviceGuid = deviceGuid;
                DeviceModel = deviceModel;
                TimeZoneInfo = timeZoneInfo ?? throw new ArgumentNullException(nameof(timeZoneInfo));
                ResourceProposal = resourceProposal;
                IsDryRun = isDryRun;
                GamePayload = gamePayload;
                SupportedArchiveCompressions = supportedArchiveCompressions;
                ClientAppPauseStatus = clientAppPauseStatus;
            }
        }

        /// <summary>
        /// Request to abort session handshake. This may occur either before or just after SessionStartSuccess. The message
        /// leads into termination of the connection, except if HasReasonTrailer is set in which case SessionStartAbortReasonTrailer
        /// is the last message.
        /// </summary>
        [MetaMessage(MessageCodesCore.SessionStartAbort, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public class SessionStartAbort : MetaMessage
        {
            [MetaMember(1)] public bool HasReasonTrailer { get; private set; }

            SessionStartAbort() { }
            public SessionStartAbort(bool hasReasonTrailer)
            {
                HasReasonTrailer = hasReasonTrailer;
            }
        }

        /// <summary>
        /// Optional trailer to SessionStartAbort. This is separated from header message to better handle poor network conditions. This message can become
        /// large which increases the risk of failure. We don't want to lose the header which is critical information in order to differentiate network issues
        /// from client issues. But at the same time, this trailer is only opportunistically delivered data and failure is acceptable since normal incident
        /// delivery system will retry the delivery later. Hence, we deliver the message in two parts where the non-critical data is in the trailer.
        /// </summary>
        [MetaMessage(MessageCodesCore.SessionStartAbortReasonTrailer, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public class SessionStartAbortReasonTrailer : MetaMessage
        {
            [MetaMember(1)] public string   IncidentId  { get; private set; }
            [MetaMember(2)] public byte[]   Incident    { get; private set; } // Deflate-compressed, TaggedSerialized<PlayerIncidentReport>

            SessionStartAbortReasonTrailer() { }
            public SessionStartAbortReasonTrailer(string incidentId, byte[] incident)
            {
                IncidentId = incidentId;
                Incident = incident;
            }
        }

        [MetaSerializable]
        public class InitialPlayerState
        {
            [MetaMember(1)] public MetaSerialized<IPlayerModelBase> PlayerModel         { get; private set; }
            [MetaMember(2)] public int                              CurrentOperation    { get; private set; } // timeline position is (Model.CurrentTick, CurrentOperation, 0)

            InitialPlayerState() { }
            public InitialPlayerState(MetaSerialized<IPlayerModelBase> playerModel, int currentOperation)
            {
                PlayerModel = playerModel;
                CurrentOperation = currentOperation;
            }
        }

        [MetaMessage(MessageCodesCore.SessionStartSuccess, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class SessionStartSuccess : MetaMessage
        {
            [MetaMember(1)] public int                                          QueryId                         { get; private set; }
            [MetaMember(2)] public int                                          LogicVersion                    { get; private set; }   // LogicVersion to use for the session.
            [MetaMember(3)] public SessionToken                                 SessionToken                    { get; private set; }
            [MetaMember(4)] public ScheduledMaintenanceModeForClient            ScheduledMaintenanceMode        { get; private set; }
            [MetaMember(5)] public EntityId                                     PlayerId                        { get; private set; }
            [MetaMember(6)] public InitialPlayerState                           PlayerState                     { get; private set; }
            [MetaMember(8)] public OrderedDictionary<LanguageId, ContentHash>   LocalizationVersions            { get; private set; }
            [MetaMember(9)] public List<EntityActiveExperiment>                 ActiveExperiments               { get; private set; }
            [MetaMember(10)] public bool                                        DeveloperMaintenanceBypass      { get; private set; }
            [MetaMember(11)] public List<EntityInitialState>                    EntityStates                    { get; private set; }
            [MetaMember(12)] public ISessionStartSuccessGamePayload             GamePayload                     { get; private set; }
            // When the DeviceGuid in SessionStartRequest is not accepted by server (or none was given) a new DeviceGuid is generated and
            // communicated back here, null otherwise.
            [MetaMember(14)] public string                                      CorrectedDeviceGuid             { get; private set; }
            [Sensitive]
            [MetaMember(15)] public byte[]                                      ResumptionToken                 { get; private set; }

            SessionStartSuccess() { }
            public SessionStartSuccess(
                int queryId,
                int logicVersion,
                SessionToken sessionToken,
                ScheduledMaintenanceModeForClient scheduledMaintenanceMode,
                EntityId playerId,
                InitialPlayerState playerState,
                OrderedDictionary<LanguageId, ContentHash> localizationVersions,
                List<EntityActiveExperiment> activeExperiments,
                bool developerMaintenanceBypass,
                List<EntityInitialState> entityStates,
                ISessionStartSuccessGamePayload gamePayload,
                string correctedDeviceGuid,
                byte[] resumptionToken)
            {
                QueryId = queryId;
                LogicVersion = logicVersion;
                SessionToken = sessionToken;
                ScheduledMaintenanceMode = scheduledMaintenanceMode;
                PlayerId = playerId;
                PlayerState = playerState;
                LocalizationVersions = localizationVersions;
                ActiveExperiments = activeExperiments;
                DeveloperMaintenanceBypass = developerMaintenanceBypass;
                EntityStates = entityStates;
                GamePayload = gamePayload;
                CorrectedDeviceGuid = correctedDeviceGuid;
                ResumptionToken = resumptionToken;
            }
        }

        [MetaMessage(MessageCodesCore.SessionStartFailure, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class SessionStartFailure : MetaMessage
        {
            [MetaSerializable]
            public enum ReasonCode
            {
                InternalError = 0,
                DryRun = 1,
                Banned = 3,
                PlayerDeserializationFailure = 4, // \todo Currently not implemented on the server - only in OfflineServer.
            }

            [MetaMember(1)] public int                          QueryId                 { get; private set; }
            [MetaMember(2)] public ReasonCode                   Reason                  { get; private set; }

            /// <summary>
            /// Human readable error message, but only if server has set <see cref="Metaplay.Cloud.Application.EnvironmentOptions.EnableDevelopmentFeatures" />.
            /// Null otherwise.
            /// </summary>
            [MetaMember(4)] public string                       DebugOnlyErrorMessage   { get; private set; }

            SessionStartFailure() { }
            public SessionStartFailure(int queryId, ReasonCode reason, string debugOnlyErrorMessage)
            {
                QueryId = queryId;
                Reason = reason;
                DebugOnlyErrorMessage = debugOnlyErrorMessage;
            }
        }

        [MetaMessage(MessageCodesCore.SessionStartResourceCorrection, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class SessionStartResourceCorrection : MetaMessage
        {
            [MetaMember(1)] public int                          QueryId             { get; private set; }
            [MetaMember(2)] public SessionResourceCorrection    ResourceCorrection  { get; private set; }

            SessionStartResourceCorrection() { }
            public SessionStartResourceCorrection(int queryId, SessionResourceCorrection resourceCorrection)
            {
                QueryId = queryId;
                ResourceCorrection = resourceCorrection;
            }
        }

        [MetaMessage(MessageCodesCore.SessionResumeSuccess, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class SessionResumeSuccess : MetaMessage
        {
            [MetaMember(1)] public SessionAcknowledgement               ServerSessionAcknowledgement    { get; private set; }
            [MetaMember(2)] public SessionToken                         SessionToken                    { get; private set; }
            [MetaMember(3)] public ScheduledMaintenanceModeForClient    ScheduledMaintenanceMode        { get; private set; }

            SessionResumeSuccess() { }
            public SessionResumeSuccess(SessionAcknowledgement serverSessionAcknowledgement, SessionToken sessionToken, ScheduledMaintenanceModeForClient scheduledMaintenanceMode)
            {
                ServerSessionAcknowledgement = serverSessionAcknowledgement;
                SessionToken = sessionToken;
                ScheduledMaintenanceMode = scheduledMaintenanceMode;
            }
        }

        [MetaMessage(MessageCodesCore.SessionResumeFailure, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class SessionResumeFailure : MetaMessage
        {
        }
    }
}
