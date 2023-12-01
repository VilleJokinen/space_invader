// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Metaplay.BotClient
{
    public class BotGameConfigProvider
    {
        readonly struct ConfigKey : IEquatable<ConfigKey>
        {
            public readonly ContentHash                     ConfigVersion;
            public readonly ContentHash                     PatchesVersion;
            public readonly GameConfigSpecializationKey?    SpecializationKey;

            public ConfigKey(ContentHash configVersion, ContentHash patchesVersion, GameConfigSpecializationKey? specializationKey)
            {
                ConfigVersion = configVersion;
                PatchesVersion = patchesVersion;
                SpecializationKey = specializationKey;
            }

            public bool Equals(ConfigKey other)
            {
                return ConfigVersion == other.ConfigVersion
                    && PatchesVersion == other.PatchesVersion
                    && SpecializationKey == other.SpecializationKey;
            }
            public override bool Equals(object obj) => obj is ConfigKey key && Equals(key);
            public override int GetHashCode() => Util.CombineHashCode(ConfigVersion.GetHashCode(), PatchesVersion.GetHashCode(), SpecializationKey?.GetHashCode() ?? 0);
        }

        /// <summary>
        /// Key for <see cref="GameConfigImportResources"/>.
        /// </summary>
        readonly struct ImportResourcesKey : IEquatable<ImportResourcesKey>
        {
            public readonly ContentHash ConfigVersion;
            public readonly ContentHash PatchesVersion;

            public ImportResourcesKey(ContentHash configVersion, ContentHash patchesVersion)
            {
                ConfigVersion = configVersion;
                PatchesVersion = patchesVersion;
            }

            public bool Equals(ImportResourcesKey other)
            {
                return ConfigVersion == other.ConfigVersion
                    && PatchesVersion == other.PatchesVersion;
            }
            public override bool Equals(object obj) => obj is ImportResourcesKey key && Equals(key);
            public override int GetHashCode() => Util.CombineHashCode(ConfigVersion.GetHashCode(), PatchesVersion.GetHashCode());
        }

        public static readonly BotGameConfigProvider                        Instance        = new BotGameConfigProvider();

        WeakReferencingCacheWithTimedRetention<ContentHash, ConfigArchive> _archiveCache = new(minimumRetentionTime: TimeSpan.FromSeconds(20));
        WeakReferencingCacheWithTimedRetention<ContentHash, GameConfigSpecializationPatches>    _patchesCache   = new(minimumRetentionTime: TimeSpan.FromSeconds(20));
        WeakReferencingCacheWithTimedRetention<ImportResourcesKey, GameConfigImportResources>   _importResourcesCache = new(minimumRetentionTime: TimeSpan.FromSeconds(20));
        WeakReferencingCacheWithTimedRetention<ConfigKey, ISharedGameConfig>                    _configCache    = new(minimumRetentionTime: TimeSpan.FromSeconds(20));

        string                                                              _cacheDirectory;
        MetaplayCdnAddress                                                  _cdnAddress;

        public void Initialize(string cacheDirectory, MetaplayCdnAddress cdnAddress)
        {
            _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
            _cdnAddress = cdnAddress;
        }

        /// <summary>
        /// Returns the specified config archive for the baseline (i.e. unpatched) (shared) game config.
        /// </summary>
        public ValueTask<ConfigArchive> GetBaselineConfigArchiveAsync(ContentHash configVersion)
        {
            return _archiveCache.GetCachedOrCreateNewAsync(
                key:            configVersion,
                createNewAsync: (key) => FetchConfigArchiveAsync(key, fetchUriSuffix: null));
        }

        /// <summary>
        /// Returns the specified config archive for the baseline (i.e. unpatched) (shared) game config.
        /// </summary>
        public ValueTask<ConfigArchive> GetBaselineConfigArchiveAsync(SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo fetchInfo)
        {
            return _archiveCache.GetCachedOrCreateNewAsync(
                key:            fetchInfo.SharedGameConfigVersion,
                createNewAsync: (key) => FetchConfigArchiveAsync(key, fetchUriSuffix: fetchInfo.UrlSuffix));
        }

        /// <summary>
        /// Returns the specified patchset for a shared game config.
        /// </summary>
        public ValueTask<GameConfigSpecializationPatches> GetSpecializationPatchesAsync(ContentHash patchsetVersion)
        {
            return _patchesCache.GetCachedOrCreateNewAsync(
                key:            patchsetVersion,
                createNewAsync: FetchSpecializationPatchesAsync);
        }

        /// <summary>
        /// Returns the specified baseline (i.e. unpatched) (shared) game config.
        /// Using this method, rather than manually constructing GameConfig from
        /// the archive from <see cref="GetBaselineConfigArchiveAsync"/>,
        /// enables caching and efficient sharing of gameconfig instances.
        /// </summary>
        public ValueTask<ISharedGameConfig> GetBaselineConfigAsync(ContentHash configVersion)
        {
            return _configCache.GetCachedOrCreateNewAsync(
                key:            new ConfigKey(configVersion, patchesVersion: ContentHash.None, specializationKey: null),
                createNewAsync: key => CreateConfigAsync(patches: default, key));
        }

        /// <summary>
        /// Returns the specified shared game config specialized with the specified patches.
        /// Using this method, rather than manually constructing GameConfig from
        /// the archive from <see cref="GetBaselineConfigArchiveAsync"/>
        /// and <paramref name="specializationPatches"/>,
        /// enables caching and efficient sharing of gameconfig instances.
        /// If <paramref name="specializationPatches"/> is null, no specialization is performed.
        /// </summary>
        public ISharedGameConfig GetSpecializedGameConfig(ConfigArchive baselineArchive, GameConfigSpecializationPatches specializationPatches, GameConfigSpecializationKey specializationKey)
        {
            if (specializationPatches == null)
            {
                if (specializationKey.VariantIds != null && specializationKey.VariantIds.Any(variant => variant != null))
                    throw new InvalidOperationException("cannot have non-all-control specialization but no patchset");

                return _configCache.GetCachedOrCreateNew(
                    key:            new ConfigKey(baselineArchive.Version, patchesVersion: ContentHash.None, specializationKey: null),
                    createNew:      _ => CreateConfig(baselineArchive, null));
            }
            else
            {
                return _configCache.GetCachedOrCreateNew(
                    key:            new ConfigKey(baselineArchive.Version, patchesVersion: specializationPatches.Version, specializationKey),
                    createNew:      _ => CreateConfig(baselineArchive, (specializationPatches, specializationKey)));
            }
        }

        /// <summary>
        /// Returns the specified shared game config specialized with the specified patches.
        /// Using this method, rather than manually constructing GameConfig from
        /// the archive from <see cref="GetBaselineConfigArchiveAsync"/>
        /// and patches from <see cref="GetSpecializationPatchesAsync"/>,
        /// enables caching and efficient sharing of gameconfig instances.
        /// If <paramref name="patchsetVersion"/> is <c>None</c>, no specialization is performed.
        /// </summary>
        public ValueTask<ISharedGameConfig> GetSpecializedGameConfigAsync(ContentHash baselineConfigVersion, ContentHash patchsetVersion, GameConfigSpecializationKey specializationKey)
        {
            if (patchsetVersion == ContentHash.None)
            {
                if (specializationKey.VariantIds != null && specializationKey.VariantIds.Any(variant => variant != null))
                    throw new InvalidOperationException("if patchset is not given, the specialization must only contain control variants");
                return GetBaselineConfigAsync(baselineConfigVersion);
            }

            return _configCache.GetCachedOrCreateNewAsync(
                key:            new ConfigKey(baselineConfigVersion, patchsetVersion, specializationKey),
                createNewAsync: async (key) =>
                {
                    var baselineArchive = await GetBaselineConfigArchiveAsync(key.ConfigVersion);
                    var specializationPatches = await GetSpecializationPatchesAsync(key.PatchesVersion);
                    return CreateConfig(baselineArchive, (specializationPatches, key.SpecializationKey.Value));
                });
        }

        /// <inheritdoc cref="GetSpecializedGameConfigAsync(ContentHash, ContentHash, GameConfigSpecializationKey)"/>
        public async ValueTask<ISharedGameConfig> GetSpecializedGameConfigAsync(ContentHash baselineConfigVersion, ContentHash patchsetVersion, OrderedDictionary<PlayerExperimentId, ExperimentVariantId> experimentAssignment)
        {
            if (patchsetVersion == ContentHash.None)
            {
                if (experimentAssignment != null && experimentAssignment.Values.Any(variant => variant != null))
                    throw new InvalidOperationException("if patchset is not given, the specialization must only contain control variants");
                return await GetBaselineConfigAsync(baselineConfigVersion);
            }

            GameConfigSpecializationPatches specializationPatches = await GetSpecializationPatchesAsync(patchsetVersion);
            GameConfigSpecializationKey specializationKey = specializationPatches.CreateKeyFromAssignment(experimentAssignment);

            return await _configCache.GetCachedOrCreateNewAsync(
                key:            new ConfigKey(baselineConfigVersion, patchsetVersion, specializationKey),
                createNewAsync: async (key) =>
                {
                    var baselineArchive = await GetBaselineConfigArchiveAsync(key.ConfigVersion);
                    return CreateConfig(baselineArchive, (specializationPatches, key.SpecializationKey.Value));
                });
        }

        async Task<ISharedGameConfig> CreateConfigAsync(GameConfigSpecializationPatches patches, ConfigKey key)
        {
            ConfigArchive archive = await GetBaselineConfigArchiveAsync(key.ConfigVersion);
            return CreateConfig(archive, key.SpecializationKey.HasValue ? (patches, key.SpecializationKey.Value) : null);
        }

        ISharedGameConfig CreateConfig(ConfigArchive archive, (GameConfigSpecializationPatches SpecializationPatches, GameConfigSpecializationKey)? specialization)
        {
            GameConfigImportResources importResources = GetImportResources(archive, specialization?.SpecializationPatches);

            OrderedSet<ExperimentVariantPair> patchIds = new OrderedSet<ExperimentVariantPair>();
            if (specialization.HasValue)
            {
                (GameConfigSpecializationPatches specializationPatches, GameConfigSpecializationKey specializationKey) = specialization.Value;

                IEnumerable<PlayerExperimentId> experimentIds = specializationPatches.Patches.Keys;
                ExperimentVariantId[] variantIds = specializationKey.VariantIds;

                if (experimentIds.Count() != variantIds.Length)
                    throw new MetaAssertException("Mismatching count of specialization patches experiments ({0}) vs specialization key VariantIds ({1})", experimentIds.Count(), variantIds.Length);

                foreach ((PlayerExperimentId experimentId, ExperimentVariantId variantId) in experimentIds.Zip(variantIds))
                {
                    if (variantId != null)
                        patchIds.Add(new ExperimentVariantPair(experimentId, variantId));
                }
            }

            return (ISharedGameConfig)GameConfigFactory.Instance.ImportGameConfig(GameConfigImportParams.Specialization(importResources, patchIds));
        }

        GameConfigImportResources GetImportResources(ConfigArchive archive, GameConfigSpecializationPatches specializationPatchesMaybe)
        {
            return _importResourcesCache.GetCachedOrCreateNew(
                key: new ImportResourcesKey(archive.Version, specializationPatchesMaybe?.Version ?? ContentHash.None),
                createNew: _ => CreateImportResources(archive, specializationPatchesMaybe));
        }

        GameConfigImportResources CreateImportResources(ConfigArchive archive, GameConfigSpecializationPatches specializationPatchesMaybe)
        {
            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> patches = new OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope>();
            if (specializationPatchesMaybe != null)
            {
                foreach ((PlayerExperimentId experimentId, OrderedDictionary<ExperimentVariantId, byte[]> variants) in specializationPatchesMaybe.Patches)
                {
                    foreach ((ExperimentVariantId variantId, byte[] serializedEnvelope) in variants)
                    {
                        ExperimentVariantPair patchId = new ExperimentVariantPair(experimentId, variantId);

                        GameConfigPatchEnvelope patchEnvelope;
                        using (IOReader reader = new IOReader(serializedEnvelope))
                            patchEnvelope = GameConfigPatchEnvelope.Deserialize(reader);

                        patches.Add(patchId, patchEnvelope);
                    }
                }
            }

            BotOptions botOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BotOptions>();
            GameConfigRuntimeStorageMode configRuntimeStorageMode = botOpts.EnableGameConfigInMemoryDeduplication
                                                                    ? GameConfigRuntimeStorageMode.Deduplicating
                                                                    : GameConfigRuntimeStorageMode.Solo;

            return GameConfigImportResources.Create(
                GameConfigRepository.Instance.SharedGameConfigType,
                archive,
                patches,
                configRuntimeStorageMode);
        }

        async Task<ConfigArchive> FetchConfigArchiveAsync(ContentHash key, string fetchUriSuffix)
        {
            // First look into cache if we have a copy we can use.
            string                  cacheDirectory  = _cacheDirectory;
            DiskBlobStorage         cacheStorage    = new DiskBlobStorage(cacheDirectory);
            StorageBlobProvider     cacheProvider   = new StorageBlobProvider(cacheStorage);

            byte[] cacheBytes = await cacheProvider.GetAsync("SharedGameConfig", key);
            if (cacheBytes != null)
            {
                try
                {
                    return ConfigArchive.FromBytes(cacheBytes);
                }
                catch
                {
                    // Could not parse GameConfig.
                    await cacheStorage.DeleteAsync(cacheProvider.GetStorageFileName("SharedGameConfig", key));
                }
            }

            // Setup http-based GameConfigProvider with on-disk caching
            HttpBlobProvider        httpProvider    = new HttpBlobProvider(MetaHttpClient.DefaultInstance, _cdnAddress, uriSuffix: fetchUriSuffix);
            CachingBlobProvider     cachingProvider = new CachingBlobProvider(httpProvider, cacheProvider);

            byte[] bytes = await cachingProvider.GetAsync("SharedGameConfig", key);
            if (bytes == null)
                throw new InvalidOperationException($"invalid archive key {key}");
            return ConfigArchive.FromBytes(bytes);
        }

        async Task<GameConfigSpecializationPatches> FetchSpecializationPatchesAsync(ContentHash key)
        {
            string                  cacheDirectory  = _cacheDirectory;
            DiskBlobStorage         cacheStorage    = new DiskBlobStorage(cacheDirectory);
            StorageBlobProvider     cacheProvider   = new StorageBlobProvider(cacheStorage);
            HttpBlobProvider        httpProvider    = new HttpBlobProvider(MetaHttpClient.DefaultInstance, _cdnAddress, uriSuffix: null);
            CachingBlobProvider     cachingProvider = new CachingBlobProvider(httpProvider, cacheProvider);

            byte[] bytes = await cachingProvider.GetAsync("SharedGameConfigPatches", key);
            if (bytes == null)
                throw new InvalidOperationException($"invalid patch key {key}");
            return GameConfigSpecializationPatches.FromBytes(bytes);
        }
    }
}
