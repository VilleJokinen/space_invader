// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Player;
using Metaplay.Server.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.GameConfig
{
    public class ServerGameConfigProvider
    {
        readonly struct FullArchiveKey : IEquatable<FullArchiveKey>
        {
            public readonly MetaGuid                        StaticConfigId;
            public readonly MetaGuid                        DynamicContentId;

            public FullArchiveKey(MetaGuid staticConfigId, MetaGuid dynamicContentId)
            {
                StaticConfigId = staticConfigId;
                DynamicContentId = dynamicContentId;
            }

            public bool Equals(FullArchiveKey other) => StaticConfigId == other.StaticConfigId && DynamicContentId == other.DynamicContentId;
            public override bool Equals(object obj) => obj is FullArchiveKey key && Equals(key);
            public override int GetHashCode() => Util.CombineHashCode(StaticConfigId.GetHashCode(), DynamicContentId.GetHashCode());
        }

        readonly struct FullConfigKey : IEquatable<FullConfigKey>
        {
            public readonly MetaGuid                        StaticConfigId;
            public readonly MetaGuid                        DynamicContentId;
            public readonly GameConfigSpecializationKey?    SpecializationKey;

            public FullConfigKey(MetaGuid staticConfigId, MetaGuid dynamicContentId, GameConfigSpecializationKey? specializationKey)
            {
                StaticConfigId = staticConfigId;
                DynamicContentId = dynamicContentId;
                SpecializationKey = specializationKey;
            }

            public bool Equals(FullConfigKey other) => StaticConfigId == other.StaticConfigId && DynamicContentId == other.DynamicContentId && SpecializationKey == other.SpecializationKey;
            public override bool Equals(object obj) => obj is FullConfigKey key && Equals(key);
            public override int GetHashCode() => Util.CombineHashCode(StaticConfigId.GetHashCode(), DynamicContentId.GetHashCode(), SpecializationKey?.GetHashCode() ?? 0);
        }

        public static readonly ServerGameConfigProvider     Instance        = new ServerGameConfigProvider();

        WeakReferencingCacheWithTimedRetention<FullArchiveKey, FullGameConfigImportResources> _importResourcesCache = new(minimumRetentionTime: TimeSpan.FromSeconds(20));
        WeakReferencingCacheWithTimedRetention<FullConfigKey, FullGameConfig> _configCache  = new(minimumRetentionTime: TimeSpan.FromSeconds(20));

        /// <summary>
        /// Returns the <see cref="FullGameConfigImportResources"/> constructed from a full game config archive.
        /// Full game configs are identified by the pair of static and dynamic source configs.
        /// </summary>
        public ValueTask<FullGameConfigImportResources> GetImportResourcesAsync(MetaGuid staticConfigId, MetaGuid dynamicContentId)
        {
            return _importResourcesCache.GetCachedOrCreateNewAsync(
                key:            new FullArchiveKey(staticConfigId, dynamicContentId),
                createNewAsync: CreateImportResourcesAsync);
        }

        /// <summary>
        /// Returns the specified baseline (i.e. unpatched) (full) game config.
        /// Using this method, rather than manually constructing GameConfig from
        /// the resources from <see cref="GetImportResourcesAsync"/>,
        /// enables caching and efficient sharing of gameconfig instances.
        /// </summary>
        public ValueTask<FullGameConfig> GetBaselineGameConfigAsync(MetaGuid staticConfigId, MetaGuid dynamicContentId)
        {
            return _configCache.GetCachedOrCreateNewAsync(
                key:            new FullConfigKey(staticConfigId, dynamicContentId, specializationKey: null),
                createNewAsync: CreateFullConfigAsync);
        }

        /// <summary>
        /// Returns the specified baseline (i.e. unpatched) (full) game config.
        /// Using this method, rather than manually constructing GameConfig from
        /// <paramref name="importResources"/>,
        /// enables caching and efficient sharing of gameconfig instances.
        /// </summary>
        /// <remarks>
        /// Use this method instead of <see cref="GetBaselineGameConfigAsync(MetaGuid, MetaGuid)"/>
        /// when you already have the <see cref="FullGameConfigImportResources"/> available
        /// and want to avoid the potentially costly (and async) operation of acquiring it.
        /// </remarks>
        public FullGameConfig GetBaselineGameConfig(MetaGuid staticConfigId, MetaGuid dynamicContentId, FullGameConfigImportResources importResources)
        {
            return _configCache.GetCachedOrCreateNew(
                key:            new FullConfigKey(staticConfigId, dynamicContentId, specializationKey: null),
                createNew:      _/*key*/ => CreateFullConfig(importResources, specializationKey: null));
        }

        /// <summary>
        /// Returns the specified full game config specialized with the specified patches.
        /// Using this method, rather than manually constructing GameConfig from
        /// the resources from <see cref="GetImportResourcesAsync"/> with the specialization,
        /// enables caching and efficient sharing of gameconfig instances.
        /// If <paramref name="specializationKey"/> is <c>null</c>, it is the same as
        /// a having all-control variants; a config with baseline contents is returned.
        /// </summary>
        public ValueTask<FullGameConfig> GetSpecializedGameConfigAsync(MetaGuid staticConfigId, MetaGuid dynamicContentId, GameConfigSpecializationKey? specializationKey)
        {
            return _configCache.GetCachedOrCreateNewAsync(
                key:            new FullConfigKey(staticConfigId, dynamicContentId, specializationKey),
                createNewAsync: CreateFullConfigAsync);
        }

        /// <summary>
        /// Returns the specified full game config specialized with the specified patches.
        /// Using this method, rather than manually constructing GameConfig from
        /// <paramref name="importResources"/> with the specialization,
        /// enables caching and efficient sharing of gameconfig instances.
        /// If <paramref name="specializationKey"/> is <c>null</c>, it is the same as
        /// a having all-control variants; a config with baseline contents is returned.
        /// </summary>
        /// <remarks>
        /// Use this method instead of <see cref="GetSpecializedGameConfigAsync(MetaGuid, MetaGuid, GameConfigSpecializationKey?)"/>
        /// when you already have the <see cref="FullGameConfigImportResources"/> available
        /// and want to avoid the potentially costly (and async) operation of acquiring it.
        /// </remarks>
        public FullGameConfig GetSpecializedGameConfig(MetaGuid staticConfigId, MetaGuid dynamicContentId, FullGameConfigImportResources importResources, GameConfigSpecializationKey? specializationKey)
        {
            return _configCache.GetCachedOrCreateNew(
                key:            new FullConfigKey(staticConfigId, dynamicContentId, specializationKey),
                createNew:      _/*key*/ => CreateFullConfig(importResources, specializationKey));
        }

        async Task<FullGameConfig> CreateFullConfigAsync(FullConfigKey key)
        {
            FullGameConfigImportResources importResources = await GetImportResourcesAsync(key.StaticConfigId, key.DynamicContentId);
            return CreateFullConfig(importResources, key.SpecializationKey);
        }

        FullGameConfig CreateFullConfig(FullGameConfigImportResources importResources, GameConfigSpecializationKey? specializationKey)
        {
            OrderedSet<ExperimentVariantPair> patchIds = new OrderedSet<ExperimentVariantPair>();

            if (specializationKey.HasValue)
            {
                List<PlayerExperimentId> experimentIds = importResources.ExperimentIds;
                ExperimentVariantId[] variantIds = specializationKey.Value.VariantIds;

                if (experimentIds.Count != variantIds.Length)
                    throw new MetaAssertException("Mismatching count of import resources ExperimentIds ({0}) vs specialization key VariantIds ({1})", experimentIds.Count, variantIds.Length);

                foreach ((PlayerExperimentId experimentId, ExperimentVariantId variantId) in experimentIds.Zip(variantIds))
                {
                    if (variantId != null)
                        patchIds.Add(new ExperimentVariantPair(experimentId, variantId));
                }
            }

            return FullGameConfig.CreateSpecialization(importResources, patchIds, omitPatchesInServerConfigExperiments: true);
        }

        async Task<FullGameConfigImportResources> CreateImportResourcesAsync(FullArchiveKey key)
        {
            ConfigArchive archive = await FetchFullConfigArchiveAsync(key);

            SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
            GameConfigRuntimeStorageMode configRuntimeStorageMode = systemOpts.EnableGameConfigInMemoryDeduplication
                                                                    ? GameConfigRuntimeStorageMode.Deduplicating
                                                                    : GameConfigRuntimeStorageMode.Solo;

            return FullGameConfigImportResources.CreateWithAllConfiguredPatches(archive, configRuntimeStorageMode);
        }

        async Task<ConfigArchive> FetchFullConfigArchiveAsync(FullArchiveKey key)
        {
            if (!key.StaticConfigId.IsValid)
                throw new ArgumentException($"Trying to fetch invalid StaticConfigId from database");

            // Fetch the active StaticGameConfig from database
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Normal);
            PersistedStaticGameConfig archive = await db.TryGetAsync<PersistedStaticGameConfig>(key.StaticConfigId.ToString());
            if (archive == null)
                throw new InvalidOperationException($"{nameof(PersistedStaticGameConfig)} version {key.StaticConfigId} not found in database!");

            // Extract the archive as a ReadOnlyArchive
            ConfigArchive fullArchive = ConfigArchive.FromBytes(archive.ArchiveBytes);

            // \todo [petri] combine with Dynamic data in the future in here

            return fullArchive;
        }
    }
}
