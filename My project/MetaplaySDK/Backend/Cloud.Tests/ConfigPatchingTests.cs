using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static System.FormattableString;

namespace Cloud.Tests
{
    [TestFixture(GameConfigRuntimeStorageMode.Deduplicating)]
    [TestFixture(GameConfigRuntimeStorageMode.Solo)]
    public class ConfigPatchingTests
    {
        GameConfigRuntimeStorageMode _configRuntimeStorageMode;

        public ConfigPatchingTests(GameConfigRuntimeStorageMode configRuntimeStorageMode)
        {
            _configRuntimeStorageMode = configRuntimeStorageMode;
        }

        #region TestConfigInfo

        // An item type with a self-reference.

        [MetaSerializable]
        public class TestConfigId : StringId<TestConfigId> { }

        [MetaSerializable]
        public class TestConfigInfo : IGameConfigData<TestConfigId>, IGameConfigPostLoad
        {
            public TestConfigId ConfigKey => Id;

            [MetaMember(1)] public TestConfigId Id { get; set; }
            [MetaMember(2)] public int Value { get; set; }
            [MetaMember(3)] public MetaRef<TestConfigInfo> Reference { get; set; }
            [MetaMember(4)] public MetaRef<TestConfigInfo> Reference2 { get; set; }

            public TestConfigInfo() { }
            public TestConfigInfo(string id, int value, string reference = null, string reference2 = null)
            {
                Id = TestConfigId.FromString(id);
                Value = value;
                Reference = reference == null ? null : MetaRef<TestConfigInfo>.FromKey(TestConfigId.FromString(reference));
                Reference2 = reference2 == null ? null : MetaRef<TestConfigInfo>.FromKey(TestConfigId.FromString(reference2));
            }

            public override string ToString()
            {
                // \note debugId is for more easily identifying different instances
                //       which have the same contents (or otherwise stringify the same way).
                return Invariant($"({Id}, {Value}, ref={Reference?.ToString() ?? "null"}, ref2={Reference2?.ToString() ?? "null"}, debugId={RuntimeHelpers.GetHashCode(this)})");
            }

            public int NumPostLoaded = 0;

            void IGameConfigPostLoad.PostLoad()
            {
                NumPostLoaded++;
            }
        }

        #endregion

        #region Test(X|Y|Z)ConfigInfo

        // Item types with references between each other: X -> Y -> Z.
        // Used for testing references between different GameConfigLibrarys.

        [MetaSerializable]
        public class TestXConfigId : StringId<TestXConfigId> { }

        [MetaSerializable]
        public class TestXConfigInfo : IGameConfigData<TestXConfigId>
        {
            public TestXConfigId ConfigKey => Id;

            [MetaMember(1)] public TestXConfigId Id { get; set; }
            [MetaMember(2)] public int Value { get; set; }
            [MetaMember(3)] public MetaRef<TestYConfigInfo> ReferenceToY { get; set; }

            public TestXConfigInfo() { }
            public TestXConfigInfo(string id, int value, string referenceToY = null)
            {
                Id = TestXConfigId.FromString(id);
                Value = value;
                ReferenceToY = referenceToY == null ? null : MetaRef<TestYConfigInfo>.FromKey(TestYConfigId.FromString(referenceToY));
            }

            public override string ToString()
                => Invariant($"X({Id}, {Value}, ref={ReferenceToY?.ToString() ?? "null"}, debugId={RuntimeHelpers.GetHashCode(this)})");
        }

        // \note Unlike the other ids, this is a struct, intended to test value-typed keys.
        [MetaSerializable]
        public struct TestYConfigId : IEquatable<TestYConfigId>
        {
            [MetaMember(1)] string _str;

            TestYConfigId(string str) { _str = str; }
            public static TestYConfigId FromString(string str) => new TestYConfigId(str);

            public override bool Equals(object obj) => obj is TestYConfigId other && Equals(other);
            public bool Equals(TestYConfigId other) => _str == other._str;
            public override int GetHashCode() => _str?.GetHashCode() ?? 0;
            public static bool operator ==(TestYConfigId left, TestYConfigId right) => left.Equals(right);
            public static bool operator !=(TestYConfigId left, TestYConfigId right) => !(left == right);

            public override string ToString() => _str;
        }

        [MetaSerializable]
        public class TestYConfigInfo : IGameConfigData<TestYConfigId>
        {
            public TestYConfigId ConfigKey => Id;

            public static TestYConfigId ConfigNullSentinelKey => default;

            [MetaMember(1)] public TestYConfigId Id { get; set; }
            [MetaMember(2)] public int Value { get; set; }
            [MetaMember(3)] public MetaRef<TestZConfigInfo> ReferenceToZ { get; set; }

            public TestYConfigInfo() { }
            public TestYConfigInfo(string id, int value, string referenceToZ = null)
            {
                Id = TestYConfigId.FromString(id);
                Value = value;
                ReferenceToZ = referenceToZ == null ? null : MetaRef<TestZConfigInfo>.FromKey(TestZConfigId.FromString(referenceToZ));
            }

            public override string ToString()
                => Invariant($"Y({Id}, {Value}, ref={ReferenceToZ?.ToString() ?? "null"}, debugId={RuntimeHelpers.GetHashCode(this)})");
        }

        [MetaSerializable]
        public class TestZConfigId : StringId<TestZConfigId> { }

        [MetaSerializable]
        public class TestZConfigInfo : IGameConfigData<TestZConfigId>
        {
            public TestZConfigId ConfigKey => Id;

            [MetaMember(1)] public TestZConfigId Id { get; set; }
            [MetaMember(2)] public int Value { get; set; }

            public TestZConfigInfo() { }
            public TestZConfigInfo(string id, int value)
            {
                Id = TestZConfigId.FromString(id);
                Value = value;
            }

            public override string ToString()
                => Invariant($"Z({Id}, {Value}, debugId={RuntimeHelpers.GetHashCode(this)})");
        }

        #endregion

        #region TestBaseConfigInfo, TestDerivedConfigInfo

        // Abstract base config class and a concrete config class derived from it.
        // Used for testing references via base types.

        [MetaSerializable]
        public abstract class TestBaseConfigInfo : IGameConfigData<TestConfigId>
        {
            public TestConfigId ConfigKey => Id;

            [MetaMember(1)] public TestConfigId Id { get; set; }
            [MetaMember(2)] public int Value { get; set; }
            [MetaMember(3)] public MetaRef<TestBaseConfigInfo> ReferenceBase { get; set; }

            public TestBaseConfigInfo() { }
            public TestBaseConfigInfo(string id, int value, string referenceBase = null)
            {
                Id = TestConfigId.FromString(id);
                Value = value;
                ReferenceBase = referenceBase == null ? null : MetaRef<TestBaseConfigInfo>.FromKey(TestConfigId.FromString(referenceBase));
            }

            public override string ToString()
                => Invariant($"({Id}, {Value}, refBase={ReferenceBase?.ToString() ?? "null"}, debugId={RuntimeHelpers.GetHashCode(this)})");
        }

        [MetaSerializableDerived(1)]
        public class TestDerivedConfigInfo : TestBaseConfigInfo
        {
            [MetaMember(4)] public MetaRef<TestDerivedConfigInfo> ReferenceDerived { get; set; }

            public TestDerivedConfigInfo() { }
            public TestDerivedConfigInfo(string id, int value, string referenceBase = null, string referenceDerived = null)
                : base(id, value, referenceBase)
            {
                ReferenceDerived = referenceDerived == null ? null : MetaRef<TestDerivedConfigInfo>.FromKey(TestConfigId.FromString(referenceDerived));
            }

            public override string ToString()
                => Invariant($"({Id}, {Value}, refBase={ReferenceBase?.ToString() ?? "null"}, refDerived={ReferenceDerived?.ToString() ?? "null"}, debugId={RuntimeHelpers.GetHashCode(this)})");
        }

        #endregion

        #region TestConfigKeyValue

        [MetaSerializable]
        public class TestConfigKeyValue : GameConfigKeyValue<TestConfigKeyValue>
        {
            [MetaMember(1)] public MetaRef<TestConfigInfo> Reference;

            public TestConfigKeyValue() { }
            public TestConfigKeyValue(string reference)
            {
                Reference = reference == null ? null : MetaRef<TestConfigInfo>.FromKey(TestConfigId.FromString(reference));
            }
        }

        #endregion

        public class TestSharedGameConfig : SharedGameConfigBase
        {
            [GameConfigEntry("TestItems")]
            public GameConfigLibrary<TestConfigId, TestConfigInfo> TestItems { get; set; }

            [GameConfigEntry("TestXItems")]
            public GameConfigLibrary<TestXConfigId, TestXConfigInfo> TestXItems { get; set; }

            [GameConfigEntry("TestYItems")]
            public GameConfigLibrary<TestYConfigId, TestYConfigInfo> TestYItems { get; set; }

            [GameConfigEntry("TestZItems")]
            public GameConfigLibrary<TestZConfigId, TestZConfigInfo> TestZItems { get; set; }

            [GameConfigEntry("TestDerivedItems")]
            public GameConfigLibrary<TestConfigId, TestDerivedConfigInfo> TestDerivedItems { get; set; }

            [GameConfigEntry("TestKeyValue")]
            public TestConfigKeyValue TestKeyValue { get; set; }
        }

        #region ConfigCopy

        // Used for taking a copy of a config, for the purposes of later asserting that
        // the config wasn't unintentionally mutated.

        struct ConfigItemCopy
        {
            public readonly IGameConfigData Item;
            public readonly object          Id;
            public readonly int             Value;
            public readonly IMetaRef        MetaRef;
            public readonly IGameConfigData ReferredItem;
            public readonly IMetaRef        MetaRef2;
            public readonly IGameConfigData ReferredItem2;

            public ConfigItemCopy(IGameConfigData item, object id, int value, IMetaRef metaRef, IGameConfigData referredItem, IMetaRef metaRef2, IGameConfigData referredItem2)
            {
                Item = item;
                Id = id;
                Value = value;
                MetaRef = metaRef;
                ReferredItem = referredItem;
                MetaRef2 = metaRef2;
                ReferredItem2 = referredItem2;
            }
        }

        struct ConfigKeyValueCopy
        {
            public TestConfigKeyValue KeyValue;
            public IMetaRef MetaRef;
            public IGameConfigData ReferredItem;

            public ConfigKeyValueCopy(TestConfigKeyValue keyValue, IMetaRef metaRef, IGameConfigData referredItem)
            {
                KeyValue = keyValue;
                MetaRef = metaRef;
                ReferredItem = referredItem;
            }
        }

        struct ConfigCopy
        {
            public ConfigItemCopy[] Items;
            public ConfigKeyValueCopy KeyValue;

            public ConfigCopy(ConfigItemCopy[] items, ConfigKeyValueCopy keyValue)
            {
                Items = items;
                KeyValue = keyValue;
            }

            public static void AssertAreEqual(ConfigCopy configBefore, ConfigCopy configAfter)
            {
                const string refErrorMessage = "A MetaRef in an underlying deduplicated config item was modified by the creation of a patched config. Likely the patched config erroneously resolved the MetaRefs in the deduplicated item well.";

                Assert.AreEqual(configBefore.Items.Length, configAfter.Items.Length);
                foreach ((ConfigItemCopy before, ConfigItemCopy after) in configBefore.Items.Zip(configAfter.Items))
                {
                    Assert.AreSame(before.Item, after.Item);
                    Assert.AreEqual(before.Id,  after.Id);
                    if (before.Id.GetType().IsClass)
                        Assert.AreSame(before.Id, after.Id);

                    Assert.AreEqual(before.Value,           after.Value);
                    Assert.AreSame(before.ReferredItem,     after.ReferredItem, refErrorMessage);
                    Assert.AreSame(before.MetaRef,          after.MetaRef, refErrorMessage);
                    Assert.AreSame(before.ReferredItem2,    after.ReferredItem2, refErrorMessage);
                    Assert.AreSame(before.MetaRef2,         after.MetaRef2, refErrorMessage);
                }

                {
                    ConfigKeyValueCopy before = configBefore.KeyValue;
                    ConfigKeyValueCopy after = configAfter.KeyValue;
                    Assert.AreSame(before.ReferredItem,     after.ReferredItem, refErrorMessage);
                    Assert.AreSame(before.MetaRef,          after.MetaRef, refErrorMessage);
                    Assert.AreSame(before.KeyValue,         after.KeyValue);
                }
            }
        }

        ConfigItemCopy GetConfigItemCopy(TestConfigInfo item) => new ConfigItemCopy(item, item.Id, item.Value, item.Reference, item.Reference?.MaybeRef, item.Reference2, item.Reference2?.MaybeRef);
        ConfigItemCopy GetConfigItemCopy(TestXConfigInfo item) => new ConfigItemCopy(item, item.Id, item.Value, item.ReferenceToY, item.ReferenceToY?.MaybeRef, null, null);
        ConfigItemCopy GetConfigItemCopy(TestYConfigInfo item) => new ConfigItemCopy(item, item.Id, item.Value, item.ReferenceToZ, item.ReferenceToZ?.MaybeRef, null, null);
        ConfigItemCopy GetConfigItemCopy(TestZConfigInfo item) => new ConfigItemCopy(item, item.Id, item.Value, null, null, null, null);
        ConfigItemCopy GetConfigItemCopy(TestDerivedConfigInfo item) => new ConfigItemCopy(item, item.Id, item.Value, item.ReferenceBase, item.ReferenceBase?.MaybeRef, item.ReferenceDerived, item.ReferenceDerived?.MaybeRef);

        ConfigItemCopy[] GatherConfigItemCopies(TestSharedGameConfig config)
        {
            return config.TestItems.Values.Select(GetConfigItemCopy)
                   .Concat(config.TestXItems.Values.Select(GetConfigItemCopy))
                   .Concat(config.TestYItems.Values.Select(GetConfigItemCopy))
                   .Concat(config.TestZItems.Values.Select(GetConfigItemCopy))
                   .Concat(config.TestDerivedItems.Values.Select(GetConfigItemCopy))
                   .ToArray();
        }

        ConfigKeyValueCopy GatherConfigKeyValueCopy(TestSharedGameConfig config)
        {
            TestConfigKeyValue keyValue = config.TestKeyValue;
            return new ConfigKeyValueCopy(keyValue, keyValue.Reference, keyValue.Reference?.MaybeRef);
        }

        ConfigCopy GatherConfigCopy(TestSharedGameConfig config)
        {
            return new ConfigCopy(
                GatherConfigItemCopies(config),
                GatherConfigKeyValueCopy(config));
        }

        #endregion

        class ConfigSpec
        {
            public BaselineSpec Baseline;
            public PatchSpec[] Patches;
            public IAliasSpec[] Aliases;

            public ConfigSpec(BaselineSpec baseline, IEnumerable<PatchSpec> patches, IEnumerable<IAliasSpec> aliases = null)
            {
                Baseline = baseline;
                Patches = patches.ToArray();
                Aliases = aliases?.ToArray() ?? Array.Empty<IAliasSpec>();

                foreach ((PatchSpec patch, int index) in patches.ZipWithIndex())
                {
                    patch.PatchId = new ExperimentVariantPair(
                        PlayerExperimentId.FromString(Invariant($"e{index}")),
                        ExperimentVariantId.FromString("v"));
                }
            }
        }

        class BaselineSpec
        {
            public IEnumerable<IGameConfigData> Items;
            public TestConfigKeyValue KeyValue;

            public BaselineSpec(IEnumerable<IGameConfigData> items, TestConfigKeyValue keyValue = null)
            {
                Items = items;
                KeyValue = keyValue ?? new TestConfigKeyValue();
            }
        }

        class PatchSpec
        {
            public ExperimentVariantPair PatchId;

            public IEnumerable<IGameConfigData> Replacements;
            public IEnumerable<IGameConfigData> Additions;

            public OrderedDictionary<string, object> KeyValueMemberReplacementsByName;

            public bool IsEnabled;

            public PatchSpec(
                IEnumerable<IGameConfigData> replacements,
                IEnumerable<IGameConfigData> additions = null,
                OrderedDictionary<string, object> keyValueMemberReplacementsByName = null,
                bool isEnabled = true)
            {
                PatchId = default; // populated later, in ConfigSpec's constructor
                Replacements = replacements;
                Additions = additions ?? Enumerable.Empty<IGameConfigData>();
                KeyValueMemberReplacementsByName = keyValueMemberReplacementsByName ?? new OrderedDictionary<string, object>();
                IsEnabled = isEnabled;
            }

            public GameConfigLibraryPatch<TKey, TInfo> CreateLibraryPatchOrNullIfEmpty<TKey, TInfo>()
                where TInfo : class, IGameConfigData<TKey>, new()
            {
                IEnumerable<TInfo> replacements = Replacements.OfType<TInfo>();
                IEnumerable<TInfo> additions = Additions.OfType<TInfo>();

                if (!replacements.Any()
                 && !additions.Any())
                {
                    return null;
                }

                return new GameConfigLibraryPatch<TKey, TInfo>(
                    replacedItems: replacements,
                    appendedItems: additions);
            }

            public GameConfigStructurePatch<TestConfigKeyValue> CreateKeyValuePatchOrNullIfEmpty()
            {
                if (KeyValueMemberReplacementsByName.Count == 0)
                    return null;

                return new GameConfigStructurePatch<TestConfigKeyValue>(replacedMembersByName: KeyValueMemberReplacementsByName);
            }
        }

        interface IAliasSpec
        {
        }

        class AliasSpec<TKey> : IAliasSpec
        {
            public TKey RealKey;
            public TKey Alias;

            public AliasSpec(TKey realKey, TKey alias)
            {
                RealKey = realKey;
                Alias = alias;
            }
        }

        byte[] ExportLibraryItems<TInfo>(IEnumerable<TInfo> items)
        {
            return MetaSerialization.SerializeTableTagged(items.ToList(), MetaSerializationFlags.IncludeAll, logicVersion: null);
        }


        byte[] ExportBaselineLibrary<TInfo>(ConfigSpec spec)
        {
            return ExportLibraryItems(spec.Baseline.Items.OfType<TInfo>());
        }

        byte[] TryExportLibraryAliases<TKey>(IEnumerable<IAliasSpec> aliasSpecs)
        {
            OrderedDictionary<TKey, TKey> aliases = aliasSpecs.OfType<AliasSpec<TKey>>().ToOrderedDictionary(spec => spec.RealKey, spec => spec.Alias);
            if (!aliases.Any())
                return null;
            return MetaSerialization.SerializeTagged(new GameConfigLibraryAliasTable<TKey> { Values = aliases }, MetaSerializationFlags.IncludeAll, logicVersion: null);
        }

        byte[] ExportBaselineKeyValue(ConfigSpec spec)
        {
            return MetaSerialization.SerializeTagged(spec.Baseline.KeyValue, MetaSerializationFlags.IncludeAll, logicVersion: null);
        }

        ConfigArchive ExportBaseline(ConfigSpec spec)
        {
            List<ConfigArchiveEntry> entries = new List<ConfigArchiveEntry>();

            void AddLibrary<TKey, TInfo>(string entryName)
            {
                entries.Add(ConfigArchiveEntry.FromBlob($"{entryName}.mpc", ExportBaselineLibrary<TInfo>(spec)));

                byte[] aliasesMaybe = TryExportLibraryAliases<TKey>(spec.Aliases);
                if (aliasesMaybe != null)
                    entries.Add(ConfigArchiveEntry.FromBlob($"{entryName}.AliasTable.mpc", aliasesMaybe));
            }

            entries.Add(ConfigArchiveEntry.FromBlob("Languages.mpc", ExportLibraryItems(new LanguageInfo[] { new LanguageInfo(LanguageId.FromString("en"), "English") })));
            AddLibrary<TestConfigId, TestConfigInfo>("TestItems");
            AddLibrary<TestXConfigId, TestXConfigInfo>("TestXItems");
            AddLibrary<TestYConfigId, TestYConfigInfo>("TestYItems");
            AddLibrary<TestZConfigId, TestZConfigInfo>("TestZItems");
            AddLibrary<TestConfigId, TestDerivedConfigInfo>("TestDerivedItems");
            entries.Add(ConfigArchiveEntry.FromBlob("TestKeyValue.mpc", ExportBaselineKeyValue(spec)));

            return new ConfigArchive(
                createdAt: MetaTime.Epoch,
                entries: entries);
        }

        GameConfigPatchEnvelope ExportPatch(PatchSpec patchSpec)
        {
            Dictionary<string, GameConfigEntryPatch> entryPatches = new Dictionary<string, GameConfigEntryPatch>(
                new List<KeyValuePair<string, GameConfigEntryPatch>>
                {
                    new("TestItems", patchSpec.CreateLibraryPatchOrNullIfEmpty<TestConfigId, TestConfigInfo>()),
                    new("TestXItems", patchSpec.CreateLibraryPatchOrNullIfEmpty<TestXConfigId, TestXConfigInfo>()),
                    new("TestYItems", patchSpec.CreateLibraryPatchOrNullIfEmpty<TestYConfigId, TestYConfigInfo>()),
                    new("TestZItems", patchSpec.CreateLibraryPatchOrNullIfEmpty<TestZConfigId, TestZConfigInfo>()),
                    new("TestDerivedItems", patchSpec.CreateLibraryPatchOrNullIfEmpty<TestConfigId, TestDerivedConfigInfo>()),
                    new("TestKeyValue", patchSpec.CreateKeyValuePatchOrNullIfEmpty()),
                }
                .Where(kv => kv.Value != null));

            GameConfigPatch patch = new GameConfigPatch(
                typeof(TestSharedGameConfig),
                entryPatches);

            return patch.SerializeToEnvelope(MetaSerializationFlags.IncludeAll);
        }

        OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> ExportPatches(ConfigSpec spec)
        {
            return spec.Patches.ToOrderedDictionary(
                patch => patch.PatchId,
                ExportPatch);
        }

        TestSharedGameConfig PatchConfig(ConfigSpec spec)
        {
            ConfigArchive baselineArchive = ExportBaseline(spec);
            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> patches = ExportPatches(spec);

            // \note Here we set initializeBaselineAndSingleVariantSpecializations to false
            //       and invoke InitializeBaseline and InitializeSingleVariantSpecialization
            //       manually. Between those calls we take copies of the baseline and single-
            //       variant specializations, and assert that no changes were made to them
            //       by the construction of subsequent config instances.

            GameConfigImportResources importResources = GameConfigImportResources.Create(
                typeof(TestSharedGameConfig),
                baselineArchive,
                patches,
                _configRuntimeStorageMode,
                initializeBaselineAndSingleVariantSpecializations: false);

            OrderedSet<ExperimentVariantPair> activePatches =
                spec.Patches
                .Where(p => p.IsEnabled)
                .Select(p => p.PatchId)
                .ToOrderedSet();

            TestSharedGameConfig resultConfig;

            if (_configRuntimeStorageMode == GameConfigRuntimeStorageMode.Deduplicating)
            {
                // Keep copies of the baseline and single-patch specializations.
                // These are parallel lists: originalConfigCopies[X] is a copy of configs[X].
                List<TestSharedGameConfig> configs = new List<TestSharedGameConfig>();
                List<ConfigCopy> originalConfigCopies = new List<ConfigCopy>();

                void AddConfigCopy(TestSharedGameConfig config)
                {
                    configs.Add(config);
                    originalConfigCopies.Add(GatherConfigCopy(config));
                }

                void AssertUnderlyingConfigAreUnchanged()
                {
                    // Asserting that none of the so-far copied configs have changed.
                    foreach ((TestSharedGameConfig config, ConfigCopy originalCopy) in configs.Zip(originalConfigCopies))
                    {
                        ConfigCopy copyAfter = GatherConfigCopy(config);
                        ConfigCopy.AssertAreEqual(configBefore: originalCopy, configAfter: copyAfter);
                    }
                }

                // Construct baseline.
                {
                    importResources.InitializeDeduplicationBaseline();
                    TestSharedGameConfig baseline = (TestSharedGameConfig)importResources.DeduplicationBaseline;

                    AddConfigCopy(baseline);
                }

                // Construct each patch.
                foreach (PatchSpec patch in spec.Patches)
                {
                    importResources.InitializeDeduplicationSingleVariantSpecialization(patch.PatchId);
                    TestSharedGameConfig patched = (TestSharedGameConfig)importResources.DeduplicationSingleVariantSpecializations[patch.PatchId];

                    AssertUnderlyingConfigAreUnchanged();

                    AddConfigCopy(patched);
                }

                // Construct the result config specialization.
                resultConfig = (TestSharedGameConfig)GameConfigFactory.Instance.ImportGameConfig(GameConfigImportParams.Specialization(importResources, activePatches));
                AssertUnderlyingConfigAreUnchanged();
            }
            else
                resultConfig = (TestSharedGameConfig)GameConfigFactory.Instance.ImportGameConfig(GameConfigImportParams.Specialization(importResources, activePatches));

            AssertGeneralSanity(resultConfig);

            return resultConfig;
        }

        void AssertGeneralSanity(TestSharedGameConfig config)
        {
            AssertGeneralLibrarySanity(config.TestItems, TestConfigId.FromString("nonExistent"));
            AssertGeneralLibrarySanity(config.TestXItems, TestXConfigId.FromString("nonExistent"));
            AssertGeneralLibrarySanity(config.TestYItems, TestYConfigId.FromString("nonExistent"));
            AssertGeneralLibrarySanity(config.TestZItems, TestZConfigId.FromString("nonExistent"));
            AssertGeneralLibrarySanity(config.TestDerivedItems, TestConfigId.FromString("nonExistent"));

            foreach (TestConfigInfo testItem in config.TestItems.Values)
                Assert.AreEqual(1, testItem.NumPostLoaded);
        }

        void AssertGeneralLibrarySanity<TKey, TInfo>(GameConfigLibrary<TKey, TInfo> library, TKey nonExistentKey)
            where TInfo : class, IGameConfigData<TKey>, new()
        {
            List<TKey> keys = library.Keys.ToList();
            List<TInfo> values = library.Values.ToList();
            List<(TKey, TInfo)> keyValues = library.Select(kv => (kv.Key, kv.Value)).ToList();
            List<(TKey, TInfo)> keyValues2 = library.EnumerateAll().Select(kv => ((TKey)kv.Key, (TInfo)kv.Value)).ToList();

            Assert.AreEqual(keys.Count, library.Count);
            Assert.AreEqual(values.Count, library.Count);
            Assert.AreEqual(keyValues.Count, library.Count);
            Assert.AreEqual(keyValues2.Count, library.Count);

            foreach ((((TKey key, TInfo value), (TKey Key, TInfo Value) keyValue), (TKey Key, TInfo Value) keyValue2) in keys.Zip(values).Zip(keyValues).Zip(keyValues2))
            {
                Assert.AreEqual(key, keyValue.Key);
                Assert.AreEqual(key, keyValue2.Key);
                Assert.AreSame(value, keyValue.Value);
                Assert.AreSame(value, keyValue2.Value);

                Assert.IsTrue(library.ContainsKey(key));
                Assert.AreSame(value, library[key]);
                Assert.AreSame(value, library.GetInfoByKey(key));
                Assert.AreSame(value, library.GetValueOrDefault(key));
                Assert.IsTrue(library.TryGetValue(key, out TInfo tryGettedInfo));
                Assert.AreSame(value, tryGettedInfo);
            }

            {
                Assert.IsFalse(library.ContainsKey(nonExistentKey));
                Assert.Catch(() => { _ = library[nonExistentKey]; });
                Assert.Catch(() => library.GetInfoByKey(nonExistentKey));
                Assert.IsNull(library.GetValueOrDefault(nonExistentKey));
                Assert.IsFalse(library.TryGetValue(nonExistentKey, out TInfo tryGettedInfo));
                Assert.IsNull(tryGettedInfo);
            }
        }

        [Test]
        public void NoPatches()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                }),
                patches: new PatchSpec[]
                {
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
        }

        [Test]
        public void Basic()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(42, infos[1].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
        }

        [Test]
        public void BasicAddition()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem4", 4),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
        }

        [Test]
        public void MultiplePatches()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 111),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 222),
                        new TestConfigInfo("TestItem3", 333),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 444),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(444, infos[1].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(333, infos[2].Value);
        }

        [Test]
        public void MultiplePatchesWithSomeDisabled()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 111),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 222),
                        new TestConfigInfo("TestItem3", 333),
                    }),
                    new PatchSpec(
                        replacements: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem1", 1111),
                            new TestConfigInfo("TestItem3", 3333),
                        },
                        isEnabled: false),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 444),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(444, infos[1].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(333, infos[2].Value);
        }

        [Test]
        public void MultiplePatchesWithSomeEmpty()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 111),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 222),
                        new TestConfigInfo("TestItem3", 333),
                    }),
                    new PatchSpec(
                        replacements: new TestConfigInfo[]
                        {
                        }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 444),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(444, infos[1].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(333, infos[2].Value);
        }

        [Test]
        public void ModifyReferred()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(42, infos[1].Value);
            Assert.IsNull(infos[1].Reference);

            Assert.AreEqual(42, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void ModifyAliasReferred()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2Alias"),
                    new TestConfigInfo("TestItem2", 2),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 42),
                    }),
                },
                aliases: new IAliasSpec[]
                {
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem2"),
                        TestConfigId.FromString("TestItem2Alias")),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreSame(
                infos[1],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem2Alias")));

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(42, infos[1].Value);
            Assert.IsNull(infos[1].Reference);

            Assert.AreEqual(42, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void ModifyIndirectlyAliasReferredInvolvingMultiplePatches()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2Alias"),
                    new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    new TestConfigInfo("TestItem4", 4),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem3", 30, "TestItem4"),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem4", 40),
                    }),
                },
                aliases: new IAliasSpec[]
                {
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem2"),
                        TestConfigId.FromString("TestItem2Alias")),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreSame(
                infos[1],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem2Alias")));

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(30, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(40, infos[3].Value);
            Assert.IsNull(infos[3].Reference);
        }

        [Test]
        public void ModifySelfReferred()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem1"),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 42, "TestItem1"),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(42, infos[0].Value);
            Assert.AreSame(infos[0], infos[0].Reference.Ref);

            Assert.AreEqual(42, infos[0].Reference.Ref.Value);
            Assert.AreEqual(42, infos[0].Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyIndirectlyReferred()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    new TestConfigInfo("TestItem4", 4),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem4", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(42, infos[3].Value);
            Assert.IsNull(infos[3].Reference);

            Assert.AreEqual(2, infos[0].Reference.Ref.Value);
            Assert.AreEqual(3, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(42, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifySeveralIndirectlyReferred()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    new TestConfigInfo("TestItem4", 4, "TestItem5"),
                    new TestConfigInfo("TestItem5", 5, "TestItem6"),
                    new TestConfigInfo("TestItem6", 6),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem3", 333, "TestItem4"),
                        new TestConfigInfo("TestItem6", 666),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(333, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
            Assert.AreSame(infos[4], infos[3].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem5"), infos[4].Id);
            Assert.AreEqual(5, infos[4].Value);
            Assert.AreSame(infos[5], infos[4].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem6"), infos[5].Id);
            Assert.AreEqual(666, infos[5].Value);
            Assert.IsNull(infos[5].Reference);

            Assert.AreEqual(2, infos[0].Reference.Ref.Value);
            Assert.AreEqual(333, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(4, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(5, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(666, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferredToBySeveral()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("Outer1", 1, "Mid1"),
                    new TestConfigInfo("Outer2", 2, "Mid2"),
                    new TestConfigInfo("Outer3", 3, "Mid3"),
                    new TestConfigInfo("Mid1", 10, "Center"),
                    new TestConfigInfo("Mid2", 20, "Center"),
                    new TestConfigInfo("Mid3", 30, "Center"),
                    new TestConfigInfo("Center", 100),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("Center", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("Outer1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[3], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Outer2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[4], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Outer3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[5], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Mid1"), infos[3].Id);
            Assert.AreEqual(10, infos[3].Value);
            Assert.AreSame(infos[6], infos[3].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Mid2"), infos[4].Id);
            Assert.AreEqual(20, infos[4].Value);
            Assert.AreSame(infos[6], infos[4].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Mid3"), infos[5].Id);
            Assert.AreEqual(30, infos[5].Value);
            Assert.AreSame(infos[6], infos[5].Reference.Ref);

            Assert.AreEqual(42, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(42, infos[1].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(42, infos[2].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(42, infos[3].Reference.Ref.Value);
            Assert.AreEqual(42, infos[4].Reference.Ref.Value);
            Assert.AreEqual(42, infos[5].Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferredToByReferrerOfSeveral0()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "X1", "Y1"),
                    new TestConfigInfo("X1", 10, "X2"),
                    new TestConfigInfo("X2", 20),
                    new TestConfigInfo("Y1", 100, "Y2"),
                    new TestConfigInfo("Y2", 200),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("X2", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreSame(infos[3], infos[0].Reference2.Ref);
            Assert.AreEqual(TestConfigId.FromString("X1"), infos[1].Id);
            Assert.AreEqual(10, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("X2"), infos[2].Id);
            Assert.AreEqual(42, infos[2].Value);
            Assert.IsNull(infos[2].Reference);
            Assert.AreEqual(TestConfigId.FromString("Y1"), infos[3].Id);
            Assert.AreEqual(100, infos[3].Value);
            Assert.AreSame(infos[4], infos[3].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Y2"), infos[4].Id);
            Assert.AreEqual(200, infos[4].Value);
            Assert.IsNull(infos[4].Reference);

            Assert.AreEqual(10, infos[0].Reference.Ref.Value);
            Assert.AreEqual(42, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(100, infos[0].Reference2.Ref.Value);
            Assert.AreEqual(200, infos[0].Reference2.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferredToByReferrerOfSeveral1()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "X1", "Y1"),
                    new TestConfigInfo("X1", 10, "X2"),
                    new TestConfigInfo("X2", 20),
                    new TestConfigInfo("Y1", 100, "Y2"),
                    new TestConfigInfo("Y2", 200),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("Y2", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreSame(infos[3], infos[0].Reference2.Ref);
            Assert.AreEqual(TestConfigId.FromString("X1"), infos[1].Id);
            Assert.AreEqual(10, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("X2"), infos[2].Id);
            Assert.AreEqual(20, infos[2].Value);
            Assert.IsNull(infos[2].Reference);
            Assert.AreEqual(TestConfigId.FromString("Y1"), infos[3].Id);
            Assert.AreEqual(100, infos[3].Value);
            Assert.AreSame(infos[4], infos[3].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Y2"), infos[4].Id);
            Assert.AreEqual(42, infos[4].Value);
            Assert.IsNull(infos[4].Reference);

            Assert.AreEqual(10, infos[0].Reference.Ref.Value);
            Assert.AreEqual(20, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(100, infos[0].Reference2.Ref.Value);
            Assert.AreEqual(42, infos[0].Reference2.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferredToByReferrerOfSeveral2()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "X1", "Y1"),
                    new TestConfigInfo("X1", 10, "X2"),
                    new TestConfigInfo("X2", 20),
                    new TestConfigInfo("Y1", 100, "Y2"),
                    new TestConfigInfo("Y2", 200),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("X2", 42),
                        new TestConfigInfo("Y2", 123),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreSame(infos[3], infos[0].Reference2.Ref);
            Assert.AreEqual(TestConfigId.FromString("X1"), infos[1].Id);
            Assert.AreEqual(10, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("X2"), infos[2].Id);
            Assert.AreEqual(42, infos[2].Value);
            Assert.IsNull(infos[2].Reference);
            Assert.AreEqual(TestConfigId.FromString("Y1"), infos[3].Id);
            Assert.AreEqual(100, infos[3].Value);
            Assert.AreSame(infos[4], infos[3].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("Y2"), infos[4].Id);
            Assert.AreEqual(123, infos[4].Value);
            Assert.IsNull(infos[4].Reference);

            Assert.AreEqual(10, infos[0].Reference.Ref.Value);
            Assert.AreEqual(42, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(100, infos[0].Reference2.Ref.Value);
            Assert.AreEqual(123, infos[0].Reference2.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferredToByReferrerOfSeveral3()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("A", 1, "B"),
                    new TestConfigInfo("B", 2, "TestItem1"),
                    new TestConfigInfo("TestItem1", 3, "X", null),
                    new TestConfigInfo("X", 10),
                    new TestConfigInfo("Y", 20),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 3, "X", "Y"),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("X", 100),
                        new TestConfigInfo("Y", 200),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("A"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("B"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreSame(infos[4], infos[2].Reference2.Ref);
            Assert.AreEqual(TestConfigId.FromString("X"), infos[3].Id);
            Assert.AreEqual(100, infos[3].Value);
            Assert.AreEqual(TestConfigId.FromString("Y"), infos[4].Id);
            Assert.AreEqual(200, infos[4].Value);

            Assert.AreEqual(100, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(200, infos[0].Reference.Ref.Reference.Ref.Reference2.Ref.Value);
        }

        [Test]
        public void ModifyReference()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3"),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.IsNull(infos[1].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(3, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void ModifyAliasReference0()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3Alias"),
                    }),
                },
                aliases: new IAliasSpec[]
                {
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem3"),
                        TestConfigId.FromString("TestItem3Alias")),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreSame(
                infos[2],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem3Alias")));

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.IsNull(infos[1].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(3, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void ModifyAliasReference1()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2Alias"),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3"),
                    }),
                },
                aliases: new IAliasSpec[]
                {
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem2"),
                        TestConfigId.FromString("TestItem2Alias")),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreSame(
                infos[1],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem2Alias")));

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.IsNull(infos[1].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(3, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void ModifyAliasReference2()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2Alias"),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3Alias"),
                    }),
                },
                aliases: new IAliasSpec[]
                {
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem2"),
                        TestConfigId.FromString("TestItem2Alias")),
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem3"),
                        TestConfigId.FromString("TestItem3Alias")),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreSame(
                infos[1],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem2Alias")));
            Assert.AreSame(
                infos[2],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem3Alias")));

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.IsNull(infos[1].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(3, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void ModifyAliasReference3()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem3"),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3Alias"),
                    }),
                },
                aliases: new IAliasSpec[]
                {
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem3"),
                        TestConfigId.FromString("TestItem3Alias")),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreSame(
                infos[2],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem3Alias")));

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.IsNull(infos[1].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(3, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void ModifyAliasReference4()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem3Alias"),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3"),
                    }),
                },
                aliases: new IAliasSpec[]
                {
                    new AliasSpec<TestConfigId>(
                        TestConfigId.FromString("TestItem3"),
                        TestConfigId.FromString("TestItem3Alias")),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreSame(
                infos[2],
                config.ResolveReference(typeof(TestConfigInfo), TestConfigId.FromString("TestItem3Alias")));

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.IsNull(infos[1].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(3, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void AddReference()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3"),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.IsNull(infos[1].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(3, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void AddReferenceAndModifyReferred()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 20),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(20, infos[1].Value);
            Assert.IsNull(infos[1].Reference);

            Assert.AreEqual(20, infos[0].Reference.Ref.Value);
        }

        [Test]
        public void AddIndirectReference()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.IsNull(infos[2].Reference);

            Assert.AreEqual(2, infos[0].Reference.Ref.Value);
            Assert.AreEqual(3, infos[0].Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferenceCycle()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    new TestConfigInfo("TestItem4", 4, "TestItem1"),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem4"),
                        new TestConfigInfo("TestItem2", 2, "TestItem1"),
                        new TestConfigInfo("TestItem3", 3, "TestItem2"),
                        new TestConfigInfo("TestItem4", 4, "TestItem3"),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[3], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[0], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[1], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
            Assert.AreSame(infos[2], infos[3].Reference.Ref);

            Assert.AreEqual(4, infos[0].Reference.Ref.Value);
            Assert.AreEqual(3, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(2, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(1, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferenceToCreateCycle()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    new TestConfigInfo("TestItem4", 4),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem4", 4, "TestItem1"),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
            Assert.AreSame(infos[0], infos[3].Reference.Ref);

            Assert.AreEqual(2, infos[0].Reference.Ref.Value);
            Assert.AreEqual(3, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(4, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(1, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferenceToBreakCycle()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    new TestConfigInfo("TestItem4", 4, "TestItem1"),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem4", 4),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[2], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
            Assert.IsNull(infos[3].Reference);

            Assert.AreEqual(2, infos[0].Reference.Ref.Value);
            Assert.AreEqual(3, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(4, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void InsertItemToReferenceChainWithSinglePatch()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2, "TestItem3"),
                    new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    new TestConfigInfo("TestItem4", 4),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2", 2, "TestItem2.5"),
                        },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2.5", 42, "TestItem3"),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[4], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
            Assert.IsNull(infos[3].Reference);
            Assert.AreEqual(TestConfigId.FromString("TestItem2.5"), infos[4].Id);
            Assert.AreEqual(42, infos[4].Value);
            Assert.AreSame(infos[2], infos[4].Reference.Ref);

            Assert.AreEqual(2, infos[0].Reference.Ref.Value);
            Assert.AreEqual(42, infos[0].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(3, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(4, infos[0].Reference.Ref.Reference.Ref.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void MultiplePatchesWithReferences()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                    new TestConfigInfo("TestItem2", 2),
                    new TestConfigInfo("TestItem3", 3),
                    new TestConfigInfo("TestItem4", 4),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem2"),
                        new TestConfigInfo("TestItem3", 3, "TestItem4"),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 2, "TestItem1"),
                        new TestConfigInfo("TestItem4", 4, "TestItem3"),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem3"),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[0], infos[1].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
            Assert.AreSame(infos[2], infos[3].Reference.Ref);

            Assert.AreEqual(1, infos[1].Reference.Ref.Value);
            Assert.AreEqual(3, infos[1].Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(4, infos[1].Reference.Ref.Reference.Ref.Reference.Ref.Value);
            Assert.AreEqual(3, infos[1].Reference.Ref.Reference.Ref.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void MultiplePatchesModifyReferred()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1, "TestItem2"),
                    new TestConfigInfo("TestItem2", 2),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 20),
                    }),
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 200),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(200, infos[1].Value);
        }

        [Test]
        public void ModifyIndirectlyReferredAcrossLibraries()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestXConfigInfo("TestXItem", 1, "TestYItem"),

                    new TestYConfigInfo("TestYItem", 2, "TestZItem"),

                    new TestZConfigInfo("TestZItem", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestZConfigInfo("TestZItem", 42),
                    }),
                }));

            TestXConfigInfo[] xInfos = config.TestXItems.Values.ToArray();
            TestYConfigInfo[] yInfos = config.TestYItems.Values.ToArray();
            TestZConfigInfo[] zInfos = config.TestZItems.Values.ToArray();

            Assert.AreEqual(TestXConfigId.FromString("TestXItem"), xInfos[0].Id);
            Assert.AreEqual(1, xInfos[0].Value);
            Assert.AreSame(yInfos[0], xInfos[0].ReferenceToY.Ref);

            Assert.AreEqual(TestYConfigId.FromString("TestYItem"), yInfos[0].Id);
            Assert.AreEqual(2, yInfos[0].Value);
            Assert.AreSame(zInfos[0], yInfos[0].ReferenceToZ.Ref);

            Assert.AreEqual(TestZConfigId.FromString("TestZItem"), zInfos[0].Id);
            Assert.AreEqual(42, zInfos[0].Value);
        }

        [Test]
        public void ModifyReferenceAcrossLibrariesWithSinglePatch()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestXConfigInfo("TestXItem", 1, "TestYItem1"),

                    new TestYConfigInfo("TestYItem1", 2, "TestZItem1"),
                    new TestYConfigInfo("TestYItem2", 3, "TestZItem1"),

                    new TestZConfigInfo("TestZItem1", 4),
                    new TestZConfigInfo("TestZItem2", 5),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestXConfigInfo("TestXItem", 1, "TestYItem2"),

                        new TestYConfigInfo("TestYItem2", 3, "TestZItem2"),
                    }),
                }));

            TestXConfigInfo[] xInfos = config.TestXItems.Values.ToArray();
            TestYConfigInfo[] yInfos = config.TestYItems.Values.ToArray();
            TestZConfigInfo[] zInfos = config.TestZItems.Values.ToArray();

            Assert.AreEqual(TestXConfigId.FromString("TestXItem"), xInfos[0].Id);
            Assert.AreEqual(1, xInfos[0].Value);
            Assert.AreSame(yInfos[1], xInfos[0].ReferenceToY.Ref);

            Assert.AreEqual(TestYConfigId.FromString("TestYItem1"), yInfos[0].Id);
            Assert.AreEqual(2, yInfos[0].Value);
            Assert.AreSame(zInfos[0], yInfos[0].ReferenceToZ.Ref);
            Assert.AreEqual(TestYConfigId.FromString("TestYItem2"), yInfos[1].Id);
            Assert.AreEqual(3, yInfos[1].Value);
            Assert.AreSame(zInfos[1], yInfos[1].ReferenceToZ.Ref);

            Assert.AreEqual(TestZConfigId.FromString("TestZItem1"), zInfos[0].Id);
            Assert.AreEqual(4, zInfos[0].Value);
            Assert.AreEqual(TestZConfigId.FromString("TestZItem2"), zInfos[1].Id);
            Assert.AreEqual(5, zInfos[1].Value);
        }

        [Test]
        public void ModifyReferenceAcrossLibrariesWithMultiplePatches0()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestXConfigInfo("TestXItem", 1, "TestYItem1"),

                    new TestYConfigInfo("TestYItem1", 2, "TestZItem1"),
                    new TestYConfigInfo("TestYItem2", 3, "TestZItem1"),

                    new TestZConfigInfo("TestZItem1", 4),
                    new TestZConfigInfo("TestZItem2", 5),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestXConfigInfo("TestXItem", 1, "TestYItem2"),
                    }),
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestYConfigInfo("TestYItem2", 3, "TestZItem2"),
                    }),
                }));

            TestXConfigInfo[] xInfos = config.TestXItems.Values.ToArray();
            TestYConfigInfo[] yInfos = config.TestYItems.Values.ToArray();
            TestZConfigInfo[] zInfos = config.TestZItems.Values.ToArray();

            Assert.AreEqual(TestXConfigId.FromString("TestXItem"), xInfos[0].Id);
            Assert.AreEqual(1, xInfos[0].Value);
            Assert.AreSame(yInfos[1], xInfos[0].ReferenceToY.Ref);

            Assert.AreEqual(TestYConfigId.FromString("TestYItem1"), yInfos[0].Id);
            Assert.AreEqual(2, yInfos[0].Value);
            Assert.AreSame(zInfos[0], yInfos[0].ReferenceToZ.Ref);
            Assert.AreEqual(TestYConfigId.FromString("TestYItem2"), yInfos[1].Id);
            Assert.AreEqual(3, yInfos[1].Value);
            Assert.AreSame(zInfos[1], yInfos[1].ReferenceToZ.Ref);

            Assert.AreEqual(TestZConfigId.FromString("TestZItem1"), zInfos[0].Id);
            Assert.AreEqual(4, zInfos[0].Value);
            Assert.AreEqual(TestZConfigId.FromString("TestZItem2"), zInfos[1].Id);
            Assert.AreEqual(5, zInfos[1].Value);
        }

        [Test]
        public void ModifyReferenceAcrossLibrariesWithMultiplePatches1()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestXConfigInfo("TestXItem", 1, "TestYItem1"),

                    new TestYConfigInfo("TestYItem1", 2, "TestZItem1"),
                    new TestYConfigInfo("TestYItem2", 3, "TestZItem1"),

                    new TestZConfigInfo("TestZItem1", 4),
                    new TestZConfigInfo("TestZItem2", 5),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestYConfigInfo("TestYItem2", 3, "TestZItem2"),
                    }),
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestXConfigInfo("TestXItem", 1, "TestYItem2"),
                    }),
                }));

            TestXConfigInfo[] xInfos = config.TestXItems.Values.ToArray();
            TestYConfigInfo[] yInfos = config.TestYItems.Values.ToArray();
            TestZConfigInfo[] zInfos = config.TestZItems.Values.ToArray();

            Assert.AreEqual(TestXConfigId.FromString("TestXItem"), xInfos[0].Id);
            Assert.AreEqual(1, xInfos[0].Value);
            Assert.AreSame(yInfos[1], xInfos[0].ReferenceToY.Ref);

            Assert.AreEqual(TestYConfigId.FromString("TestYItem1"), yInfos[0].Id);
            Assert.AreEqual(2, yInfos[0].Value);
            Assert.AreSame(zInfos[0], yInfos[0].ReferenceToZ.Ref);
            Assert.AreEqual(TestYConfigId.FromString("TestYItem2"), yInfos[1].Id);
            Assert.AreEqual(3, yInfos[1].Value);
            Assert.AreSame(zInfos[1], yInfos[1].ReferenceToZ.Ref);

            Assert.AreEqual(TestZConfigId.FromString("TestZItem1"), zInfos[0].Id);
            Assert.AreEqual(4, zInfos[0].Value);
            Assert.AreEqual(TestZConfigId.FromString("TestZItem2"), zInfos[1].Id);
            Assert.AreEqual(5, zInfos[1].Value);
        }

        [Test]
        public void ModifyReferenceAcrossLibrariesWithSinglePatchInvolvingAddition()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestXConfigInfo("TestXItem", 1, "TestYItem"),

                    new TestYConfigInfo("TestYItem", 2, "TestZItem"),

                    new TestZConfigInfo("TestZItem", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new IGameConfigData[]
                        {
                            new TestXConfigInfo("TestXItem", 1, "TestYItemAdded"),
                        },
                        additions: new IGameConfigData[]
                        {
                            new TestYConfigInfo("TestYItemAdded", 222, "TestZItemAdded"),

                            new TestZConfigInfo("TestZItemAdded", 333),
                        }),
                }));

            TestXConfigInfo[] xInfos = config.TestXItems.Values.ToArray();
            TestYConfigInfo[] yInfos = config.TestYItems.Values.ToArray();
            TestZConfigInfo[] zInfos = config.TestZItems.Values.ToArray();

            Assert.AreEqual(TestXConfigId.FromString("TestXItem"), xInfos[0].Id);
            Assert.AreEqual(1, xInfos[0].Value);
            Assert.AreSame(yInfos[1], xInfos[0].ReferenceToY.Ref);

            Assert.AreEqual(TestYConfigId.FromString("TestYItem"), yInfos[0].Id);
            Assert.AreEqual(2, yInfos[0].Value);
            Assert.AreSame(zInfos[0], yInfos[0].ReferenceToZ.Ref);
            Assert.AreEqual(TestYConfigId.FromString("TestYItemAdded"), yInfos[1].Id);
            Assert.AreEqual(222, yInfos[1].Value);
            Assert.AreSame(zInfos[1], yInfos[1].ReferenceToZ.Ref);

            Assert.AreEqual(TestZConfigId.FromString("TestZItem"), zInfos[0].Id);
            Assert.AreEqual(3, zInfos[0].Value);
            Assert.AreEqual(TestZConfigId.FromString("TestZItemAdded"), zInfos[1].Id);
            Assert.AreEqual(333, zInfos[1].Value);
        }

        [Test]
        public void ModifyReferredToByAdded()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem1", 42),
                        },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2", 2, "TestItem1"),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(42, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[0], infos[1].Reference.Ref);
        }

        [Test]
        public void ModifyReferredToByAddedInvolvingMultiplePatches0()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem1", 42),
                        }),
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2", 2, "TestItem1"),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(42, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[0], infos[1].Reference.Ref);
        }

        [Test]
        public void ModifyReferredToByAddedInvolvingMultiplePatches1()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2", 2, "TestItem1"),
                        }),
                    new PatchSpec(
                        replacements: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem1", 42),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(42, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[0], infos[1].Reference.Ref);
        }

        [Test]
        public void MultiplePatchesAddSameId0()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem3", 3),
                            new TestConfigInfo("TestItem4", 4),
                        },
                        isEnabled: false),
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2", 2),
                            new TestConfigInfo("TestItem3", 3),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
        }

        [Test]
        public void MultiplePatchesAddSameId1()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem3", 3),
                            new TestConfigInfo("TestItem2", 2),
                        },
                        isEnabled: false),
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2", 2),
                            new TestConfigInfo("TestItem3", 3),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[2].Id);
        }

        [Test]
        public void MultiplePatchesAddSameId2()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new TestConfigInfo[]
                {
                    new TestConfigInfo("TestItem1", 1),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem3", 3),
                            new TestConfigInfo("TestItem2", 2),
                            new TestConfigInfo("TestItem4", 4),
                        }),
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        additions: new TestConfigInfo[]
                        {
                            new TestConfigInfo("TestItem2", 20),
                            new TestConfigInfo("TestItem3", 30),
                            new TestConfigInfo("TestItem5", 50),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(TestConfigId.FromString("TestItem3"), infos[1].Id);
            Assert.AreEqual(30, infos[1].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[2].Id);
            Assert.AreEqual(20, infos[2].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem4"), infos[3].Id);
            Assert.AreEqual(TestConfigId.FromString("TestItem5"), infos[4].Id);
        }

        [Test]
        public void ModifyReferredFromKeyValueConfig()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(
                    items: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1),
                    },
                    keyValue: new TestConfigKeyValue("TestItem1")),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(42, infos[0].Value);

            Assert.AreSame(infos[0], config.TestKeyValue.Reference.Ref);

            Assert.AreEqual(42, config.TestKeyValue.Reference.Ref.Value);
        }

        [Test]
        public void ModifyIndirectlyReferredFromKeyValueConfig()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(
                    items: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1, "TestItem2"),
                        new TestConfigInfo("TestItem2", 2),
                    },
                    keyValue: new TestConfigKeyValue("TestItem1")),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem2", 42),
                    }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(42, infos[1].Value);
            Assert.IsNull(infos[1].Reference);

            Assert.AreSame(infos[0], config.TestKeyValue.Reference.Ref);

            Assert.AreEqual(42, config.TestKeyValue.Reference.Ref.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferenceInKeyValueConfig0()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(
                    items: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1),
                    },
                    keyValue: new TestConfigKeyValue(reference: null)),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        keyValueMemberReplacementsByName:
                            new OrderedDictionary<string, object>
                            {
                                {
                                    nameof(TestConfigKeyValue.Reference),
                                    MetaRef<TestConfigInfo>.FromKey(TestConfigId.FromString("TestItem1"))
                                },
                            }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);

            Assert.AreSame(infos[0], config.TestKeyValue.Reference.Ref);

            Assert.AreEqual(1, config.TestKeyValue.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferenceInKeyValueConfig1()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(
                    items: new TestConfigInfo[]
                    {
                        new TestConfigInfo("TestItem1", 1),
                        new TestConfigInfo("TestItem2", 2),
                    },
                    keyValue: new TestConfigKeyValue("TestItem1")),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new TestConfigInfo[]{ },
                        keyValueMemberReplacementsByName:
                            new OrderedDictionary<string, object>
                            {
                                {
                                    nameof(TestConfigKeyValue.Reference),
                                    MetaRef<TestConfigInfo>.FromKey(TestConfigId.FromString("TestItem2"))
                                },
                            }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("TestItem1"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreEqual(TestConfigId.FromString("TestItem2"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);

            Assert.AreSame(infos[1], config.TestKeyValue.Reference.Ref);

            Assert.AreEqual(2, config.TestKeyValue.Reference.Ref.Value);
        }

        [Test]
        public void ModifyReferredButModifyReferenceToReferElsewhere()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestConfigInfo("A", 1, "B"),
                    new TestConfigInfo("B", 2),
                    new TestConfigInfo("C", 3),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(
                        replacements: new IGameConfigData[]
                        {
                            new TestConfigInfo("A", 1, "C"),
                        }),
                    new PatchSpec(
                        replacements: new IGameConfigData[]
                        {
                            new TestConfigInfo("B", 200),
                        }),
                }));

            TestConfigInfo[] infos = config.TestItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("A"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[2], infos[0].Reference.Ref);
            Assert.AreEqual(TestConfigId.FromString("B"), infos[1].Id);
            Assert.AreEqual(200, infos[1].Value);
            Assert.AreEqual(TestConfigId.FromString("C"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
        }

        [Test]
        public void ModifyReferredToViaBaseTypeReference()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestDerivedConfigInfo("A", 1, referenceBase: "X"),
                    new TestDerivedConfigInfo("X", 2),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestDerivedConfigInfo("X", 20),
                    }),
                }));

            TestDerivedConfigInfo[] infos = config.TestDerivedItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("A"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].ReferenceBase.Ref);
            Assert.AreEqual(TestConfigId.FromString("X"), infos[1].Id);
            Assert.AreEqual(20, infos[1].Value);
        }

        [Test]
        public void ModifyReferredToIndirectlyViaBaseAndDerivedTypeReferences()
        {
            TestSharedGameConfig config = PatchConfig(new ConfigSpec(
                baseline: new BaselineSpec(new IGameConfigData[]
                {
                    new TestDerivedConfigInfo("A", 1, referenceBase: "B"),
                    new TestDerivedConfigInfo("B", 2, referenceDerived: "X"),

                    new TestDerivedConfigInfo("C", 3, referenceBase: "D"),
                    new TestDerivedConfigInfo("D", 4, referenceDerived: "E"),
                    new TestDerivedConfigInfo("E", 5, referenceBase: "X"),

                    new TestDerivedConfigInfo("F", 6, referenceBase: "G"),
                    new TestDerivedConfigInfo("G", 7, referenceDerived: "H"),
                    new TestDerivedConfigInfo("H", 8, referenceBase: "I"),
                    new TestDerivedConfigInfo("I", 9, referenceDerived: "X"),

                    new TestDerivedConfigInfo("J", 10, referenceBase: "K"),
                    new TestDerivedConfigInfo("K", 11, referenceDerived: "L"),
                    new TestDerivedConfigInfo("L", 12, referenceBase: "M"),
                    new TestDerivedConfigInfo("M", 13, referenceDerived: "N"),
                    new TestDerivedConfigInfo("N", 14, referenceBase: "X"),

                    new TestDerivedConfigInfo("X", 99),
                }),
                patches: new PatchSpec[]
                {
                    new PatchSpec(replacements: new IGameConfigData[]
                    {
                        new TestDerivedConfigInfo("X", 990),
                    }),
                }));

            TestDerivedConfigInfo[] infos = config.TestDerivedItems.Values.ToArray();

            Assert.AreEqual(TestConfigId.FromString("A"), infos[0].Id);
            Assert.AreEqual(1, infos[0].Value);
            Assert.AreSame(infos[1], infos[0].ReferenceBase.Ref);
            Assert.AreEqual(TestConfigId.FromString("B"), infos[1].Id);
            Assert.AreEqual(2, infos[1].Value);
            Assert.AreSame(infos[14], infos[1].ReferenceDerived.Ref);

            Assert.AreEqual(TestConfigId.FromString("C"), infos[2].Id);
            Assert.AreEqual(3, infos[2].Value);
            Assert.AreSame(infos[3], infos[2].ReferenceBase.Ref);
            Assert.AreEqual(TestConfigId.FromString("D"), infos[3].Id);
            Assert.AreEqual(4, infos[3].Value);
            Assert.AreSame(infos[4], infos[3].ReferenceDerived.Ref);
            Assert.AreEqual(TestConfigId.FromString("E"), infos[4].Id);
            Assert.AreEqual(5, infos[4].Value);
            Assert.AreSame(infos[14], infos[4].ReferenceBase.Ref);

            Assert.AreEqual(TestConfigId.FromString("F"), infos[5].Id);
            Assert.AreEqual(6, infos[5].Value);
            Assert.AreSame(infos[6], infos[5].ReferenceBase.Ref);
            Assert.AreEqual(TestConfigId.FromString("G"), infos[6].Id);
            Assert.AreEqual(7, infos[6].Value);
            Assert.AreSame(infos[7], infos[6].ReferenceDerived.Ref);
            Assert.AreEqual(TestConfigId.FromString("H"), infos[7].Id);
            Assert.AreEqual(8, infos[7].Value);
            Assert.AreSame(infos[8], infos[7].ReferenceBase.Ref);
            Assert.AreEqual(TestConfigId.FromString("I"), infos[8].Id);
            Assert.AreEqual(9, infos[8].Value);
            Assert.AreSame(infos[14], infos[8].ReferenceDerived.Ref);

            Assert.AreEqual(TestConfigId.FromString("J"), infos[9].Id);
            Assert.AreEqual(10, infos[9].Value);
            Assert.AreSame(infos[10], infos[9].ReferenceBase.Ref);
            Assert.AreEqual(TestConfigId.FromString("K"), infos[10].Id);
            Assert.AreEqual(11, infos[10].Value);
            Assert.AreSame(infos[11], infos[10].ReferenceDerived.Ref);
            Assert.AreEqual(TestConfigId.FromString("L"), infos[11].Id);
            Assert.AreEqual(12, infos[11].Value);
            Assert.AreSame(infos[12], infos[11].ReferenceBase.Ref);
            Assert.AreEqual(TestConfigId.FromString("M"), infos[12].Id);
            Assert.AreEqual(13, infos[12].Value);
            Assert.AreSame(infos[13], infos[12].ReferenceDerived.Ref);
            Assert.AreEqual(TestConfigId.FromString("N"), infos[13].Id);
            Assert.AreEqual(14, infos[13].Value);
            Assert.AreSame(infos[14], infos[13].ReferenceBase.Ref);

            Assert.AreEqual(TestConfigId.FromString("X"), infos[14].Id);
            Assert.AreEqual(990, infos[14].Value);
        }

        [Test]
        public void RandomTests()
        {
            // Run simpler cases first, because they'll be easier to debug when they fail.

            for (int i = 0; i < 200; i++)
                RandomTestCase(randomSeed: (ulong)i, maxNumItems: 3, maxNumPatches: 2, maxNumAppendedItems: 0, allowDisabledPatches: false);
            for (int i = 0; i < 200; i++)
                RandomTestCase(randomSeed: (ulong)i, maxNumItems: 5, maxNumPatches: 2, maxNumAppendedItems: 0, allowDisabledPatches: false);
            for (int i = 0; i < 200; i++)
                RandomTestCase(randomSeed: (ulong)i, maxNumItems: 10, maxNumPatches: 3, maxNumAppendedItems: 5, allowDisabledPatches: true);
            for (int i = 0; i < 100; i++)
                RandomTestCase(randomSeed: (ulong)i, maxNumItems: 100, maxNumPatches: 5, maxNumAppendedItems: 50, allowDisabledPatches: true);
        }

        void RandomTestCase(ulong randomSeed, int maxNumItems, int maxNumPatches, int maxNumAppendedItems, bool allowDisabledPatches)
        {
            try
            {
                RandomTestCaseImpl(randomSeed, maxNumItems, maxNumPatches, maxNumAppendedItems, allowDisabledPatches);
            }
            catch (Exception)
            {
                // Print info that shows the specific parameters. Makes it easier to debug.
                // These are all in the same NUnit test case, and the stack trace doesn't tell us the arguments.
                Console.WriteLine($"Failed {nameof(RandomTestCaseImpl)}({randomSeed}, {maxNumItems}, {maxNumPatches}, {maxNumAppendedItems}, {(allowDisabledPatches ? "true" : "false")})");
                throw;
            }
        }

        void RandomTestCaseImpl(ulong randomSeed, int maxNumItems, int maxNumPatches, int maxNumAppendedItems, bool allowDisabledPatches)
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(randomSeed);

            IGameConfigData CreateRandomItem(string id, int value)
            {
                int type = rnd.NextInt(5);
                switch (type)
                {
                    case 0: return new TestConfigInfo(id, value);
                    case 1: return new TestXConfigInfo(id, value);
                    case 2: return new TestYConfigInfo(id, value);
                    case 3: return new TestZConfigInfo(id, value);
                    case 4: return new TestDerivedConfigInfo(id, value);
                    default:
                        throw new InvalidOperationException("unreachable");
                }
            }

            void AssignRandomReference(IGameConfigData dstItem, IList<MetaRef<TestConfigInfo>> testRefs, IList<MetaRef<TestYConfigInfo>> testYRefs, IList<MetaRef<TestZConfigInfo>> testZRefs, IList<MetaRef<TestDerivedConfigInfo>> testDerivedRefs)
            {
                switch (dstItem)
                {
                    case TestConfigInfo item:
                        if (rnd.NextBool())
                            item.Reference = rnd.Choice(testRefs);
                        if (rnd.NextBool())
                            item.Reference2 = rnd.Choice(testRefs);
                        break;
                    case TestXConfigInfo item:
                        if (rnd.NextBool())
                            item.ReferenceToY = rnd.Choice(testYRefs);
                        break;
                    case TestYConfigInfo item:
                        if (rnd.NextBool())
                            item.ReferenceToZ = rnd.Choice(testZRefs);
                        break;
                    case TestZConfigInfo _:
                        break;
                    case TestDerivedConfigInfo item:
                        if (rnd.NextBool())
                            item.ReferenceBase = rnd.Choice(testDerivedRefs)?.CastItem<TestBaseConfigInfo>();
                        if (rnd.NextBool())
                            item.ReferenceDerived = rnd.Choice(testDerivedRefs);
                        break;
                    default:
                        throw new InvalidOperationException("unreachable");
                }
            }

            static IMetaRef CreateRef(IGameConfigData gameConfigData)
            {
                switch (gameConfigData)
                {
                    case TestConfigInfo item: return MetaRef<TestConfigInfo>.FromKey(item.ConfigKey);
                    case TestXConfigInfo item: return MetaRef<TestXConfigInfo>.FromKey(item.ConfigKey);
                    case TestYConfigInfo item: return MetaRef<TestYConfigInfo>.FromKey(item.ConfigKey);
                    case TestZConfigInfo item: return MetaRef<TestZConfigInfo>.FromKey(item.ConfigKey);
                    case TestDerivedConfigInfo item: return MetaRef<TestDerivedConfigInfo>.FromKey(item.ConfigKey);
                    default:
                        throw new InvalidOperationException("unreachable");
                }
            }

            int numItems = rnd.NextInt(maxNumItems + 1);

            // Create baseline items.
            // But this doesn't yet assign the inter-item references,
            // that's easier done as a separate pass below.

            List<IGameConfigData> baselineItems = new List<IGameConfigData>();

            for (int i = 0; i < numItems; i++)
                baselineItems.Add(CreateRandomItem(Invariant($"item{i}"), i));

            // Construct MetaRefs corresponding to the items.
            IEnumerable<IMetaRef> baselineRefs = baselineItems.Select(CreateRef);
            List<MetaRef<TestConfigInfo>> baselineTestItemRefs = baselineRefs.OfType<MetaRef<TestConfigInfo>>().ToList();
            List<MetaRef<TestYConfigInfo>> baselineTestYItemRefs = baselineRefs.OfType<MetaRef<TestYConfigInfo>>().ToList();
            List<MetaRef<TestZConfigInfo>> baselineTestZItemRefs = baselineRefs.OfType<MetaRef<TestZConfigInfo>>().ToList();
            List<MetaRef<TestDerivedConfigInfo>> baselineTestDerivedItemRefs = baselineRefs.OfType<MetaRef<TestDerivedConfigInfo>>().ToList();

            // Create references between the baseline items.
            foreach (IGameConfigData item in baselineItems)
                AssignRandomReference(item, baselineTestItemRefs, baselineTestYItemRefs, baselineTestZItemRefs, baselineTestDerivedItemRefs);

            // Create the baseline TestConfigKeyValue.

            TestConfigKeyValue keyValue = new TestConfigKeyValue();
            if (rnd.NextBool())
                keyValue.Reference = rnd.Choice(baselineTestItemRefs);

            // Create patches.

            int numPatches = rnd.NextInt(maxNumPatches + 1);
            List<PatchSpec> patchSpecs = new List<PatchSpec>();
            for (int patchNdx = 0; patchNdx < numPatches; patchNdx++)
            {
                // Create appended items for the patch.

                List<IGameConfigData> appendedItems = new List<IGameConfigData>();
                int numAppendedItems = rnd.NextBool()
                                       ? rnd.NextInt(maxNumAppendedItems + 1)
                                       : 0;

                IEnumerable<int> appendedItemIds = Enumerable.Range(0, maxNumAppendedItems).Shuffle(rnd).Take(numAppendedItems);

                foreach (int id in appendedItemIds)
                {
                    int value = 10000 + 1000*patchNdx + id;
                    appendedItems.Add(CreateRandomItem(Invariant($"appended_{id}"), value));
                }

                // Construct MetaRefs corresponding to the baseline and appended items.
                IEnumerable<IMetaRef> appendedRefs = appendedItems.Select(CreateRef);
                List<MetaRef<TestConfigInfo>> patchTestItemRefs = baselineTestItemRefs.Concat(appendedRefs.OfType<MetaRef<TestConfigInfo>>()).ToList();
                List<MetaRef<TestYConfigInfo>> patchTestYItemRefs = baselineTestYItemRefs.Concat(appendedRefs.OfType<MetaRef<TestYConfigInfo>>()).ToList();
                List<MetaRef<TestZConfigInfo>> patchTestZItemRefs = baselineTestZItemRefs.Concat(appendedRefs.OfType<MetaRef<TestZConfigInfo>>()).ToList();
                List<MetaRef<TestDerivedConfigInfo>> patchTestDerivedItemRefs = baselineTestDerivedItemRefs.Concat(appendedRefs.OfType<MetaRef<TestDerivedConfigInfo>>()).ToList();

                // Assign random references in the appended items, referring to already-existing and appended items.
                foreach (IGameConfigData item in appendedItems)
                    AssignRandomReference(item, patchTestItemRefs, patchTestYItemRefs, patchTestZItemRefs, patchTestDerivedItemRefs);

                // Create replacement items for the patch.
                List<IGameConfigData> replacementItems = new List<IGameConfigData>();
                foreach (IGameConfigData baselineItem in baselineItems)
                {
                    // With some probability, don't replace the item.
                    if (rnd.NextInt(100) < 70)
                        continue;

                    // Clone the baseline.
                    IGameConfigData replacementItem;
                    switch (baselineItem)
                    {
                        case TestConfigInfo item: replacementItem = CloneConfigItem(item); break;
                        case TestXConfigInfo item: replacementItem = CloneConfigItem(item); break;
                        case TestYConfigInfo item: replacementItem = CloneConfigItem(item); break;
                        case TestZConfigInfo item: replacementItem = CloneConfigItem(item); break;
                        case TestDerivedConfigInfo item: replacementItem = CloneConfigItem(item); break;
                        default:
                            throw new InvalidOperationException("unreachable");
                    }

                    // Do modifications to the replacement.
                    switch (replacementItem)
                    {
                        case TestConfigInfo item:
                            if (rnd.NextBool())
                                item.Value += 1000;
                            if (rnd.NextBool())
                                item.Reference = rnd.Choice(patchTestItemRefs);
                            if (rnd.NextBool())
                                item.Reference2 = rnd.Choice(patchTestItemRefs);
                            break;
                        case TestXConfigInfo item:
                            if (rnd.NextBool())
                                item.Value += 1000;
                            if (rnd.NextBool())
                                item.ReferenceToY = rnd.Choice(patchTestYItemRefs);
                            break;
                        case TestYConfigInfo item:
                            if (rnd.NextBool())
                                item.Value += 1000;
                            if (rnd.NextBool())
                                item.ReferenceToZ = rnd.Choice(patchTestZItemRefs);
                            break;
                        case TestZConfigInfo item:
                            if (rnd.NextBool())
                                item.Value += 1000;
                            break;
                        case TestDerivedConfigInfo item:
                            if (rnd.NextBool())
                                item.Value += 1000;
                            if (rnd.NextBool())
                                item.ReferenceBase = rnd.Choice(patchTestDerivedItemRefs)?.CastItem<TestBaseConfigInfo>();
                            if (rnd.NextBool())
                                item.ReferenceDerived = rnd.Choice(patchTestDerivedItemRefs);
                            break;
                        default:
                            throw new InvalidOperationException("unreachable");
                    }

                    replacementItems.Add(replacementItem);
                }

                // Modify the TestConfigKeyValue.
                OrderedDictionary<string, object> keyValueMemberReplacements = new OrderedDictionary<string, object>();
                if (rnd.NextBool())
                {
                    keyValueMemberReplacements.Add(
                        nameof(TestConfigKeyValue.Reference),
                        rnd.Choice(patchTestItemRefs));
                }

                bool patchIsEnabled = allowDisabledPatches
                                      ? rnd.NextBool()
                                      : true;

                patchSpecs.Add(new PatchSpec(
                    replacements: replacementItems,
                    additions: appendedItems,
                    keyValueMemberReplacementsByName: keyValueMemberReplacements,
                    isEnabled: patchIsEnabled));
            }

            ConfigSpec configSpec = new ConfigSpec(
                new BaselineSpec(baselineItems, keyValue),
                patchSpecs);

            // Create the expected (reference) config by using a naive, assumed-valid patching implementation.
            ExpectedConfigContent expectedConfig = CreateExpectedPatchedConfig(configSpec);

            // Create the patched config to test.
            TestSharedGameConfig resultConfig = PatchConfig(configSpec);

            // Assert equality between expected and result patched config.
            // Compare contents; MetaRef equality is by key only, and additionally
            // MetaRefs in the result config are asserted to point to the instance
            // that is acquired by accessing the config explicitly by the key.

            Assert.AreEqual(expectedConfig.TestItems.Count, resultConfig.TestItems.Count);
            foreach ((TestConfigInfo expected, TestConfigInfo item) in expectedConfig.TestItems.Values.Zip(resultConfig.TestItems.Values))
            {
                Assert.AreEqual(expected.Id, item.Id);
                Assert.AreEqual(expected.Value, item.Value);
                Assert.AreEqual(expected.Reference?.KeyObject, item.Reference?.KeyObject);
                Assert.AreEqual(expected.Reference2?.KeyObject, item.Reference2?.KeyObject);

                if (item.Reference != null)
                    Assert.AreSame(resultConfig.TestItems[(TestConfigId)item.Reference.KeyObject], item.Reference.Ref);
                if (item.Reference2 != null)
                    Assert.AreSame(resultConfig.TestItems[(TestConfigId)item.Reference2.KeyObject], item.Reference2.Ref);
            }

            Assert.AreEqual(expectedConfig.TestXItems.Count, resultConfig.TestXItems.Count);
            foreach ((TestXConfigInfo expected, TestXConfigInfo item) in expectedConfig.TestXItems.Values.Zip(resultConfig.TestXItems.Values))
            {
                Assert.AreEqual(expected.Id, item.Id);
                Assert.AreEqual(expected.Value, item.Value);
                Assert.AreEqual(expected.ReferenceToY?.KeyObject, item.ReferenceToY?.KeyObject);

                if (item.ReferenceToY != null)
                    Assert.AreSame(resultConfig.TestYItems[(TestYConfigId)item.ReferenceToY.KeyObject], item.ReferenceToY.Ref);
            }

            Assert.AreEqual(expectedConfig.TestYItems.Count, resultConfig.TestYItems.Count);
            foreach ((TestYConfigInfo expected, TestYConfigInfo item) in expectedConfig.TestYItems.Values.Zip(resultConfig.TestYItems.Values))
            {
                Assert.AreEqual(expected.Id, item.Id);
                Assert.AreEqual(expected.Value, item.Value);
                Assert.AreEqual(expected.ReferenceToZ?.KeyObject, item.ReferenceToZ?.KeyObject);

                if (item.ReferenceToZ != null)
                    Assert.AreSame(resultConfig.TestZItems[(TestZConfigId)item.ReferenceToZ.KeyObject], item.ReferenceToZ.Ref);
            }

            Assert.AreEqual(expectedConfig.TestZItems.Count, resultConfig.TestZItems.Count);
            foreach ((TestZConfigInfo expected, TestZConfigInfo item) in expectedConfig.TestZItems.Values.Zip(resultConfig.TestZItems.Values))
            {
                Assert.AreEqual(expected.Id, item.Id);
                Assert.AreEqual(expected.Value, item.Value);
            }

            Assert.AreEqual(expectedConfig.TestDerivedItems.Count, resultConfig.TestDerivedItems.Count);
            foreach ((TestDerivedConfigInfo expected, TestDerivedConfigInfo item) in expectedConfig.TestDerivedItems.Values.Zip(resultConfig.TestDerivedItems.Values))
            {
                Assert.AreEqual(expected.Id, item.Id);
                Assert.AreEqual(expected.Value, item.Value);
                Assert.AreEqual(expected.ReferenceBase?.KeyObject, item.ReferenceBase?.KeyObject);
                Assert.AreEqual(expected.ReferenceDerived?.KeyObject, item.ReferenceDerived?.KeyObject);

                if (item.ReferenceBase != null)
                    Assert.AreSame(resultConfig.TestDerivedItems[(TestConfigId)item.ReferenceBase.KeyObject], item.ReferenceBase.Ref);
                if (item.ReferenceDerived != null)
                    Assert.AreSame(resultConfig.TestDerivedItems[(TestConfigId)item.ReferenceDerived.KeyObject], item.ReferenceDerived.Ref);
            }
        }

        static TConfigData CloneConfigItem<TConfigData>(TConfigData item)
            where TConfigData : class, IGameConfigData, new()
        {
            GameConfigDataContent<TConfigData> originalContent = new GameConfigDataContent<TConfigData>(item);
            GameConfigDataContent<TConfigData> clonedContent = MetaSerialization.CloneTagged(originalContent, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);
            return clonedContent.ConfigData;
        }

        class ExpectedConfigContent
        {
            public OrderedDictionary<TestConfigId, TestConfigInfo> TestItems { get; set; } = new OrderedDictionary<TestConfigId, TestConfigInfo>();
            public OrderedDictionary<TestXConfigId, TestXConfigInfo> TestXItems { get; set; } = new OrderedDictionary<TestXConfigId, TestXConfigInfo>();
            public OrderedDictionary<TestYConfigId, TestYConfigInfo> TestYItems { get; set; } = new OrderedDictionary<TestYConfigId, TestYConfigInfo>();
            public OrderedDictionary<TestZConfigId, TestZConfigInfo> TestZItems { get; set; } = new OrderedDictionary<TestZConfigId, TestZConfigInfo>();
            public OrderedDictionary<TestConfigId, TestDerivedConfigInfo> TestDerivedItems { get; set; } = new OrderedDictionary<TestConfigId, TestDerivedConfigInfo>();
            public TestConfigKeyValue TestKeyValue { get; set; } = new TestConfigKeyValue();
        }

        static ExpectedConfigContent CreateExpectedPatchedConfig(ConfigSpec configSpec)
        {
            ExpectedConfigContent expectedConfig = new ExpectedConfigContent();

            PutItemClones(expectedConfig.TestItems, configSpec.Baseline.Items);
            PutItemClones(expectedConfig.TestXItems, configSpec.Baseline.Items);
            PutItemClones(expectedConfig.TestYItems, configSpec.Baseline.Items);
            PutItemClones(expectedConfig.TestZItems, configSpec.Baseline.Items);
            PutItemClones(expectedConfig.TestDerivedItems, configSpec.Baseline.Items);

            expectedConfig.TestKeyValue.Reference = configSpec.Baseline.KeyValue.Reference != null
                                                    ? MetaRef<TestConfigInfo>.FromKey(configSpec.Baseline.KeyValue.Reference.KeyObject)
                                                    : null;

            foreach (PatchSpec patch in configSpec.Patches)
            {
                if (!patch.IsEnabled)
                    continue;

                PutItemClones(expectedConfig.TestItems, patch.Replacements);
                PutItemClones(expectedConfig.TestItems, patch.Additions);
                PutItemClones(expectedConfig.TestXItems, patch.Replacements);
                PutItemClones(expectedConfig.TestXItems, patch.Additions);
                PutItemClones(expectedConfig.TestYItems, patch.Replacements);
                PutItemClones(expectedConfig.TestYItems, patch.Additions);
                PutItemClones(expectedConfig.TestZItems, patch.Replacements);
                PutItemClones(expectedConfig.TestZItems, patch.Additions);
                PutItemClones(expectedConfig.TestDerivedItems, patch.Replacements);
                PutItemClones(expectedConfig.TestDerivedItems, patch.Additions);

                if (patch.KeyValueMemberReplacementsByName.TryGetValue(nameof(TestConfigKeyValue.Reference), out object referenceObj))
                {
                    MetaRef<TestConfigInfo> reference = (MetaRef<TestConfigInfo>)referenceObj;
                    expectedConfig.TestKeyValue.Reference = reference != null
                                                          ? MetaRef<TestConfigInfo>.FromKey(reference.KeyObject)
                                                          : null;
                }
            }

            return expectedConfig;
        }

        static void PutItemClones<TKey, TInfo>(OrderedDictionary<TKey, TInfo> dst, IEnumerable<IGameConfigData> allItems)
            where TInfo : class, IGameConfigData<TKey>, new()
        {
            foreach (TInfo item in allItems.OfType<TInfo>())
                dst.AddOrReplace(item.ConfigKey, CloneConfigItem(item));
        }
    }
}
