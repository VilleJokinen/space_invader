// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Core.Schedule;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;

namespace Metaplay.Server.League
{
    [MetaSerializable]
    public abstract class LeagueJoinRequestPayloadBase { }

    [MetaSerializableDerived(101)]
    public class EmptyLeagueJoinRequestPayload : LeagueJoinRequestPayloadBase
    {
        public static EmptyLeagueJoinRequestPayload Instance { get; } = new EmptyLeagueJoinRequestPayload();
    }

    /// <summary>
    /// Player or Participant -> League a request to join the leagues. Response is <see cref="InternalLeagueJoinResponse"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueJoinRequest, MessageDirection.ServerInternal)]
    public class InternalLeagueJoinRequest : MetaMessage
    {
        public EntityId                     ParticipantId { get; private set; }
        public LeagueJoinRequestPayloadBase Payload       { get; private set; }

        public InternalLeagueJoinRequest(EntityId participantId, LeagueJoinRequestPayloadBase payload)
        {
            ParticipantId = participantId;
            Payload       = payload;
        }

        InternalLeagueJoinRequest() { }
    }

    /// <summary>
    /// Dashboard -> League request to add a participant to the league on a certain rank. Response is <see cref="InternalLeagueJoinResponse"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueJoinRankRequest, MessageDirection.ServerInternal)]
    public class InternalLeagueDebugJoinRankRequest : MetaMessage
    {
        public EntityId ParticipantId { get; private set; }
        public int      StartingRank  { get; private set; }

        public InternalLeagueDebugJoinRankRequest(EntityId participantId, int startingRank)
        {
            ParticipantId = participantId;
            StartingRank  = startingRank;
        }

        InternalLeagueDebugJoinRankRequest() { }
    }

    /// <summary>
    /// League -> Participant a response to <see cref="Metaplay.Server.League.InternalLeagueJoinRequest"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueJoinResponse, MessageDirection.ServerInternal)]
    public class InternalLeagueJoinResponse : MetaMessage
    {
        public bool                   Success        { get; private set; }
        public DivisionIndex          DivisionToJoin { get; private set; }
        public LeagueJoinRefuseReason RefuseReason   { get; private set; }

        public InternalLeagueJoinResponse(bool success, DivisionIndex divisionToJoin, LeagueJoinRefuseReason refuseReason = LeagueJoinRefuseReason.UnknownReason)
        {
            DivisionToJoin = divisionToJoin;
            RefuseReason   = refuseReason;
            Success        = success;
        }

        InternalLeagueJoinResponse() { }

        public static InternalLeagueJoinResponse ForSuccess(DivisionIndex divisionToJoin)
            => new InternalLeagueJoinResponse(true, divisionToJoin);

        public static InternalLeagueJoinResponse ForFailure(LeagueJoinRefuseReason refuseReason)
            => new InternalLeagueJoinResponse(false, default, refuseReason);
    }

    /// <summary>
    /// Dashboard or Participant -> League or League -> Division request to leave the leagues. Response is <see cref="EntityAskOk"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueLeaveRequest, MessageDirection.ServerInternal)]
    public class InternalLeagueLeaveRequest : MetaMessage
    {
        public EntityId ParticipantId { get; private set; }
        /// <summary>
        /// Whether or not this was initiated from the dashboard. If so, the participant will be informed with a <see cref="InternalLeagueParticipantDivisionForceUpdated"/> message.
        /// </summary>
        public bool     IsAdminAction { get; private set; }

        public InternalLeagueLeaveRequest(EntityId participantId, bool isAdminAction)
        {
            ParticipantId = participantId;
            IsAdminAction = isAdminAction;
        }

        InternalLeagueLeaveRequest() { }
    }

    /// <summary>
    /// Dashboard -> League a request to forcibly add a player to a certain division. Response is <see cref="InternalLeagueDebugAddResponse"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueDebugAddRequest, MessageDirection.ServerInternal)]
    public class InternalLeagueDebugAddRequest : MetaMessage
    {
        public EntityId ParticipantId { get; private set; }
        public EntityId DivisionId    { get; private set; }

        public InternalLeagueDebugAddRequest(EntityId participantId, EntityId divisionId)
        {
            ParticipantId = participantId;
            DivisionId    = divisionId;
        }

        InternalLeagueDebugAddRequest() { }
    }

    /// <summary>
    /// League -> Admin API response to <see cref="InternalLeagueDebugAddRequest"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueDebugAddResponse, MessageDirection.ServerInternal)]
    public class InternalLeagueDebugAddResponse : MetaMessage
    {
        public bool WasAlreadyInDivision { get; private set; }

        public InternalLeagueDebugAddResponse(
            bool wasAlreadyInDivision)
        {
            WasAlreadyInDivision     = wasAlreadyInDivision;
        }

        InternalLeagueDebugAddResponse() { }
    }

    /// <summary>
    /// Admin API -> League request league manager state. Response is <see cref="InternalLeagueStateResponse"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueStateRequest, MessageDirection.ServerInternal)]
    public class InternalLeagueStateRequest : MetaMessage
    {
        public static InternalLeagueStateRequest Instance { get; } = new InternalLeagueStateRequest();
        InternalLeagueStateRequest() { }
    }

    [MetaSerializable]
    public class LeagueSeasonMigrationProgressState
    {
        [MetaMember(1)] public bool   IsInProgress     { get; private set; }
        [MetaMember(2)] public float  ProgressEstimate { get; private set; }
        [MetaMember(3)] public string Phase            { get; private set; }
        [MetaMember(4)] public string Error            { get; private set; }

        LeagueSeasonMigrationProgressState() { }

        public LeagueSeasonMigrationProgressState(bool isInProgress, float progressEstimate, string phase, string error)
        {
            IsInProgress     = isInProgress;
            ProgressEstimate = progressEstimate;
            Phase            = phase;
            Error            = error;
        }
    }

    /// <summary>
    /// League -> Admin API response to <see cref="InternalLeagueStateRequest"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueStateResponse, MessageDirection.ServerInternal)]
    public class InternalLeagueStateResponse : MetaMessage
    {
        public bool                               Enabled                  { get; private set; }
        public LeagueManagerActorStateBase        LeagueManagerState       { get; private set; }
        public MetaScheduleBase                   Schedule                 { get; private set; }
        public LeagueRankDetails[]                CurrentSeasonRankDetails { get; private set; }
        public LeagueSeasonDetails                CurrentSeasonDetails     { get; private set; }
        public LeagueDetails                      LeagueDetails            { get; private set; }
        public LeagueSeasonMigrationProgressState MigrationProgress        { get; private set; }

        public InternalLeagueStateResponse(
            LeagueManagerActorStateBase leagueManagerState,
            MetaScheduleBase schedule,
            bool enabled,
            LeagueRankDetails[] rankDetails,
            LeagueSeasonDetails currentSeasonDetails,
            LeagueDetails leagueDetails,
            LeagueSeasonMigrationProgressState migrationProgress)
        {
            LeagueManagerState       = leagueManagerState;
            Schedule                 = schedule;
            Enabled                  = enabled;
            CurrentSeasonRankDetails = rankDetails;
            CurrentSeasonDetails     = currentSeasonDetails;
            LeagueDetails            = leagueDetails;
            MigrationProgress        = migrationProgress;
        }

        InternalLeagueStateResponse() { }
    }

    /// <summary>
    /// Admin API -> League. Request league to either start a new season or end the current one. Response is <see cref="EntityAskOk"/> or <see cref="InvalidEntityAsk"/>.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueDebugAdvanceSeasonRequest, MessageDirection.ServerInternal)]
    public class InternalLeagueDebugAdvanceSeasonRequest : MetaMessage
    {
        /// <summary>
        /// Whether to end the current season or start a new one.
        /// </summary>
        public bool IsEndSeasonRequest { get; private set; }

        /// <inheritdoc />
        public InternalLeagueDebugAdvanceSeasonRequest(bool isEndSeasonRequest)
        {
            IsEndSeasonRequest = isEndSeasonRequest;
        }

        InternalLeagueDebugAdvanceSeasonRequest() { }
    }

    /// <summary>
    /// League -> Participant message sent from league to participant when their division has been updated via an admin action.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalLeagueParticipantDivisionForceUpdated, MessageDirection.ServerInternal)]
    public class InternalLeagueParticipantDivisionForceUpdated : MetaMessage
    {
        /// <summary>
        /// The new assigned division.
        /// </summary>
        public EntityId NewDivision { get; private set; }

        public InternalLeagueParticipantDivisionForceUpdated(EntityId newDivision)
        {
            NewDivision = newDivision;
        }

        public InternalLeagueParticipantDivisionForceUpdated() { }
    }
}
