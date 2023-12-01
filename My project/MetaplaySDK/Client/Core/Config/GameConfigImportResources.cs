// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Player;
using System;
using System.Threading;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Resources used for constructing game config instances (<see cref="IGameConfig"/>),
    /// either baseline or specialized.
    /// Corresponds to a single version of game config.
    /// <para>
    /// See also <see cref="GameConfigImportParams"/>, which specifies a single specialization (or baseline).
    /// </para>
    /// <para>
    /// Implementation note (applies when <see cref="ConfigRuntimeStorageMode"/> is <see cref="GameConfigRuntimeStorageMode.Deduplicating"/>):
    /// When <see cref="Create"/> is used to create an instance of this,
    /// it will internally construct some "special" instances of the game config:
    /// the baseline instance and one instance per patch. During their construction,
    /// these config instances will populate and mutate <see cref="LibraryDeduplicationStorages"/>.
    /// After these config instances have been created, and <see cref="Create"/> returns,
    /// <see cref="LibraryDeduplicationStorages"/> is not supposed to be modified anymore,
    /// and then specialized config instances can be created using the created resources.
    /// These "special" baseline and single-patch config instances are be assigned
    /// into <see cref="Baseline"/> and <see cref="SingleVariantSpecializations"/>
    /// for the purpose of being available in user-defined overrides of the <see cref="GameConfigBase.PopulateConfigEntries"/>
    /// method, where they can be used for certain kinds of deduplication of custom-typed config entries.
    /// </para>
    /// </summary>
    public class GameConfigImportResources
    {
        public readonly Type GameConfigType;

        public readonly ConfigArchive BaselineArchive;
        public readonly OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> Patches;

        public readonly GameConfigRuntimeStorageMode ConfigRuntimeStorageMode;

        #region For when ConfigRuntimeStorageMode is Deduplicating

        /// <summary>
        /// Holds the deduplication storages which are shared between the specializations of the libraries.
        /// This is populated during config importing. The key should identify the specific library, but
        /// otherwise can be arbitrary.
        /// </summary>
        public readonly OrderedDictionary<string, IGameConfigLibraryDeduplicationStorage> LibraryDeduplicationStorages;

        /// <inheritdoc cref="GameConfigTopLevelDeduplicationStorage"/>
        public readonly GameConfigTopLevelDeduplicationStorage TopLevelDeduplicationStorage;

        /// <summary>
        /// The baseline config created during <see cref="Create"/> when using <see cref="GameConfigRuntimeStorageMode.Deduplicating"/>.
        /// See comment on <see cref="GameConfigImportResources"/>.
        /// </summary>
        public IGameConfig DeduplicationBaseline;
        /// <summary>
        /// The single-patch configs created during <see cref="Create"/> when using <see cref="GameConfigRuntimeStorageMode.Deduplicating"/>.
        /// See comment on <see cref="GameConfigImportResources"/>.
        /// </summary>
        public OrderedDictionary<ExperimentVariantPair, IGameConfig> DeduplicationSingleVariantSpecializations;

        #endregion

        GameConfigImportResources(
            Type gameConfigType,
            ConfigArchive baselineArchive,
            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> patches,
            GameConfigRuntimeStorageMode configRuntimeStorageMode,
            OrderedDictionary<string, IGameConfigLibraryDeduplicationStorage> libraryDeduplicationStorages,
            GameConfigTopLevelDeduplicationStorage topLevelDeduplicationStorage,
            IGameConfig deduplicationBaseline,
            OrderedDictionary<ExperimentVariantPair, IGameConfig> deduplicationSingleVariantSpecializations)
        {
            GameConfigType = gameConfigType;
            BaselineArchive = baselineArchive;
            Patches = patches;
            ConfigRuntimeStorageMode = configRuntimeStorageMode;
            LibraryDeduplicationStorages = libraryDeduplicationStorages;
            TopLevelDeduplicationStorage = topLevelDeduplicationStorage;
            DeduplicationBaseline = deduplicationBaseline;
            DeduplicationSingleVariantSpecializations = deduplicationSingleVariantSpecializations;
        }

        /// <summary>
        /// Create resources from which can be constructed any specialization
        /// (possibly baseline) that is based on the baseline and uses a subset
        /// of the patches.
        /// </summary>
        /// <param name="gameConfigType">
        /// The type of the <see cref="IGameConfig"/>-implementing game config class
        /// which this will be used to construct.
        /// </param>
        /// <param name="initializeBaselineAndSingleVariantSpecializations">
        /// Should only be <c>false</c> for testing purposes, leave to default <c>true</c> otherwise.
        /// Setting this to <c>false</c> will omit the construction of <see cref="DeduplicationBaseline"/>
        /// and <see cref="DeduplicationSingleVariantSpecializations"/>. The use case for this omission
        /// is in ConfigPatchingTests, which calls <see cref="InitializeDeduplicationBaseline"/> and
        /// <see cref="InitializeDeduplicationSingleVariantSpecialization"/> manually, inbetween taking copies
        /// of the baseline and single-patch config contents, for the purpose of asserting
        /// that the deduplicated content was not buggily modified in an inappropriate way.
        /// </param>
        public static GameConfigImportResources Create(
            Type gameConfigType,
            ConfigArchive baselineArchive,
            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> patches,
            GameConfigRuntimeStorageMode storageMode,
            bool initializeBaselineAndSingleVariantSpecializations = true,
            CancellationToken ct = default)
        {
            GameConfigImportResources importResources;
            switch (storageMode)
            {
                case GameConfigRuntimeStorageMode.Deduplicating:
                    importResources = new GameConfigImportResources(
                        gameConfigType,
                        baselineArchive,
                        patches,
                        storageMode,
                        libraryDeduplicationStorages: new OrderedDictionary<string, IGameConfigLibraryDeduplicationStorage>(),
                        topLevelDeduplicationStorage: new GameConfigTopLevelDeduplicationStorage(),
                        deduplicationBaseline: null,
                        deduplicationSingleVariantSpecializations: new OrderedDictionary<ExperimentVariantPair, IGameConfig>());
                    break;

                case GameConfigRuntimeStorageMode.Solo:
                    importResources = new GameConfigImportResources(
                        gameConfigType,
                        baselineArchive,
                        patches,
                        storageMode,
                        libraryDeduplicationStorages: null,
                        topLevelDeduplicationStorage: null,
                        deduplicationBaseline: null,
                        deduplicationSingleVariantSpecializations: null);
                    break;

                default:
                    throw new MetaAssertException("unreachable");
            }

            if (storageMode == GameConfigRuntimeStorageMode.Deduplicating)
            {
                if (initializeBaselineAndSingleVariantSpecializations)
                {
                    importResources.InitializeDeduplicationBaseline();
                    ct.ThrowIfCancellationRequested();
                    importResources.InitializeAllDeduplicationSingleVariantSpecializations(ct: ct);
                }
            }

            return importResources;
        }

        /// <summary>
        /// Create resources from which can be constructed just the baseline config.
        /// <see cref="Patches"/> will be empty.
        /// </summary>
        /// <param name="gameConfigType">
        /// The type of the <see cref="IGameConfig"/>-implementing game config class
        /// which this will be used to construct.
        /// </param>
        public static GameConfigImportResources CreateWithoutPatches(
            Type gameConfigType,
            ConfigArchive archive,
            GameConfigRuntimeStorageMode storageMode)
        {
            return Create(
                gameConfigType,
                archive,
                patches: new OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope>(),
                storageMode);
        }

        /// <summary>
        /// Used internally, otherwise should only be needed in tests or debug checking.
        /// See comment on parameter <c>initializeBaselineAndSingleVariantSpecializations</c> of <see cref="Create"/>.
        /// </summary>
        public void InitializeDeduplicationBaseline()
        {
            if (ConfigRuntimeStorageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new InvalidOperationException($"{nameof(InitializeDeduplicationBaseline)} shouldn't be called with {nameof(GameConfigRuntimeStorageMode)}.{ConfigRuntimeStorageMode}");

            if (DeduplicationBaseline != null)
                throw new InvalidOperationException($"{nameof(InitializeDeduplicationBaseline)} done more than once");

            IGameConfig baseline = GameConfigFactory.Instance.CreateGameConfig(GameConfigType);
            baseline.Import(new GameConfigImportParams(
                resources: this,
                GameConfigDeduplicationOwnership.Baseline,
                new OrderedSet<ExperimentVariantPair>(),
                // \note isBuildingConfigs does not really matter during the construction of the import resources.
                //       It only affects certain config validation, which is going to be run anyway for the config
                //       that ultimately gets created from these import resources.
                isBuildingConfigs: false,
                isConfigBuildParent: false));

            DeduplicationBaseline = baseline;
        }

        /// <summary>
        /// Used internally, otherwise should only be needed in tests or debug checking.
        /// See comment on parameter <c>initializeBaselineAndSingleVariantSpecializations</c> of <see cref="Create"/>.
        /// </summary>
        public void InitializeDeduplicationSingleVariantSpecialization(ExperimentVariantPair patchId)
        {
            if (ConfigRuntimeStorageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new InvalidOperationException($"{nameof(InitializeDeduplicationSingleVariantSpecialization)} shouldn't be called with {nameof(GameConfigRuntimeStorageMode)}.{ConfigRuntimeStorageMode}");

            if (DeduplicationSingleVariantSpecializations.ContainsKey(patchId))
                throw new InvalidOperationException($"{nameof(InitializeDeduplicationSingleVariantSpecialization)} for {patchId} done more than once");

            IGameConfig specialization = GameConfigFactory.Instance.CreateGameConfig(GameConfigType);
            specialization.Import(new GameConfigImportParams(
                resources: this,
                GameConfigDeduplicationOwnership.SinglePatch,
                new OrderedSet<ExperimentVariantPair>{ patchId },
                // \note isBuildingConfigs does not really matter during the construction of the import resources.
                //       It only affects certain config validation, which is going to be run anyway for the config
                //       that ultimately gets created from these import resources.
                isBuildingConfigs: false,
                isConfigBuildParent: false));

            DeduplicationSingleVariantSpecializations.Add(patchId, specialization);
        }

        public void InitializeAllDeduplicationSingleVariantSpecializations(CancellationToken ct = default)
        {
            if (ConfigRuntimeStorageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new InvalidOperationException($"{nameof(InitializeAllDeduplicationSingleVariantSpecializations)} shouldn't be called with {nameof(GameConfigRuntimeStorageMode)}.{ConfigRuntimeStorageMode}");

            foreach (ExperimentVariantPair patchId in Patches.Keys)
            {
                InitializeDeduplicationSingleVariantSpecialization(patchId);
                ct.ThrowIfCancellationRequested();
            }
        }
    }
}
