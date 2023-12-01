// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Session;
using Metaplay.Core.TypeCodes;
using Metaplay.Server;
using System.Collections.Generic;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Session-level client->server ping. Server will respond with a <see cref="SessionPong"/>
    /// with the same <c>Id</c> as in the ping.
    /// </summary>
    [MetaMessage(MessageCodesCore.SessionPing, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class SessionPing : MetaMessage
    {
        public int Id;

        SessionPing(){ }
        public SessionPing(int id)
        {
            Id = id;
        }
    }

    /// <summary>
    /// Session-level server->client pong; response to <see cref="SessionPing"/>.
    /// The <c>Id</c> is the same as in the corresponding ping.
    /// </summary>
    [MetaMessage(MessageCodesCore.SessionPong, MessageDirection.ServerToClient)]
    public class SessionPong : MetaMessage
    {
        public int Id;

        SessionPong(){ }
        public SessionPong(int id)
        {
            Id = id;
        }
    }

    /// <summary>
    /// Update to the currently available localization versions.
    /// </summary>
    [MetaMessage(MessageCodesCore.UpdateLocalizationVersions, MessageDirection.ServerToClient)]
    [LocalizationsEnabledCondition]
    public class UpdateLocalizationVersions : MetaMessage
    {
        public OrderedDictionary<LanguageId, ContentHash>   LocalizationVersions    { get; private set; }

        UpdateLocalizationVersions() { }
        public UpdateLocalizationVersions(OrderedDictionary<LanguageId, ContentHash> localizationVersions)
        {
            LocalizationVersions = localizationVersions;
        }
    }

    /// <summary>
    /// Representation of scheduled maintenance mode for a particular client. See <see cref="ScheduledMaintenanceMode"/> on server.
    /// </summary>
    [MetaSerializable]
    public class ScheduledMaintenanceModeForClient
    {
        [MetaMember(1)] public MetaTime StartAt { get; private set; }
        /// <summary>
        /// If EstimationIsValid is true then the operator entered an estimated duration for the maintenance window in EstimatedDurationInMinutes,
        /// otherwise there is no estimation of how long the maintenance will take
        /// </summary>
        [MetaMember(2)] public int EstimatedDurationInMinutes { get; private set; }
        /// <inheritdoc cref="EstimatedDurationInMinutes"/>
        [MetaMember(3)] public bool EstimationIsValid { get; private set; }

        ScheduledMaintenanceModeForClient() { }
        public ScheduledMaintenanceModeForClient(MetaTime startAt, int estimatedDurationInMinutes, bool estimationIsValid)
        {
            StartAt = startAt;
            EstimatedDurationInMinutes = estimatedDurationInMinutes;
            EstimationIsValid = estimationIsValid;
        }
    }

    /// <summary>
    /// Update to scheduled maintenance mode
    /// </summary>
    [MetaMessage(MessageCodesCore.UpdateScheduledMaintenanceMode, MessageDirection.ServerToClient)]
    public class UpdateScheduledMaintenanceMode : MetaMessage
    {
        public ScheduledMaintenanceModeForClient ScheduledMaintenanceMode { get; private set; }

        UpdateScheduledMaintenanceMode() { }
        public UpdateScheduledMaintenanceMode(ScheduledMaintenanceModeForClient scheduledMaintenanceMode)
        {
            ScheduledMaintenanceMode = scheduledMaintenanceMode;
        }
    }

    [MetaSerializable]
    public abstract class SessionForceTerminateReason
    {
        /// <summary> Another connection came in, forcing this one to be terminated. </summary>
        [MetaSerializableDerived(1)] public class ReceivedAnotherConnection : SessionForceTerminateReason { }
        /// <summary> A disruptive admin action was done on the player. </summary>
        [MetaSerializableDerived(2)] public class KickedByAdminAction       : SessionForceTerminateReason { }
        /// <summary> An unexpected situation in the server. </summary>
        [MetaSerializableDerived(3)] public class InternalServerError       : SessionForceTerminateReason { }
        /// <summary> Reason not specified by server. </summary>
        [MetaSerializableDerived(4)] public class Unknown                   : SessionForceTerminateReason { }
        /// <summary> Client has fallen too far behind the server's time. </summary>
        [MetaSerializableDerived(5)] public class ClientTimeTooFarBehind    : SessionForceTerminateReason { }
        /// <summary> Client has gone too far ahead the server's time. </summary>
        [MetaSerializableDerived(6)] public class ClientTimeTooFarAhead     : SessionForceTerminateReason { }
        /// <summary> Session has exceeded the maximum duration. </summary>
        [MetaSerializableDerived(7)] public class SessionTooLong            : SessionForceTerminateReason { }
        /// <summary> Session was terminated because player was banned. </summary>
        [MetaSerializableDerived(8)] public class PlayerBanned              : SessionForceTerminateReason { }
        /// <summary> Session was terminated because maintenance mode started. </summary>
        [MetaSerializableDerived(9)] public class MaintenanceModeStarted    : SessionForceTerminateReason { }
        /// <summary> Session was terminated because client was on pause for too long. </summary>
        [MetaSerializableDerived(10)] public class PauseDeadlineExceeded    : SessionForceTerminateReason { }
    }

    /// <summary>
    /// A new connection has come in for the session, and the old connection gets terminated.
    /// </summary>
    [MetaMessage(MessageCodesCore.SessionForceTerminateMessage, MessageDirection.ServerToClient)]
    public class SessionForceTerminateMessage : MetaMessage
    {
        public SessionForceTerminateReason Reason { get; private set; }

        public SessionForceTerminateMessage() { }
        public SessionForceTerminateMessage(SessionForceTerminateReason reason)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// Base class for session control messages, i.e. session-time messages that are not
    /// payload messages. All non-control messages sent in a session are assumed to
    /// be "business logic" payload messages that are delivered from one session participant
    /// to the other, and that are subjects of acknowledgement, re-sending, and other
    /// such session mechanisms.
    /// </summary>
    public abstract class SessionControlMessage : MetaMessage
    {
    }

    /// <summary>
    /// Client or server does a session acknowledgement. See <see cref="SessionAcknowledgement"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.SessionAcknowledgementMessage, MessageDirection.Bidirectional), MessageRoutingRuleProtocol]
    public class SessionAcknowledgementMessage : SessionControlMessage
    {
        public SessionAcknowledgement Acknowledgement { get; private set; }

        public SessionAcknowledgementMessage(){ }
        public SessionAcknowledgementMessage(SessionAcknowledgement acknowledgement)
        {
            Acknowledgement = acknowledgement;
        }
    }

    [MetaImplicitMembersRange(150, 200)]
    public abstract class MetaRequestMessage : MetaMessage
    {
        public int Id { get; set; }
        public MetaRequest Payload { get; set; }

        public MetaRequestMessage() { }
    }

    [MetaImplicitMembersRange(150, 200)]
    public abstract class MetaResponseMessage : MetaMessage
    {
        public int RequestId { get; set; }
        public MetaResponse Payload { get; set; }

        public MetaResponseMessage() { }
    }


    [MetaMessage(MessageCodesCore.SessionMetaRequestMessage, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class SessionMetaRequestMessage : MetaRequestMessage
    {
    }

    [MetaMessage(MessageCodesCore.SessionMetaResponseMessage, MessageDirection.ServerToClient)]
    public class SessionMetaResponseMessage : MetaResponseMessage
    {
    }

    [MetaSerializableDerived(RequestTypeCodes.DevOverwritePlayerStateRequest)]
    public class DevOverwritePlayerStateRequest : MetaRequest
    {
        public string EntityArchiveJson { get; private set; }

        public DevOverwritePlayerStateRequest() { }
        public DevOverwritePlayerStateRequest(string json) { EntityArchiveJson = json; }
    }

    [MetaSerializableDerived(RequestTypeCodes.DevOverwritePlayerStateFailure)]
    public class DevOverwritePlayerStateFailure : MetaResponse
    {
        public string Reason;

        public DevOverwritePlayerStateFailure() { }
        public DevOverwritePlayerStateFailure(string failureReason) { Reason = failureReason; }
    }
}
