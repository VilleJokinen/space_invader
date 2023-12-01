// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Metaplay.Server
{
    /// <summary>
    /// Statistics concerning activables of all kinds.
    /// </summary>
    [MetaSerializable]
    public class FullMetaActivableStatistics
    {
        [MetaMember(1)] public Dictionary<MetaActivableKindId, MetaActivableKindStatistics> KindStatistics { get; private set; } = new Dictionary<MetaActivableKindId, MetaActivableKindStatistics>();
    }

    /// <summary>
    /// Statistics concerning activables of a specific kind.
    /// </summary>
    [MetaSerializable]
    public class MetaActivableKindStatistics
    {
        // \note Using the `string` type here for the activable id, instead of the proper activable id type.
        //       The proper type isn't known until runtime, so it'd be a bit tricky to deal with that here.
        //       Some indirection could probably be invented to deal with this, but for now, `string` works
        //       well enough, assuming that the activable id types are all `IStringId`s (there's a constraint
        //       for that in IMetaActivableConfigData<>, for this very reason).
        //       #activable-id-type
        [MetaMember(1)] public Dictionary<string, MetaActivableStatistics> ActivableStatistics { get; private set; } = new Dictionary<string, MetaActivableStatistics>();
    }

    /// <summary>
    /// Statistics concerning a single activable.
    /// </summary>
    [MetaSerializable]
    public struct MetaActivableStatistics
    {
        [MetaMember(1)] public long NumActivated;
        [MetaMember(2)] public long NumActivatedForFirstTime;
        [MetaMember(3)] public long NumConsumed;
        [MetaMember(4)] public long NumConsumedForFirstTime;
        [MetaMember(5)] public long NumFinalized;
        [MetaMember(6)] public long NumFinalizedForFirstTime;

        public MetaActivableStatistics(long numActivated, long numActivatedForFirstTime, long numConsumed, long numConsumedForFirstTime, long numFinalized, long numFinalizedForFirstTime)
        {
            NumActivated = numActivated;
            NumActivatedForFirstTime = numActivatedForFirstTime;
            NumConsumed = numConsumed;
            NumConsumedForFirstTime = numConsumedForFirstTime;
            NumFinalized = numFinalized;
            NumFinalizedForFirstTime = numFinalizedForFirstTime;
        }

        public static MetaActivableStatistics ForSingleActivation(bool isFirstActivation)
        {
            return new MetaActivableStatistics(
                numActivated:               1,
                numActivatedForFirstTime:   isFirstActivation ? 1 : 0,
                numConsumed:                0,
                numConsumedForFirstTime:    0,
                numFinalized:               0,
                numFinalizedForFirstTime:   0);
        }

        public static MetaActivableStatistics ForSingleConsumption(bool isFirstConsumption)
        {
            return new MetaActivableStatistics(
                numActivated:               0,
                numActivatedForFirstTime:   0,
                numConsumed:                1,
                numConsumedForFirstTime:    isFirstConsumption ? 1 : 0,
                numFinalized:               0,
                numFinalizedForFirstTime:   0);
        }

        public static MetaActivableStatistics ForSingleFinalization(bool isFirstFinalization)
        {
            return new MetaActivableStatistics(
                numActivated:               0,
                numActivatedForFirstTime:   0,
                numConsumed:                0,
                numConsumedForFirstTime:    0,
                numFinalized:               1,
                numFinalizedForFirstTime:   isFirstFinalization ? 1 : 0);
        }

        public static MetaActivableStatistics Sum(MetaActivableStatistics a, MetaActivableStatistics b)
        {
            return new MetaActivableStatistics(
                numActivated:               a.NumActivated                + b.NumActivated,
                numActivatedForFirstTime:   a.NumActivatedForFirstTime    + b.NumActivatedForFirstTime,
                numConsumed:                a.NumConsumed                 + b.NumConsumed,
                numConsumedForFirstTime:    a.NumConsumedForFirstTime     + b.NumConsumedForFirstTime,
                numFinalized:               a.NumFinalized                + b.NumFinalized,
                numFinalizedForFirstTime:   a.NumFinalizedForFirstTime    + b.NumFinalizedForFirstTime);
        }
    }
}
