// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Forms;
using Metaplay.Core.IO;
using Metaplay.Core.Json;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    // Declare an entry in a GameConfig
    [AttributeUsage(AttributeTargets.Property)]
    public class GameConfigEntryAttribute : Attribute
    {
        public readonly string EntryName;
        public readonly bool   MpcFormat;
        public readonly bool   RequireArchiveEntry;
        public readonly bool   ResolveContainedMetaRefs;
        // Specifies the name of the `GameConfigBuildParameters` member to be used for fetching the source data
        // of the game config build of this entry.
        public readonly string ConfigBuildSource;

        public GameConfigEntryAttribute(string entryName, bool mpcFormat = true, bool requireArchiveEntry = true, bool resolveContainedMetaRefs = true, string configBuildSource = null)
        {
            EntryName                = entryName ?? throw new ArgumentNullException(nameof(entryName));
            MpcFormat                = mpcFormat;
            RequireArchiveEntry      = requireArchiveEntry;
            ResolveContainedMetaRefs = resolveContainedMetaRefs;
            ConfigBuildSource        = configBuildSource;
        }
    }

    // Declare a GameConfig build transformation source item type. The source item type must implement interface
    // IGameConfigSourceItem<TGameConfigData> where TGameConfigData is the type of the GameConfigData that this
    // attribute is assigned to.
    [AttributeUsage(AttributeTargets.Property)]
    public class GameConfigEntryTransformAttribute : Attribute
    {
        public readonly Type SourceItemType;

        public GameConfigEntryTransformAttribute(Type sourceItemType)
        {
            SourceItemType = sourceItemType ?? throw new ArgumentNullException(nameof(sourceItemType));
        }
    }

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
    /// <summary>
    /// Use legacy (pre-R25) config parsing pipeline for the library this attribute is on.
    /// This is a temporary fallback solution in case the new pipeline (with <see cref="GameConfigSyntaxAdapterAttribute"/>)
    /// doesn't work from the get-go.
    /// See also <see cref="MetaplayFeatureFlags.UseLegacyConfigParsingPipelineByDefault"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class GameConfigUseLegacyParsingPipelineAttribute : Attribute
    {
    }
#endif

    /// <summary>
    /// Interface for a config data resolver.
    ///
    /// <para>
    /// A config data resolver maps <see cref="IGameConfigData{TKey}.ConfigKey"/> into a concrete <see cref="IGameConfigData{TKey}"/> instance.
    /// </para>
    /// </summary>
    public interface IGameConfigDataResolver
    {
        /// <summary>
        /// Searches for a <see cref="IGameConfigData{TKey}"/> instance whose concrete type is
        /// <paramref name="type"/> and <see cref="IGameConfigData{TKey}.ConfigKey"/> is
        /// equal to <paramref name="configKey"/>.
        /// If not found, returns null.
        /// </summary>
        /// <remarks>
        /// See also <see cref="GameConfigDataResolverExtensions.ResolveReference"/>,
        /// which throws when TryResolveReference would return null. Most users will
        /// want to use that instead.
        /// </remarks>
        object TryResolveReference(Type type, object configKey);
    }

    public static class GameConfigDataResolverExtensions
    {
        public static object ResolveReference(this IGameConfigDataResolver resolver, Type type, object configKey)
        {
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver), $"Trying to resolve a config data reference to '{configKey}' (type {type.ToGenericTypeString()}), but reference resolver is null");

            return resolver.TryResolveReference(type, configKey)
                   ?? throw new InvalidOperationException($"Failed to resolve config data reference to '{configKey}' of type {type.ToGenericTypeString()}");
        }
    }

    /// <summary>
    /// Resolver that searches for the key in multiple <see cref="IGameConfigDataResolver"/>s by testing each in order.
    /// </summary>
    public class MultiGameConfigDataResolver : IGameConfigDataResolver
    {
        List<IGameConfigDataResolver> _resolvers;

        public MultiGameConfigDataResolver(List<IGameConfigDataResolver> resolvers)
        {
            int count = resolvers.Count;
            for (int i = 0; i < count; i++)
            {
                if (resolvers[i] == null)
                    throw new ArgumentException($"Null resolver at index {i}");
            }

            _resolvers = resolvers;
        }

        public MultiGameConfigDataResolver(params IGameConfigDataResolver[] resolvers)
            : this(new List<IGameConfigDataResolver>(resolvers))
        {
        }

        public object TryResolveReference(Type type, object configKey)
        {
            foreach (IGameConfigDataResolver resolver in _resolvers)
            {
                object res = resolver.TryResolveReference(type, configKey);
                if (res != null)
                    return res;
            }

            return null;
        }
    }

    /// <summary>
    /// A helper resolver that doesn't contain any data.
    /// I.e., <see cref="TryResolveReference"/> always returns null.
    /// </summary>
    public class EmptyGameConfigDataResolver : IGameConfigDataResolver
    {
        public static readonly EmptyGameConfigDataResolver Instance = new EmptyGameConfigDataResolver();

        public object TryResolveReference(Type type, object configKey)
        {
            return null;
        }
    }

    /// <summary>
    /// Helper class for operating on <see cref="IGameConfigData"/> items.
    /// </summary>
    internal static class GameConfigItemHelper
    {
        /// <summary>
        /// Resolve the <code>ConfigKey</code> of a <see cref="IGameConfigData"/>.
        /// Warning: This method uses reflection and is slow. Only use it where performance isn't critical!
        /// </summary>
        /// <param name="configItem"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static object GetItemConfigKey(IGameConfigData configItem)
        {
            if (configItem is null)
                throw new ArgumentNullException(nameof(configItem));

            // \todo Optimize this to work without reflection
            BindingFlags bindingFlags = BindingFlags.Default | BindingFlags.Instance | BindingFlags.FlattenHierarchy |
                BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo configKeyProperty = configItem.GetType().GetGenericInterface(typeof(IGameConfigKey<>))
                .GetProperty(nameof(IGameConfigKey<object>.ConfigKey), bindingFlags);
            object configKey = configKeyProperty?.GetGetMethod(nonPublic: true)
                ?.Invoke(configItem, bindingFlags, null, null, CultureInfo.InvariantCulture);

            return configKey;
        }
    }

    /// <summary>
    /// Interface used to identify pieces of GameConfig data.
    /// </summary>
    public interface IGameConfigData
    {
    }

    /// <summary>
    /// Config library entry with a key of type <see cref="TKey"/>.
    /// </summary>
    public interface IGameConfigData<TKey> : IGameConfigData, IGameConfigKey<TKey>
    {
    }

    /// <summary>
    /// Source-config item for <see cref="TGameConfigData"/>.
    /// </summary>
    /// An item of this type is parsed from config data source (i.e. sheet) and converted to
    /// <see cref="TGameConfigData"/> by the <see cref="ToConfigData"/> method.
    ///
    /// This allows non-trivial mappings from a flat sheet structure to the final config data
    /// item. This transformation for an entry in the game config is declared via the
    /// <see cref="GameConfigEntryAttribute"/> attribute on the corresponding game config entry
    /// property.
    public interface IGameConfigSourceItem<TGameConfigKey, TGameConfigData> : IGameConfigKey<TGameConfigKey> where TGameConfigData : IGameConfigData<TGameConfigKey>
    {
        TGameConfigData ToConfigData();
    }

    public interface IGameConfigKey<TGameConfigKey>
    {
        TGameConfigKey ConfigKey { get; }
    }

    /// <summary>
    /// Wrapper for game config data which tells the serializer to
    /// serialize the config data contents (in contrast with the
    /// normal IGameConfigData serialization which serializes
    /// just the config key).
    ///
    /// Specifically, the serialized format of a
    /// GameConfigDataContent{TConfigData}-typed object is equal to
    /// what the serialized format of its ConfigData member object
    /// would be if TConfigData wasn't an IGameConfigData.
    /// </summary>
    public struct GameConfigDataContent<TConfigData> : IGameConfigDataContent
        // \note `class` constraint is here so that the serializer
        //       can (for simplicity) assume that TConfigData is nullable.
        //       If you remove this constraint, please make serializer
        //       consider the case where ConfigData is not nullable.
        //       #config-data-content-nullable
        // \note In practice, the class constraint is implicitly fulfilled
        //       due to GameConfigLibrary requiring it. It is specified
        //       here for explicitness, and also just to be safe.
        // \note new() constraint is here to ensure TConfigData is a concrete
        //       type. The current serializer implementation of
        //       GameConfigDataContent does not handle abstract types,
        //       but just assumes that TConfigData is by-members-serialized.
        //       There is likely no fundamental reason for this and this
        //       could be fixed.
        where TConfigData : class, IGameConfigData, new()
    {
        public TConfigData ConfigData { get; private set; }

        public GameConfigDataContent(TConfigData configData)
        {
            ConfigData = configData;
        }

        object IGameConfigDataContent.ConfigDataObject => ConfigData;
    }

    public interface IGameConfigDataContent
    {
        object ConfigDataObject { get; }
    }

    /// <summary>
    /// Optional post-load hook for <see cref="IGameConfigData{TKey}"/> types.
    /// </summary>
    public interface IGameConfigPostLoad
    {
        /// <summary>
        /// Called when a library containing this <see cref="IGameConfigData{TKey}"/> is loaded. This method can be used
        /// for custom data validation.
        /// </summary>
        void PostLoad();
    }

    // IGameConfigEntry

    public interface IGameConfigEntry
    {
        void ResolveMetaRefs(IGameConfigDataResolver resolver);
        void PostLoad();
        void BuildTimeValidate();
    }

    // IGameConfigLibrary

    public interface IGameConfigLibrary
    {
        IEnumerable<KeyValuePair<object, object>>   EnumerateAll    ();
    }

    public interface IGameConfigLibraryEntry : IGameConfigLibrary, IGameConfigEntry
    {
        Type              ItemType          { get; }
        IEnumerable<Type> ItemTypeHierarchy { get; }

        int Count { get; }

        object                                      GetInfoByKey    (object key);

        object                                      TryResolveReference(object key);

        bool TryResolveRealKeyFromAlias(object alias, out object realKey);

        #region For config deduplication

        /// <summary>
        /// Enumerate the key of each item which is directly modified by a patch in this specialization.
        /// Note: an item may be returned multiple times if it's modified by multiple patches.
        /// This is due to an implementation detail.
        /// </summary>
        IEnumerable<object> EnumerateDirectlyPatchedKeys();
        /// <summary>
        /// Enumerate the key of each item which is indirectly affected by a patch in this specialization.
        /// Note: an item may be returned multiple times if it's affected by multiple patches.
        /// This is due to an implementation detail.
        /// </summary>
        IEnumerable<object> EnumerateIndirectlyPatchedKeys();
        /// <summary>
        /// Get the ids referred to by the item identified by <paramref name="key"/>.
        /// If the item does not refer to any, then this returns null.
        /// The specified item must exist in this library.
        /// </summary>
        OrderedSet<ConfigItemId> GetReferencesOrNullFromItem(object key);

        /// <summary>
        /// Determines which patch defines an item in the deduplication storage,
        /// for this library's specialization:
        /// If this library gets the given item from a patch, this returns the index of that patch.
        /// If this library gets the given item from the baseline, this returns null.
        ///
        /// If the item is affected by multiple patches, the behavior is a bit complex.
        /// See <see cref="GameConfigLibraryPatchedItemEntry{TInfo}.TryGetItem"/> for details.
        /// </summary>
        /// <param name="key">
        /// The key of the item whose definer to determine.
        /// </param>
        ConfigPatchIndex? GetItemDefinerPatchOrNullForBaseline(object key);

        /// <summary>
        /// Duplicate the specified items due to indirect patching.
        /// This takes the instance of the item from the deduplication storage,
        /// clones it, and puts the new item instance in a storage owned by
        /// this library.
        /// </summary>
        /// <param name="keys">
        /// The keys of the items to duplicate.
        /// </param>
        void DuplicateIndirectlyPatchedItems(IEnumerable<object> keys);

        #endregion
    }

    public interface IGameConfigLibrary<TKey, out TInfo> : IGameConfigLibrary where TInfo : IGameConfigData<TKey>
    {
        int Count { get; }
        IEnumerable<TKey> Keys { get; }
        IEnumerable<TInfo> Values { get; }
        TInfo GetValueOrDefault(TKey key);
    }

    public static class GameConfigLibraryExtensions
    {
        public static bool TryGetValue<TKey, TInfo>(this IGameConfigLibrary<TKey, TInfo> library, TKey key, out TInfo result)
             where TInfo : class, IGameConfigData<TKey>
        {
            result = library.GetValueOrDefault(key);
            return result != null;
        }
    }

    // GameConfigKeyValue

    // \todo [petri] consider renaming
    public abstract class GameConfigKeyValue : IGameConfigEntry
    {
        public void ResolveMetaRefs(IGameConfigDataResolver resolver)
        {
            object resolvedThis = this;
            MetaSerialization.ResolveMetaRefs(GetType(), ref resolvedThis, resolver);
            MetaDebug.Assert(
                ReferenceEquals(resolvedThis, this),
                $"{nameof(MetaSerialization.ResolveMetaRefs)} shallow-copied a {nameof(GameConfigKeyValue)}; expected identity to be retained.");
        }

        public virtual void PostLoad(){ }

        public void BuildTimeValidate()
        {
        }
    }
    public class GameConfigKeyValue<TKeyValue> : GameConfigKeyValue
        where TKeyValue : GameConfigKeyValue<TKeyValue>, new()
    {
        // Explicit wrappers for this config entry type's import and export methods, to avoid needing MakeGenericMethod in GameConfigBinarySerialization, to avoid AOT build problems.
        public static TKeyValue ImportBinaryKeyValueStructure(GameConfigImporter importer, string fileName) => importer.ImportBinaryKeyValueStructure<TKeyValue>(fileName);
        public static TKeyValue ImportNakedBinaryKeyValueStructure(ConfigArchive archive, string fileName) => GameConfigUtil.ImportBinaryKeyValueStructure<TKeyValue>(archive, fileName);
        public static byte[] ExportBinaryKeyValueStructure(TKeyValue keyValue) => GameConfigUtil.ExportBinaryKeyValueStructure(keyValue);
        public static byte[] ReserializeKeyValueStructure(ConfigArchive archive, string fileName, MetaSerializationFlags flags) => GameConfigUtil.ReserializeBinaryKeyValueStructure<TKeyValue>(archive, fileName, flags);
    }

    /// <summary>
    /// Controls the in-memory storage of game configs, in particular of <see cref="GameConfigLibrary{TKey, TInfo}"/>.
    /// Specifically, this is used to control whether config deduplication is used.
    /// <para>
    /// There are memory and CPU usage tradeoffs between the storage modes.
    /// For <see cref="Deduplicating"/>, the setup cost of <see cref="GameConfigImportResources"/>
    /// is higher than for <see cref="Solo"/>, but then constructing specializations is typically cheaper.
    /// Additionally, for <see cref="Deduplicating"/>, accessing the config items is somewhat costlier.
    /// </para>
    /// </summary>
    public enum GameConfigRuntimeStorageMode
    {
        Invalid = 0,

        /// <summary>
        /// Config library item in-memory instances are stored in a deduplication storage that may
        /// be shared across multiple config specializations.
        /// <para>
        /// This should be used when multiple configs (possibly with different specializations)
        /// are expected to be created using the same <see cref="GameConfigImportResources"/>.
        /// </para>
        /// </summary>
        Deduplicating,
        /// <summary>
        /// Config library has its own in-memory copy of the library content.
        /// </summary>
        /// <para>
        /// This should be used when only one config gets created from the <see cref="GameConfigImportResources"/>.
        /// </para>
        Solo,
    }

    // GameConfigLibrary

    // A read-only stub game config library that is always empty. Used internally as the implementation of
    // SDK configs when no concrete implementation exists.
    internal sealed class EmptyGameConfigLibrary<TKey, TInfo> : IGameConfigLibrary<TKey, TInfo>
        where TInfo : IGameConfigData<TKey>
    {
        public int                Count  => 0;
        public IEnumerable<TKey>  Keys   => Enumerable.Empty<TKey>();
        public IEnumerable<TInfo> Values => Enumerable.Empty<TInfo>();
        public TInfo GetValueOrDefault(TKey key)
        {
            return default;
        }

        public IEnumerable<KeyValuePair<object, object>> EnumerateAll()
        {
            return Enumerable.Empty<KeyValuePair<object, object>>();
        }
    }

    // A read-only stub game config library that is populated with a static set of members on construction. Used
    // internally as the implementation of SDK configs when no concrete implementation exists.
    internal sealed class BuiltinGameConfigLibrary<TKey, TInfo> : IGameConfigLibrary<TKey, TInfo>
        where TInfo : IGameConfigData<TKey>
    {
        public int                Count  => _items.Count;
        public IEnumerable<TKey>  Keys   => _items.Keys;
        public IEnumerable<TInfo> Values => _items.Values;
        public TInfo GetValueOrDefault(TKey key)
        {
            return _items.TryGetValue(key, out TInfo ret) ? ret : default;
        }

        readonly Dictionary<TKey, TInfo> _items;
        public BuiltinGameConfigLibrary(IEnumerable<TInfo> values)
        {
            _items = new Dictionary<TKey, TInfo>();
            foreach (TInfo v in values)
                _items[v.ConfigKey] = v;
        }
        public IEnumerable<KeyValuePair<object, object>> EnumerateAll()
        {
            return _items.Select(kv => new KeyValuePair<object, object>(kv.Key, kv.Value));
        }
    }

    public sealed partial class GameConfigLibrary<TKey, TInfo> : IGameConfigLibraryEntry, IGameConfigLibrary<TKey, TInfo>, IReadOnlyDictionary<TKey, TInfo>
        where TInfo : class, IGameConfigData<TKey>, new()
    {
        Type IGameConfigLibraryEntry.ItemType => typeof(TInfo);
        // Return ItemType and all of its ancestors that are also IGameConfigData<TKey>
        IEnumerable<Type> IGameConfigLibraryEntry.ItemTypeHierarchy
        {
            get
            {
                for (Type t = typeof(TInfo); t != null && t.ImplementsInterface<IGameConfigData<TKey>>(); t = t.BaseType)
                    yield return t;
            }
        }


        GameConfigRuntimeStorageMode _storageMode;

        #region For when _storageMode is Deduplicating

        GameConfigLibraryDeduplicationStorage<TKey, TInfo> _deduplicationStorage;
        GameConfigDeduplicationOwnership _deduplicationOwnership;
        ConfigPatchIdSet _activePatches;

        /// <summary>
        /// The keys which are appended (not replaced) by the patches in this specialization.
        /// This is used for getting a consistent ordering of the appended items.
        /// See <see cref="LibraryEnumerator.MoveNext"/> for more info.
        /// </summary>
        OrderedSet<TKey> _patchAppendedKeys;

        /// <summary>
        /// Items which have been duplicated specifically for this config specialization,
        /// and aren't being shared (via <see cref="_deduplicationStorage"/>) with any
        /// other library instance.
        /// </summary>
        OrderedDictionary<TKey, TInfo> _exclusivelyOwnedItems;

        #endregion

        #region For when _storageMode is Solo

        OrderedDictionary<TKey, TInfo> _soloStorageItems;

        #endregion

        OrderedDictionary<TKey, TKey> _aliasToRealKey = null;

        // \note Count is resolved at construction time (non-trivial to calculate because of patch-appended items)
        public int Count { get; }

        public KeysEnumerable Keys => new KeysEnumerable(this);
        public ValuesEnumerable Values => new ValuesEnumerable(this);

        public LibraryEnumerator GetEnumerator() => new LibraryEnumerator(this);

        public IEnumerable<KeyValuePair<object, object>>    EnumerateAll()              => this.Select(kv => new KeyValuePair<object, object>(kv.Key, kv.Value));
        public object                                       GetInfoByKey(object key)    => this[(TKey)key];

        IEnumerable<TKey> IGameConfigLibrary<TKey, TInfo>.Keys => Keys;
        IEnumerable<TInfo> IGameConfigLibrary<TKey, TInfo>.Values => Values;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TInfo>.Keys => Keys;
        IEnumerable<TInfo> IReadOnlyDictionary<TKey, TInfo>.Values => Values;

        IEnumerator<KeyValuePair<TKey, TInfo>> IEnumerable<KeyValuePair<TKey, TInfo>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Create an empty library.
        /// </summary>
        public GameConfigLibrary()
            : this(
                  GameConfigRuntimeStorageMode.Solo,
                  soloStorageItems: new OrderedDictionary<TKey, TInfo>(),
                  deduplicationStorage: null,
                  deduplicationOwnership: GameConfigDeduplicationOwnership.None,
                  activePatches: null)
        {
        }

        GameConfigLibrary(
            GameConfigRuntimeStorageMode storageMode,
            OrderedDictionary<TKey, TInfo> soloStorageItems,
            GameConfigLibraryDeduplicationStorage<TKey, TInfo> deduplicationStorage,
            GameConfigDeduplicationOwnership deduplicationOwnership,
            OrderedSet<ExperimentVariantPair> activePatches)
        {
            _storageMode = storageMode;

            switch (storageMode)
            {
                case GameConfigRuntimeStorageMode.Deduplicating:
                    if (soloStorageItems != null)
                        throw new ArgumentException($"{nameof(soloStorageItems)} must be null when using {nameof(GameConfigRuntimeStorageMode)}.{storageMode}");

                    _deduplicationStorage = deduplicationStorage ?? throw new ArgumentNullException(nameof(deduplicationStorage));
                    _deduplicationOwnership = deduplicationOwnership;
                    _activePatches = deduplicationStorage.CreatePatchIdSet(activePatches ?? throw new ArgumentNullException(nameof(activePatches)));
                    _exclusivelyOwnedItems = new OrderedDictionary<TKey, TInfo>();

                    // Construct _patchAppendedKeys.
                    // \todo _patchAppendedKeys is duplicated for each specialization. It could probably
                    //       be optimized in the common case. But the benefit will only be significant
                    //       if there's lots of patch-appended items.
                    _patchAppendedKeys = new OrderedSet<TKey>();
                    foreach (ConfigPatchIndex patchIndex in _activePatches.Enumerate())
                    {
                        foreach (TKey key in _deduplicationStorage.PatchInfos[patchIndex].AppendedItems)
                            _patchAppendedKeys.Add(key);
                    }

                    Count = _deduplicationStorage.NumBaselineItems + _patchAppendedKeys.Count;
                    break;

                case GameConfigRuntimeStorageMode.Solo:
                    _soloStorageItems = soloStorageItems ?? throw new ArgumentNullException(nameof(soloStorageItems));

                    if (deduplicationStorage != null)
                        throw new ArgumentException($"{nameof(deduplicationStorage)} must be null when using {nameof(GameConfigRuntimeStorageMode)}.{storageMode}");
                    if (deduplicationOwnership != GameConfigDeduplicationOwnership.None)
                        throw new ArgumentException($"{nameof(deduplicationOwnership)} must be {GameConfigDeduplicationOwnership.None} when using {nameof(GameConfigRuntimeStorageMode)}.{storageMode}");
                    if (activePatches != null)
                        throw new ArgumentException($"{nameof(activePatches)} must be null when using {nameof(GameConfigRuntimeStorageMode)}.{storageMode}");

                    Count = _soloStorageItems.Count;
                    break;

                default:
                    throw new MetaAssertException("unreachable");
            }
        }

        /// <summary>
        /// Create a library for a config that is a specialization using 0 or more patches.
        /// The library will use the content in <paramref name="deduplicationStorage"/>,
        /// but will not modify any of it.
        /// </summary>
        internal static GameConfigLibrary<TKey, TInfo> CreateSpecialization(
            GameConfigLibraryDeduplicationStorage<TKey, TInfo> deduplicationStorage,
            OrderedSet<ExperimentVariantPair> activePatches)
        {
            return new GameConfigLibrary<TKey, TInfo>(
                GameConfigRuntimeStorageMode.Deduplicating,
                soloStorageItems: null,
                deduplicationStorage,
                GameConfigDeduplicationOwnership.None,
                activePatches: activePatches);
        }

        /// <summary>
        /// Create a library for a baseline config, with the <paramref name="baselineItems"/>.
        /// <para>
        /// This method is only used during the construction of <see cref="GameConfigImportResources"/>,
        /// for the purpose of populating <paramref name="deduplicationStorage"/>.
        /// This library "owns" the baseline parts of <paramref name="deduplicationStorage"/>
        /// and will populate it as necessary during the <see cref="GameConfigBase.Import"/> of the containing config,
        /// </para>
        /// </summary>
        internal static GameConfigLibrary<TKey, TInfo> CreateWithOwnershipOfBaseline(
            OrderedDictionary<TKey, TInfo> baselineItems,
            GameConfigLibraryDeduplicationStorage<TKey, TInfo> deduplicationStorage)
        {
            // Populate the baseline item instances in the deduplicationStorage.
            // Later during the import of the containing config, MetaRefs will be resolved, PostLoads called, and such.

            deduplicationStorage.PopulateBaseline(baselineItems);

            return new GameConfigLibrary<TKey, TInfo>(
                GameConfigRuntimeStorageMode.Deduplicating,
                soloStorageItems: null,
                deduplicationStorage,
                GameConfigDeduplicationOwnership.Baseline,
                activePatches: new OrderedSet<ExperimentVariantPair>());
        }

        /// <summary>
        /// Create a library for a single-patch config, with the patched items given
        /// by <paramref name="patch"/>.
        /// <para>
        /// This method is only used during the construction of <see cref="GameConfigImportResources"/>,
        /// for the purpose of populating <paramref name="deduplicationStorage"/>.
        /// This library "owns" the specified single-patch parts of <paramref name="deduplicationStorage"/>
        /// and will populate it as necessary during the <see cref="GameConfigBase.Import"/> of the containing config,
        /// </para>
        /// </summary>
        internal static GameConfigLibrary<TKey, TInfo> CreateWithOwnershipOfSinglePatch(
            GameConfigLibraryPatch<TKey, TInfo> patch,
            ExperimentVariantPair patchId,
            GameConfigLibraryDeduplicationStorage<TKey, TInfo> deduplicationStorage)
        {
            // Populate the initial patched item instances in the deduplicationStorage.
            // Later during the import of the containing config, indirectly-patched items will be duplicated,
            // and then MetaRefs resolved, PostLoads called, and such.

            ConfigPatchIndex patchIndex = deduplicationStorage.PatchIdToIndex[patchId];
            deduplicationStorage.PopulatePatch(patchIndex, patch);

            return new GameConfigLibrary<TKey, TInfo>(
                GameConfigRuntimeStorageMode.Deduplicating,
                soloStorageItems: null,
                deduplicationStorage,
                GameConfigDeduplicationOwnership.SinglePatch,
                activePatches: new OrderedSet<ExperimentVariantPair> { patchId });
        }

        /// <summary>
        /// Construct a library from the items.
        /// <para>
        /// This should typically not be used directly by user-defined <see cref="GameConfigBase.PopulateConfigEntries(GameConfigImporter)"/>,
        /// where instead the provided importer parameter should be used.
        /// </para>
        /// <para>
        /// The library created by this will not share its contents with any other library (hence "solo").
        /// In other words it will not use deduplication.
        /// This is used during config import when deduplication is not used (such as on the client),
        /// as well as in special situations like config building.
        /// </para>
        /// </summary>
        public static GameConfigLibrary<TKey, TInfo> CreateSolo(OrderedDictionary<TKey, TInfo> items)
        {
            return new GameConfigLibrary<TKey, TInfo>(
                GameConfigRuntimeStorageMode.Solo,
                soloStorageItems: items,
                deduplicationStorage: null,
                GameConfigDeduplicationOwnership.None,
                activePatches: null);
        }

        /// <summary>
        /// Enumerate only the items that are owned by this library, according to <see cref="_deduplicationOwnership"/>.
        /// These are the items that this library is allowed to mutate (e.g. to call <see cref="IGameConfigPostLoad.PostLoad"/>).
        /// </summary>
        IEnumerable<TInfo> EnumerateOwnedItems()
        {
            switch (_storageMode)
            {
                case GameConfigRuntimeStorageMode.Deduplicating:
                    switch (_deduplicationOwnership)
                    {
                        case GameConfigDeduplicationOwnership.None:         return _exclusivelyOwnedItems.Values;
                        case GameConfigDeduplicationOwnership.Baseline:     return _deduplicationStorage.EnumerateItemsBelongingToBaseline();
                        case GameConfigDeduplicationOwnership.SinglePatch:  return _deduplicationStorage.EnumerateItemsBelongingToPatch(_activePatches.Single());
                        default:
                            throw new MetaAssertException("unreachable");
                    }

                case GameConfigRuntimeStorageMode.Solo:
                    return _soloStorageItems.Values;

                default:
                    throw new MetaAssertException("unreachable");
            }
        }

        /// <inheritdoc/>
        public ConfigPatchIndex? GetItemDefinerPatchOrNullForBaseline(object keyObject)
        {
            if (_storageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(GetItemDefinerPatchOrNullForBaseline)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{_storageMode}");

            if (_exclusivelyOwnedItems.Count != 0)
                throw new MetaAssertException($"Didn't expect {nameof(GetItemDefinerPatchOrNullForBaseline)} to get called when {nameof(_exclusivelyOwnedItems)} was already populated");

            TKey key = (TKey)keyObject;
            return _deduplicationStorage.PatchedItemEntries[key].GetItemDefinerPatchOrNullForBaseline(_activePatches);
        }

        /// <inheritdoc/>
        public IEnumerable<object> EnumerateDirectlyPatchedKeys()
        {
            // \note Tolerate empty library special case even when _storageMode isn't Deduplicating.
            //       Parameterless constructor creates an empty library with Solo mode, which may appear within an otherwise deduplicated config.
            if (Count == 0)
                return Enumerable.Empty<object>();

            if (_storageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(EnumerateDirectlyPatchedKeys)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{_storageMode}");

            return _activePatches
                .Enumerate()
                .SelectMany(patchId => _deduplicationStorage.PatchInfos[patchId].DirectlyPatchedItems)
                .Cast<object>();
        }

        /// <inheritdoc/>
        public IEnumerable<object> EnumerateIndirectlyPatchedKeys()
        {
            // \note Tolerate empty library special case even when _storageMode isn't Deduplicating.
            //       Parameterless constructor creates an empty library with Solo mode, which may appear within an otherwise deduplicated config.
            if (Count == 0)
                return Enumerable.Empty<object>();

            if (_storageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(EnumerateIndirectlyPatchedKeys)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{_storageMode}");

            return _activePatches
                .Enumerate()
                .SelectMany(patchId => _deduplicationStorage.PatchInfos[patchId].IndirectlyPatchedItems)
                .Cast<object>();
        }

        /// <inheritdoc/>
        public OrderedSet<ConfigItemId> GetReferencesOrNullFromItem(object keyObject)
        {
            if (_storageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(GetReferencesOrNullFromItem)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{_storageMode}");

            TKey key = (TKey)keyObject;
            return _deduplicationStorage.PatchedItemEntries[key].GetReferencesOrNullFromItem(_activePatches);
        }

        /// <inheritdoc/>
        public void DuplicateIndirectlyPatchedItems(IEnumerable<object> keyObjects)
        {
            if (_storageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(DuplicateIndirectlyPatchedItems)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{_storageMode}");

            switch (_deduplicationOwnership)
            {
                case GameConfigDeduplicationOwnership.None:
                {
                    List<TInfo> originalItems = new List<TInfo>();
                    foreach (object key in keyObjects)
                        originalItems.Add(this[(TKey)key]);
                    IReadOnlyList<TInfo> clonedItems = MetaSerialization.CloneTableTagged(originalItems, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null, maxCollectionSizeOverride: int.MaxValue);

                    foreach (TInfo clonedItem in clonedItems)
                    {
                        // This library doesn't own any part of the deduplicated storage:
                        // place the item in _exclusivelyOwnedItems.
                        bool added = _exclusivelyOwnedItems.TryAdd(clonedItem.ConfigKey, clonedItem);
                        if (!added)
                            throw new MetaAssertException("Item was duplicated multiple times into exclusively owned storage");
                    }
                    break;
                }

                case GameConfigDeduplicationOwnership.Baseline:
                    // Within baseline, there cannot be any indirect patching.
                    throw new MetaAssertException("Did not expect item to get duplicated in baseline");

                case GameConfigDeduplicationOwnership.SinglePatch:
                {
                    // Within a single-patch-owning library, clone the items from the baseline
                    // and put into patch overrides in the deduplication storage.
                    _deduplicationStorage.DuplicateIndirectlyPatchedItems(_activePatches.Single(), keyObjects.Cast<TKey>());
                    break;
                }

                default:
                    throw new MetaAssertException("unreachable");
            }
        }

        public void ResolveMetaRefs(IGameConfigDataResolver resolver)
        {
            // Resolve MetaRefs only within owned items.
            MetaSerialization.ResolveMetaRefsInTable(EnumerateOwnedItems().ToList(), resolver);
        }

        public void PostLoad()
        {
            // If implements IGameConfigPostLoad, invoke PostLoad() for all owned items.
            Type infoType = typeof(TInfo);
            if (infoType.ImplementsInterface<IGameConfigPostLoad>())
            {
                foreach (TInfo info in EnumerateOwnedItems())
                    ((IGameConfigPostLoad)info).PostLoad();
            }
        }

        public void BuildTimeValidate()
        {
            // Check that non-primitive, non-enum TKey conforms to various requirements:
            // - Overrides Equals(object), class keys will use identity equality, which breaks all fetching from the GameConfigLibraries.
            // - Overrides ToString(), the default ToString() will just output the name of the type, and conversion to json maps will collapse all the elements to the same entry.
            // - Implements IEquatable<TKey>, to avoid lots of allocations when using the value as dictionary key.
            // \note The methods can be implemented anywhere in the type hierarchy (except the base type), like how StringId does
            Type keyType = typeof(TKey);
            if (!keyType.IsPrimitive && !keyType.IsEnum)
            {
                Type equalsDefinedIn = keyType.GetMethod("Equals", new Type[] { typeof(object) }).DeclaringType;
                if (equalsDefinedIn == typeof(object) || equalsDefinedIn == typeof(ValueType))
                    throw new InvalidOperationException($"The type {keyType.ToGenericTypeString()} must override 'bool Equals(object obj)', because it is used as a GameConfigLibrary<> key.");

                Type toStringDefinedIn = keyType.GetMethod("ToString", new Type[] { }).DeclaringType;
                if (toStringDefinedIn == typeof(object) || toStringDefinedIn == typeof(ValueType))
                    throw new InvalidOperationException($"The type {keyType.ToGenericTypeString()} must override 'string ToString()', because it is used as a GameConfigLibrary<> key.");

                if (!keyType.ImplementsInterface(typeof(IEquatable<>).MakeGenericType(keyType)))
                    throw new InvalidOperationException($"The type {keyType.ToGenericTypeString()} must implement 'IEquatable<{keyType.ToGenericTypeString()}>', because it is used as a GameConfigLibrary<> key.");
            }

            // Check that all keys have a unique value when converted to string via ToString().
            // The values are communicated to the dashboard in JSON which uses the ToString() representation.
            Dictionary<string, TKey> keysAsStrings = new Dictionary<string, TKey>();
            foreach ((TKey key, TInfo info) in this)
            {
                string keyStr = key.ToString();
                if (keysAsStrings.TryGetValue(keyStr, out TKey duplicateKey))
                    throw new InvalidOperationException($"GameConfigLibrary<{keyType.ToGenericTypeString()}, {typeof(TInfo).ToGenericTypeString()}> contains items '{PrettyPrint.Compact(key)}' and '{PrettyPrint.Compact(duplicateKey)}' whose ToString() produces identical value '{keyStr}'. This is usually because the default ToString() returns the class name. The ToString() must produce unique string values for unique keys for the JSON serialization to dashboard to work correctly.");
                else
                    keysAsStrings.Add(keyStr, key);
            }
        }

        object IGameConfigLibraryEntry.TryResolveReference(object keyObject)
        {
            TKey key = (TKey)keyObject;
            if (TryGetValue(key, out TInfo info))
                return info;
            else if (_aliasToRealKey != null
                     && _aliasToRealKey.TryGetValue(key, out TKey realKey)
                     && TryGetValue(realKey, out TInfo infoFromAlias))
            {
                return infoFromAlias;
            }
            else
                return null;
        }

        public void RegisterAlias(TKey id, TKey alias)
        {
            if (ContainsKey(alias) || !ContainsKey(id))
                return;
            if (_aliasToRealKey == null)
                _aliasToRealKey = new OrderedDictionary<TKey, TKey>();
            _aliasToRealKey[alias] = id;
        }

        public IEnumerable<(TKey RealKey, TKey Alias)> GetAliases()
        {
            return _aliasToRealKey?.Select(kv => (kv.Value, kv.Key)) ?? Enumerable.Empty<(TKey, TKey)>();
        }

        public bool HasAlias(TKey alias)
        {
            return _aliasToRealKey != null && _aliasToRealKey.ContainsKey(alias);
        }

        public TKey ResolveAlias(TKey alias)
        {
            return _aliasToRealKey[alias];
        }

        bool IGameConfigLibraryEntry.TryResolveRealKeyFromAlias(object alias, out object realKeyObject)
        {
            if (_aliasToRealKey != null && _aliasToRealKey.TryGetValue((TKey)alias, out TKey realKey))
            {
                realKeyObject = realKey;
                return true;
            }
            else
            {
                realKeyObject = null;
                return false;
            }
        }

        public bool TryGetValue(TKey key, out TInfo info)
        {
            switch (_storageMode)
            {
                case GameConfigRuntimeStorageMode.Deduplicating:
                    if (_exclusivelyOwnedItems.TryGetValue(key, out info))
                        return true;

                    info = _deduplicationStorage.TryGetItem(key, _activePatches);
                    return info != null;

                case GameConfigRuntimeStorageMode.Solo:
                    return _soloStorageItems.TryGetValue(key, out info);

                default:
                    throw new MetaAssertException("unreachable");
            }
        }

        public bool ContainsKey(TKey key) => TryGetValue(key, out _);
        public TInfo GetValueOrDefault(TKey key)
        {
            return TryGetValue(key, out TInfo info)
                   ? info
                   : null;
        }

        public TInfo this[TKey key]
        {
            get
            {
                if (TryGetValue(key, out TInfo value))
                    return value;
                else
                    throw new KeyNotFoundException($"GameConfigLibrary<{typeof(TKey)}, {typeof(TInfo).Name}> does not contain key {key}");
            }
        }

        // Explicit wrappers for this config entry type's import and export methods, to avoid needing MakeGenericMethod in GameConfigBinarySerialization, to avoid AOT build problems.
        public static GameConfigLibrary<TKey, TInfo> ImportBinaryLibrary(GameConfigImporter importer, string fileName) => importer.ImportBinaryLibrary<TKey, TInfo>(fileName);

        public static GameConfigLibrary<TKey, TInfo> ImportNakedBinaryLibrary(ConfigArchive archive, string fileName)
        {
            return CreateSolo(items: GameConfigUtil.ImportBinaryLibraryItems<TKey, TInfo>(archive, fileName));
        }

        public static byte[] ExportBinaryLibrary(GameConfigLibrary<TKey, TInfo> library) => GameConfigUtil.ExportBinaryLibrary(library);
        public static byte[] ExportBinaryLibraryAliases(GameConfigLibrary<TKey, TInfo> library) => GameConfigUtil.ExportBinaryLibraryAliases(library);
        public static byte[] ReserializeBinaryLibraryItems(ConfigArchive archive, string fileName, MetaSerializationFlags flags) => GameConfigUtil.ReserializeBinaryLibraryItems<TInfo>(archive, fileName, flags);
    }

    public class GameConfigImporter
    {
        GameConfigImportParams _importParams;
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        CsvParseOptions        _parseOptions;
#endif

        public GameConfigImportParams Params => _importParams;

        public bool IsDeduplicationBaseline => _importParams.DeduplicationOwnership == GameConfigDeduplicationOwnership.Baseline;
        public IGameConfig DeduplicationBaseline => _importParams.Resources.DeduplicationBaseline;

        public GameConfigImporter(
            GameConfigImportParams importParams
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
            , CsvParseOptions parseOptions
#endif
            )
        {
            _importParams = importParams;
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
            _parseOptions = parseOptions;
#endif
        }

        public bool Contains(string fileName)
        {
            return _importParams.Resources.BaselineArchive.ContainsEntryWithName(fileName);
        }

        static string MpcFileNameToEntryName(string fileName)
        {
            if (fileName.EndsWith(".mpc", StringComparison.Ordinal))
                return fileName.Substring(0, fileName.Length-4);
            else
                throw new ArgumentException($"Expected binary library file name to end in .mpc: {fileName}");
        }

        /// <summary>
        /// Importer-internal library creation helper, used by the different
        /// format importers below (binary, csv, json).
        /// Reads the items/patch using the given callbacks and constructs a library
        /// according to <see cref="_importParams"/>.
        /// </summary>
        GameConfigLibrary<TKey, TInfo> ImportLibrary<TKey, TInfo>(
            string libraryId,
            Func<OrderedDictionary<TKey, TInfo>> importBaselineItems,
            Func<ExperimentVariantPair, GameConfigLibraryPatch<TKey, TInfo>> importPatch,
            Action<GameConfigLibrary<TKey, TInfo>> importAliases
            )
            where TInfo : class, IGameConfigData<TKey>, new()
        {
            GameConfigLibrary<TKey, TInfo> library;

            switch (_importParams.Resources.ConfigRuntimeStorageMode)
            {
                case GameConfigRuntimeStorageMode.Deduplicating:
                {
                    GameConfigLibraryDeduplicationStorage<TKey, TInfo> deduplicationStorage;
                    if (_importParams.DeduplicationOwnership == GameConfigDeduplicationOwnership.Baseline)
                    {
                        deduplicationStorage = new GameConfigLibraryDeduplicationStorage<TKey, TInfo>(allPatchIds: _importParams.Resources.Patches.Keys);
                        _importParams.Resources.LibraryDeduplicationStorages.Add(libraryId, deduplicationStorage);
                    }
                    else
                        deduplicationStorage = (GameConfigLibraryDeduplicationStorage<TKey, TInfo>)_importParams.Resources.LibraryDeduplicationStorages[libraryId];

                    switch (_importParams.DeduplicationOwnership)
                    {
                        case GameConfigDeduplicationOwnership.None:
                            library = GameConfigLibrary<TKey, TInfo>.CreateSpecialization(deduplicationStorage, _importParams.ActivePatches);
                            break;

                        case GameConfigDeduplicationOwnership.Baseline:
                            library = GameConfigLibrary<TKey, TInfo>.CreateWithOwnershipOfBaseline(importBaselineItems(), deduplicationStorage);
                            break;

                        case GameConfigDeduplicationOwnership.SinglePatch:
                        {
                            ExperimentVariantPair patchId = _importParams.ActivePatches.Single();
                            library = GameConfigLibrary<TKey, TInfo>.CreateWithOwnershipOfSinglePatch(importPatch(patchId), patchId, deduplicationStorage);
                            break;
                        }

                        default:
                            throw new MetaAssertException("unreachable");
                    }

                    break;
                }

                case GameConfigRuntimeStorageMode.Solo:
                {
                    OrderedDictionary<TKey, TInfo> items = importBaselineItems();

                    foreach (ExperimentVariantPair patchId in _importParams.ActivePatches)
                    {
                        GameConfigLibraryPatch<TKey, TInfo> patch = importPatch(patchId);
                        patch.PatchContentDangerouslyInPlace(libraryItems: items);
                    }

                    library = GameConfigLibrary<TKey, TInfo>.CreateSolo(items);

                    break;
                }

                default:
                    throw new MetaAssertException("unreachable");
            }

            importAliases(library);

            return library;
        }

        // Binary import/parse

        public GameConfigLibrary<TKey, TInfo> ImportBinaryLibrary<TKey, TInfo>(string fileName) where TInfo : class, IGameConfigData<TKey>, new()
        {
            return ImportLibrary<TKey, TInfo>(
                libraryId: $"file:{fileName}",
                importBaselineItems:
                    () => GameConfigUtil.ImportBinaryLibraryItems<TKey, TInfo>(_importParams.Resources.BaselineArchive, fileName),
                importPatch:
                    (ExperimentVariantPair patchId) =>
                    {
                        string entryName = MpcFileNameToEntryName(fileName);
                        GameConfigPatchEnvelope patchEnvelope = _importParams.Resources.Patches[patchId];

                        if (patchEnvelope.TryDeserializeEntryPatch(entryName, typeof(GameConfigLibraryPatch<TKey, TInfo>), out GameConfigEntryPatch entryPatch))
                            return (GameConfigLibraryPatch<TKey, TInfo>)entryPatch;
                        else
                            return GameConfigLibraryPatch<TKey, TInfo>.CreateEmpty();
                    },
                importAliases:
                    (GameConfigLibrary<TKey, TInfo> library) =>
                    {
                        string entryName = MpcFileNameToEntryName(fileName);
                        string aliasFileName = $"{entryName}.AliasTable.mpc";
                        if (_importParams.Resources.BaselineArchive.ContainsEntryWithName(aliasFileName))
                            GameConfigUtil.ImportBinaryLibraryAliases(library, _importParams.Resources.BaselineArchive, aliasFileName);
                    });
        }

        public TKeyValue ImportBinaryKeyValueStructure<TKeyValue>(string fileName) where TKeyValue : GameConfigKeyValue, new()
        {
            // \note Key-value structure deduplication hasn't been implemented.
            //       Each config specialization deserializes the full structure
            //       and applies patches on it.
            //       Deduplication could be implemented, at top-level member granularity.

            string entryName = MpcFileNameToEntryName(fileName);

            TKeyValue keyValueStructure = GameConfigUtil.ImportBinaryKeyValueStructure<TKeyValue>(_importParams.Resources.BaselineArchive, fileName);

            IEnumerable<GameConfigPatchEnvelope> patchEnvelopes =
                _importParams.Resources.Patches
                .Where(kv => _importParams.ActivePatches.Contains(kv.Key))
                .Select(kv => kv.Value);

            foreach (GameConfigPatchEnvelope patchEnvelope in patchEnvelopes)
                patchEnvelope.PatchEntryContentInPlace(keyValueStructure, entryName, entryPatchType: typeof(GameConfigStructurePatch<TKeyValue>));

            return keyValueStructure;
        }

        // Csv import/parse

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public GameConfigLibrary<TKey, TInfo> ImportCsvLibrary<TKey, TInfo>(string fileName) where TInfo : class, IGameConfigData<TKey>, new()
        {
            return ImportLibrary<TKey, TInfo>(
                libraryId: $"file:{fileName}",
                importBaselineItems:
                    () => GameConfigUtil.ImportCsvLibraryItems<TKey, TInfo>(_parseOptions, _importParams.Resources.BaselineArchive, fileName),
                importPatch:
                    (ExperimentVariantPair _) => GameConfigLibraryPatch<TKey, TInfo>.CreateEmpty(),
                importAliases:
                    _ => { });
        }

        public GameConfigLibrary<TKey, TInfo> ParseCsvLibrary<TKey, TInfo>(string fileName, byte[] bytes) where TInfo : class, IGameConfigData<TKey>, new()
        {
            return ImportLibrary<TKey, TInfo>(
                libraryId: $"file:{fileName}",
                importBaselineItems:
                    () =>
                    {
                        using (MemoryStream stream = new MemoryStream(bytes, false))
                            return GameConfigUtil.ParseCsvLibraryItems<TKey, TInfo>(_parseOptions, fileName, stream);
                    },
                importPatch:
                    (ExperimentVariantPair _) => GameConfigLibraryPatch<TKey, TInfo>.CreateEmpty(),
                importAliases:
                    _ => { });
        }

        public T ImportCsvKeyValueStructure<T>(string fileName) where T : GameConfigKeyValue, new()
            => GameConfigUtil.ImportCsvKeyValueStructure<T>(_importParams.Resources.BaselineArchive, fileName);
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE

        // Json import/parse

        public GameConfigLibrary<TKey, TInfo> ImportJsonLibrary<TKey, TInfo>(string fileName) where TInfo : class, IGameConfigData<TKey>, new()
        {
            return ImportLibrary<TKey, TInfo>(
                libraryId: $"file:{fileName}",
                importBaselineItems:
                    () => GameConfigUtil.ImportJsonLibraryItems<TKey, TInfo>(_importParams.Resources.BaselineArchive, fileName),
                importPatch:
                    (ExperimentVariantPair _) => GameConfigLibraryPatch<TKey, TInfo>.CreateEmpty(),
                importAliases:
                    _ => { });
        }

        // Raw bytes import

        public byte[] ImportRawBytes(string fileName)
        {
            return _importParams.Resources.BaselineArchive.GetEntryBytes(fileName).ToArray();
        }
    }

    /// <summary>
    /// Base class for game-specific GameConfig registries.
    /// </summary>
    public abstract class GameConfigBase : IGameConfig
    {
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        protected virtual CsvParseOptions           DefaultParseOptions { get; }
#endif

        Dictionary<Type, List<IGameConfigLibraryEntry>>  _libraries = new Dictionary<Type, List<IGameConfigLibraryEntry>>();

        protected GameConfigRuntimeStorageMode StorageMode { get; private set; } = GameConfigRuntimeStorageMode.Solo;

        [field: ObjectGraphDump.Ignore]
        protected GameConfigTopLevelDeduplicationStorage DeduplicationStorage { get; private set; } = null;
        protected GameConfigDeduplicationOwnership DeduplicationOwnership { get; private set; } = GameConfigDeduplicationOwnership.None;
        protected bool IsConstructingDeduplicationStorage => DeduplicationOwnership != GameConfigDeduplicationOwnership.None;

        public GameConfigBase()
        {
            if (!MetaplayCore.IsInitialized)
                throw new InvalidOperationException("MetaplayCore.Initialize() must be called before GameConfigs can be used");
        }

        public virtual ConfigArchiveEntry[] ExportMpcArchiveEntries()
        {
            return GameConfigRepository.Instance.GetGameConfigTypeInfo(GetType()).Serialization.Export(this);
        }

        public void Import(GameConfigImportParams importParams)
        {
            StorageMode = importParams.Resources.ConfigRuntimeStorageMode;
            DeduplicationStorage = importParams.Resources.TopLevelDeduplicationStorage;
            DeduplicationOwnership = importParams.DeduplicationOwnership;
            GameConfigImporter importer = new GameConfigImporter(importParams
#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
                , DefaultParseOptions
#endif
                );
            PopulateConfigEntries(importer);
            if (!importParams.IsConfigBuildParent)
                OnConfigEntriesPopulated(importParams.IsBuildingConfigs);
        }

        /// <summary>
        /// Populate GameConfig from archive.
        /// </summary>
        public virtual void PopulateConfigEntries(GameConfigImporter importer)
        {
            GameConfigRepository.Instance.GetGameConfigTypeInfo(GetType()).Serialization.ImportInto(this, importer);
        }

        /// <summary>
        /// Register contained game config libraries as resolvers for their respective game config types
        /// </summary>
        void RegisterResolvers()
        {
            foreach (IGameConfigLibraryEntry library in GetLibraries())
            {
                // Register this library to use as a reference resolver for type (to enable deserialization & parsing).
                // Register the library's concrete item type and the ancestor types that are also config data.
                foreach (Type itemType in library.ItemTypeHierarchy)
                {
                    if (!_libraries.TryGetValue(itemType, out List<IGameConfigLibraryEntry> typeLibraries))
                    {
                        typeLibraries = new List<IGameConfigLibraryEntry>();
                        _libraries.Add(itemType, typeLibraries);
                    }

                    typeLibraries.Add(library);
                }
            }
        }

        public void OnConfigEntriesPopulated(bool isBuildingConfigs = false)
        {
            CheckConfigEntriesAreNotNull();

            RegisterResolvers();

            // This checks there are no duplicates _across libraries_. That can happen
            // because you are allowed to have multiple libraries with the same item type
            // (either exactly the same type, or same base config item type).
            // (Note that duplicates _within a library_ are already checked earlier,
            // before the library is created.)
            //
            // This is not a super cheap check so it is only done at config build time.
            // Normally, config build time checks are in BuildTimeValidate, but this
            // needs to be done already as early as this, because otherwise duplicate items
            // can cause less understandable errors in ComputeBaselineReferences.
            if (isBuildingConfigs)
                CheckNoDuplicatesPerItemTypeAcrossLibraries();

            if (StorageMode == GameConfigRuntimeStorageMode.Deduplicating)
            {
                if (DeduplicationOwnership == GameConfigDeduplicationOwnership.Baseline)
                    ComputeBaselineReferences();

                DuplicateItemsDueToReferences();
            }

            ResolveMetaRefs();
            CallEntryPostLoadHooks();
            RegisterSDKIntegrations();

            if (IsConstructingDeduplicationStorage)
                OnLoadedWhenConstructingDeduplicationStorage();
            else
                OnLoaded();

            // For performance, skip validation for the special config instances that are created
            // during the creation of the GameConfigImportResources. Validation will be done when the
            // proper specialized config instance is eventually created using the resources.
            if (!IsConstructingDeduplicationStorage && !isBuildingConfigs)
                Validate();
        }

        void CheckConfigEntriesAreNotNull()
        {
            foreach ((GameConfigRepository.GameConfigEntryInfo entryInfo, IGameConfigEntry entry) in GetConfigEntries())
            {
                if (entry == null)
                    throw new InvalidOperationException($"{GetType().Name} entry {entryInfo.Name} is null after config import.");
            }
        }

        void CheckNoDuplicatesPerItemTypeAcrossLibraries()
        {
            // Collect all the "basemost" item types among all the library item types.

            OrderedSet<Type> basemostItemTypes = new OrderedSet<Type>();

            foreach (IGameConfigLibraryEntry library in GetLibraries())
            {
                IEnumerable<Type> types = library.ItemTypeHierarchy;
                // Assertion: typeHierarchy should be in order from most-derived to most-base.
                Type basemostType = types.Last();
                foreach (Type type in types.Take(types.Count()-1))
                {
                    if (!type.IsSubclassOf(basemostType))
                        throw new MetaAssertException($"{library.GetType().ToGenericTypeString()}: {nameof(library.ItemTypeHierarchy)}'s last element was expected to be the basemost type but it wasn't ({type.ToGenericTypeString()} isn't a subclass)");
                }

                basemostItemTypes.Add(basemostType); // \note Might exist already, that's ok
            }

            // For each basemost item type, check that there are no duplicates.
            // It is enough to check just the basemost item types. Any intermediate
            // base classes will have a subset of the types of the basemost class.

            foreach (Type itemType in basemostItemTypes)
            {
                // Skip item types for which there's only 1 library.
                // This is an optimization: this is the most common case, and does not need
                // to be checked here, because duplicates within a single library are checked earlier.
                if (_libraries[itemType].Count == 1)
                    continue;

                Dictionary<object, IGameConfigLibraryEntry> keyToLibrary = new Dictionary<object, IGameConfigLibraryEntry>();

                foreach (IGameConfigLibraryEntry library in _libraries[itemType])
                {
                    foreach (object key in library.EnumerateAll().Select(kv => kv.Key))
                    {
                        if (keyToLibrary.TryGetValue(key, out IGameConfigLibraryEntry otherLibrary))
                        {
                            // Based on the IGameConfigLibraryEntry reference, this resolves which entry it is. A little bit ugly but works.
                            GameConfigRepository.GameConfigEntryInfo currentLibraryInfo = GetConfigEntries().Single(e => ReferenceEquals(e.Entry, library)).EntryInfo;
                            GameConfigRepository.GameConfigEntryInfo otherLibraryInfo   = GetConfigEntries().Single(e => ReferenceEquals(e.Entry, otherLibrary)).EntryInfo;
                            throw new InvalidOperationException(Invariant($"Duplicate config item id for type {itemType.ToGenericTypeString()} across libraries: {key} exists in both {currentLibraryInfo.Name} and in {otherLibraryInfo.Name}"));
                        }

                        keyToLibrary.Add(key, library);
                    }
                }
            }
        }

        void ComputeBaselineReferences()
        {
            if (StorageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(ComputeBaselineReferences)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{StorageMode}");

            if (DeduplicationOwnership != GameConfigDeduplicationOwnership.Baseline)
                throw new MetaAssertException($"Didn't expect {nameof(ComputeBaselineReferences)} to get called when {nameof(DeduplicationOwnership)}={DeduplicationOwnership}");
            if (DeduplicationStorage.BaselineReferences != null)
                throw new MetaAssertException($"{nameof(DeduplicationStorage.BaselineReferences)} was already assigned");

            OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> baselineReferences = new OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>>();
            foreach (IGameConfigLibraryEntry referringLibrary in GetLibraries())
            {
                foreach (object referringKey in referringLibrary.EnumerateAll().Select(kv => kv.Key))
                {
                    OrderedSet<ConfigItemId> referredIds = referringLibrary.GetReferencesOrNullFromItem(referringKey);
                    if (referredIds == null)
                        continue;

                    ConfigItemId referringId = new ConfigItemId(referringLibrary.ItemType, referringKey);

                    referredIds = referredIds.Select(CanonicalizedConfigItemId).ToOrderedSet();
                    baselineReferences.Add(referringId, referredIds);
                }
            }

            DeduplicationStorage.BaselineReferences = baselineReferences;
            DeduplicationStorage.BaselineReverseReferences = ComputeReverseReferences(baselineReferences);
        }

        void DuplicateItemsDueToReferences()
        {
            if (StorageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(DuplicateItemsDueToReferences)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{StorageMode}");

            // \note DeduplicationStorage can be null when the config was populated by means other
            //       than the normal config import path, such as during config building, or in tests.
            //       In that case we also assume we're dealing with only a single config with no deduplication,
            //       and can skip this.
            if (DeduplicationStorage == null)
                return;

            // Get the reverse references for this specialization.
            // We do not construct an explicit dictionary for the final reverse references, but instead use a lookup function
            // (see CreateReverseReferencesLookup for details).
            Func<ConfigItemId, OrderedSet<ConfigItemId>> tryGetReverseReferencesForItem = CreateReverseReferencesLookup();

            // Which items need to be duplicated because of references?
            //
            // The following kinds of references do *not* cause duplication:
            // - A baseline instance refers to an instance which (in this specialization) comes from the baseline
            // - A patch instance refers to an instance which    (in this specialization) comes from the baseline
            // - A patch instance refers to an instance which    (in this specialization) comes from that same patch
            //
            // Conversely, the following references do cause duplication:
            // - A baseline instance refers to an instance which (in this specialization) comes from a patch
            // - A patch instance refers to an instance which    (in this specialization) comes from a different patch
            // With such references, the referring item must be duplicated.
            //
            // Additionally, the duplication must be propagated according to the references.

            // First, find the references which directly cause duplication, according to the above rules.
            // We don't need to loop through the whole config, but only need to look at the patched items
            // (direct and indirect both), and for each of those, the items referred from and referring to
            // it, and consider whether that reference causes duplication.

            OrderedSet<ConfigItemId> rootItemsToDuplicate = new OrderedSet<ConfigItemId>();

            foreach (IGameConfigLibraryEntry library in GetLibraries())
            {
                IEnumerable<object> patchedKeys = library.EnumerateDirectlyPatchedKeys()
                                                  .Concat(library.EnumerateIndirectlyPatchedKeys());
                foreach (object key in patchedKeys)
                {
                    ConfigItemId patchedItemId = new ConfigItemId(library.ItemType, key);

                    OrderedSet<ConfigItemId> referredIds = library.GetReferencesOrNullFromItem(key);
                    if (referredIds != null)
                    {
                        foreach (ConfigItemId referredId in referredIds)
                            ConsiderReference(referringId: patchedItemId, referredId: CanonicalizedConfigItemId(referredId));
                    }

                    OrderedSet<ConfigItemId> referringIds = tryGetReverseReferencesForItem(patchedItemId);
                    if (referringIds != null)
                    {
                        foreach (ConfigItemId referringId in referringIds)
                            ConsiderReference(referringId: referringId, referredId: patchedItemId);
                    }
                }
            }

            void ConsiderReference(ConfigItemId referringId, ConfigItemId referredId)
            {
                IGameConfigLibraryEntry referredLibrary = GetLibrary(referredId);
                ConfigPatchIndex? referredDefiner = referredLibrary.GetItemDefinerPatchOrNullForBaseline(referredId.Key);

                IGameConfigLibraryEntry referringLibrary = GetLibrary(referringId);
                ConfigPatchIndex? referringDefiner = referringLibrary.GetItemDefinerPatchOrNullForBaseline(referringId.Key);

                bool baselineRefersToPatch = !referringDefiner.HasValue && referredDefiner.HasValue;
                bool patchRefersToDifferentPatch = referringDefiner.HasValue && referredDefiner.HasValue && referringDefiner.Value != referredDefiner.Value;

                if (baselineRefersToPatch || patchRefersToDifferentPatch)
                    rootItemsToDuplicate.Add(referringId);
            }

            // Then, propagate the duplication, and perform the actual duplication operations.

            OrderedSet<ConfigItemId> itemsToDuplicate = Util.ComputeReachableNodes(rootItemsToDuplicate, tryGetReverseReferencesForItem);

            IEnumerable<IGrouping<IGameConfigLibraryEntry, ConfigItemId>> itemsToDuplicatePerLibrary = itemsToDuplicate.GroupBy(GetLibrary);

            foreach (IGrouping<IGameConfigLibraryEntry, ConfigItemId> grouping in itemsToDuplicatePerLibrary)
            {
                IGameConfigLibraryEntry library = grouping.Key;
                IEnumerable<object> keysToDuplicate = grouping.Select(itemId => itemId.Key);
                library.DuplicateIndirectlyPatchedItems(keysToDuplicate);
            }
        }

        /// <summary>
        /// Returns a lookup method for reverse references: referredId -> referringIds .
        /// referringIds are the ids of the items which directly refer to referredId.
        /// referringIds are called the "reverse references" of referredId.
        /// </summary>
        Func<ConfigItemId, OrderedSet<ConfigItemId>> CreateReverseReferencesLookup()
        {
            if (StorageMode != GameConfigRuntimeStorageMode.Deduplicating)
                throw new MetaAssertException($"Didn't expect {nameof(CreateReverseReferencesLookup)} to get called when using {nameof(GameConfigRuntimeStorageMode)}.{StorageMode}");

            // We don't want to construct an actual dictionary or similar data structure for the reverse references,
            // because it would be a significant amount of work to do for each specialization.
            // Instead we reuse the pre-computed (by the baseline config instance) baselineReverseReferences
            // and just apply overrides on top of that. The overriding happens in the lookup function
            // that this method returns.

            // How to figure out the overrides for the reverse references, i.e. the reverse references
            // that in this specialization differ from the baseline's reverse references?
            // - An item X can have different reverse references than in the baseline
            //   if there is an item Y which has been patched such that Y refers to X
            //   in the baseline or in the patched version.
            // Therefore, the algorithm is:
            // 1. Take the set of items patched in this specialization. Let's call these the "patched items".
            // 2. Collect the set of items referred-to by the patched items both in the baseline and this specialization.
            //    Call this the "refreshable referreds" set. This is the set of items whose reverse references may differ
            //    from the baseline.
            // 3. Collect the set which consists of the patched items, plus the items which in the baseline refer to
            //    the "refreshable referreds" set.
            //    Call this set the "refreshable referrings" set. This is the set of items required to determine the
            //    reverse references of the "refreshable referreds" set.
            // 4. Establish overrides for the "refreshable referreds" items, based on the references from the
            //    "refreshable referrings" items.
            // 5. Now we have the data required to look up the reverse references of an item:
            //    If the item belongs to the "refreshable referreds" set, take its reverse references from the overrides
            //    established in step 4. Otherwise, take from the baseline reverse references.

            OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> baselineReferences = DeduplicationStorage.BaselineReferences;
            OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> baselineReverseReferences = DeduplicationStorage.BaselineReverseReferences;

            // 1. Take the set of items patched in this specialization.
            // Note that it's enough to consider just direct patching, as indirect patching cannot mutate reference ids.

            OrderedSet<ConfigItemId> directlyPatchedItemIds = new OrderedSet<ConfigItemId>();
            foreach (IGameConfigLibraryEntry library in GetLibraries())
            {
                foreach (object patchedKey in library.EnumerateDirectlyPatchedKeys())
                {
                    ConfigItemId patchedId = new ConfigItemId(library.ItemType, patchedKey);
                    directlyPatchedItemIds.Add(patchedId);
                }
            }

            // 2. Collect the set of items referred-to by the patched items both in the baseline and this specialization.

            OrderedSet<ConfigItemId> refreshableReferreds = new OrderedSet<ConfigItemId>();

            foreach (ConfigItemId patchedId in directlyPatchedItemIds)
            {
                // Collect items referred to by patchedId, in this specialization.
                IGameConfigLibraryEntry patchedLibrary = GetLibrary(patchedId);
                OrderedSet<ConfigItemId> patchedReferredIds = patchedLibrary.GetReferencesOrNullFromItem(patchedId.Key);
                if (patchedReferredIds != null)
                {
                    foreach (ConfigItemId id in patchedReferredIds)
                        refreshableReferreds.Add(CanonicalizedConfigItemId(id));
                }

                // Collect items referred to by patchedId, in the baseline.
                if (baselineReferences.TryGetValue(patchedId, out OrderedSet<ConfigItemId> baselineReferredIds))
                {
                    foreach (ConfigItemId id in baselineReferredIds)
                        refreshableReferreds.Add(CanonicalizedConfigItemId(id));
                }
            }

            // 3. Collect the set which consists of the patched items, plus the items which in the baseline refer to
            //    the "refreshable referreds" set.

            OrderedSet<ConfigItemId> refreshableReferrings = new OrderedSet<ConfigItemId>(
                // \note Start with the patched items.
                directlyPatchedItemIds);

            foreach (ConfigItemId referred in refreshableReferreds)
            {
                if (baselineReverseReferences.TryGetValue(referred, out OrderedSet<ConfigItemId> baselineRefreshableReferrings))
                {
                    foreach (ConfigItemId id in baselineRefreshableReferrings)
                        refreshableReferrings.Add(id);
                }
            }

            // 4. Establish overrides for the "refreshable referreds" items, based on the references from the
            //    "refreshable referrings" items.

            OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> overrideReverseReferences = new OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>>();
            // Initialize with empty sets of reverse-references.
            foreach (ConfigItemId id in refreshableReferreds)
                overrideReverseReferences.Add(id, new OrderedSet<ConfigItemId>());

            // For each item in `refreshableReferrings`, where it refers to an item in `refreshableReferreds`,
            // record it in `overrideReverseReferences`.
            // But note that referred items that are not in `refreshableReferreds` are ignored,
            // as we've determined that their reverse references remain the same as in baseline.
            foreach (ConfigItemId referringId in refreshableReferrings)
            {
                IGameConfigLibraryEntry referringLibrary = GetLibrary(referringId);
                OrderedSet<ConfigItemId> referredIds = referringLibrary.GetReferencesOrNullFromItem(referringId.Key);
                if (referredIds == null)
                    continue;

                foreach (ConfigItemId referredIdNonCanonicalized in referredIds)
                {
                    ConfigItemId referredId = CanonicalizedConfigItemId(referredIdNonCanonicalized);
                    if (!refreshableReferreds.Contains(referredId))
                        continue;

                    overrideReverseReferences[referredId].Add(referringId);
                }
            }

            // 5. Create the lookup function.

            return (ConfigItemId referredId) =>
            {
                if (overrideReverseReferences.TryGetValue(referredId, out OrderedSet<ConfigItemId> overrideReferringIds))
                    return overrideReferringIds;
                else if (baselineReverseReferences.TryGetValue(referredId, out OrderedSet<ConfigItemId> baselineReferringIds))
                    return baselineReferringIds;
                else
                    return null;
            };
        }

        static OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> ComputeReverseReferences(OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> references)
        {
            OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> reverseReferences = new OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>>();

            foreach ((ConfigItemId referringId, OrderedSet<ConfigItemId> referredIds) in references)
            {
                foreach (ConfigItemId referredId in referredIds)
                {
                    if (!reverseReferences.TryGetValue(referredId, out OrderedSet<ConfigItemId> referringIds))
                    {
                        referringIds = new OrderedSet<ConfigItemId>();
                        reverseReferences.Add(referredId, referringIds);
                    }
                    referringIds.Add(referringId);
                }
            }

            return reverseReferences;
        }

        public ConfigItemId CanonicalizedConfigItemId(ConfigItemId id)
        {
            // _libraries has an entry for each item type that can be reference-resolved;
            // this includes non-concrete game config item types (e.g. InAppPurchaseInfoBase).
            // GetLibrary(X) is the same object as GetLibrary(Y) when the item type of X is an
            // ancestor of item type Y (or vice versa) and they have the same keys.
            IGameConfigLibraryEntry library = GetLibrary(id);

            // The canonical item type is the type actually being used as the item type of the library.
            // For example, this canonicalizes typeof(InAppPurchaseInfoBase) and typeof(InAppPurchaseInfo)
            // both to typeof(InAppPurchaseInfo).
            Type canonicalItemType = library.ItemType;

            // Canonicalize possible alias to the real key.
            object canonicalKey = library.TryResolveRealKeyFromAlias(id.Key, out object aliasResolvedKey)
                                  ? aliasResolvedKey
                                  : id.Key;

            return new ConfigItemId(canonicalItemType, canonicalKey);
        }

        IGameConfigLibraryEntry GetLibrary(ConfigItemId id)
        {
            List<IGameConfigLibraryEntry> typeLibraries = _libraries[id.ItemType];
            foreach (IGameConfigLibraryEntry library in typeLibraries)
            {
                if (library.TryResolveReference(id.Key) != null)
                    return library;
            }

            // \note This checks validity of MetaRefs. Would break later without this, with a less useful error message.
            throw new InvalidOperationException($"Failed to resolve config data reference to '{id.Key}' of type {id.ItemType.ToGenericTypeString()}");
        }

        void ResolveMetaRefs()
        {
            foreach ((GameConfigRepository.GameConfigEntryInfo entryInfo, IGameConfigEntry entry) in GetConfigEntries())
            {
                // \todo: add support for server config being able to contain references to shared config items?
                if (entryInfo.ResolveContainedMetaRefs)
                {
                    try
                    {
                        entry.ResolveMetaRefs(resolver: this);
                    }
                    catch (MetaRefResolveError ex)
                    {
                        ex.GameConfigEntryName = entryInfo.Name;
                        throw;
                    }
                }
            }
        }

        void CallEntryPostLoadHooks()
        {
            foreach ((GameConfigRepository.GameConfigEntryInfo _, IGameConfigEntry entry) in GetConfigEntries())
                entry.PostLoad();
        }

        /// <summary>
        /// Register concrete game config entries as implementations of SDK config interfaces.
        /// </summary>
        protected abstract void RegisterSDKIntegrations();

        /// <summary>
        /// Initialize derived data based on the loaded config entries.
        /// </summary>
        protected virtual void OnLoaded()
        {
        }

        /// <summary>
        /// A version of <see cref="OnLoaded"/> that is called when this config instance is not a
        /// normal config instance that will be accessed at runtime, but an instance that is
        /// created for the purpose of constructing the deduplication resources in a
        /// <see cref="GameConfigImportResources"/>.
        /// This can be usually left empty, unless the user code wants to do custom deduplication.
        /// </summary>
        protected virtual void OnLoadedWhenConstructingDeduplicationStorage()
        {
        }

        /// <summary>
        /// Validate that all the data is valid, especially references between the various libraries.
        /// </summary>
        protected virtual void Validate()
        {
        }

        /// <summary>
        /// Validation that is run only when the config is built, as opposed to every time the config is loaded.
        /// Intended for more expensive validation.
        /// </summary>
        public virtual void BuildTimeValidate(GameConfigValidationResult validationResult)
        {
            foreach ((GameConfigRepository.GameConfigEntryInfo _, IGameConfigEntry entry) in GetConfigEntries())
                entry.BuildTimeValidate();
        }

        IEnumerable<(GameConfigRepository.GameConfigEntryInfo EntryInfo, IGameConfigEntry Entry)> GetConfigEntries()
        {
            return GameConfigRepository.Instance.GetGameConfigTypeInfo(GetType())
                .Entries.Values
                .Select(entryInfo =>
                {
                    IGameConfigEntry entry = (IGameConfigEntry)entryInfo.MemberInfo.GetDataMemberGetValueOnDeclaringType()(this);
                    return (entryInfo, entry);
                });
        }

        IEnumerable<IGameConfigLibraryEntry> GetLibraries()
        {
            return GetConfigEntries().Select(x => x.Entry).OfType<IGameConfigLibraryEntry>();
        }

        // Finds all registered GameConfigEntry libraries based on the contained element info type.
        IEnumerable<(GameConfigRepository.GameConfigEntryInfo EntryInfo, IGameConfigEntry Entry)> FindLibrariesByItemType<TInfo>() where TInfo : class
        {
            return GetConfigEntries().Where(
                x =>
                {
                    Type memberType = x.EntryInfo.MemberInfo.GetDataMemberType();
                    if (!memberType.IsGameConfigLibrary())
                        return false;
                    Type infoType = memberType.GenericTypeArguments[1];
                    return infoType.IsDerivedFrom<TInfo>();
                });
        }

        protected IGameConfigLibrary<TKey, TInfo> RegisterIntegration<TKey, TInfo>(string entryName, IGameConfigLibrary<TKey, TInfo> stub) where TInfo : class, IGameConfigData<TKey>
        {
            if (!GameConfigRepository.Instance.GetGameConfigTypeInfo(GetType()).Entries.TryGetValue(entryName, out GameConfigRepository.GameConfigEntryInfo entry))
            {
                // Check that there are no config entries for the TInfo type
                IEnumerable<string> librariesByItemType = FindLibrariesByItemType<TInfo>().Select(x => x.EntryInfo.Name);
                if (librariesByItemType.Any())
                    throw new InvalidOperationException($"{GetType()} doesn't have config entry '{entryName}' but libraries with item type {typeof(TInfo)} found: {string.Join(',', librariesByItemType)}");
                return stub;
            }

            Type memberType = entry.MemberInfo.GetDataMemberType();
            MetaDebug.Assert(memberType.IsGameConfigLibrary(), $"{GetType()} entry {entryName} is not of type {typeof(GameConfigLibrary<,>)}");
            MetaDebug.Assert(memberType.GenericTypeArguments[1].IsDerivedFrom<TInfo>(), $"{GetType()} library {entryName} must have {typeof(TInfo)} item type");

            IGameConfigLibrary<TKey, TInfo> libraryInstance = (IGameConfigLibrary<TKey, TInfo>)entry.MemberInfo.GetDataMemberGetValueOnDeclaringType()(this);
            if (libraryInstance == null)
                throw new InvalidOperationException($"{GetType()} entry {entryName} is null");

            return libraryInstance;
        }

        #region IGameConfigDataResolver

        object IGameConfigDataResolver.TryResolveReference(Type type, object id)
        {
            if (_libraries.TryGetValue(type, out List<IGameConfigLibraryEntry> typeLibraries))
            {
                foreach (IGameConfigLibraryEntry library in typeLibraries)
                {
                    object item = library.TryResolveReference(id);
                    if (item != null)
                        return item;
                }
            }

            return null;
        }

        #endregion
    }

    public static class GameConfigUtil
    {
        public static OrderedDictionary<TKey, TValue> ConvertToOrderedDictionary<TKey, TValue>(IEnumerable<TValue> items) where TValue : class, IGameConfigData<TKey>, new()
        {
            // Manually convert to OrderedDictionary<> for better error messages
            OrderedDictionary<TKey, TValue> dict = new OrderedDictionary<TKey, TValue>(capacity: items.Count());
            foreach (TValue item in items)
            {
                if (item is null)
                    throw new InvalidOperationException($"Imported a null item in GameConfigLibrary<{typeof(TKey).Name}, {typeof(TValue).Name}>");

                TKey key = item.ConfigKey;

                if (key == null) // Can't use 'is null' with older c# since TKey might be non-nullable.
                    throw new InvalidOperationException($"Imported an item with ConfigKey==null in GameConfigLibrary<{typeof(TKey).Name}, {typeof(TValue).Name}>");

                if (MetaSerializerTypeRegistry.TryGetTypeSpec(typeof(TValue), out MetaSerializableType infoTypeSpec)
                    && infoTypeSpec.ConfigNullSentinelKey != null
                    && key.Equals(infoTypeSpec.ConfigNullSentinelKey))
                {
                    throw new InvalidOperationException($"Imported an item with ConfigKey '{key}' in GameConfigLibrary<{typeof(TKey).Name}, {typeof(TValue).Name}>. This key is equal to the ConfigNullSentinelKey of {typeof(TValue).Name} and is reserved for representing null config references.");
                }

                if (!dict.AddIfAbsent(key, item))
                    throw new InvalidOperationException($"Imported an item with non-unique key: {key} in GameConfigLibrary<{typeof(TKey).Name}, {typeof(TValue).Name}>");
            }

            return dict;
        }

        // Binary import/parse

        public static OrderedDictionary<TKey, TValue> ImportBinaryLibraryItems<TKey, TValue>(ConfigArchive archive, string fileName) where TValue : class, IGameConfigData<TKey>, new()
        {
            using (IOReader reader = archive.ReadEntry(fileName))
            {
                IReadOnlyList<TValue> items = MetaSerialization.DeserializeTableTagged<TValue>(reader, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null, maxCollectionSizeOverride: int.MaxValue);
                return ConvertToOrderedDictionary<TKey, TValue>(items);
            }
        }

        public static void ImportBinaryLibraryAliases<TKey, TValue>(GameConfigLibrary<TKey, TValue> library, ConfigArchive archive, string fileName) where TValue : class, IGameConfigData<TKey>, new()
        {
            using (IOReader reader = archive.ReadEntry(fileName))
            {
                GameConfigLibraryAliasTable<TKey> aliases = MetaSerialization.DeserializeTagged<GameConfigLibraryAliasTable<TKey>>(reader, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                foreach (var alias in aliases.Values)
                {
                    library.RegisterAlias(alias.Key, alias.Value);
                }
            }
        }

        public static T ImportBinaryKeyValueStructure<T>(ConfigArchive archive, string fileName) where T : GameConfigKeyValue, new()
        {
            using (IOReader reader = archive.ReadEntry(fileName))
                return MetaSerialization.DeserializeTagged<T>(reader, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
        }

        // Csv import/parse

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public static OrderedDictionary<TKey, TValue> ImportCsvLibraryItems<TKey, TValue>(CsvParseOptions parseOptions, ConfigArchive archive, string fileName) where TValue : class, IGameConfigData<TKey>, new()
        {
            using (Stream input = archive.ReadEntryStream(fileName))
                return ParseCsvLibraryItems<TKey, TValue>(parseOptions, fileName, input);
        }

        public static OrderedDictionary<TKey, TValue> ParseCsvLibraryItems<TKey, TValue>(CsvParseOptions parseOptions, string fileName, Stream input) where TValue : class, IGameConfigData<TKey>, new()
        {
            using (CsvStream reader = new CsvStream(fileName, input))
            {
                TValue[] infos = CsvReader.ReadStructArray<TValue>(reader, parseOptions);
                return ConvertToOrderedDictionary<TKey, TValue>(infos);
            }
        }

        public static T ImportCsvKeyValueStructure<T>(ConfigArchive archive, string fileName) where T : GameConfigKeyValue, new()
        {
            using (Stream input = archive.ReadEntryStream(fileName))
                return ParseCsvKeyValueStructure<T>(fileName, input);
        }

        public static T ParseCsvKeyValueStructure<T>(string fileName, Stream input) where T : GameConfigKeyValue, new()
        {
            using (CsvStream reader = new CsvStream(fileName, input))
                return CsvReader.ReadStruct<T>(reader);
        }

        // SpreadsheetContent parse

        public static T ParseKeyValueStructure<T>(SpreadsheetContent sheet) where T : GameConfigKeyValue, new()
        {
            // \todo [petri] huge kludge to convert to .csv first -- refactor to parse SpreadsheetContent directly
            byte[] encoded = Encoding.UTF8.GetBytes(sheet.ToString());
            using (MemoryStream stream = new MemoryStream(encoded))
            using (CsvStream reader = new CsvStream(sheet.Name, stream))
                return CsvReader.ReadStruct<T>(reader);
        }
#endif // METAPLAY_LEGACY_GAMECONFIG_PIPELINE

        // Json import/parse

        public static OrderedDictionary<TKey, TValue> ImportJsonLibraryItems<TKey, TValue>(ConfigArchive archive, string fileName) where TValue : class, IGameConfigData<TKey>, new()
        {
            using (Stream stream = archive.ReadEntryStream(fileName))
                return ParseJsonLibraryItems<TKey, TValue>(fileName, stream);
        }

        public static OrderedDictionary<TKey, TValue> ParseJsonLibraryItems<TKey, TValue>(string fileName, Stream input) where TValue : class, IGameConfigData<TKey>, new()
        {
            JsonSerializer serializer = JsonSerialization.CreateSerializer(
                new JsonSerialization.Options(JsonSerialization.Options.DefaultOptions)
                {
                    TraverseGameConfigDataType = typeof(TValue),
                });

            List<TValue> list = JsonSerialization.Deserialize<List<TValue>>(input, serializer);
            return ConvertToOrderedDictionary<TKey, TValue>(list);
        }

        // Export

        public static byte[] ExportBinaryLibrary<TKey, TInfo>(GameConfigLibrary<TKey, TInfo> library) where TInfo : class, IGameConfigData<TKey>, new() =>
            MetaSerialization.SerializeTableTagged(library.Values.ToList(), MetaSerializationFlags.IncludeAll, logicVersion: null, maxCollectionSizeOverride: int.MaxValue);

        public static byte[] ExportBinaryLibraryAliases<TKey, TInfo>(GameConfigLibrary<TKey, TInfo> library) where TInfo : class, IGameConfigData<TKey>, new()
        {
            if (!library.GetAliases().Any())
                return null;
            GameConfigLibraryAliasTable<TKey> table = new GameConfigLibraryAliasTable<TKey>();
            table.Values = new OrderedDictionary<TKey, TKey>(library.GetAliases().Select(x => new KeyValuePair<TKey, TKey>(x.RealKey, x.Alias)));
            return MetaSerialization.SerializeTagged(table, MetaSerializationFlags.IncludeAll, null);
        }

        public static byte[] ExportBinaryKeyValueStructure<TKeyValue>(TKeyValue config) where TKeyValue : GameConfigKeyValue =>
            MetaSerialization.SerializeTagged(config, MetaSerializationFlags.IncludeAll, logicVersion: null);

        public static byte[] EncodeRawSheet(SpreadsheetContent sheet) =>
            Encoding.UTF8.GetBytes(sheet.ToString());

        // Reserialize

        public static byte[] ReserializeBinaryLibraryItems<TValue>(ConfigArchive archive, string fileName, MetaSerializationFlags flags) where TValue : IGameConfigData, new()
        {
            using (IOReader reader = archive.ReadEntry(fileName))
            {
                IReadOnlyList<TValue> items = MetaSerialization.DeserializeTableTagged<TValue>(reader, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null, maxCollectionSizeOverride: int.MaxValue);
                return MetaSerialization.SerializeTableTagged<TValue>(items, flags, logicVersion: null, maxCollectionSizeOverride: int.MaxValue);
            }
        }

        public static byte[] ReserializeBinaryKeyValueStructure<TKeyValue>(ConfigArchive archive, string fileName, MetaSerializationFlags flags) where TKeyValue : GameConfigKeyValue, new()
        {
            using (IOReader reader = archive.ReadEntry(fileName))
            {
                TKeyValue val = MetaSerialization.DeserializeTagged<TKeyValue>(reader, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                return MetaSerialization.SerializeTagged(val, flags, logicVersion: null);
            }
        }

        // FullGameConfig archive

        public static ConfigArchive GetSharedArchiveFromFullArchive(ConfigArchive fullArchive, List<string> filters = null)
            => ConfigArchive.FromBytes(fullArchive.GetEntryBytes("Shared.mpa"), filters);

        public static ConfigArchive GetServerArchiveFromFullArchive(ConfigArchive fullArchive, List<string> filters = null)
            => ConfigArchive.FromBytes(fullArchive.GetEntryBytes("Server.mpa"), filters);

        // Extracts SharedGameConfig archive from "full" game config archive and re-serializes it for client use, stripping
        // away server only data and optionally compressing the archive contents.
        public static (ContentHash, byte[]) GetSharedArchiveFromFullArchiveForClient(ConfigArchive fullArchive, CompressionAlgorithm compression = CompressionAlgorithm.None, int minSizeBeforeCompression = 32)
        {
            GameConfigRepository.GameConfigTypeInfo typeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(GameConfigRepository.Instance.SharedGameConfigType);

            // Extract shared archive
            ReadOnlyMemory<byte> sharedGameConfigBytes   = fullArchive.GetEntryByName("Shared.mpa").Bytes;
            ConfigArchive sharedGameConfigArchive = ConfigArchive.FromBytes(sharedGameConfigBytes);

            // Re-serialize archive for client
            IEnumerable<ConfigArchiveEntry> entriesForClient = typeInfo.Serialization.Reserialize(sharedGameConfigArchive, MetaSerializationFlags.SendOverNetwork);

            // Write out & compress
            return ConfigArchiveBuildUtility.ToBytes(sharedGameConfigArchive.CreatedAt, entriesForClient, compression, minSizeBeforeCompression);
        }

        public static GameConfigPatchEnvelope GetSharedPatchEnvelopeFromFullArchive(ConfigArchive fullArchive, PlayerExperimentId experimentId, ExperimentVariantId variantId)
            => GetPatchEnvelopeFromFullArchive(fullArchive, "SharedPatch", experimentId, variantId);

        public static GameConfigPatchEnvelope GetServerPatchEnvelopeFromFullArchive(ConfigArchive fullArchive, PlayerExperimentId experimentId, ExperimentVariantId variantId)
            => GetPatchEnvelopeFromFullArchive(fullArchive, "ServerPatch", experimentId, variantId);

        static GameConfigPatchEnvelope GetPatchEnvelopeFromFullArchive(ConfigArchive fullArchive, string prefix, PlayerExperimentId experimentId, ExperimentVariantId variantId)
        {
            string patchName = $"{prefix}.{experimentId}.{variantId}.mpp";

            if (fullArchive.ContainsEntryWithName(patchName))
            {
                using (IOReader reader = fullArchive.ReadEntry(patchName))
                    return GameConfigPatchEnvelope.Deserialize(reader);
            }
            else
                return GameConfigPatchEnvelope.Empty;
        }

        // Misc

        /// <summary>
        /// For each item in <paramref name="items"/>, collect the <see cref="ConfigItemId"/>s
        /// to which them item refer (via MetaRefs).
        /// If an item doesn't refer to any, then it is omitted from the result.
        /// </summary>
        public static OrderedDictionary<TKey, OrderedSet<ConfigItemId>> CollectReferencesFromItems<TKey, TInfo>(List<TInfo> items)
            where TInfo : class, IGameConfigData<TKey>
        {
            OrderedDictionary<TKey, OrderedSet<ConfigItemId>> referredIdsByItem = new OrderedDictionary<TKey, OrderedSet<ConfigItemId>>();

            TKey currentKey = default;
            OrderedSet<ConfigItemId> currentReferredIds = null;
            MetaSerialization.TraverseMetaRefsInTable(
                items,
                resolver: null,
                new MetaSerializationMetaRefTraversalParams(
                    visitTableTopLevelConfigItem: (ref MetaSerializationContext context, IGameConfigData item) =>
                    {
                        if (currentReferredIds != null)
                            referredIdsByItem.Add(currentKey, currentReferredIds);

                        currentKey = ((TInfo)item).ConfigKey;
                        currentReferredIds = null;
                    },
                    visitMetaRef: (ref MetaSerializationContext context, ref IMetaRef metaRef) =>
                    {
                        if (currentReferredIds == null)
                            currentReferredIds = new OrderedSet<ConfigItemId>();
                        currentReferredIds.Add(new ConfigItemId(metaRef.ItemType, metaRef.KeyObject));
                    },
                    isMutatingOperation: false));

            if (currentReferredIds != null)
                referredIdsByItem.Add(currentKey, currentReferredIds);

            return referredIdsByItem;
        }

        /// <summary>
        /// Import an unpatched SharedGameConfig from the archive.
        /// </summary>
        public static ISharedGameConfig ImportSharedConfig(ConfigArchive archive, bool isBuildingConfigs = false, bool isConfigBuildParent = false)
        {
            GameConfigImportParams importParams = GameConfigImportParams.CreateSoloUnpatched(GameConfigRepository.Instance.SharedGameConfigType, archive, isBuildingConfigs, isConfigBuildParent);
            return (ISharedGameConfig)GameConfigFactory.Instance.ImportGameConfig(importParams);
        }

        /// <summary>
        /// Import an unpatched ServerGameConfig from the archive.
        /// </summary>
        public static IServerGameConfig ImportServerConfig(ConfigArchive archive, bool isBuildingConfigs = false, bool isConfigBuildParent = false)
        {
            GameConfigImportParams importParams = GameConfigImportParams.CreateSoloUnpatched(GameConfigRepository.Instance.ServerGameConfigType, archive, isBuildingConfigs, isConfigBuildParent);
            return (IServerGameConfig)GameConfigFactory.Instance.ImportGameConfig(importParams);
        }
    }

    /// <summary>
    /// Game-specific parameters for the game config build
    /// </summary>
    [MetaSerializable, MetaReservedMembers(101, 200)]
    public abstract class GameConfigBuildParameters : IMetaIntegration<GameConfigBuildParameters>
    {
        public abstract bool IsIncremental { get; }

        [MetaMember(101), MetaValidateRequired, MetaFormLayoutOrderHint(-1)]
        public GameConfigBuildSource DefaultSource;
    }

    [MetaSerializableDerived(100)]
    public class DefaultGameConfigBuildParameters : GameConfigBuildParameters
    {
        public override bool IsIncremental => false;
    }

    // [2023/11/6] MetaMember 100 is blocked as it is used in legacy build reports in customer projects
    [MetaSerializable, MetaBlockedMembers(100)]
    public class GameConfigMetaData
    {
        public const int MaxReportMessages = 500;

        /// <summary>
        /// The content hash of the parent config in a partial gameconfig build
        /// </summary>
        [MetaMember(1)] public ContentHash ParentConfigHash { get; private set; }

        [MetaMember(2)] public GameConfigBuildParameters BuildParams { get; private set; }

        /// Currently unused
        [MetaMember(3)] public string BuildDescription { get; private set; }

        /// <summary>
        /// The id of the parent config in a partial gameconfig build
        /// </summary>
        [MetaMember(4)] public MetaGuid ParentConfigId { get;       private set; }

        /// <summary>
        /// Build report containing validation logs
        /// </summary>
        [MetaMember(5)] public GameConfigBuildReport BuildReport { get; private set; }

        /// <summary>
        /// Metadata associated to build sources (if applicable), keyed by build source property name.
        /// </summary>
        [MetaMember(6)] public OrderedDictionary<string, GameConfigBuildSourceMetadata> BuildSourceMetadata;

        /// <summary>
        /// Summary of the BuildReport
        /// </summary>
        [MetaMember(7)] public GameConfigBuildSummary BuildSummary { get; private set; }

        public GameConfigMetaData()
        {
        }

        public GameConfigMetaData(MetaGuid parentConfigId, ContentHash parentConfigHash, GameConfigBuildParameters buildParams, GameConfigBuildReport buildReport, OrderedDictionary<string, GameConfigBuildSourceMetadata> buildSourceMetadata, string buildDescription = null)
        {
            ParentConfigId      = parentConfigId;
            ParentConfigHash    = parentConfigHash;
            BuildParams         = buildParams;
            BuildDescription    = buildDescription;
            BuildSourceMetadata = buildSourceMetadata;

            BuildSummary = GameConfigBuildSummary.GenerateFromReport(buildReport, MaxReportMessages);
            BuildReport  = buildReport.TrimAndClone(MaxReportMessages);
        }

        GameConfigMetaData(
            MetaGuid parentConfigId,
            ContentHash parentConfigHash,
            GameConfigBuildParameters buildParams,
            GameConfigBuildReport buildReport,
            string buildDescription,
            GameConfigBuildSummary buildSummary)
        {
            ParentConfigId   = parentConfigId;
            ParentConfigHash = parentConfigHash;
            BuildParams      = buildParams;
            BuildReport      = buildReport;
            BuildDescription = buildDescription;
            BuildSummary     = buildSummary;
        }

        /// <summary>
        /// The build report is stripped for persisting in the metadataBytes column, this is done to prevent excess data being stored and transferred to the dashboard when it is not needed.
        /// </summary>
        public GameConfigMetaData StripBuildReportAndCloneForPersisting()
        {
            return new GameConfigMetaData(
                ParentConfigId,
                ParentConfigHash,
                BuildParams,
                null,
                BuildDescription,
                BuildSummary);
        }

        [MetaOnDeserialized]
        void OnDeserialized()
        {
            // This indicates that we're loading an older GameConfigMetaData, therefore we're trimming the data to prevent excessive dashboard load
            if (BuildReport != null && BuildSummary == null)
            {
                BuildSummary = GameConfigBuildSummary.GenerateFromReport(BuildReport, MaxReportMessages);
                BuildReport  = BuildReport.TrimAndClone(MaxReportMessages);
            }
        }

        public byte[] ToBytes()
        {
            return MetaSerialization.SerializeTagged(this, MetaSerializationFlags.IncludeAll, null);
        }

        public static GameConfigMetaData FromBytes(byte[] bytes)
        {
            using (IOReader reader = new IOReader(bytes))
                return Read(reader);
        }

        public static GameConfigMetaData Read(IOReader reader)
        {
            return MetaSerialization.DeserializeTagged<GameConfigMetaData>(reader, MetaSerializationFlags.IncludeAll, null, null);
        }

        public static GameConfigMetaData FromArchive(ConfigArchive archive)
        {
            if (!archive.ContainsEntryWithName("_metadata"))
                return null;
            return Read(archive.ReadEntry("_metadata"));
        }
    }

    [MetaSerializable]
    public struct GameConfigLibraryAliasTable<TKey>
    {
        /// <summary>
        /// Maps a real key to its alias.
        /// </summary>
        [MetaMember(1)] public OrderedDictionary<TKey, TKey> Values;
    }

    /// Introduce additional serializable types related to GameConfig classes
    [MetaSerializableTypeProvider]
    static class GameConfigSerializableTypeProvider
    {
        [MetaSerializableTypeGetter]
        public static IEnumerable<Type> GetSerializableTypes()
        {
            List<Type> gameConfigTypes = TypeScanner.GetAllTypes()
                                            .Where(type => type.IsGameConfigClass())
                                            .ToList();

            foreach (Type config in gameConfigTypes)
            {
                foreach (MemberInfo m in GameConfigTypeUtil.EnumerateLibraryMembersOfGameConfig(config))
                {
                    Type[] typeArguments = m.GetDataMemberType().GetGenericArguments();

                    // Register alias table type.
                    // \note Only mpc-format (i.e. binary-serializable) libraries get their aliases serialized.
                    bool isBinaryLibrary = m.GetCustomAttribute<GameConfigEntryAttribute>() != null
                                        && m.GetCustomAttribute<GameConfigEntryAttribute>().MpcFormat;
                    if (isBinaryLibrary)
                    {
                        Type keyType = typeArguments[0];
                        yield return typeof(GameConfigLibraryAliasTable<>).MakeGenericType(keyType);
                    }

                    // Register GameConfigDataContent<> type.
                    // Among other uses, this is used for cloning config items when doing
                    // MetaRef-based item duplication when constructing deduplicating config specializations.
                    Type itemType = typeArguments[1];
                    yield return typeof(GameConfigDataContent<>).MakeGenericType(itemType);
                }
            }
        }
    }

    public class PartialGameConfig
    {
        public IReadOnlyDictionary<string, IGameConfigEntry> SharedLibraries { get; private set; }
        public IReadOnlyDictionary<string, IGameConfigEntry> ServerLibraries { get; private set; }
        public Dictionary<string, Exception>                 Exceptions      { get; }
        public GameConfigMetaData                            MetaData        { get; }

        private PartialGameConfig(Dictionary<string, IGameConfigEntry> sharedLibraries, Dictionary<string, IGameConfigEntry> serverLibraries, Dictionary<string, Exception> exceptions, GameConfigMetaData metaData = null)
        {
            SharedLibraries = sharedLibraries;
            ServerLibraries = serverLibraries;
            MetaData        = metaData;
            Exceptions      = exceptions;
        }

        public static PartialGameConfig PartialFromArchive(ConfigArchive archive, bool omitPatchesInServerConfigExperiments = false, List<string> filters = null, bool includeMetadata = false)
        {
            ConfigArchive sharedArchive = GameConfigUtil.GetSharedArchiveFromFullArchive(archive, filters);
            ConfigArchive serverArchive = GameConfigUtil.GetServerArchiveFromFullArchive(archive, filters);

            Dictionary<string, Exception> exceptions = new Dictionary<string, Exception>();

            (Dictionary<string, IGameConfigEntry> entries, Dictionary<string, Exception> exceptions) sharedConfigLibraries = GameConfigRepository.Instance.GetGameConfigTypeInfo(GameConfigRepository.Instance.SharedGameConfigType).Serialization.ImportLibraries(sharedArchive, filters);
            (Dictionary<string, IGameConfigEntry> entries, Dictionary<string, Exception> exceptions) serverConfigLibraries = GameConfigRepository.Instance.GetGameConfigTypeInfo(GameConfigRepository.Instance.ServerGameConfigType).Serialization.ImportLibraries(serverArchive, filters);

            foreach ((string key, Exception ex) in sharedConfigLibraries.exceptions)
                exceptions.Add(key, ex);
            foreach ((string key, Exception ex) in serverConfigLibraries.exceptions)
                exceptions.Add(key, ex);

            if (!omitPatchesInServerConfigExperiments)
            {
                var playerExperimentsLibrary = serverConfigLibraries.entries.FirstOrDefault(x=>x.Value is GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo>);
                if (playerExperimentsLibrary.Value is GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo> lib)
                {
                    try
                    {
                        PopulatePatchesInServerConfigExperimentLibrary(archive, lib);
                    }
                    catch (Exception ex)
                    {
                        // ReSharper disable once CanSimplifyDictionaryLookupWithTryAdd
                        if (!exceptions.ContainsKey(ServerGameConfigBase.PlayerExperimentsEntryName))
                            exceptions.Add(ServerGameConfigBase.PlayerExperimentsEntryName, ex);
                    }
                }
            }

            GameConfigMetaData metaData = null;
            if (includeMetadata)
                metaData = GameConfigMetaData.FromArchive(archive);

            return new PartialGameConfig(sharedConfigLibraries.entries, serverConfigLibraries.entries, exceptions, metaData);
        }

        static void PopulatePatchesInServerConfigExperimentLibrary(ConfigArchive archive, GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo> playerExperiments)
        {
            // Populate serverConfig.PlayerExperiments[].Variants[].ConfigPatch based by deserializing patches stored in the archive.
            foreach (PlayerExperimentInfo experimentInfo in playerExperiments.Values)
            {
                foreach (PlayerExperimentInfo.Variant variantInfo in experimentInfo.Variants.Values)
                {
                    FullGameConfigPatch patch = FullGameConfigPatch.ForExperimentVariant(
                        archive,
                        experimentInfo.ExperimentId,
                        variantInfo.Id,
                        GameConfigRepository.Instance.SharedGameConfigType,
                        GameConfigRepository.Instance.ServerGameConfigType);

                    variantInfo.ConfigPatch = patch;
                }
            }
        }
    }

    public class FullGameConfig
    {
        // \todo [petri] add identifier / version
        public ISharedGameConfig SharedConfig { get; private set; }
        public IServerGameConfig ServerConfig { get; private set; }
        public GameConfigMetaData MetaData { get; private set; }

        FullGameConfig(ISharedGameConfig sharedConfig, IServerGameConfig serverConfig, GameConfigMetaData metaData)
        {
            SharedConfig = sharedConfig;
            ServerConfig = serverConfig;
            MetaData = metaData;
        }

        public T GetSharedConfig<T>() where T : ISharedGameConfig
        {
            return (T)SharedConfig;
        }
        public T GetServerConfig<T>() where T : IServerGameConfig
        {
            return (T)ServerConfig;
        }

        public static FullGameConfig MetaDataOnly(GameConfigMetaData metaData)
        {
            return new FullGameConfig(null, null, metaData);
        }

        /// <summary>
        /// Construct a specialization (possibly baseline, if <paramref name="activePatches"/> is empty)
        /// from the given resources.
        /// </summary>
        public static FullGameConfig CreateSpecialization(
            FullGameConfigImportResources resources,
            OrderedSet<ExperimentVariantPair> activePatches,
            bool omitPatchesInServerConfigExperiments = false,
            bool isBuildingConfigs = false)
        {
            GameConfigFactory factory = IntegrationRegistry.Get<GameConfigFactory>();
            ISharedGameConfig sharedConfig = (ISharedGameConfig)factory.ImportGameConfig(GameConfigImportParams.Specialization(resources.Shared, activePatches, isBuildingConfigs: isBuildingConfigs));
            IServerGameConfig serverConfig = (IServerGameConfig)factory.ImportGameConfig(GameConfigImportParams.Specialization(resources.Server, activePatches, isBuildingConfigs: isBuildingConfigs));
            FullGameConfig config = new FullGameConfig(sharedConfig, serverConfig, resources.MetaData);

            if (resources.Server.ConfigRuntimeStorageMode == GameConfigRuntimeStorageMode.Solo && !omitPatchesInServerConfigExperiments)
                PopulatePatchesInServerConfigExperiments(resources.FullArchive, sharedConfig, serverConfig);

            return config;
        }

        /// <summary>
        /// Shorthand helper for constructing <see cref="FullGameConfigImportResources"/> from <paramref name="archive"/> but with no patches,
        /// and then constructing a baseline config from it.
        /// <para>
        /// "Solo" signifies that a <see cref="FullGameConfigImportResources"/> instance is not being shared,
        /// but is being constructed solely for this <see cref="FullGameConfig"/>.
        /// </para>
        /// </summary>
        public static FullGameConfig CreateSoloUnpatched(ConfigArchive archive, bool isBuildingConfigs = false)
        {
            FullGameConfigImportResources resources = FullGameConfigImportResources.CreateWithoutPatches(archive, GameConfigRuntimeStorageMode.Solo);
            return CreateSpecialization(resources, new OrderedSet<ExperimentVariantPair>(), isBuildingConfigs);
        }

        public static void PopulatePatchesInServerConfigExperiments(ConfigArchive archive, ISharedGameConfig sharedConfig, IServerGameConfig serverConfig, CancellationToken ct = default)
        {
            // Populate serverConfig.PlayerExperiments[].Variants[].ConfigPatch based by deserializing patches stored in the archive.
            foreach (PlayerExperimentInfo experimentInfo in serverConfig.PlayerExperiments.Values)
            {
                foreach (PlayerExperimentInfo.Variant variantInfo in experimentInfo.Variants.Values)
                {
                    FullGameConfigPatch patch = FullGameConfigPatch.ForExperimentVariant(archive, experimentInfo.ExperimentId, variantInfo.Id, sharedConfig.GetType(), serverConfig.GetType());

                    variantInfo.ConfigPatch = patch;

                    // Resolve MetaRefs within the patch.
                    // These ConfigPatches get jsonified for the dashboard, and the json may include computed
                    // properties which may dereference the MetaRefs, which is why we want them resolved.
                    //
                    // \todo Note however that the resolved references are not guaranteed to be entirely correct:
                    //       Say the baseline config contains item X, and item Y which refers to X. Now say we
                    //       have a patch which modifies item X, and also has an item which refers to Y. Now,
                    //       the patch's reference to Y will end up referring to the baseline item Y, in which
                    //       the reference to X is still baseline. To be correct, the patch's reference to Y would
                    //       need to refer to an item Y in which the reference to X refers to the patched X.
                    //       A simple implementation of this would require cloning the baseline config, applying
                    //       the patch on that clone, and then resolving the references there. That is however
                    //       rather expensive.
                    // \todo Could we simply dictate that patches themselves always contain MetaRefs in unresolved form?
                    //       In that case, some other way is needed to tolerate exceptions from user-defined
                    //       property getters.
                    variantInfo.ConfigPatch.ResolveMetaRefs(
                        sharedResolver: new GameConfigPatchUtil.PatchBuildResolver(baseResolver: sharedConfig, initialEntryPatches: patch.SharedConfigPatch.EnumerateEntryPatches()),
                        serverResolver: new GameConfigPatchUtil.PatchBuildResolver(baseResolver: serverConfig, initialEntryPatches: patch.ServerConfigPatch.EnumerateEntryPatches()));

                    ct.ThrowIfCancellationRequested();
                }
            }
        }
    }

    [MetaSerializableTypeProvider]
    public class FullGameConfigPatch
    {
        public GameConfigPatch SharedConfigPatch { get; private set; }
        public GameConfigPatch ServerConfigPatch { get; private set; }

        public FullGameConfigPatch(GameConfigPatch sharedConfigPatch, GameConfigPatch serverConfigPatch)
        {
            SharedConfigPatch = sharedConfigPatch ?? throw new ArgumentNullException(nameof(sharedConfigPatch));
            ServerConfigPatch = serverConfigPatch ?? throw new ArgumentNullException(nameof(serverConfigPatch));
        }

        public bool IsEmpty => SharedConfigPatch.IsEmpty && ServerConfigPatch.IsEmpty;

        public static FullGameConfigPatch ForExperimentVariant(ConfigArchive fullArchive, PlayerExperimentId experimentId, ExperimentVariantId variantId, Type sharedConfigType, Type serverConfigType)
        {
            GameConfigPatchEnvelope sharedConfigPatchEnvelope = GameConfigUtil.GetSharedPatchEnvelopeFromFullArchive(fullArchive, experimentId, variantId);
            GameConfigPatchEnvelope serverConfigPatchEnvelope = GameConfigUtil.GetServerPatchEnvelopeFromFullArchive(fullArchive, experimentId, variantId);
            GameConfigPatch sharedConfigPatch = GameConfigPatch.DeserializeFromEnvelope(sharedConfigType, sharedConfigPatchEnvelope);
            GameConfigPatch serverConfigPatch = GameConfigPatch.DeserializeFromEnvelope(serverConfigType, serverConfigPatchEnvelope);

            return new FullGameConfigPatch(sharedConfigPatch, serverConfigPatch);
        }

        public void ResolveMetaRefs(IGameConfigDataResolver sharedResolver, IGameConfigDataResolver serverResolver)
        {
            SharedConfigPatch.ResolveMetaRefs(sharedResolver);
            ServerConfigPatch.ResolveMetaRefs(serverResolver);
        }

        [MetaSerializableTypeGetter]
        public static IEnumerable<Type> GetSerializableTypes()
        {
            if (GameConfigRepository.Instance == null)
                GameConfigRepository.InitializeSingleton();

            return GameConfigRepository.Instance.AllGameConfigTypes.SelectMany(x => GameConfigPatch.GetSerializableTypesForConfig(x.GameConfigType));
        }

        public FullGameConfigPatchEnvelope SerializeToEnvelope()
        {
            return new FullGameConfigPatchEnvelope(
                sharedConfigPatchEnvelope: SharedConfigPatch.SerializeToEnvelope(MetaSerializationFlags.IncludeAll),
                serverConfigPatchEnvelope: ServerConfigPatch.SerializeToEnvelope(MetaSerializationFlags.IncludeAll));
        }
    }

    public class FullGameConfigPatchEnvelope
    {
        public GameConfigPatchEnvelope SharedConfigPatchEnvelope { get; }
        public GameConfigPatchEnvelope ServerConfigPatchEnvelope { get; }

        public FullGameConfigPatchEnvelope(GameConfigPatchEnvelope sharedConfigPatchEnvelope, GameConfigPatchEnvelope serverConfigPatchEnvelope)
        {
            SharedConfigPatchEnvelope = sharedConfigPatchEnvelope ?? throw new ArgumentNullException(nameof(sharedConfigPatchEnvelope));
            ServerConfigPatchEnvelope = serverConfigPatchEnvelope ?? throw new ArgumentNullException(nameof(serverConfigPatchEnvelope));
        }
    }

    /// <summary>
    /// Factory for creating concrete GameConfig-related classes. The default implementation uses reflection to create deduced userland types,
    /// user can override this implementation in cases where needed by declaring a class that derives from this.
    /// </summary>
    public class GameConfigFactory : IMetaIntegrationSingleton<GameConfigFactory>
    {
        public virtual IGameConfig ImportGameConfig(GameConfigImportParams importParams)
        {
            IGameConfig config = CreateGameConfig(importParams.Resources.GameConfigType);
            config.Import(importParams);
            return config;
        }

        public virtual IGameConfig CreateGameConfig(Type gameConfigType)
        {
            return (IGameConfig)Activator.CreateInstance(gameConfigType);
        }

        // Convenience accessor for the integration GameConfigFactory
        public static GameConfigFactory Instance => IntegrationRegistry.Get<GameConfigFactory>();
    }
}
