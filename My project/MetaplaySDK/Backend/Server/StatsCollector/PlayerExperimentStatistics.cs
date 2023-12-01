// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;

namespace Metaplay.Server
{
    [MetaSerializable]
    public class PlayerExperimentStatistics
    {
        [MetaSerializable]
        public class VariantStats
        {
            /// <summary>
            /// The few lastest players assigned into this variant.
            /// </summary>
            [MetaMember(1)] public List<EntityId> PlayerSample;

            public VariantStats()
            {
                PlayerSample = new List<EntityId>();
            }
        }

        /// <summary>
        /// The few lastest players assigned into this experiment.
        /// </summary>
        [MetaMember(1)] public List<EntityId> PlayerSample = new List<EntityId>();

        /// <summary>
        /// Per-variant statistics. Note that this is neither super- or subset of the State Variants:
        /// Statistics are created on demand when players are assigned into variants. Hence state may have
        /// variants for which there are no statistics yet. If a variant is deleted, the state is deleted,
        /// but statistics are not. Hence statistics may have variants, the state does not.
        /// </summary>
        [MetaMember(2)] public OrderedDictionary<ExperimentVariantId, VariantStats> Variants { get; private set; } = new OrderedDictionary<ExperimentVariantId, VariantStats>();
    }

    public static class PlayerExperimentPlayerSampleUtil
    {
        // \todo [jarkko]: this is very hacky for now.
        const int SampleMaxSize = 10;
        public static void InsertPlayerSample(List<EntityId> playerSample, EntityId playerId)
        {
            if (playerSample.Contains(playerId))
                return;

            if (playerSample.Count >= SampleMaxSize)
                playerSample.RemoveRange(0, playerSample.Count - SampleMaxSize + 1);
            playerSample.Add(playerId);
        }
        public static void RemovePlayerSample(List<EntityId> playerSample, EntityId playerId)
        {
            playerSample.Remove(playerId);
        }
    }
}
