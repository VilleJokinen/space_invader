// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Delivery format and container the Specialization patch set of a GameConfig. This only contains the SharedGameConfig
    /// which, from the client's perspective, is the only game config there is.
    /// </summary>
    [MetaSerializable]
    public class GameConfigSpecializationPatches
    {
        [MetaMember(1)] public ContentHash                                                                              Version { get; private set; }
        [MetaMember(2)] public OrderedDictionary<PlayerExperimentId, OrderedDictionary<ExperimentVariantId, byte[]>>    Patches { get; private set; }

        GameConfigSpecializationPatches() { }
        GameConfigSpecializationPatches(ContentHash version, OrderedDictionary<PlayerExperimentId, OrderedDictionary<ExperimentVariantId, byte[]>> patches)
        {
            Version = version;
            Patches = patches;
        }

        /// <summary>
        /// Creates a key from assignment. Essentially this chooses the intersection of the Experiments and orders them
        /// in patch order.
        /// </summary>
        public GameConfigSpecializationKey CreateKeyFromAssignment(OrderedDictionary<PlayerExperimentId, ExperimentVariantId> assignment)
        {
            ExperimentVariantId[] variants = new ExperimentVariantId[Patches.Count];
            int ndx = 0;
            foreach ((PlayerExperimentId patchExperimentId, OrderedDictionary<ExperimentVariantId, byte[]> patchVariants) in Patches)
            {
                ExperimentVariantId variantId;
                if (!assignment.TryGetValue(patchExperimentId, out ExperimentVariantId variantInAssignment))
                {
                    // not assigned.
                    variantId = null;
                }
                else if (variantInAssignment == null)
                {
                    // assigned into control
                    variantId = null;
                }
                else if (patchVariants.ContainsKey(variantInAssignment))
                {
                    // assigned into a known variant
                    variantId = variantInAssignment;
                }
                else
                {
                    // assigned into an unknown variant
                    throw new InvalidOperationException($"cannot create specialization key, unknown variant {variantInAssignment} in experiment {patchExperimentId}");
                }

                variants[ndx] = variantId;
                ndx++;
            }
            return GameConfigSpecializationKey.FromRaw(variants);
        }

        /// <summary>
        /// Returns the patches defined by the specialization key.
        /// </summary>
        public OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> GetPatchesForSpecialization(GameConfigSpecializationKey specializationKey)
        {
            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> patchesToApply = new OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope>();
            int ndx = 0;
            foreach ((PlayerExperimentId experimentId, OrderedDictionary<ExperimentVariantId, byte[]> variantPatches) in Patches)
            {
                ExperimentVariantId variantId = specializationKey.VariantIds[ndx++];
                if (variantId == null)
                {
                    // control group, no patching
                    continue;
                }
                ExperimentVariantPair patchId = new ExperimentVariantPair(experimentId, variantId);
                using (IOReader reader = new IOReader(variantPatches[variantId]))
                {
                    GameConfigPatchEnvelope patch = GameConfigPatchEnvelope.Deserialize(reader);
                    patchesToApply.Add(patchId, patch);
                }
            }
            return patchesToApply;
        }

        /// <summary>
        /// Encodes patches into a byte blob.
        /// </summary>
        public byte[] ToBytes()
        {
            // \todo [jarkko]: compress the data?
            return MetaSerialization.SerializeTagged<GameConfigSpecializationPatches>(this, MetaSerializationFlags.IncludeAll, logicVersion: null);
        }

        /// <summary>
        /// Decodes byte blob into patchset.
        /// </summary>
        public static GameConfigSpecializationPatches FromBytes(byte[] bytes)
        {
            return MetaSerialization.DeserializeTagged<GameConfigSpecializationPatches>(bytes, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
        }

        public static GameConfigSpecializationPatches FromContents(ContentHash version, OrderedDictionary<PlayerExperimentId, OrderedDictionary<ExperimentVariantId, byte[]>> patches)
        {
            return new GameConfigSpecializationPatches(version, patches);
        }
    }
}
