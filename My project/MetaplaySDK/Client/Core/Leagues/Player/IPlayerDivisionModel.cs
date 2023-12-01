// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.League.Player
{
    /// <summary>
    /// Untyped subset of <see cref="IPlayerDivisionModel{TModel}"/>.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    public interface IPlayerDivisionModel : IDivisionModel
    {
        new IPlayerDivisionModelServerListenerCore ServerListenerCore { get; }
        new IPlayerDivisionModelClientListenerCore ClientListenerCore { get; }

        /// <summary>
        /// Adds a new participant player into Participants. Or updates an existing participant.
        /// <paramref name="participantId"/> may be None if server doesn't share participant ids with clients.
        /// </summary>
        IPlayerDivisionParticipantState AddOrUpdateParticipant(int participantIndex, EntityId participantId, PlayerDivisionAvatarBase playerAvatarBase);

        /// <summary>
        /// Returns true and the participant state of the player if the player is a participant. Otherwise returns false, and null state.
        /// </summary>
        bool TryGetParticipant(int participantIndex, out IPlayerDivisionParticipantState participant);
    }

    /// <summary>
    /// The base interface Division Models for Divisions where a Players are Participants. The <typeparamref name="TDivisionModel"/>
    /// parameter should be the type for the concrete division class itself. For example <c>class MyDivision : IPlayerDivisionModel&lt;MyDivision&gt;</c>.
    /// </summary>
    /// <typeparam name="TDivisionModel">The concrete model type.</typeparam>
    public interface IPlayerDivisionModel<TDivisionModel> : IPlayerDivisionModel, IDivisionModel<TDivisionModel>
        where TDivisionModel : IDivisionModel<TDivisionModel>
    {
    }

    /// <summary>
    /// Necessary state of all Participant players in a Division.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    [MetaSerializable]
    public interface IPlayerDivisionParticipantState : IDivisionParticipantState
    {
        /// <summary>
        /// The visual representation of the player.
        /// </summary>
        PlayerDivisionAvatarBase PlayerAvatar { get; set; }

        /// <summary>
        /// The score contribution of the player.
        /// </summary>
        IDivisionContribution PlayerContribution { get; set; }

        /// <summary>
        /// Initializes this state for a new player.
        /// </summary>
        void InitializeForPlayer(int participantIndex, PlayerDivisionAvatarBase playerAvatar);
    }
}
