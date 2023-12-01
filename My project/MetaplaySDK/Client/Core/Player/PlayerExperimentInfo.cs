// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Configuration (config data) for a player experiment.
    /// </summary>
    [MetaSerializable]
    public class PlayerExperimentInfo : IGameConfigData<PlayerExperimentId>, IGameConfigPostLoad
    {
        // \todo [nuutti] Properly implement this Group instead of Variant? #ab-config
        //[MetaSerializable]
        //public class Group
        //{
        //    // \todo [nuutti] Distinguish control from variants explicitly?
        //    [MetaMember(1)] public ExperimentVariantId    Id;
        //    [MetaMember(2)] public string                 DisplayName;
        //    [MetaMember(3)] public int                    Weight;
        //}
        void IGameConfigPostLoad.PostLoad()
        {
            // If dealing with source config (as opposed to final built config), validate source data and construct derived data.
            // \todo [nuutti] Clean up by splitting to separate source and final config data types? #ab-config

            bool isSourceConfig = Variants == null;
            if (isSourceConfig)
            {
                // Validate source config data

                if (VariantId.Count != VariantAnalyticsId.Count)
                    throw new InvalidOperationException($"Experiment {ExperimentId}: {nameof(VariantId)} and {nameof(VariantAnalyticsId)} should have the same amount of entries");
                if (VariantId.Contains(null))
                    throw new InvalidOperationException($"Experiment {ExperimentId}: elements in {nameof(VariantId)} cannot be null or empty; has null or empty at index {VariantId.IndexOf(null)}");
                if (VariantAnalyticsId.Contains(null))
                    throw new InvalidOperationException($"Experiment {ExperimentId}: elements in {nameof(VariantAnalyticsId)} cannot be null or empty; has null or empty at index {VariantAnalyticsId.IndexOf(null)}");

                foreach (IGrouping<ExperimentVariantId, ExperimentVariantId> group in VariantId.GroupBy(id => id))
                {
                    if (group.Count() > 1)
                        throw new InvalidOperationException($"Experiment {ExperimentId}: variant id {group.Key} specified multiple times");
                }

                // Construct Variants based on source config data

                int numVariants = VariantId.Count;
                Variants =
                    Enumerable.Range(0, numVariants)
                    .Select(ndx => new Variant(VariantId[ndx], VariantAnalyticsId[ndx]))
                    .ToOrderedDictionary(variant => variant.Id);

                // Unset source-only members

                VariantId       = null;
                VariantAnalyticsId  = null;
            }
        }

        [MetaSerializable]
        public class Variant
        {
            [MetaMember(1)] public ExperimentVariantId  Id              { get; private set; }
            [MetaMember(2)] public string               AnalyticsId     { get; private set; }

            public FullGameConfigPatch ConfigPatch { get; set; } // \note Currently this is not serialized here, but instead gets ad-hoc populated in FullGameConfig.FromArchive .

            Variant(){ }

            public Variant(ExperimentVariantId id, string analyticsId)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                AnalyticsId = analyticsId ?? throw new ArgumentNullException(nameof(analyticsId));
            }
        }

        [MetaMember(1)] public PlayerExperimentId                               ExperimentId                { get; private set; }
        [MetaMember(2)] public string                                           DisplayName                 { get; private set; }
        [MetaMember(8)] public string                                           Description                 { get; private set; }
        [MetaMember(6)] public OrderedDictionary<ExperimentVariantId, Variant>  Variants                    { get; private set; }
        [MetaMember(9)] public string                                           ExperimentAnalyticsId       { get; private set; }
        [MetaMember(10)] public string                                          ControlVariantAnalyticsId   { get; private set; }

        // Source-config-only members. Not serialized, used to populate the Variants array (in PostLoad).
        // \todo [nuutti] Clean up by splitting to separate source and final config data types? #ab-config
        /// <summary> Source-only member. Not set in the final built config! </summary>
        public List<ExperimentVariantId>    VariantId               { get; private set; }
        /// <summary> Source-only member. Not set in the final built config! </summary>
        public List<string>                 VariantAnalyticsId      { get; private set; }

        PlayerExperimentId IGameConfigKey<PlayerExperimentId>.ConfigKey => ExperimentId;
    }
}
