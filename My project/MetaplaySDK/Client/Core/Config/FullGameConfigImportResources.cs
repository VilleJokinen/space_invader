// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Resources used for constructing a <see cref="FullGameConfig"/>,
    /// either baseline or specialized.
    /// Corresponds to a single version of full game config.
    /// Use <see cref="FullGameConfig.CreateSpecialization"/> to create the config.
    /// <para>
    /// On the server, typically a single instance of this class is held per game config version
    /// and then multiple specializations are constructed using it.
    /// </para>
    /// </summary>
    public class FullGameConfigImportResources
    {
        /// <summary>
        /// The full config archive.
        /// </summary>
        public readonly ConfigArchive FullArchive;
        /// <summary>
        /// The ids of the experiments whose variants are supported for patching
        /// when using these resources.
        /// </summary>
        public readonly List<PlayerExperimentId> ExperimentIds;
        /// <summary>
        /// The metadata loaded from <see cref="FullArchive"/>.
        /// </summary>
        public readonly GameConfigMetaData MetaData;
        /// <summary>
        /// The resources for importing the <see cref="ISharedGameConfig"/>-implementing class.
        /// </summary>
        public readonly GameConfigImportResources Shared;
        /// <summary>
        /// The resources for importing the <see cref="IServerGameConfig"/>-implementing class.
        /// </summary>
        public readonly GameConfigImportResources Server;

        FullGameConfigImportResources(ConfigArchive fullArchive, List<PlayerExperimentId> experimentIds, GameConfigMetaData metaData, GameConfigImportResources shared, GameConfigImportResources server)
        {
            FullArchive = fullArchive;
            ExperimentIds = experimentIds;
            MetaData = metaData;
            Shared = shared;
            Server = server;
        }

        /// <summary>
        /// Create resources from which can be constructed any specialization
        /// (possibly baseline) that is based on the baseline and uses a subset
        /// of the patches contained in the given archive.
        /// </summary>
        /// <param name="initializeBaselineAndSingleVariantSpecializations">
        /// See same parameter on <see cref="GameConfigImportResources.Create"/>.
        /// </param>
        public static FullGameConfigImportResources CreateWithAllConfiguredPatches(
            ConfigArchive fullArchive,
            GameConfigRuntimeStorageMode storageMode,
            bool initializeBaselineAndSingleVariantSpecializations = true,
            CancellationToken ct = default)
        {
            ConfigArchive sharedArchive = GameConfigUtil.GetSharedArchiveFromFullArchive(fullArchive);
            ConfigArchive serverArchive = GameConfigUtil.GetServerArchiveFromFullArchive(fullArchive);

            // \note Experiments are loaded in an ad-hoc manner directly from the serverArchive.
            //       That's the ground truth for what experiments and variants exist.
            //       This way we avoid importing the entire server config at this point.
            OrderedDictionary<PlayerExperimentId, PlayerExperimentInfo> experiments;
            if (serverArchive.ContainsEntryWithName($"{ServerGameConfigBase.PlayerExperimentsEntryName}.mpc"))
                experiments = GameConfigUtil.ImportBinaryLibraryItems<PlayerExperimentId, PlayerExperimentInfo>(serverArchive, $"{ServerGameConfigBase.PlayerExperimentsEntryName}.mpc");
            else
                experiments = new OrderedDictionary<PlayerExperimentId, PlayerExperimentInfo>();

            // Extract the patches (in envelope, i.e. serialized, form) from the archive.

            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> sharedConfigPatches = new OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope>();
            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> serverConfigPatches = new OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope>();

            foreach (PlayerExperimentInfo experiment in experiments.Values)
            {
                foreach (ExperimentVariantId variantId in experiment.Variants.Keys)
                {
                    ExperimentVariantPair configPatchId = new ExperimentVariantPair(experiment.ExperimentId, variantId);

                    sharedConfigPatches.Add(configPatchId, GameConfigUtil.GetSharedPatchEnvelopeFromFullArchive(fullArchive, experiment.ExperimentId, variantId));
                    serverConfigPatches.Add(configPatchId, GameConfigUtil.GetServerPatchEnvelopeFromFullArchive(fullArchive, experiment.ExperimentId, variantId));

                    ct.ThrowIfCancellationRequested();
                }
            }

            // Assert: each patch is expected to be represented, even empty patches.
            // (We want this because the existence of patches in GameConfigImportResources is used to assert that
            //  we don't attempt to specialize using a patch that isn't represented in the GameConfigImportResources.
            //  See the check in the constructor of GameConfigImportParams.)
            IEnumerable<ExperimentVariantPair> allPatchIds =
                experiments.Values
                .SelectMany(e => e.Variants.Keys.Select(variantId => new ExperimentVariantPair(e.ExperimentId, variantId)));
            if (!sharedConfigPatches.Keys.SequenceEqual(allPatchIds))
                throw new MetaAssertException($"Expected {nameof(sharedConfigPatches)} to contain an entry for each patch in {nameof(allPatchIds)}");
            if (!serverConfigPatches.Keys.SequenceEqual(allPatchIds))
                throw new MetaAssertException($"Expected {nameof(serverConfigPatches)} to contain an entry for each patch in {nameof(allPatchIds)}");

            FullGameConfigImportResources importResources = new FullGameConfigImportResources(
                fullArchive,
                experiments.Keys.ToList(),
                GameConfigMetaData.FromArchive(fullArchive),
                GameConfigImportResources.Create(
                    GameConfigRepository.Instance.SharedGameConfigType,
                    sharedArchive,
                    sharedConfigPatches,
                    storageMode,
                    initializeBaselineAndSingleVariantSpecializations: initializeBaselineAndSingleVariantSpecializations,
                    ct: ct),
                GameConfigImportResources.Create(
                    GameConfigRepository.Instance.ServerGameConfigType,
                    serverArchive,
                    serverConfigPatches,
                    storageMode,
                    initializeBaselineAndSingleVariantSpecializations: initializeBaselineAndSingleVariantSpecializations,
                    ct: ct));

            // Populate serverConfig.PlayerExperiments[].Variants[].ConfigPatch, but only ever on the baseline config instance.
            // The experiment library is deduplicated among the specializations anyway, so this will set it in all of them.
            // \todo This is a bit magical anyway. Remove PlayerExperimentInfo.Variant.ConfigPatch, and load the patches
            //       explicitly from the archive/import-resources when needed (it's only really needed to send it to dashboard?)?
            if (storageMode == GameConfigRuntimeStorageMode.Deduplicating && initializeBaselineAndSingleVariantSpecializations)
                FullGameConfig.PopulatePatchesInServerConfigExperiments(fullArchive, (ISharedGameConfig)importResources.Shared.DeduplicationBaseline, (IServerGameConfig)importResources.Server.DeduplicationBaseline, ct: ct);

            return importResources;
        }

        /// <summary>
        /// Create resources from which can be constructed just the baseline config.
        /// No patches will be loaded from the archive, and the <see cref="GameConfigImportResources.Patches">
        /// in <see cref="Shared"/> and <see cref="Server"/> will be empty.
        /// </summary>
        public static FullGameConfigImportResources CreateWithoutPatches(ConfigArchive fullArchive, GameConfigRuntimeStorageMode storageMode)
        {
            ConfigArchive sharedArchive = GameConfigUtil.GetSharedArchiveFromFullArchive(fullArchive);
            ConfigArchive serverArchive = GameConfigUtil.GetServerArchiveFromFullArchive(fullArchive);

            FullGameConfigImportResources importResources = new FullGameConfigImportResources(
                fullArchive,
                new List<PlayerExperimentId>(),
                GameConfigMetaData.FromArchive(fullArchive),
                GameConfigImportResources.CreateWithoutPatches(GameConfigRepository.Instance.SharedGameConfigType, sharedArchive, storageMode),
                GameConfigImportResources.CreateWithoutPatches(GameConfigRepository.Instance.ServerGameConfigType, serverArchive, storageMode));

            // Populate serverConfig.PlayerExperiments[].Variants[].ConfigPatch, but only ever on the baseline config instance.
            // The experiment library is deduplicated among the specializations anyway, so this will set it in all of them.
            // \todo This is a bit magical anyway. Remove PlayerExperimentInfo.Variant.ConfigPatch, and load the patches
            //       explicitly from the archive/import-resources when needed (it's only really needed to send it to dashboard?)?
            if (storageMode == GameConfigRuntimeStorageMode.Deduplicating)
                FullGameConfig.PopulatePatchesInServerConfigExperiments(fullArchive, (ISharedGameConfig)importResources.Shared.DeduplicationBaseline, (IServerGameConfig)importResources.Server.DeduplicationBaseline);

            return importResources;
        }
    }
}
