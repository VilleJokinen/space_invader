// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Canonical identifier of a config specialization. Specialization key identifies the specialization combination of a certain GameConfig.
    /// </summary>
    [MetaSerializable]
    public struct GameConfigSpecializationKey : IEquatable<GameConfigSpecializationKey>
    {
        /// <summary>
        /// Experiment variant of each Experiment in the source data the key was generated against. For FullGameConfigs, i.e. in server context, this is the set
        /// of all experiments declared in the FullGameConfigs. For SharedGameConfigs, i.e. in client context, this is the set of Active Experiments, i.e. the
        /// set of experiments in the delivered Patch Archive.
        /// </summary>
        [MetaMember(1)] public ExperimentVariantId[] VariantIds { get; private set; }

        GameConfigSpecializationKey(ExperimentVariantId[] variantIds)
        {
            VariantIds = variantIds;
        }

        public static GameConfigSpecializationKey FromRaw(ExperimentVariantId[] variantIds) => new GameConfigSpecializationKey(variantIds);

        public bool Equals(GameConfigSpecializationKey other)
        {
            if (VariantIds.Length != other.VariantIds.Length)
                return false;

            for (int ndx = 0; ndx < VariantIds.Length; ++ndx)
            {
                if (VariantIds[ndx] != other.VariantIds[ndx])
                    return false;
            }
            return true;
        }

        public override bool Equals(object obj) => (obj is GameConfigSpecializationKey otherKey) && Equals(otherKey);

        public override int GetHashCode()
        {
            unchecked
            {
                uint hashCode = 0;
                for (int ndx = 0; ndx < VariantIds.Length; ++ndx)
                {
                    ExperimentVariantId variantId = VariantIds[ndx];
                    hashCode = hashCode * 2551 + (uint)(variantId?.GetHashCode() ?? 0);
                }
                return (int)hashCode;
            }
        }

        public static bool operator== (GameConfigSpecializationKey lhs, GameConfigSpecializationKey rhs) => lhs.Equals(rhs);
        public static bool operator!= (GameConfigSpecializationKey lhs, GameConfigSpecializationKey rhs) => !lhs.Equals(rhs);
    }

    public static class GameConfigSpecializationKeyUtil
    {
        public readonly struct BuildStep
        {
            public readonly GameConfigSpecializationKey SpecializationKey;
            public readonly PlayerExperimentId          Experiment;
            public readonly ExperimentVariantId         Variant;

            public BuildStep(GameConfigSpecializationKey specializationKey, PlayerExperimentId experiment, ExperimentVariantId variant)
            {
                SpecializationKey = specializationKey;
                Experiment = experiment;
                Variant = variant;
            }
        }

        public static List<BuildStep> EnumerateSpecializationBuildSteps(GameConfigSpecializationKey key, IEnumerable<PlayerExperimentId> experiments)
        {
            List<BuildStep> steps = new List<BuildStep>();
            List<PlayerExperimentId> experimentsList = new List<PlayerExperimentId>(experiments);

            steps.Add(new BuildStep(
                specializationKey:  CopyKeyUpTo(key, 0),
                experiment:     null,
                variant:        null));

            for (int ndx = 0; ndx < key.VariantIds.Length; ++ndx)
            {
                if (key.VariantIds[ndx] == null)
                    continue;

                steps.Add(new BuildStep(
                    specializationKey:  CopyKeyUpTo(key, ndx + 1),
                    experiment:         experimentsList[ndx],
                    variant:            key.VariantIds[ndx]));
            }

            return steps;
        }

        static GameConfigSpecializationKey CopyKeyUpTo(GameConfigSpecializationKey key, int len)
        {
            ExperimentVariantId[] raw = new ExperimentVariantId[key.VariantIds.Length];
            Array.Copy(key.VariantIds, 0, raw, 0, len);
            return GameConfigSpecializationKey.FromRaw(raw);
        }
    }
}
