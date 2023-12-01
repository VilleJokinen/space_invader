// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;

namespace Metaplay.Server
{
    /// <summary>
    /// Pig in a pipeline. Used for pigging message channels to establish synchronization.
    ///
    /// A Pig is placed on a pubsub channel (i.e. sent) by a source entity. When the target
    /// entity receives the pig, the target entity knows all preceeding messages on that
    /// channel have been flushed. Using this information, entities can then coordinate,
    /// and create form desired partial message orderings between two or more entities.
    /// </summary>
    // \todo better place
    [MetaMessage(MessageCodesCore.InternalPig, MessageDirection.ServerInternal)]
    public class InternalPig : MetaMessage
    {
        public int PiggingId { get; private set; }

        public InternalPig() { }
        public InternalPig(int piggingId)
        {
            PiggingId = piggingId;
        }
    }

    /// <summary>
    /// Connection -> Session subscription request. Succeeds with <see cref="InternalSessionStartNewResponse"/>, or fails
    /// by throwing <see cref="InternalSessionStartNewRefusal"/> error.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalSessionStartNewRequest, MessageDirection.ServerInternal)]
    public class InternalSessionStartNewRequest : MetaMessage
    {
        public int                                              LogicVersion                { get; private set; }
        public uint                                             AppLaunchId                 { get; private set; }
        public uint                                             ClientSessionNonce          { get; private set; }
        public uint                                             ClientSessionConnectionNdx  { get; private set; }
        public string                                           ClientVersion               { get; private set; }
        public string                                           DeviceGuid                  { get; private set; }
        public LoginDebugDiagnostics                            DebugDiagnostics            { get; private set; }
        public Handshake.ILoginRequestGamePayload               LoginGamePayload            { get; private set; }
        public SessionProtocol.SessionResourceProposal          SessionResourceProposal     { get; private set; }
        public ISessionStartRequestGamePayload                  SessionGamePayload          { get; private set; }
        public string                                           DeviceModel                 { get; private set; }
        public PlayerTimeZoneInfo                               PlayerTimeZoneInfo          { get; private set; }
        public PlayerLocation?                                  PlayerLocation              { get; private set; }
        public bool                                             IsDryRun                    { get; private set; }
        public CompressionAlgorithmSet                          SupportedArchiveCompressions{ get; private set; }
        public ClientPlatform                                   ClientPlatform              { get; private set; }
        public AuthenticationKey                                AuthKey                     { get; private set; }

        InternalSessionStartNewRequest(){ }
        public InternalSessionStartNewRequest(
            int logicVersion,
            uint appLaunchId,
            uint clientSessionNonce,
            uint clientSessionConnectionNdx,
            string clientVersion,
            string deviceGuid,
            LoginDebugDiagnostics debugDiagnostics,
            Handshake.ILoginRequestGamePayload loginGamePayload,
            SessionProtocol.SessionResourceProposal sessionResourceProposal,
            ISessionStartRequestGamePayload sessionGamePayload,
            string deviceModel,
            PlayerTimeZoneInfo playerTimeZoneInfo,
            PlayerLocation? playerLocation,
            bool isDryRun,
            CompressionAlgorithmSet supportedArchiveCompressions,
            ClientPlatform clientPlatform,
            AuthenticationKey authKey)
        {
            LogicVersion = logicVersion;
            AppLaunchId = appLaunchId;
            ClientSessionNonce = clientSessionNonce;
            ClientSessionConnectionNdx = clientSessionConnectionNdx;
            ClientVersion = clientVersion;
            DeviceGuid = deviceGuid;
            DebugDiagnostics = debugDiagnostics;
            LoginGamePayload = loginGamePayload;
            SessionResourceProposal = sessionResourceProposal;
            SessionGamePayload = sessionGamePayload;
            DeviceModel = deviceModel;
            PlayerTimeZoneInfo = playerTimeZoneInfo;
            PlayerLocation = playerLocation;
            IsDryRun = isDryRun;
            SupportedArchiveCompressions = supportedArchiveCompressions;
            ClientPlatform = clientPlatform;
            AuthKey = authKey;
        }
    }

    [MetaMessage(MessageCodesCore.InternalSessionStartNewResponse, MessageDirection.ServerInternal)]
    public class InternalSessionStartNewResponse : MetaMessage
    {
        public MetaSessionAuxiliaryInfo                     MetaAuxiliaryInfo       { get; private set; }
        public ISessionStartSuccessGamePayload              GamePayload             { get; private set; }
        public SessionToken                                 Token                   { get; private set; }

        InternalSessionStartNewResponse() { }
        public InternalSessionStartNewResponse(MetaSessionAuxiliaryInfo metaAuxiliaryInfo, ISessionStartSuccessGamePayload gamePayload, SessionToken token)
        {
            MetaAuxiliaryInfo = metaAuxiliaryInfo;
            GamePayload = gamePayload;
            Token = token;
        }
    }

    [MetaSerializableDerived(MessageCodesCore.InternalSessionStartNewRefusal)]
    public class InternalSessionStartNewRefusal : EntityAskRefusal
    {
        [MetaSerializable]
        public enum ResultCode
        {
            EntityIsRestarting = 1,
            ConnectionIsStale = 2,
            ResourceCorrection = 3,
            DryRunSuccess = 4,
            PlayerIsBanned = 5,
        }

        [MetaMember(1)] public ResultCode                                   Result                  { get; private set; }
        [MetaMember(2)] public SessionProtocol.SessionResourceCorrection    ResourceCorrection      { get; private set; }

        public override string Message => $"Session setup refused with {Result}";

        InternalSessionStartNewRefusal() { }
        InternalSessionStartNewRefusal(ResultCode result, SessionProtocol.SessionResourceCorrection resourceCorrection)
        {
            Result = result;
            ResourceCorrection = resourceCorrection;
        }

        public static InternalSessionStartNewRefusal ForEntityIsRestarting() => new InternalSessionStartNewRefusal(ResultCode.EntityIsRestarting, default);
        public static InternalSessionStartNewRefusal ForStaleConnection() => new InternalSessionStartNewRefusal(ResultCode.ConnectionIsStale, default);
        public static InternalSessionStartNewRefusal ForResourceCorrection(SessionProtocol.SessionResourceCorrection correction) => new InternalSessionStartNewRefusal(ResultCode.ResourceCorrection, correction);
        public static InternalSessionStartNewRefusal ForDryRunSuccess() => new InternalSessionStartNewRefusal(ResultCode.DryRunSuccess, default);
        public static InternalSessionStartNewRefusal ForPlayerIsBanned() => new InternalSessionStartNewRefusal(ResultCode.PlayerIsBanned, default);
    }

    /// <summary>
    /// Connection -> Session subscription request. Succeeds with <see cref="InternalSessionResumeResponse"/>, or fails
    /// by throwing <see cref="InternalSessionResumeRequest"/> error.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalSessionResumeRequest, MessageDirection.ServerInternal)]
    public class InternalSessionResumeRequest : MetaMessage
    {
        public int                                              LogicVersion                { get; private set; }
        public uint                                             AppLaunchId                 { get; private set; }
        public uint                                             ClientSessionNonce          { get; private set; }
        public uint                                             ClientSessionConnectionNdx  { get; private set; }
        public string                                           ClientVersion               { get; private set; }
        public LoginDebugDiagnostics                            DebugDiagnostics            { get; private set; }
        public Handshake.ILoginRequestGamePayload               LoginGamePayload            { get; private set; }
        public SessionResumptionInfo                            SessionToResume             { get; private set; }

        InternalSessionResumeRequest() { }
        public InternalSessionResumeRequest(
            int logicVersion,
            uint appLaunchId,
            uint clientSessionNonce,
            uint clientSessionConnectionNdx,
            string clientVersion,
            LoginDebugDiagnostics debugDiagnostics,
            Handshake.ILoginRequestGamePayload loginGamePayload,
            SessionResumptionInfo sessionToResume)
        {
            LogicVersion = logicVersion;
            AppLaunchId = appLaunchId;
            ClientSessionNonce = clientSessionNonce;
            ClientSessionConnectionNdx = clientSessionConnectionNdx;
            ClientVersion = clientVersion;
            DebugDiagnostics = debugDiagnostics;
            LoginGamePayload = loginGamePayload;
            SessionToResume = sessionToResume;
        }
    }

    [MetaMessage(MessageCodesCore.InternalSessionResumeResponse, MessageDirection.ServerInternal)]
    public class InternalSessionResumeResponse : MetaMessage
    {
        public ScheduledMaintenanceModeForClient            ScheduledMaintenanceMode    { get; private set; }
        public SessionToken                                 Token                       { get; private set; }
        public SessionAcknowledgement                       ServerAcknowledgement       { get; private set; }

        InternalSessionResumeResponse() { }
        public InternalSessionResumeResponse(ScheduledMaintenanceModeForClient scheduledMaintenanceMode, SessionToken token, SessionAcknowledgement serverAcknowledgement)
        {
            ScheduledMaintenanceMode = scheduledMaintenanceMode;
            Token = token;
            ServerAcknowledgement = serverAcknowledgement;
        }
    }

    [MetaSerializableDerived(MessageCodesCore.InternalSessionResumeRefusal)]
    public class InternalSessionResumeRefusal : EntityAskRefusal
    {
        [MetaSerializable]
        public enum ResultCode
        {
            ResumeFailure = 1,
            ConnectionIsStale = 2,
        }

        [MetaMember(1)] public ResultCode                                   Result                  { get; private set; }

        public override string Message => $"Session resume refused with {Result}";

        InternalSessionResumeRefusal() { }
        InternalSessionResumeRefusal(ResultCode result)
        {
            Result = result;
        }

        public static InternalSessionResumeRefusal ForStaleConnection() => new InternalSessionResumeRefusal(ResultCode.ConnectionIsStale);
        public static InternalSessionResumeRefusal ForResumeFailure() => new InternalSessionResumeRefusal(ResultCode.ResumeFailure);
    }

    /// <summary>
    /// Wrapper message for messages from client. Sent by ClientConnection to SessionActor.
    /// </summary>
    /// <remarks>
    /// This is used so we can use HandleMessage instead of HandleUnknownMessage.
    /// The latter isn't async, while some session messages need async handling.
    /// </remarks>
    [MetaMessage(MessageCodesCore.SessionMessageFromClient, MessageDirection.ServerInternal)]
    public class SessionMessageFromClient : MetaMessage
    {
        public MetaMessage Message { get; private set; }

        SessionMessageFromClient(){ }
        public SessionMessageFromClient(MetaMessage message)
        {
            Message = message;
        }
    }

    #if !METAPLAY_DISABLE_GUILDS

    /// <summary>
    /// PubSub kick payload for when Guild kicks Session when session player is being kicked from the guild.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalSessionGuildMemberKicked, MessageDirection.ServerInternal)]
    public class InternalSessionGuildMemberKicked : MetaMessage
    {
        public static readonly InternalSessionGuildMemberKicked Instance = new InternalSessionGuildMemberKicked();
    }

    #endif

    /// <summary>
    /// Session -> Entity notification when client app status changes.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalSessionNotifyClientAppStatusChanged, MessageDirection.ServerInternal)]
    public class InternalSessionNotifyClientAppStatusChanged : MetaMessage
    {
        public bool IsClientConnected { get; private set; }
        public ClientAppPauseStatus PauseStatus { get; private set; }

        InternalSessionNotifyClientAppStatusChanged() { }
        public InternalSessionNotifyClientAppStatusChanged(bool isClientConnected, ClientAppPauseStatus pauseStatus)
        {
            IsClientConnected = isClientConnected;
            PauseStatus = pauseStatus;
        }
    }

    [MetaMessage(MessageCodesCore.InternalNewPlayerIncidentRecorded, MessageDirection.ServerInternal)]
    public class InternalNewPlayerIncidentRecorded : MetaMessage
    {
        public string IncidentId { get; private set; }
        public MetaTime OccurredAt { get; private set; }
        public string Type { get; private set; }
        public string SubType { get; private set; }
        public string Reason { get; private set; }

        InternalNewPlayerIncidentRecorded() { }
        public InternalNewPlayerIncidentRecorded(string incidentId, MetaTime occurredAt, string type, string subType, string reason)
        {
            IncidentId = incidentId;
            OccurredAt = occurredAt;
            Type = type;
            SubType = subType;
            Reason = reason;
        }
    }

    /// <summary>
    /// Entity -> Session notification when an associated entity is added or removed.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalSessionEntityAssociationUpdate, MessageDirection.ServerInternal)]
    public class InternalSessionEntityAssociationUpdate : MetaMessage
    {
        public ClientSlot Slot { get; private set; }

        /// <summary>
        /// <c>null</c> if association from the slot is removed.
        /// </summary>
        public AssociatedEntityRefBase AssociationRef { get; private set; }

        InternalSessionEntityAssociationUpdate() { }

        /// <summary>
        /// Updates association in slot <paramref name="slot"/>. If <paramref name="associationRef"/> is <c>null</c>,
        /// the existing association in the slot is removed.
        /// </summary>
        public InternalSessionEntityAssociationUpdate(ClientSlot slot, AssociatedEntityRefBase associationRef)
        {
            Slot = slot;
            AssociationRef = associationRef;
        }
    }

    /// <summary>
    /// Entity -> Session message to broadcast the Payload message from Session to the subscribed Entities.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalSessionEntityBroadcastMessage, MessageDirection.ServerInternal)]
    public class InternalSessionEntityBroadcastMessage : MetaMessage
    {
        /// <summary>
        /// Entity slots which should receive the payload message.
        /// </summary>
        public OrderedSet<ClientSlot> TargetSlots { get; private set; }

        /// <summary>
        /// The message sent by Session to the target entitities.
        /// </summary>
        public MetaMessage PayloadMessage;

        InternalSessionEntityBroadcastMessage() { }
        public InternalSessionEntityBroadcastMessage(OrderedSet<ClientSlot> targetSlots, MetaMessage payloadMessage)
        {
            TargetSlots = targetSlots ?? throw new ArgumentNullException(nameof(targetSlots));
            PayloadMessage = payloadMessage ?? throw new ArgumentNullException(nameof(payloadMessage));
        }
    }
}
