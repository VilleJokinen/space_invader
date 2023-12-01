// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System.Collections.Generic;

namespace Metaplay.Server.GameConfig
{
    public class GameConfigSpecializationPatchesBuilder
    {
        public struct BuildStats
        {
            public int NumVariants;
            public int TotalNumBytes;

            public BuildStats(int numVariants, int totalNumBytes)
            {
                NumVariants = numVariants;
                TotalNumBytes = totalNumBytes;
            }
        }

        /// <summary>
        /// Builds patchset from state. If patchset would be empty, returns null instead.
        /// </summary>
        public static GameConfigSpecializationPatches TryBuildNonEmpty(IServerGameConfig serverConfig, IEnumerable<PlayerExperimentId> activeSubset, out BuildStats outBuildStats)
        {
            int numVariants = 0;
            OrderedDictionary<PlayerExperimentId, OrderedDictionary<ExperimentVariantId, byte[]>> patches = new OrderedDictionary<PlayerExperimentId, OrderedDictionary<ExperimentVariantId, byte[]>>();

            foreach (PlayerExperimentId experimentId in activeSubset)
            {
                PlayerExperimentInfo experiment = serverConfig.PlayerExperiments.GetValueOrDefault(experimentId);
                OrderedDictionary<ExperimentVariantId, byte[]> variantPatches = new OrderedDictionary<ExperimentVariantId, byte[]>();
                foreach ((ExperimentVariantId key, PlayerExperimentInfo.Variant variant) in experiment.Variants)
                {
                    byte[] patchBytes = variant.ConfigPatch.SharedConfigPatch.Serialize(MetaSerializationFlags.SendOverNetwork);

                    variantPatches.Add(key, patchBytes);
                    numVariants++;
                }
                patches.Add(experimentId, variantPatches);
            }

            // Additive protocol: If there are no patches, we don't build a patchset. This
            //                    allows backwards compatible protocol as long as we don't
            //                    have patches.
            if (numVariants == 0)
            {
                outBuildStats = new BuildStats(
                    numVariants:    0,
                    totalNumBytes:  0);
                return null;
            }

            // Compute version. Version is the content hash of a serialized patchset otherwise identical except version is set to None.

            byte[]          tempResultBytes = GameConfigSpecializationPatches.FromContents(version: ContentHash.None, patches).ToBytes();
            ContentHash     contentHash     = ContentHash.ComputeFromBytes(tempResultBytes);
            int             totalNumBytes   = tempResultBytes.Length;

            outBuildStats = new BuildStats(
                numVariants:    numVariants,
                totalNumBytes:  totalNumBytes);
            return GameConfigSpecializationPatches.FromContents(contentHash, patches);
        }
    }
}
