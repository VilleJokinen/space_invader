// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Metaplay.Core.Player
{
    [MetaSerializable]
    public abstract class PlayerCondition
    {
        public abstract bool MatchesPlayer(IPlayerModelBase player);

        /// <summary>
        /// This should return the ids of all the segments this PlayerCondition refers to.
        /// More precisely, all the segments whose PlayerConditions' MatchesPlayer might
        /// get evaluated when evaluating this PlayerCondition's MatchesPlayer.
        ///
        /// This is used in the validation of internal references in the
        /// player segmentation configs: segments contain PlayerConditions, and
        /// PlayerConditions can refer to other segments; there mustn't be cycles
        /// in this graph.
        ///
        /// This will probably be replaced by something else in the future when more
        /// general sdk-side segmentation support is implemented (which might involve
        /// more general cycle-detection), or when config-internal IGameConfigData
        /// references are more properly supported.
        /// </summary>
        public abstract IEnumerable<PlayerSegmentId> GetSegmentReferences();
    }

    public struct PlayerFilterCriteria
    {
        public List<EntityId> PlayersToInclude;
        public PlayerCondition Condition;

        public bool IsEmpty => (PlayersToInclude == null || PlayersToInclude.Count == 0) && Condition == null;

        public PlayerFilterCriteria(List<EntityId> explicitPlayerIds, PlayerCondition condition)
        {
            PlayersToInclude = explicitPlayerIds;
            Condition = condition;
        }
    }
}
