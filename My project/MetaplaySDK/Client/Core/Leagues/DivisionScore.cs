// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.League
{
    /// <summary>
    /// Computed total score of an Division participant. The score is computed from <see cref="IDivisionContribution"/>s based
    /// as defined using the rules and settings of the DivisionModel. This result is then used to rank Division participants.
    /// <para>
    /// The simplest implementation is to just have one integer named "Score" in the implementation, which is the sum of
    /// the Contribution scores.
    /// </para>
    /// <para>
    /// It is recommended to have a member `MetaTime LastActionAt` and to use it as the last resort tiebreaker. This can be used
    /// to make it so that in order to overtake another Participant, a later Participant must have reach higher score and
    /// in particular, just reaching the same score will not result in overtaking the earlier Participant.
    /// </para>
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaSerializable]
    public interface IDivisionScore
    {
        /// <summary>
        /// Returns less than 0, if the score is <i>worse</i> than <paramref name="other"/>.<br/>
        /// Returns greater than 0, if the score is <i>better</i> than <paramref name="other"/>.<br/>
        /// Otherwise, returns 0.
        /// </summary>
        int CompareTo(IDivisionScore other);
    }

    /// <summary>
    /// The total Score Contribution of a single source in a Division Participant. For example, in guild division, each player
    /// of the guild would have their own Score Contribution. Note that score contribution permanent unlike guild membership,
    /// meaning that a contribution may exist for sources which are no longer present in the Guild.
    /// <para>
    /// The simplest implementation is to just have one integer named "Score" in the implementation, which is updated when Score
    /// Events change it.
    /// </para>
    /// <para>
    /// It is recommended to have a member <c>MetaTime LastActionAt</c> and which is the timestamp of the latest accumualted Event.
    /// This can be used as a last-resort timestamp as described in <see cref="IDivisionScore"/>.
    /// </para>
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaSerializable]
    public interface IDivisionContribution
    {
    }

    /// <summary>
    /// <para>
    /// A single contribution of a Player/Guild into a League Division during a Season.
    /// </para>
    /// <para>
    /// Certain score events can be passed around in the server-code-only, so it can contain
    /// some more sensitive information. It will be applied into a <see cref="IDivisionContribution"/>
    /// and the results are sent to clients to not leak any data that we don't want other players to see.
    /// </para>
    /// <para>
    /// To avoid manual type casting, use <see cref="DivisionScoreEventBase{TContribution}"/>.
    /// </para>
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaSerializable]
    public interface IDivisionScoreEvent
    {
        /// <summary>
        /// Applies the changes of this event into a contribution. This method does not need to be
        /// deterministic as this is only executed on server and only the results are sent to clients.
        /// </summary>
        public abstract void AccumulateToContribution(IDivisionContribution contributionBase);
    }

    /// <summary>
    /// A typed helper for <see cref="IDivisionScoreEvent"/>.
    /// </summary>
    public abstract class DivisionScoreEventBase<TContribution> : IDivisionScoreEvent
        where TContribution : IDivisionContribution
    {
        void IDivisionScoreEvent.AccumulateToContribution(IDivisionContribution contributionBase)
        {
            AccumulateToContribution((TContribution)contributionBase);
        }

        /// <inheritdoc cref="IDivisionScoreEvent.AccumulateToContribution(IDivisionContribution)"/>
        public abstract void AccumulateToContribution(TContribution contribution);
    }
}
