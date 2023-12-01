// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// \todo [nuutti] Make activables not specific to Player?

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Activables
{
    /// <summary>
    /// A player-condition for requiring that a certain activable has been
    /// deactivated, and either has or hasn't been consumed, and a given duration
    /// has passed since the deactivation.
    /// Can be used for defining dependencies between activables.
    /// </summary>
    [MetaSerializable]
    public abstract class MetaActivablePrecursorCondition<TId> : PlayerCondition
    {
        [MetaMember(1)] public TId          Id          { get; private set; }
        [MetaMember(2)] public bool         Consumed    { get; private set; }
        [MetaMember(3)] public MetaDuration Delay       { get; private set; }

        public MetaActivablePrecursorCondition(){ }
        public MetaActivablePrecursorCondition(TId id, bool consumed, MetaDuration delay)
        {
            Consumed    = consumed;
            Id          = id;
            Delay       = delay;
        }

        protected abstract IMetaActivableSet<TId> GetActivableSet(IPlayerModelBase player);

        public override bool MatchesPlayer(IPlayerModelBase player)
        {
            MetaActivableState activableState = GetActivableSet(player).TryGetState(Id);
            if (activableState == null)
                return false;
            if (!activableState.LatestActivation.HasValue)
                return false;

            MetaActivableState.Activation activation = activableState.LatestActivation.Value;

            if (!activation.EndAt.HasValue || player.CurrentTime < activation.EndAt.Value + Delay)
                return false;

            bool wasConsumed = activation.NumConsumed > 0;
            return wasConsumed == Consumed;
        }

        public override IEnumerable<PlayerSegmentId> GetSegmentReferences()
        {
            return Enumerable.Empty<PlayerSegmentId>();
        }
    }
}
