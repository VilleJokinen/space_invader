// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using static System.FormattableString;

namespace Metaplay.Server.Matchmaking
{
    [MetaSerializableDerived(100)]
    public sealed class MmrBucketingStrategyState : IAsyncMatchmakerBucketingStrategyState
    {
        [MetaMember(1)] public int MmrHigh { get; set; }
        [MetaMember(2)] public int MmrLow  { get; set; }
    }

    [MetaSerializableDerived(100)]
    public sealed class MmrBucketingStrategyLabel : IRangedBucketLabel<MmrBucketingStrategyLabel>
    {
        [MetaMember(1)] public int BucketIndex { get; private set; }

        // These are modified after each rebalance.
        // Not used for hash or equality check
        [MetaMember(2), Transient] public int MmrLow  { get; set; }
        [MetaMember(3), Transient] public int MmrHigh { get; set; }

        public string DashboardLabel => Invariant($"{MmrLow}  - {MmrHigh}");

        public MmrBucketingStrategyLabel() { }

        public MmrBucketingStrategyLabel(int bucketIndex)
        {
            BucketIndex = bucketIndex;
        }

        public bool Equals(MmrBucketingStrategyLabel other)
        {
            if (ReferenceEquals(null, other))
                return false;

            return BucketIndex == other.BucketIndex;
        }

        public int CompareTo(object obj)
        {
            if(obj is MmrBucketingStrategyLabel other)
                return BucketIndex.CompareTo(other.BucketIndex);

            throw new InvalidOperationException($"Cannot compare {this} and {obj}");
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is MmrBucketingStrategyLabel other && Equals(other);
        }

        public override int GetHashCode()
        {
            uint x = (uint)BucketIndex;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return unchecked((int)x);
        }
    }

    public class MmrBucketingStrategy<TMmPlayerModel, TMmQuery> : AsyncMatchmakerBucketingStrategyBase<
        MmrBucketingStrategyLabel,
        MmrBucketingStrategyState,
        TMmPlayerModel,
        TMmQuery> , IAutoRebalancingBucketingStrategy<TMmPlayerModel>
        where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
        where TMmQuery : class, IAsyncMatchmakerQuery, new()
    {
        /// <inheritdoc />
        public int NumBuckets => _labels.Length;

        /// <inheritdoc />
        public override bool IsHardRequirement => false;

        AsyncMatchmakerOptionsBase _matchmakerOptions;

        MmrBucketingStrategyLabel[] _labels;

        int _minSampledMmr = Int32.MaxValue;
        int _maxSampledMmr = Int32.MinValue;

        /// <inheritdoc />
        public override MmrBucketingStrategyLabel GetBucketLabel(TMmPlayerModel model)
        {
            return _labels[GetBucketIndexForMmr(model.DefenseMmr)];
        }

        /// <inheritdoc />
        public override MmrBucketingStrategyLabel GetBucketLabel(TMmQuery query)
        {
            return _labels[GetBucketIndexForMmr(query.AttackMmr)];
        }

        void UpdateLabels()
        {
            for (int i = 0; i < _labels.Length; i++)
            {
                GetMmrRangeOfBucket(i, out int mmrLow, out int mmrHigh);
                _labels[i].MmrLow  = mmrLow;
                _labels[i].MmrHigh = mmrHigh;
            }
        }

        int GetBucketIndexForMmr(int mmr)
        {
            float mmrI = (mmr - State.MmrLow) / (float)(State.MmrHigh - State.MmrLow);
            return Math.Clamp((int)MathF.Floor(mmrI * NumBuckets), 0, NumBuckets - 1);
        }

        public override void PostLoad(AsyncMatchmakerOptionsBase matchmakerOptions)
        {
            _matchmakerOptions = matchmakerOptions;

            _labels = new MmrBucketingStrategyLabel[_matchmakerOptions.MmrBucketCount];

            for (int i = 0; i < _labels.Length; i++)
            {
                _labels[i] = new MmrBucketingStrategyLabel(i);
                GetMmrRangeOfBucket(i, out int mmrLow, out int mmrHigh);
                _labels[i].MmrLow = mmrLow;
                _labels[i].MmrHigh = mmrHigh;
            }
        }

        public override void OnResetState(AsyncMatchmakerOptionsBase matchmakerOptions)
        {
            UpdateLabels();
        }

        public override IAsyncMatchmakerBucketingStrategyState InitializeNew(AsyncMatchmakerOptionsBase matchmakerOptions)
        {
            MmrBucketingStrategyState state = new MmrBucketingStrategyState();
            state.MmrLow  = matchmakerOptions.InitialMinMmr;
            state.MmrHigh = matchmakerOptions.InitialMaxMmr;
            return state;
        }

        public void CollectSample(TMmPlayerModel model)
        {
            _minSampledMmr = Math.Min(_minSampledMmr, model.DefenseMmr);
            _maxSampledMmr = Math.Max(_maxSampledMmr, model.DefenseMmr);
        }

        public bool OnRebalance(AsyncMatchmakerOptionsBase options)
        {
            bool didChange = State.MmrLow != _minSampledMmr ||
                State.MmrHigh != _maxSampledMmr;

            if (_minSampledMmr >= _maxSampledMmr)
                return false;

            State.MmrLow  = _minSampledMmr;
            State.MmrHigh = _maxSampledMmr;

            UpdateLabels();

            return didChange;
        }

        void GetMmrRangeOfBucket(int idx, out int mmrLow, out int mmrHigh)
        {
            float mmrPerBucket = (State.MmrHigh - State.MmrLow) / (float)NumBuckets;
            mmrLow             = (int)(State.MmrLow + mmrPerBucket * idx);
            mmrHigh            = (int)(State.MmrLow + mmrPerBucket * (idx + 1));
        }
    }
}
