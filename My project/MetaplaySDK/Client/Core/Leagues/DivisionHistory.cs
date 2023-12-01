// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.League.Player;
using Metaplay.Core.Model;

namespace Metaplay.Core.League
{
    /// <summary>
    /// <para>
    /// A participant specific history entry for a division.
    /// This is created by the division after the season has concluded, and should contain any data needed
    /// by the client to represent a historical division.
    /// </para>
    /// Extend this by extending the base class <see cref="PlayerDivisionHistoryEntryBase{TRewards}"/>.
    /// </summary>
    [MetaSerializable]
    [LeaguesEnabledCondition]
    public interface IDivisionHistoryEntry
    {
        EntityId         DivisionId    { get; }
        DivisionIndex    DivisionIndex { get; }
        IDivisionRewards Rewards       { get; }
    }

    /// <summary>
    /// <para>
    /// The result for an individual participant after a division has concluded. Can include things like
    /// the participant's placement, its avatar, and other things.
    /// </para>
    /// <para>
    /// This info is used by the league manager to decide on participants' promotions and demotions,
    /// and to gather data on the past season to show on the dashboard.
    /// </para>
    /// <para>Use <see cref="PlayerDivisionParticipantConclusionResultBase{TAvatar}"/> for players.</para>
    /// </summary>
    [MetaSerializable]
    [LeaguesEnabledCondition]
    public interface IDivisionParticipantConclusionResult
    {
        EntityId ParticipantId { get; }
    }

    public interface IPlayerDivisionParticipantConclusionResult : IDivisionParticipantConclusionResult
    {
        PlayerDivisionAvatarBase Avatar { get; }
    }

    [MetaReservedMembers(100, 200)]
    public abstract class PlayerDivisionParticipantConclusionResultBase<TAvatar> : IPlayerDivisionParticipantConclusionResult
        where TAvatar : PlayerDivisionAvatarBase
    {
        [MetaMember(100)] public EntityId ParticipantId { get; private set; }
        [MetaMember(101)] public TAvatar  Avatar        { get; private set; }

        PlayerDivisionAvatarBase IPlayerDivisionParticipantConclusionResult.Avatar => Avatar;

        protected PlayerDivisionParticipantConclusionResultBase(EntityId participantId, TAvatar avatar)
        {
            ParticipantId = participantId;
            Avatar        = avatar;
        }
    }
}
