// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Player Experiment and VariantId pair. By convention, null VariantId refers to control group.
    /// </summary>
    [MetaSerializable]
    public struct ExperimentVariantPair : IEquatable<ExperimentVariantPair>
    {
        [MetaMember(1)] public PlayerExperimentId   ExperimentId;
        [MetaMember(2)] public ExperimentVariantId  VariantId;

        public ExperimentVariantPair(PlayerExperimentId experimentId, ExperimentVariantId variantId)
        {
            ExperimentId = experimentId ?? throw new ArgumentNullException(nameof(experimentId));
            VariantId = variantId;
        }

        public void Deconstruct(out PlayerExperimentId experimentId, out ExperimentVariantId variantId)
        {
            experimentId = ExperimentId;
            variantId = VariantId;
        }

        public bool Equals(ExperimentVariantPair other) => ExperimentId == other.ExperimentId && VariantId == other.VariantId;
        public override bool Equals(object obj) => obj is ExperimentVariantPair other && Equals(other);
        public override int GetHashCode() => Util.CombineHashCode(ExperimentId.GetHashCode(), VariantId?.GetHashCode() ?? 0);

        public static bool operator ==(ExperimentVariantPair left, ExperimentVariantPair right) => left.Equals(right);
        public static bool operator !=(ExperimentVariantPair left, ExperimentVariantPair right) => !(left == right);

        public override string ToString() => $"({ExperimentId}/{VariantId})";
    }
}
