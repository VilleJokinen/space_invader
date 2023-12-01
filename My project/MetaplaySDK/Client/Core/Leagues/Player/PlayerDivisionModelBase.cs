// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System.Runtime.Serialization;

namespace Metaplay.Core.League.Player
{
    /// <summary>
    /// Base type for Division Model participant state where participant is a Player.
    /// </summary>
    /// <typeparam name="TDivisionScore">The concrete type defining the unit for a participant's total score.</typeparam>
    /// <typeparam name="TDivisionContribution">The concrete type defining the total score contribution of a participant.</typeparam>
    /// <typeparam name="TDivisionPlayerAvatar">The concrete type defining the avatar representation of a player. Default implementation <see cref="PlayerDivisionAvatarBase.Default"/>.</typeparam>
    [MetaReservedMembers(200, 300)]
    public abstract class PlayerDivisionParticipantStateBase<TDivisionScore, TDivisionContribution, TDivisionPlayerAvatar>
        : DivisionParticipantStateBase<TDivisionScore>
        , IPlayerDivisionParticipantState
        where TDivisionScore : IDivisionScore, new()
        where TDivisionContribution : IDivisionContribution, new()
        where TDivisionPlayerAvatar : PlayerDivisionAvatarBase
    {
        /// <inheritdoc cref="IPlayerDivisionParticipantState.PlayerAvatar"/>
        [MetaMember(201)] public TDivisionPlayerAvatar PlayerAvatar { get; set; }

        /// <inheritdoc cref="IPlayerDivisionParticipantState.PlayerContribution"/>
        [MetaMember(202)] public TDivisionContribution PlayerContribution { get; set; }

        protected PlayerDivisionParticipantStateBase() { }
        void IPlayerDivisionParticipantState.InitializeForPlayer(int participantIndex, PlayerDivisionAvatarBase playerAvatar)
        {
            ParticipantIndex        = participantIndex;
            PlayerAvatar            = (TDivisionPlayerAvatar)playerAvatar;
            PlayerContribution      = new TDivisionContribution();
            ResolvedDivisionRewards = null;
        }

        PlayerDivisionAvatarBase IPlayerDivisionParticipantState.PlayerAvatar
        {
            get { return PlayerAvatar; }
            set { PlayerAvatar = (TDivisionPlayerAvatar)value; }
        }
        IDivisionContribution IPlayerDivisionParticipantState.PlayerContribution
        {
            get { return PlayerContribution; }
            set { PlayerContribution = (TDivisionContribution)value; }
        }
    }

    /// <summary>
    /// Base type for Division Models where participant is a Player.
    /// </summary>
    /// <typeparam name="TModel">The concrete Model type, i.e. the inheriting type itself.</typeparam>
    /// <typeparam name="TParticipantState">The concrete type containing per-participant data. See <see cref="PlayerDivisionParticipantStateBase"/> for default implementation.</typeparam>
    /// <typeparam name="TDivisionScore">The concrete type defining the unit for a participant's total score.</typeparam>
    /// <typeparam name="TDivisionPlayerAvatar">The concrete type defining the avatar representation of a player. Default implementation <see cref="PlayerDivisionAvatarBase.Default"/>.</typeparam>
    [MetaReservedMembers(400, 500)]
    public abstract class PlayerDivisionModelBase<TModel, TParticipantState, TDivisionScore, TDivisionPlayerAvatar>
        : DivisionModelBase<TModel, TParticipantState, TDivisionScore>
        , IPlayerDivisionModel<TModel>
        where TModel : PlayerDivisionModelBase<TModel, TParticipantState, TDivisionScore, TDivisionPlayerAvatar>
        where TParticipantState : IPlayerDivisionParticipantState, new()
        where TDivisionScore : IDivisionScore
        where TDivisionPlayerAvatar : PlayerDivisionAvatarBase
    {

#pragma warning disable IDE1006 // Naming Styles
        [IgnoreDataMember] IPlayerDivisionModelServerListenerCore  _BackingField_ServerListenerCore = EmptyPlayerDivisionModelServerListenerCore.Instance;
        [IgnoreDataMember] IPlayerDivisionModelClientListenerCore  _BackingField_ClientListenerCore = EmptyPlayerDivisionModelClientListenerCore.Instance;
#pragma warning restore IDE1006 // Naming Styles
        [IgnoreDataMember] public new IDivisionModelServerListenerCore ServerListenerCore => _BackingField_ServerListenerCore;
        [IgnoreDataMember] public new IDivisionModelClientListenerCore ClientListenerCore => _BackingField_ClientListenerCore;
        IPlayerDivisionModelServerListenerCore IPlayerDivisionModel.ServerListenerCore => _BackingField_ServerListenerCore;
        IPlayerDivisionModelClientListenerCore IPlayerDivisionModel.ClientListenerCore => _BackingField_ClientListenerCore;
        protected override IDivisionModelServerListenerCore GetServerListenerCore() => _BackingField_ServerListenerCore;
        protected override IDivisionModelClientListenerCore GetClientListenerCore() => _BackingField_ClientListenerCore;
        public override void SetServerListenerCore(IDivisionModelServerListenerCore listener) { _BackingField_ServerListenerCore = (IPlayerDivisionModelServerListenerCore)listener; }
        public override void SetClientListenerCore(IDivisionModelClientListenerCore listener) { _BackingField_ClientListenerCore = (IPlayerDivisionModelClientListenerCore)listener; }

        protected PlayerDivisionModelBase() : base() { }

        /// <inheritdoc cref="IPlayerDivisionModel.AddParticipant"/>
        public virtual TParticipantState AddOrUpdateParticipant(int participantIndex, EntityId participantId, TDivisionPlayerAvatar playerAvatarBase)
        {
            if(participantIndex == -1)
                throw new System.ArgumentOutOfRangeException(nameof(participantIndex), "Participant index must be a non-negative integer.");
            
            // Ensure player exists
            TParticipantState newState = new TParticipantState();
            newState.InitializeForPlayer(participantIndex, playerAvatarBase);
            Participants.AddIfAbsent(participantIndex, newState);

            // Update avatar
            TParticipantState newOrExistingState = Participants[participantIndex];
            newOrExistingState.ParticipantId = participantId;
            newOrExistingState.PlayerAvatar = playerAvatarBase;
            return newOrExistingState;
        }

        bool IPlayerDivisionModel.TryGetParticipant(int participantIndex, out IPlayerDivisionParticipantState participant)
        {
            bool success = Participants.TryGetValue(participantIndex, out TParticipantState typedParticipant);
            participant = typedParticipant;
            return success;
        }

        IPlayerDivisionParticipantState IPlayerDivisionModel.AddOrUpdateParticipant(int participantIndex, EntityId participantId, PlayerDivisionAvatarBase playerAvatarBase)
            => AddOrUpdateParticipant(participantIndex, participantId, (TDivisionPlayerAvatar)playerAvatarBase);
    }
}
