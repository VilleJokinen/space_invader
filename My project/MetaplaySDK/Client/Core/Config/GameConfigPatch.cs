// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Represents a patch for an entire <see cref="GameConfigBase"/>-derived
    /// game config.
    /// </summary>
    public class GameConfigPatch
    {
        public Type ConfigType { get; private set; }

        public static IEnumerable<Type> GetSerializableTypesForConfig(Type configType)
        {
            IEnumerable<Type> entryPatchTypes =
                GetPatchableGameConfigEntryMembers(configType).Values
                .Select(entryMember => entryMember.GetDataMemberType())
                .Select(entryType => GameConfigPatchUtil.GetEntryPatchType(entryType));

            return entryPatchTypes
                .Append(typeof(Dictionary<string, byte[]>)); // used for top-level entry->patch lookup
        }

        Dictionary<string, GameConfigEntryPatch> _entryPatches = new Dictionary<string, GameConfigEntryPatch>();

        public IEnumerable<KeyValuePair<string, GameConfigEntryPatch>> EnumerateEntryPatches() => _entryPatches;

        public bool IsEmpty => _entryPatches.Count == 0;

        public bool TryGetSpecifiedEntryPatch(string name, out GameConfigEntryPatch value)
        {
            return _entryPatches.TryGetValue(name, out value);
        }

        public GameConfigPatch(Type configType, Dictionary<string, GameConfigEntryPatch> entryPatches)
        {
            ConfigType = configType;

            if (entryPatches == null)
                throw new ArgumentNullException(nameof(entryPatches));

            OrderedDictionary<string, MemberInfo> entryMemberInfos = GetPatchableGameConfigEntryMembers(ConfigType);

            // Validate the given entry patches against the config entries:
            // - all keys in entryPatches are found also among the config entries
            // - the patch types match the corresponding config entry types
            foreach ((string entryName, GameConfigEntryPatch entryPatch) in entryPatches)
            {
                if (!entryMemberInfos.TryGetValue(entryName, out MemberInfo entryMemberInfo))
                    throw new ArgumentException($"A patch was specified for an entry named '{entryName}', but {ConfigType.Name} does not contain an mpc-format entry with that name");
                if (!entryPatch.IsCompatibleWithEntryType(entryMemberInfo.GetDataMemberType()))
                    throw new ArgumentException($"Patch specified for entry '{entryName}' has type {entryPatch.GetType().ToGenericTypeString()}, which is not compatible with entry type {entryMemberInfo.GetDataMemberType().ToGenericTypeString()}");
            }

            _entryPatches = entryPatches;
        }

        public void ResolveMetaRefs(IGameConfigDataResolver resolver)
        {
            foreach (GameConfigEntryPatch patch in _entryPatches.Values)
                patch.ResolveMetaRefs(resolver);
        }

        /// <summary>
        /// Serializes the config patch in an ad-hoc format.
        /// The format is: a MetaSerialized dictionary from config entry names to MetaSerialized config entry patches.
        /// </summary>
        public byte[] Serialize(MetaSerializationFlags serializationFlags)
        {
            GameConfigPatchEnvelope envelope = SerializeToEnvelope(serializationFlags);
            return envelope.Serialize();
        }

        public GameConfigPatchEnvelope SerializeToEnvelope(MetaSerializationFlags serializationFlags)
        {
            Dictionary<string, byte[]> serializedEntryPatches = new Dictionary<string, byte[]>();

            foreach ((string entryName, MemberInfo _) in GetPatchableGameConfigEntryMembers(ConfigType))
            {
                if (!_entryPatches.TryGetValue(entryName, out GameConfigEntryPatch entryPatch))
                    continue;

                byte[] serializedEntryPatch = MetaSerialization.SerializeTagged(entryPatch.GetType(), entryPatch, serializationFlags, logicVersion: null);

                serializedEntryPatches.Add(entryName, serializedEntryPatch);
            }

            return new GameConfigPatchEnvelope(serializedEntryPatches);
        }

        /// <summary>
        /// Deserializes a config patch from a byte[] previously produced by <see cref="Serialize"/>.
        /// The current schema of the patch is assumed to be compatible with the schema at the time of
        /// serialization. In particular, the config entry names are assumed to be the same, and the
        /// GameConfigLibarys' key and info types as well as the GameConfigKeyValues' concrete types
        /// are assumed to be compatible.
        /// </summary>
        /// <param name="baseResolver">
        /// The resolver for resolving references to config items that are not defined or overridden
        /// in the patch itself. In other words, the base config onto which this patch is intended to
        /// be applied.
        /// </param>
        public static GameConfigPatch Deserialize(Type type, IOReader serialized)
        {
            GameConfigPatchEnvelope envelope = GameConfigPatchEnvelope.Deserialize(serialized);
            return DeserializeFromEnvelope(type, envelope);
        }

        public static GameConfigPatch DeserializeFromEnvelope(Type type, GameConfigPatchEnvelope envelope)
        {
            Dictionary<string, GameConfigEntryPatch> entryPatches = new Dictionary<string, GameConfigEntryPatch>();

            foreach ((string entryName, MemberInfo entryMemberInfo) in GetPatchableGameConfigEntryMembers(type))
            {
                Type entryPatchType = GameConfigPatchUtil.GetEntryPatchType(entryMemberInfo.GetDataMemberType());

                if (!envelope.TryDeserializeEntryPatch(entryName, entryPatchType: entryPatchType, out GameConfigEntryPatch entryPatch))
                    continue;

                entryPatches.Add(entryName, entryPatch);
            }

            return new GameConfigPatch(type, entryPatches);
        }

        static OrderedDictionary<string, MemberInfo> GetPatchableGameConfigEntryMembers(Type type)
        {
            return type
                .EnumerateInstanceDataMembersInUnspecifiedOrder()
                .Where(member => member.GetCustomAttribute<GameConfigEntryAttribute>() != null
                              && member.GetCustomAttribute<GameConfigEntryAttribute>().MpcFormat) // \note Only mpc-format (i.e. binary-serializable) entries are patchable.
                .ToOrderedDictionary(
                    keySelector:        member => member.GetCustomAttribute<GameConfigEntryAttribute>().EntryName,
                    elementSelector:    member => member);
        }
    }

    /// <summary>
    /// Partially-serialized <see cref="GameConfigPatch"/>.
    /// Specifically, contains the outer `entryName -> serializedEntryPatch`
    /// mapping in non-serialized form, but each entry patch is still in
    /// serialized form.
    ///
    /// This is analogous to <see cref="ConfigArchive"/> in that
    /// it has the outer structure as non-serialized, but contents are
    /// still serialized and furthermore it is not strongly typed
    /// for the specific config type (i.e. it's not GameConfigPatchEnvelope{TGameConfig}).
    ///
    /// Used as a helper in deserializing entry patches one at a time,
    /// which is useful when importing and patching a game config.
    /// By importing and patching a game config one entry at a time, we can
    /// do the following:
    ///   - for each entry:
    ///     - deserialize entry baseline (from ConfigArchive), using current
    ///       partially-imported config as resolver
    ///     - deserialize entry patch (from envelope), using current
    ///       partially-imported config as resolver
    ///     - apply patch onto entry baseline
    /// Whereas if we wanted to deserialize the patch as a whole first,
    /// we'd need to do the following:
    ///   - acquire a temporary baseline config (which, if we don't already have
    ///     such a baseline config handily available, may require deserializing it
    ///     from the ConfigArchive)
    ///   - deserialize the whole patch, using the above-acquired temporary
    ///     baseline config as resolver
    ///   - for each entry:
    ///     - deserialize entry baseline
    ///     - apply patch onto entry baseline
    /// Note the need for the temporary baseline config. That's what we avoid
    /// by deserializing entry patches one at a time at the same time as we
    /// import the baseline config entries.
    /// Note furthermore that applying a patch onto an already-materialized
    /// game config (as opposed applying during import from archive) is
    /// currently not an option if non-MetaSerializable configs are involved,
    /// due to reference re-resolving issues;
    /// grep #config-patch-application-during-import for more info.
    /// </summary>
    public class GameConfigPatchEnvelope
    {
        Dictionary<string, byte[]> _serializedEntryPatches = new Dictionary<string, byte[]>();

        public bool IsEmpty => _serializedEntryPatches.Count == 0;

        public static readonly GameConfigPatchEnvelope Empty = new GameConfigPatchEnvelope(new Dictionary<string, byte[]>());

        internal GameConfigPatchEnvelope(Dictionary<string, byte[]> serializedEntryPatches)
        {
            _serializedEntryPatches = serializedEntryPatches ?? throw new ArgumentNullException(nameof(serializedEntryPatches));
        }

        public IEnumerable<string> EnumeratePatchedEntryNames() => _serializedEntryPatches.Keys;

        /// <summary>
        /// Apply the contained patch, if any, for entry with name <paramref name="entryName"/>,
        /// in-place on the entry content <paramref name="entryContent"/>.
        /// (For an explanation of the "content" of an entry, see comment on <see cref="GameConfigEntryPatch.PatchContentDangerouslyInPlace"/>).
        /// The entry patch is freshly deserialized with <paramref name="resolver"/>.
        /// The patch must be of type <paramref name="entryPatchType"/>.
        /// If this envelope doesn't contain a patch for the entry, this does nothing.
        /// </summary>
        public void PatchEntryContentInPlace(object entryContent, string entryName, Type entryPatchType)
        {
            if (TryDeserializeEntryPatch(entryName, entryPatchType: entryPatchType, out GameConfigEntryPatch entryPatch))
                entryPatch.PatchContentDangerouslyInPlace(entryContent);
        }

        /// <summary>
        /// Deserialize the config entry patch with name <paramref name="entryName"/>, if any, with the given resolver.
        /// The patch must be of type <paramref name="entryPatchType"/>.
        /// If this envelope contains a patch for the entry, this returns <c>true</c> and assigns the patch to <paramref name="entryPatch"/>.
        /// Otherwise, this returns <c>false</c> and assigns <c>null</c> to <paramref name="entryPatch"/>.
        /// </summary>
        public bool TryDeserializeEntryPatch(string entryName, Type entryPatchType, out GameConfigEntryPatch entryPatch)
        {
            if (_serializedEntryPatches.TryGetValue(entryName, out byte[] serializedEntryPatch))
            {
                entryPatch = (GameConfigEntryPatch)MetaSerialization.DeserializeTagged(serializedEntryPatch, entryPatchType, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                return true;
            }
            else
            {
                entryPatch = null;
                return false;
            }
        }

        /// <summary>
        /// Deserialize an envelope from a byte[] which holds a fully-serialized representation
        /// of a <see cref="GameConfigPatch"/>.
        /// The given byte[] should have been produced by either
        /// <see cref="GameConfigPatch.Serialize"/>
        /// or <see cref="GameConfigPatchEnvelope.Serialize"/>
        /// (these two produce the same serialized format).
        /// </summary>
        public static GameConfigPatchEnvelope Deserialize(IOReader serializedEnvelope)
        {
            Dictionary<string, byte[]> serializedEntries = MetaSerialization.DeserializeTagged<Dictionary<string, byte[]>>(serializedEnvelope, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            return new GameConfigPatchEnvelope(serializedEntries);
        }

        /// <summary>
        /// Serialize into a serialized patch representation.
        /// Produces the same format as <see cref="GameConfigPatch.Serialize"/>,
        /// and may thus be later deserialized with
        /// either <see cref="GameConfigPatch.Deserialize(byte[], IGameConfigDataResolver)"/>
        /// or <see cref="GameConfigPatchEnvelope.Deserialize(byte[])"/>.
        /// </summary>
        public byte[] Serialize()
        {
            return MetaSerialization.SerializeTagged(_serializedEntryPatches, MetaSerializationFlags.IncludeAll, logicVersion: null);
        }
    }

    public static class GameConfigPatchUtil
    {
        // \todo [nuutti] These utilities currently rely on MakeGenericType and MakeGenericMethod
        //                to instantiate the patch types and helper methods at runtime.
        //                This is problematic with il2cpp when using value types as type parameters,
        //                which could happen with GameConfigLibrary key types.
        //                As a workaround, we'd need to statically instantiate those generic types
        //                and methods. That can be done in the GameConfigLibrary and GameConfigKeyValue
        //                classes themselves. Then MakeGeneric* for those types/methods can be used
        //                safely. Or to be yet more explicit, usage of MakeGeneric* can be avoided
        //                altogether.
        //                #config-patch

        public static GameConfigEntryPatch CreateNoOpEntryPatch(Type entryType)
        {
            if (entryType.HasGenericAncestor(typeof(GameConfigLibrary<,>)))
            {
                Type[] keyAndInfoTypes = entryType.GetGenericAncestorTypeArguments(typeof(GameConfigLibrary<,>));
                return CreateNoOpLibraryPatch(keyAndInfoTypes[0], keyAndInfoTypes[1]);
            }
            else if (entryType.IsDerivedFrom<GameConfigKeyValue>())
                return CreateNoOpStructurePatch(entryType);
            else
                throw new ArgumentException($"{entryType.ToGenericTypeString()} is not a supported kind of game config entry type (it is neither a GameConfigLibrary nor a GameConfigKeyValue).");
        }

        public static Type GetEntryPatchType(Type entryType)
        {
            if (entryType.HasGenericAncestor(typeof(GameConfigLibrary<,>)))
            {
                Type[] keyAndInfoTypes = entryType.GetGenericAncestorTypeArguments(typeof(GameConfigLibrary<,>));
                return typeof(GameConfigLibraryPatch<,>).MakeGenericType(keyAndInfoTypes);
            }
            else if (entryType.IsDerivedFrom<GameConfigKeyValue>())
                return typeof(GameConfigStructurePatch<>).MakeGenericType(entryType);
            else
                throw new ArgumentException($"{entryType.ToGenericTypeString()} is not a supported kind of game config entry type (it is neither a GameConfigLibrary nor a GameConfigKeyValue).");
        }

        static GameConfigPatchUtil()
        {
            MetaDebug.Assert(CreateNoOpLibraryPatchGenericMethod != null, $"{nameof(CreateNoOpLibraryPatchGenericMethod)} shouldn't be null");
            MetaDebug.Assert(CreateNoOpStructurePatchGenericMethod != null, $"{nameof(CreateNoOpStructurePatchGenericMethod)} shouldn't be null");
        }

        static MethodInfo CreateNoOpLibraryPatchGenericMethod   = typeof(GameConfigPatchUtil).GetMethod(nameof(CreateNoOpLibraryPatch), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null);
        static MethodInfo CreateNoOpStructurePatchGenericMethod = typeof(GameConfigPatchUtil).GetMethod(nameof(CreateNoOpStructurePatch), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null);

        public static GameConfigEntryPatch CreateNoOpLibraryPatch(Type keyType, Type infoType)
        {
            return (GameConfigEntryPatch)CreateNoOpLibraryPatchGenericMethod.MakeGenericMethod(keyType, infoType).InvokeWithoutWrappingError(obj: null, parameters: null);
        }

        public static GameConfigEntryPatch CreateNoOpStructurePatch(Type structureType)
        {
            return (GameConfigEntryPatch)CreateNoOpStructurePatchGenericMethod.MakeGenericMethod(structureType).InvokeWithoutWrappingError(obj: null, parameters: null);
        }

        public static GameConfigLibraryPatch<TKey, TInfo> CreateNoOpLibraryPatch<TKey, TInfo>()
            where TInfo : class, IGameConfigData<TKey>, new()
        {
            return new GameConfigLibraryPatch<TKey, TInfo>(
                replacedItems: Enumerable.Empty<TInfo>(),
                appendedItems: Enumerable.Empty<TInfo>());
        }

        public static GameConfigStructurePatch<TStructure> CreateNoOpStructurePatch<TStructure>()
            where TStructure : GameConfigKeyValue, new()
        {
            return new GameConfigStructurePatch<TStructure>(
                replacementValues:      new TStructure(),
                replacedMemberTagIds:   new OrderedSet<int>());
        }

        /// <summary>
        /// Helper for stacking entry patches on top of a base resolver.
        /// </summary>
        public class PatchBuildResolver : IGameConfigDataResolver
        {
            IGameConfigDataResolver                         _baseResolver;
            OrderedDictionary<string, GameConfigEntryPatch> _entryPatches = new OrderedDictionary<string, GameConfigEntryPatch>();

            public PatchBuildResolver(IGameConfigDataResolver baseResolver, IEnumerable<KeyValuePair<string, GameConfigEntryPatch>> initialEntryPatches = null)
            {
                _baseResolver = baseResolver ?? throw new ArgumentNullException(nameof(baseResolver));
                _entryPatches = new OrderedDictionary<string, GameConfigEntryPatch>(initialEntryPatches ?? Enumerable.Empty<KeyValuePair<string, GameConfigEntryPatch>>());
            }

            public void SetEntryPatch(string entryName, GameConfigEntryPatch entryPatch)
            {
                _entryPatches[entryName] = entryPatch ?? throw new ArgumentNullException(nameof(entryPatch));
            }

            public object TryResolveReference(Type type, object configKey)
            {
                foreach (GameConfigEntryPatch entryPatch in _entryPatches.Values)
                {
                    object resultFromPatch = entryPatch.TryResolveReference(type, configKey);
                    if (resultFromPatch != null)
                        return resultFromPatch;
                }

                return _baseResolver.TryResolveReference(type, configKey);
            }
        }
    }

    /// <summary>
    /// A patch for a single game config entry.
    /// A game config entry means a single <see cref="GameConfigEntryAttribute"/>-equipped
    /// <see cref="GameConfigLibrary{TKey, TInfo}"/> or <see cref="GameConfigKeyValue"/>
    /// member of a game config.
    /// </summary>
    public abstract class GameConfigEntryPatch
    {
        /// <summary>
        /// <para>
        /// WARNING: This method has some risky caveats, see explanation of "dangerously"
        /// below in this comment.
        /// </para>
        ///
        /// Applies this entry patch onto the given content of a config entry.
        /// The given content is modified in-place. The "content" of an
        /// entry depends on the kind of entry, and may or may not be the
        /// same as the entry itself:
        ///   - for a <see cref="GameConfigLibrary{TKey, TInfo}"/> entry, the
        ///     content is the <see cref="OrderedDictionary{TKey, TInfo}"/>
        ///     from which the library is going to be constructed
        ///   - for a <see cref="GameConfigKeyValue"/> entry, the content is
        ///     the entry itself
        /// The reason for modifying the "content" instead of the entry itself
        /// is that <see cref="GameConfigLibrary{TKey, TInfo}"/> does not support
        /// in-place modification.
        ///
        /// "Dangerously" here means:
        /// Objects in the entry patch itself are assigned directly into the config
        /// entry without re-resolving config data references, and without cloning
        /// the objects.
        /// This implies that if entry patch X is dangerously applied on a config entry,
        /// and then entry patch Y is dangerously applied on the same config entry,
        /// then if Y destructively mutates any items in the config entry which
        /// originate from patch X, **items in patch X may end up being mutated**
        /// (since they are the same objects).
        /// Furthermore, since config data references are not re-resolved,
        /// the references are whatever they were resolved to during deserialization
        /// of the entry patch.
        ///
        /// Altogether, this means that it's probably not a good idea to re-use
        /// entry patch instances that have been dangerously applied.
        /// Usage via <see cref="GameConfigPatchEnvelope.PatchEntryContentInPlace"/>
        /// is safe since a patch is deserialized, dangerously applied, and then
        /// discarded.
        /// </summary>
        internal abstract void PatchContentDangerouslyInPlace(object entryContent);

        internal abstract bool IsCompatibleWithEntryType(Type entryType);

        public abstract object TryResolveReference(Type type, object configKey);

        public abstract void ResolveMetaRefs(IGameConfigDataResolver resolver);
    }

    /// <summary>
    /// Static typing helper for <see cref="GameConfigEntryPatch"/>.
    /// </summary>
    [MetaSerializable]
    public abstract class GameConfigEntryPatch<TEntry, TEntryContent> : GameConfigEntryPatch
        where TEntry : IGameConfigEntry
    {
        internal sealed override void PatchContentDangerouslyInPlace(object entryContent)
        {
            PatchContentDangerouslyInPlace((TEntryContent)entryContent);
        }
        internal abstract void PatchContentDangerouslyInPlace(TEntryContent entryContent);

        internal override bool IsCompatibleWithEntryType(Type entryType)
        {
            return typeof(TEntry).IsAssignableFrom(entryType);
        }
    }

    /// <summary>
    /// A patch for a single <see cref="GameConfigLibrary{TKey, TInfo}"/>.
    /// </summary>
    [MetaSerializable]
    public class GameConfigLibraryPatch<TKey, TInfo> : GameConfigEntryPatch<GameConfigLibrary<TKey, TInfo>, OrderedDictionary<TKey, TInfo>>
        where TInfo : class, IGameConfigData<TKey>, new()
    {
        // \note _replacedItems and _appendedItems are not MetaMembers.
        //       Instead, they have MetaMember property counterparts whose accessors
        //       translate them to/from a type more convenient for serialization.

        OrderedDictionary<TKey, TInfo> _replacedItems = new OrderedDictionary<TKey, TInfo>();
        OrderedDictionary<TKey, TInfo> _appendedItems = new OrderedDictionary<TKey, TInfo>();

        [MetaMember(1), MaxCollectionSize(int.MaxValue)]
        List<GameConfigDataContent<TInfo>> _replacedItemsForSerialization
        {
            get => _replacedItems.Values.Select(info => new GameConfigDataContent<TInfo>(info)).ToList();
            set { _replacedItems = GameConfigUtil.ConvertToOrderedDictionary<TKey, TInfo>(value.Select(wrapper => wrapper.ConfigData)); }
        }

        [MetaMember(2), MaxCollectionSize(int.MaxValue)]
        List<GameConfigDataContent<TInfo>> _appendedItemsForSerialization
        {
            get => _appendedItems.Values.Select(info => new GameConfigDataContent<TInfo>(info)).ToList();
            set { _appendedItems = GameConfigUtil.ConvertToOrderedDictionary<TKey, TInfo>(value.Select(wrapper => wrapper.ConfigData)); }
        }

        public TInfo GetReplacedItem(TKey key) => _replacedItems[key];
        public TInfo GetAppendedItem(TKey key) => _appendedItems[key];

        public IEnumerable<KeyValuePair<TKey, TInfo>> EnumerateReplacedItems() => _replacedItems;
        public IEnumerable<KeyValuePair<TKey, TInfo>> EnumerateAppendedItems() => _appendedItems;

        GameConfigLibraryPatch(){ }
        public GameConfigLibraryPatch(IEnumerable<TInfo> replacedItems, IEnumerable<TInfo> appendedItems)
        {
            if (replacedItems == null)
                throw new ArgumentNullException(nameof(replacedItems));
            if (appendedItems == null)
                throw new ArgumentNullException(nameof(appendedItems));

            _replacedItems = GameConfigUtil.ConvertToOrderedDictionary<TKey, TInfo>(replacedItems);
            _appendedItems = GameConfigUtil.ConvertToOrderedDictionary<TKey, TInfo>(appendedItems);

            // Check for duplicate keys between _replacedItems and _appendedItems
            IEnumerable<TInfo> allAffectedItems = replacedItems.Concat(appendedItems);
            HashSet<TKey> seenKeys = new HashSet<TKey>();
            foreach (TInfo info in allAffectedItems)
            {
                bool added = seenKeys.Add(info.ConfigKey);
                if (!added)
                    throw new InvalidOperationException($"Duplicate key {info.ConfigKey}");
            }
        }

        public static GameConfigLibraryPatch<TKey, TInfo> CreateEmpty()
        {
            return new GameConfigLibraryPatch<TKey, TInfo>(
                replacedItems: Enumerable.Empty<TInfo>(),
                appendedItems: Enumerable.Empty<TInfo>());
        }

        internal override void PatchContentDangerouslyInPlace(OrderedDictionary<TKey, TInfo> libraryItems)
        {
            foreach ((TKey key, TInfo info) in _replacedItems)
            {
                if (!libraryItems.ContainsKey(key))
                    throw new InvalidOperationException($"Cannot replace item with key {key}, because it does not already exist");

                libraryItems[key] = info;
            }

            foreach ((TKey key, TInfo info) in _appendedItems)
            {
                // \note This allows duplicate appended items. Multiple patches are permitted to "append" the same item,
                //       in which case its value will be that of the last one. Its index in the config, however,
                //       is determined by where it was put by the first patch that appended it.
                //
                //       Previously, this would throw when trying to append an item multiple times. But that would
                //       easily only trigger late at runtime when a player actually happens to be assigned to such
                //       experiments. So it's likely better to be more tolerant.
                //
                //       It is still intended that an appended item must not exist already in the baseline.
                //       But that cannot be checked here because `libraryItems` may already have had some patches
                //       applied on it.

                libraryItems[key] = info;
            }
        }

        public override object TryResolveReference(Type type, object configKey)
        {
            if (type != typeof(TInfo))
                return null;

            TKey typedKey = (TKey)configKey;

            TInfo typedResult;
            if (_replacedItems.TryGetValue(typedKey, out typedResult)
             || _appendedItems.TryGetValue(typedKey, out typedResult))
            {
                return typedResult;
            }

            return null;
        }

        public override void ResolveMetaRefs(IGameConfigDataResolver resolver)
        {
            foreach (TInfo item in _replacedItems.Values.Concat(_appendedItems.Values))
            {
                GameConfigDataContent<TInfo> itemContent = new GameConfigDataContent<TInfo>(item);
                MetaSerialization.ResolveMetaRefs(ref itemContent, resolver);
                if (!ReferenceEquals(item, itemContent.ConfigData))
                    throw new MetaAssertException($"{nameof(MetaSerialization.ResolveMetaRefs)} shallow-copied a {typeof(TInfo).ToGenericTypeString()}; expected identity to be retained.");
            }
        }
    }

    /// <summary>
    /// A patch for a single <see cref="GameConfigKeyValue"/>.
    /// </summary>
    [MetaSerializable]
    public class GameConfigStructurePatch<TStructure> : GameConfigEntryPatch<TStructure, TStructure>
        where TStructure : GameConfigKeyValue, new()
    {
        [MetaMember(1)] TStructure      _replacementValues      = new TStructure();
        [MetaMember(2)] OrderedSet<int> _replacedMemberTagIds   = new OrderedSet<int>();

        GameConfigStructurePatch(){ }
        public GameConfigStructurePatch(TStructure replacementValues, OrderedSet<int> replacedMemberTagIds)
        {
            if (replacementValues == null)
                throw new ArgumentNullException(nameof(replacementValues));
            if (replacedMemberTagIds == null)
                throw new ArgumentNullException(nameof(replacedMemberTagIds));

            MetaSerializableType structureTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeof(TStructure));

            foreach (int memberTagId in _replacedMemberTagIds)
            {
                if (!structureTypeSpec.MemberByTagId.ContainsKey(memberTagId))
                    throw new InvalidOperationException($"{typeof(TStructure).ToGenericTypeString()} does not have a MetaMember with tag id {memberTagId}");
            }

            _replacementValues = replacementValues;
            _replacedMemberTagIds = replacedMemberTagIds;
        }

        public GameConfigStructurePatch(OrderedDictionary<int, object> replacedMembersByTagId)
            : this(CreateReplacementValuesStructureByTagIds(replacedMembersByTagId), replacedMemberTagIds: new OrderedSet<int>(replacedMembersByTagId.Keys))
        {
        }

        public GameConfigStructurePatch(OrderedDictionary<string, object> replacedMembersByName)
            : this(CreateReplacementValuesStructureByNames(replacedMembersByName), replacedMemberTagIds: new OrderedSet<int>(TagIdsToNames(replacedMembersByName.Keys)))
        {
        }

        public IEnumerable<(MetaSerializableMember, object)> EnumerateReplacedMemberValues()
        {
            return EnumerateReplacedMemberSpecs()
                .Select(memberSpec => (memberSpec, memberSpec.GetValue(_replacementValues)));
        }

        internal override void PatchContentDangerouslyInPlace(TStructure structure)
        {
            foreach (MetaSerializableMember memberSpec in EnumerateReplacedMemberSpecs())
                memberSpec.SetValue(structure, memberSpec.GetValue(_replacementValues));
        }

        IEnumerable<MetaSerializableMember> EnumerateReplacedMemberSpecs()
        {
            MetaSerializableType structureTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeof(TStructure));

            return _replacedMemberTagIds
                .Select(memberTagId =>
                {
                    // \note Tolerate unknown memberTagId in case members were removed since this patch was persisted
                    if (!structureTypeSpec.MemberByTagId.TryGetValue(memberTagId, out MetaSerializableMember memberSpec))
                        return null;

                    return memberSpec;
                })
                .Where(memberSpec => memberSpec != null);
        }

        static TStructure CreateReplacementValuesStructureByTagIds(OrderedDictionary<int, object> replacedMembersByTagId)
        {
            MetaSerializableType structureTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeof(TStructure));

            return CreateReplacementValuesStructure(replacedMembersByTagId.Select(kv =>
                {
                    (int memberTagId, object memberValue) = kv;
                    if (!structureTypeSpec.MemberByTagId.TryGetValue(memberTagId, out MetaSerializableMember memberSpec))
                        throw new InvalidOperationException($"No MetaMember with tag id {memberTagId} found in {typeof(TStructure).ToGenericTypeString()}");
                    return (memberSpec, memberValue);
                }));
        }

        static TStructure CreateReplacementValuesStructureByNames(OrderedDictionary<string, object> replacedMembersByName)
        {
            MetaSerializableType structureTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeof(TStructure));

            return CreateReplacementValuesStructure(replacedMembersByName.Select(kv =>
                {
                    (string memberName, object memberValue) = kv;
                    if (!structureTypeSpec.MemberByName.TryGetValue(memberName, out MetaSerializableMember memberSpec))
                        throw new InvalidOperationException($"No MetaMember with name {memberName} found in {typeof(TStructure).ToGenericTypeString()}");
                    return (memberSpec, memberValue);
                }));
        }

        static TStructure CreateReplacementValuesStructure(IEnumerable<(MetaSerializableMember, object)> replacedMembers)
        {
            TStructure replacementValues = new TStructure();

            foreach ((MetaSerializableMember memberSpec, object memberValue) in replacedMembers)
                memberSpec.SetValue(replacementValues, memberValue);

            return replacementValues;
        }

        static IEnumerable<int> TagIdsToNames(IEnumerable<string> names)
        {
            MetaSerializableType structureTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeof(TStructure));

            return names.Select(name => structureTypeSpec.MemberByName[name].TagId);
        }

        public override object TryResolveReference(Type type, object configKey)
        {
            return null;
        }

        public override void ResolveMetaRefs(IGameConfigDataResolver resolver)
        {
            MetaSerialization.ResolveMetaRefs(ref _replacementValues, resolver);
        }
    }
}
