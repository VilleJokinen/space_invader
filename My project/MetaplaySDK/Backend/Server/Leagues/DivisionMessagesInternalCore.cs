// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Core.Schedule;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System.Runtime.Serialization;

namespace Metaplay.Server.League.InternalMessages
{
    //
    // This file contains Server-internal messages that are part of Metaplay core. To add
    // game-specific server-internal messages, see DivisionMessagesInternal.cs
    //

    [LeaguesEnabledCondition]
    [MetaSerializableDerived(102)]
    public class AssociatedDivisionRef : AssociatedEntityRefBase
    {
        public ClientSlot   ClientSlot          { get; private set; }
        public int          DivisionAvatarEpoch { get; private set; }

        public override ClientSlot GetClientSlot() => ClientSlot;
        [IgnoreDataMember] public EntityId DivisionId => AssociatedEntity;

        AssociatedDivisionRef() { }
        public AssociatedDivisionRef(ClientSlot clientSlot, EntityId sourceEntity, EntityId divisionEntity, int divisionAvatarEpoch) : base(sourceEntity, divisionEntity)
        {
            ClientSlot = clientSlot;
            DivisionAvatarEpoch = divisionAvatarEpoch;
        }
    }

    [LeaguesEnabledCondition]
    [MetaSerializableDerived(MessageCodesCore.InternalDivisionSubscribeRefusedParticipantAvatarDesync)]
    public class InternalDivisionSubscribeRefusedParticipantAvatarDesync : InternalEntitySubscribeRefusedBase
    {
        public override string Message => $"Session subscribe refused with {nameof(InternalDivisionSubscribeRefusedParticipantAvatarDesync)}";

        public InternalDivisionSubscribeRefusedParticipantAvatarDesync()
        {
        }
    }

    /// <summary>
    /// Player or Participant -> Division notification of a new Score event.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionScoreEventMessage, MessageDirection.ServerInternal)]
    public class InternalDivisionScoreEventMessage : MetaMessage
    {
        public EntityId             ParticipantId;
        public EntityId             PlayerId;
        public IDivisionScoreEvent  ScoreEvent;

        InternalDivisionScoreEventMessage() { }
        public InternalDivisionScoreEventMessage(EntityId participantId, EntityId playerId, IDivisionScoreEvent scoreEvent)
        {
            ParticipantId   = participantId;
            PlayerId        = playerId;
            ScoreEvent      = scoreEvent;
        }
    }

    /// <summary>
    /// Division -> SlotEntity notification when Division progress state (has started, has ended, has concluded) changes.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionProgressStateChangedMessage, MessageDirection.ServerInternal)]
    public class InternalDivisionProgressStateChangedMessage : MetaMessage
    {
        public static readonly InternalDivisionProgressStateChangedMessage Instance = new InternalDivisionProgressStateChangedMessage();
        InternalDivisionProgressStateChangedMessage() { }
    }

    [MetaSerializableDerived(100)]
    public class DivisionSetupParams : IMultiplayerEntitySetupParams
    {
        public EntityId CreatorId               { get; private set; }
        public int      League                  { get; private set; }
        public int      Season                  { get; private set; }
        public int      Rank                    { get; private set; }
        public int      Division                { get; private set; }
        public MetaTime ScheduledStartTime      { get; private set; }
        public MetaTime ScheduledEndTime        { get; private set; }
        public MetaTime ScheduledEndingSoonTime { get; private set; }

        DivisionSetupParams() { }

        public DivisionSetupParams(EntityId creatorId, int league, int season, int rank, int division, MetaTime startTime, MetaTime endTime, MetaTime endingSoonTime)
        {
            CreatorId               = creatorId;
            Season                  = season;
            Rank                    = rank;
            Division                = division;
            League                  = league;
            ScheduledStartTime      = startTime;
            ScheduledEndTime        = endTime;
            ScheduledEndingSoonTime = endingSoonTime;
        }
    }

    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionForceSetupDebugRequest, MessageDirection.ServerInternal)]
    public class InternalDivisionForceSetupDebugRequest : MetaMessage
    {
        public DivisionSetupParams Args { get; private set; }

        InternalDivisionForceSetupDebugRequest() { }
        public InternalDivisionForceSetupDebugRequest(DivisionSetupParams args)
        {
            Args = args;
        }
    }

    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionForceSetupDebugResponse, MessageDirection.ServerInternal)]
    public class InternalDivisionForceSetupDebugResponse : MetaMessage
    {
        public bool IsSuccess { get; private set; }
        InternalDivisionForceSetupDebugResponse() { }
        public InternalDivisionForceSetupDebugResponse(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }
    }

    /// <summary>
    /// Request the division for a participant's history entry. Replies with <see cref="InternalDivisionParticipantHistoryResponse"/>.
    /// This is safe to ask for even before the division has concluded.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionParticipantHistoryRequest, MessageDirection.ServerInternal)]
    public class InternalDivisionParticipantHistoryRequest : MetaMessage
    {
        public EntityId ParticipantId { get; private set; }

        InternalDivisionParticipantHistoryRequest() { }
        public InternalDivisionParticipantHistoryRequest(EntityId participantId)
        {
            ParticipantId = participantId;
        }
    }

    /// <summary>
    /// Contains the division's response to a <see cref="InternalDivisionParticipantHistoryRequest"/>.
    /// If the division has not concluded, the history entry will be null.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionParticipantHistoryResponse, MessageDirection.ServerInternal)]
    public class InternalDivisionParticipantHistoryResponse : MetaMessage
    {
        public bool                        IsParticipant { get; private set; }
        public bool                        IsConcluded   { get; private set; }
        public IDivisionHistoryEntry       HistoryEntry  { get; private set; } // Null if not available

        InternalDivisionParticipantHistoryResponse() { }
        public InternalDivisionParticipantHistoryResponse(bool isParticipant, bool isConcluded, IDivisionHistoryEntry historyEntry)
        {
            IsParticipant = isParticipant;
            IsConcluded   = isConcluded;
            HistoryEntry  = historyEntry;
        }
    }

    /// <summary>
    /// Request for Division to reply with current season progress state. Replies with <see cref="InternalDivisionProgressStateResponse"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionProgressStateRequest, MessageDirection.ServerInternal)]
    public class InternalDivisionProgressStateRequest : MetaMessage
    {
        public static readonly InternalDivisionProgressStateRequest Instance = new InternalDivisionProgressStateRequest();
        private InternalDivisionProgressStateRequest() { }
    }

    /// <summary>
    /// Reply to <see cref="InternalDivisionProgressStateRequest"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionProgressStateResponse, MessageDirection.ServerInternal)]
    public class InternalDivisionProgressStateResponse : MetaMessage
    {
        public bool     IsConcluded { get; private set; }
        public MetaTime StartsAt    { get; private set; }
        public MetaTime EndsAt      { get; private set; }

        InternalDivisionProgressStateResponse() { }

        public InternalDivisionProgressStateResponse(bool isConcluded, MetaTime startsAt, MetaTime endsAt)
        {
            IsConcluded = isConcluded;
            StartsAt    = startsAt;
            EndsAt      = endsAt;
        }
    }

    /// <summary>
    /// Kick reason for session when a division has performed some debug/development actions and
    /// existing subscription is no longer valid.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalSessionDivisionDebugReset, MessageDirection.ServerInternal)]
    public class InternalSessionDivisionDebugReset : MetaMessage
    {
        public EntityId CreatorId { get; private set; }

        InternalSessionDivisionDebugReset() { }
        public InternalSessionDivisionDebugReset(EntityId creatorId)
        {
            CreatorId = creatorId;
        }
    }

    /// <summary>
    /// Request for Division to debug-force transition into the next phase.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionMoveToNextSeasonPhaseDebugRequest, MessageDirection.ServerInternal)]
    public class InternalDivisionMoveToNextSeasonPhaseDebugRequest : MetaMessage
    {
        public DivisionSeasonPhase RequestedNextPhase;

        InternalDivisionMoveToNextSeasonPhaseDebugRequest() { }
        public InternalDivisionMoveToNextSeasonPhaseDebugRequest(DivisionSeasonPhase requestedNextPhase)
        {
            RequestedNextPhase = requestedNextPhase;
        }
    }

    /// <summary>
    /// Sent by the league manager to the divisions when the league manager's season schedule is changed mid-season.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionDebugSeasonScheduleUpdate, MessageDirection.ServerInternal)]
    public class InternalDivisionDebugSeasonScheduleUpdate : MetaMessage
    {
        public MetaTime NewStartTime { get; private set; }
        public MetaTime NewEndTime   { get; private set; }

        public InternalDivisionDebugSeasonScheduleUpdate(MetaTime newStartTime, MetaTime newEndTime)
        {
            NewStartTime = newStartTime;
            NewEndTime   = newEndTime;
        }

        InternalDivisionDebugSeasonScheduleUpdate() { }
    }
}
