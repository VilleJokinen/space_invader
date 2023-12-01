// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cloud.Tests
{
    [TestFixture]
    public class ConfigIdRenameTests
    {
        [MetaSerializable]
        public class TestConfigId : StringId<TestConfigId> { }

        [MetaSerializable]
        public class TestConfigInfo : IGameConfigData<TestConfigId>
        {
            public TestConfigId ConfigKey => Id;

            [MetaMember(1)] public TestConfigId Id { get; private set; }
            [MetaMember(2)] public int Value { get; private set; }
            [MetaMember(3)] public MetaRef<TestConfigInfo> Reference { get; private set; }
        }

        class TestSharedGameConfig : SharedGameConfigBase
        {
            [GameConfigEntry("TestItems")]
            public GameConfigLibrary<TestConfigId, TestConfigInfo> TestItems { get; set; }
        }

        GameConfigLibrary<TKey, TItem> MakeConfigLibrary<TKey, TItem>(GameConfigBuildLog buildLog, GameConfigParseLibraryPipelineConfig config, SpreadsheetContent spreadsheet) where TItem : class, IGameConfigData<TKey>, new()
        {
            VariantConfigItem<TKey, TItem>[] items = GameConfigParsePipeline.ProcessSpreadsheetLibrary<TKey, TItem>(buildLog, config, spreadsheet);

            // Check that no errors/warnings occurred during parsing
            GameConfigTestHelper.CheckNoErrorsOrWarnings(buildLog);

            // Assume no variants
            OrderedDictionary<TKey, TItem> dict = GameConfigUtil.ConvertToOrderedDictionary<TKey, TItem>(items.Select(item => item.Item));
            return GameConfigLibrary<TKey, TItem>.CreateSolo(dict);
        }

        TestSharedGameConfig MakeConfig(string[] items, IEnumerable<(string, string)> aliases = null)
        {
            string[] header = new string[] { "Id #key,Value,Reference" };
            TestSharedGameConfig config = new TestSharedGameConfig();

            byte[] csvBytes = Encoding.ASCII.GetBytes(string.Join('\n', header.Concat(items)));
            SpreadsheetContent spreadsheet = GameConfigHelper.ParseCsvToSpreadsheet("Test.csv", csvBytes);

            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(spreadsheet.Name);
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);

            config.TestItems = MakeConfigLibrary<TestConfigId, TestConfigInfo>(
                buildLog,
                new GameConfigParseLibraryPipelineConfig(),
                spreadsheet);

            if (aliases != null)
            {
                foreach ((string id, string alias) in aliases)
                    config.TestItems.RegisterAlias(TestConfigId.FromString(id), TestConfigId.FromString(alias));
            }

            config.OnConfigEntriesPopulated(true);
            return config;
        }

        [MetaSerializable]
        public class TestModel
        {
            [MetaMember(1)]
            public TestConfigInfo Reference;
        }

        [Test]
        public void BasicRuntimeConfigIdAliasTest()
        {
            TestSharedGameConfig config1 = MakeConfig(
                new string[]
                {
                    "TestItem1,1",
                    "TestItem2,2"
                });
            TestSharedGameConfig config2 = MakeConfig(
                new string[]
                {
                    "TestItem2,2",
                    "TestItem3,3",
                },
                aliases: Enumerable.Repeat(("TestItem3", "TestItem1"), 1));

            TestModel model = new TestModel();
            model.Reference = config1.TestItems[TestConfigId.FromString("TestItem1")];
            byte[] serialized = MetaSerialization.SerializeTagged(model, MetaSerializationFlags.IncludeAll, null);

            TestModel loadedModel1 = MetaSerialization.DeserializeTagged<TestModel>(serialized, MetaSerializationFlags.IncludeAll, config1, null);
            Assert.IsTrue(loadedModel1.Reference.Value == 1);

            TestModel loadedModel2 = MetaSerialization.DeserializeTagged<TestModel>(serialized, MetaSerializationFlags.IncludeAll, config2, null);
            Assert.IsTrue(loadedModel2.Reference.Value == 3);
        }

        [Test]
        public void BasicIntraConfigReferenceAliasTest()
        {
            TestSharedGameConfig config = MakeConfig(
                new string[]
                {
                    "TestItem1,1,TestItem3",
                    "TestItem2,2,TestItem3Alias",
                    "TestItem3,3",
                },
                aliases: Enumerable.Repeat(("TestItem3", "TestItem3Alias"), 1));

            TestConfigInfo item1 = config.TestItems[TestConfigId.FromString("TestItem1")];
            TestConfigInfo item2 = config.TestItems[TestConfigId.FromString("TestItem2")];
            TestConfigInfo item3 = config.TestItems[TestConfigId.FromString("TestItem3")];

            Assert.AreSame(item3, item1.Reference.Ref);
            Assert.AreSame(item3, item2.Reference.Ref);
            // Implied by the above - asserting just for sanity.
            Assert.AreEqual(3, item1.Reference.Ref.Value);
            Assert.AreEqual(3, item2.Reference.Ref.Value);
        }
    }
}
