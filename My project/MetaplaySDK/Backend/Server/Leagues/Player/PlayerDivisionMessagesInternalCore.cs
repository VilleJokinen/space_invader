// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using System.Collections.Generic;

namespace Metaplay.Server.League.Player.InternalMessages
{
    /// <summary>
    /// Sent by the PlayerActor to the PlayerDivisionActor to join or update the player's avatar in a Division.
    /// Division replies with <see cref="InternalPlayerDivisionJoinOrUpdateAvatarResponse"/>.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalPlayerDivisionJoinOrUpdateAvatarRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerDivisionJoinOrUpdateAvatarRequest : MetaMessage
    {
        public EntityId                 ParticipantId           { get; private set; }
        public int                      AvatarDataEpoch         { get; private set; }
        public PlayerDivisionAvatarBase PlayerAvatar            { get; private set; }

        /// <summary>
        /// Debug switch to allow joining a Division that is already concluded.
        /// </summary>
        public bool                     AllowJoiningConcluded   { get; private set; }

        InternalPlayerDivisionJoinOrUpdateAvatarRequest() { }
        public InternalPlayerDivisionJoinOrUpdateAvatarRequest(EntityId participantId, int avatarDataEpoch, PlayerDivisionAvatarBase playerAvatar, bool allowJoiningConcluded)
        {
            ParticipantId = participantId;
            AllowJoiningConcluded = allowJoiningConcluded;
            AvatarDataEpoch = avatarDataEpoch;
            PlayerAvatar = playerAvatar;
            AllowJoiningConcluded = allowJoiningConcluded;
        }
    }

    /// <summary>
    /// Sent by the division actor in response to with <see cref="InternalPlayerDivisionJoinOrUpdateAvatarRequest"/>.
    /// Contains the index of the participant in the division.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalPlayerDivisionJoinOrUpdateAvatarResponse, MessageDirection.ServerInternal)]
    public class InternalPlayerDivisionJoinOrUpdateAvatarResponse : MetaMessage
    {
        /// <summary>
        /// The index of the participant in the division.
        /// </summary>
        public int DivisionParticipantIndex { get; private set; }

        public InternalPlayerDivisionJoinOrUpdateAvatarResponse(int divisionParticipantIndex)
        {
            DivisionParticipantIndex = divisionParticipantIndex;
        }

        InternalPlayerDivisionJoinOrUpdateAvatarResponse() { }
    }

    [PlayerLeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionParticipantResultRequest, MessageDirection.ServerInternal)]
    public class InternalDivisionParticipantResultRequest : MetaMessage
    {
        /// <summary>
        /// Can be null if all participants are requested.
        /// </summary>
        public List<EntityId> ParticipantEntityIds { get; private set; }

        public InternalDivisionParticipantResultRequest() { }

        public InternalDivisionParticipantResultRequest(List<EntityId> participantEntityIds)
        {
            ParticipantEntityIds = participantEntityIds;
        }
    }

    [PlayerLeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionParticipantResultResponse, MessageDirection.ServerInternal)]
    public class InternalDivisionParticipantResultResponse : MetaMessage
    {
        public Dictionary<EntityId, IDivisionParticipantConclusionResult> ParticipantResults { get; private set; }

        InternalDivisionParticipantResultResponse() { }

        public InternalDivisionParticipantResultResponse(Dictionary<EntityId, IDivisionParticipantConclusionResult> participantResults)
        {
            ParticipantResults = participantResults;
        }
    }

    [PlayerLeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalPlayerDivisionAvatarBatchUpdate, MessageDirection.ServerInternal)]
    public class InternalPlayerDivisionAvatarBatchUpdate : MetaMessage
    {
        public Dictionary<EntityId, PlayerDivisionAvatarBase> PlayerAvatars { get; private set; }

        InternalPlayerDivisionAvatarBatchUpdate() { }

        public InternalPlayerDivisionAvatarBatchUpdate(Dictionary<EntityId, PlayerDivisionAvatarBase> playerAvatars)
        {
            PlayerAvatars = playerAvatars;
        }
    }

    /// <summary>
    /// Ask the division actor for the participant entity id of a participant.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionParticipantIdRequest, MessageDirection.ServerInternal)]
    public class InternalDivisionParticipantIdRequest : MetaMessage
    {
        public int ParticipantIndex { get; private set; }

        InternalDivisionParticipantIdRequest() { }

        public InternalDivisionParticipantIdRequest(int participantIndex)
        {
            ParticipantIndex = participantIndex;
        }
    }

    /// <summary>
    /// Reply to <see cref="InternalDivisionParticipantIdRequest"/>.
    /// Contains the participant entity id.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    [MetaMessage(MessageCodesCore.InternalDivisionParticipantIdResponse, MessageDirection.ServerInternal)]
    public class InternalDivisionParticipantIdResponse : MetaMessage
    {
        public EntityId ParticipantEntityId { get; private set; }

        InternalDivisionParticipantIdResponse() { }

        public InternalDivisionParticipantIdResponse(EntityId participantEntityId)
        {
            ParticipantEntityId = participantEntityId;
        }
    }
}
