// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Serialization;
using Metaplay.Server.League.InternalMessages;
using Metaplay.Server.League.Player.InternalMessages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Server.League.Player
{
    /// <summary>
    /// Base class for Division Actors where the participant is a Player.
    /// </summary>
    public abstract class PlayerDivisionActorBase<TModel, TServerModel, TPersisted>
        : DivisionActorBase<TModel, TServerModel, TPersisted>,
            IPlayerDivisionModelServerListenerCore
        where TModel : class, IPlayerDivisionModel<TModel>, new()
        where TPersisted : PersistedDivisionBase, new()
        where TServerModel : class, IDivisionServerModel, new()
    {
        protected PlayerDivisionActorBase(EntityId entityId) : base(entityId)
        {
        }

        protected override bool TryApplyScoreEvent(int participantIdx, EntityId playerId, IDivisionScoreEvent scoreEvent)
        {
            if (!Model.TryGetParticipant(participantIdx, out IPlayerDivisionParticipantState participant))
                return false;

            IDivisionContribution originalContribution = participant.PlayerContribution;
            IDivisionContribution updatedContribution = MetaSerialization.CloneTagged(originalContribution, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            scoreEvent.AccumulateToContribution(updatedContribution);
            ExecuteAction(new PlayerDivisionUpdateContribution(participantIdx, updatedContribution));
            return true;
        }

        [EntityAskHandler]
        async Task<InternalPlayerDivisionJoinOrUpdateAvatarResponse> HandleInternalPlayerDivisionJoinRequest(InternalPlayerDivisionJoinOrUpdateAvatarRequest request)
        {
            if(Model == null)
                throw new InvalidEntityAsk("Division not initialized yet.");
            if(!request.ParticipantId.IsOfKind(EntityKindCore.Player))
                throw new InvalidEntityAsk("Only players can join player divisions.");

            bool isExistingParticipant = Model.TryGetParticipant(Model.GetParticipantIndexById(request.ParticipantId), out _);

            // If model is concluded, still allow joining if the participant is already in the division to update avatar.
            if (Model.IsConcluded && !isExistingParticipant && !request.AllowJoiningConcluded)
                throw new InvalidEntityAsk("Division is already concluded. Cannot join into the division.");

            int participantIdx = Model.GetParticipantIndexById(request.ParticipantId);
            ExecuteAction(new PlayerDivisionAddOrUpdateParticipant(participantIdx,
                request.ParticipantId,
                request.PlayerAvatar));

            if (!isExistingParticipant)
            {
                // Participant is a new participant. Add analytics event.
                DivisionEventParticipantInfo participantInfo = GetParticipantInfo(participantIdx, request.ParticipantId, request.PlayerAvatar);
                Model.EventStream.Event(new DivisionEventParticipantJoined(participantInfo, false));

                await GameOnDivisionParticipantJoined(request);
            }

            if (Model.TryGetParticipant(Model.GetParticipantIndexById(request.ParticipantId), out IPlayerDivisionParticipantState participant))
            {
                participant.AvatarDataEpoch = request.AvatarDataEpoch;
                return new InternalPlayerDivisionJoinOrUpdateAvatarResponse(participant.ParticipantIndex);
            }

            throw new InvalidEntityAsk("Failed to join division. Participant not found after adding.");
        }

        [EntityAskHandler]
        async Task<EntityAskOk> HandleInternalPlayerDivisionAvatarBatchUpdate(InternalPlayerDivisionAvatarBatchUpdate request)
        {
            if (Model == null)
                throw new InvalidEntityAsk("Division not initialized yet.");

            foreach ((EntityId playerId, PlayerDivisionAvatarBase playerAvatar) in request.PlayerAvatars)
            {
                int  participantIdx        = Model.GetParticipantIndexById(playerId);
                bool isExistingParticipant = participantIdx >= 0;
                if (!isExistingParticipant)
                {
                    // Participant is a new participant. Add analytics event.
                    DivisionEventParticipantInfo participantInfo = GetParticipantInfo(participantIdx, playerId, playerAvatar);
                    Model.EventStream.Event(new DivisionEventParticipantJoined(participantInfo, true));
                }
                ExecuteAction(new PlayerDivisionAddOrUpdateParticipant(participantIdx, playerId, playerAvatar));
            }

            await PersistStateIntermediate();

            return EntityAskOk.Instance;
        }

        protected override IDivisionHistoryEntry HandleParticipantHistoryRequestInternal(EntityId participantId, IDivisionParticipantState participant)
        {
            IDivisionRewards resolvedRewards = participant.ResolvedDivisionRewards;
            if (resolvedRewards != null)
            {
                // Clone value and set IsClaimed to false before sending to client.
                resolvedRewards = MetaSerialization.CloneTagged(resolvedRewards, MetaSerializationFlags.IncludeAll, _logicVersion, _baselineGameConfigResolver);

                resolvedRewards.IsClaimed = false;

                // Set original resolved rewards IsClaimed to true to show in dashboard.
                participant.ResolvedDivisionRewards.IsClaimed = true;
            }
            IDivisionHistoryEntry historyEntry = GetDivisionHistoryEntryForPlayer(Model.GetParticipantIndexById(participantId), resolvedRewards);

            return historyEntry;
        }

        protected virtual DivisionEventParticipantInfo GetParticipantInfo(int participantIdx, EntityId particpantId, PlayerDivisionAvatarBase avatar)
        {
            if (avatar is PlayerDivisionAvatarBase.Default defaultAvatar)
                return new DivisionEventParticipantInfo(participantIdx, particpantId, defaultAvatar.DisplayName);

            throw new NotImplementedException($"Implement {nameof(GetParticipantInfo)} for avatar type {avatar.GetType().Name}.");
        }

        #region Callbacks to userland

        protected virtual Task GameOnDivisionParticipantJoined(InternalPlayerDivisionJoinOrUpdateAvatarRequest request) => Task.CompletedTask;

        #endregion
    }

    public abstract class PlayerDivisionActorBase<TModel, TPersisted> : PlayerDivisionActorBase<TModel, DefaultDivisionServerModel, TPersisted>
        where TModel : class, IPlayerDivisionModel<TModel>, new()
        where TPersisted : PersistedDivisionBase, new()
    {
        protected PlayerDivisionActorBase(EntityId entityId) : base(entityId)
        {
        }
    }
}
