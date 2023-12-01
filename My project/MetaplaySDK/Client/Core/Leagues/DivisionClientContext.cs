// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Player;
using System;

namespace Metaplay.Core.League
{
    /// <summary>
    /// The Client Context for the client's current Division. This maintains the DivisionModel state and the context
    /// state required for executing server updates and state required for client to enqueue actions for execution.
    /// </summary>
    public class DivisionClientContext<TDivisionModel> : MultiplayerEntityClientContext<TDivisionModel>
        where TDivisionModel : class, IDivisionModel, IMultiplayerModel<TDivisionModel>
    {
        EntityId _playerId;
        EntityId _participantId;

        /// <summary>
        /// Get the participant index for the current player.
        /// Only available if the player has a valid division.
        /// </summary>
        public int GetParticipantIndex()
        {
            IPlayerModelBase playerModel = _clientStore.GetPlayerClientContext().Model as IPlayerModelBase;
            if(!playerModel.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionState))
                throw new InvalidOperationException("No division SubClient state found for player");
            if(!(playerDivisionState is IDivisionClientState divisionClientState))
                throw new InvalidOperationException("Division SubClient state is not of type IDivisionClientState");

            return divisionClientState.CurrentDivisionParticipantIdx;
        }

        public DivisionClientContext(ClientMultiplayerEntityContextInitArgs initArgs, EntityId participantId)
            : base(initArgs)
        {
            _playerId       = initArgs.PlayerId;
            _participantId  = participantId;
        }

        /// <summary>
        /// Enqueues the Action for execution.
        /// </summary>
        public void EnqueueAction(DivisionActionBase action)
        {
            // Mark this action as originating from this client.
            action.InvokingParticipantId = _participantId;
            action.InvokingPlayerId = _playerId;

            base.EnqueueAction(action);
        }

        /// <inheritdoc />
        public override void EnqueueAction(ModelAction action)
        {
            if(action is DivisionActionBase divisionAction)
                EnqueueAction(divisionAction);
            else
                base.EnqueueAction(action);
        }

        public void SetClientListeners(Action<TDivisionModel> applyFn)
        {
            applyFn?.Invoke(Journal.StagedModel);
        }
    }
}
