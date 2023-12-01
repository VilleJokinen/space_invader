// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    [TestFixture]
    public class GameConfigPipelineTests
    {
        public static class Helper
        {
            public static bool SequenceEquals<T>(IEnumerable<T> a, IEnumerable<T> b)
            {
                if (ReferenceEquals(a, b))
                    return true;

                if (a is null || b is null)
                    return false;

                return a.SequenceEqual(b);
            }
        }

        [MetaSerializable]
        public class LeftId : StringId<LeftId> { }

        [MetaSerializable]
        public class LeftInfo : IGameConfigData<LeftId>
        {
            public LeftId ConfigKey => Id;

            [MetaMember(1)] public LeftId Id    { get; set; }
            [MetaMember(2)] public string Name  { get; set; }

            [MetaMember(3)] public Nested Nested { get; set; }
            [MetaMember(4)] public List<int> IntList { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is not LeftInfo other)
                    return false;

                return Equals(Id, other.Id)
                    && Equals(Name, other.Name)
                    && Equals(Nested, other.Nested)
                    && Helper.SequenceEquals(IntList, other.IntList);
            }

            public override int GetHashCode() => throw new NotSupportedException("do not use");
        }

        [MetaSerializable]
        public class RightId : StringId<RightId> { }

        [MetaSerializable]
        public class RightInfo : IGameConfigData<RightId>
        {
            public RightId ConfigKey => Id;

            [MetaMember(1)] public RightId              Id      { get; set; }
            [MetaMember(2)] public string               Name    { get; set; }
            [MetaMember(3)] public MetaRef<LeftInfo>    Left    { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is not RightInfo other)
                    return false;

                return Equals(Id, other.Id)
                    && Equals(Name, other.Name)
                    && Equals(Left, other.Left);
            }

            public override int GetHashCode() => throw new NotSupportedException("do not use");
        }

        [MetaSerializable]
        public class Nested
        {
            [MetaMember(1)] public string StringMember;

            public override bool Equals(object obj)
            {
                if (obj is not Nested other)
                    return false;

                return Equals(StringMember, other.StringMember);
            }

            public override int GetHashCode() => throw new NotSupportedException("do not use");
        }

        public class TestSharedGameConfig : SharedGameConfigBase
        {
            [GameConfigEntry("Left")]
            public GameConfigLibrary<LeftId, LeftInfo> Left { get; set; }

            [GameConfigEntry("Right")]
            public GameConfigLibrary<RightId, RightInfo> Right { get; set; }

            public override void BuildTimeValidate(GameConfigValidationResult validationResult)
            {
                base.BuildTimeValidate(validationResult);

                foreach (LeftInfo left in Left.Values)
                    validationResult.Info(nameof(Left), left.ConfigKey.ToString(), $"Test validation message: {left.Name}");
            }
        }

        class TestGameConfigBuild : GameConfigBuildTemplate<TestSharedGameConfig>
        {
            public TestGameConfigBuild()
            {
                SharedConfigType = typeof(TestSharedGameConfig);
            }
        }

        class TestConfigBuildIntegration : GameConfigBuildIntegration
        {
            public UnknownConfigMemberHandling? UnknownMemberHandlingOverride = null;
            public override UnknownConfigMemberHandling UnknownConfigItemMemberHandling => UnknownMemberHandlingOverride ?? base.UnknownConfigItemMemberHandling;

            public override Type GetDefaultBuildParametersType() => typeof(DefaultGameConfigBuildParameters);

            public override GameConfigBuild MakeGameConfigBuild(IGameConfigSourceFetcherConfig fetcherConfig, GameConfigBuildDebugOptions debugOpts)
            {
                return new TestGameConfigBuild()
                {
                    SourceFetcherProvider = MakeSourceFetcherProvider(fetcherConfig),
                    DebugOptions          = debugOpts,
                    Integration           = this,
                };
            }
        }

        Task<(TestSharedGameConfig, GameConfigBuildReport)> BuildSharedGameConfigAsync(params SpreadsheetContent[] inputSheets)
        {
            return BuildSharedGameConfigAsync(new TestConfigBuildIntegration(), inputSheets);
        }

        async Task<(TestSharedGameConfig, GameConfigBuildReport)> BuildSharedGameConfigAsync(TestConfigBuildIntegration integration, params SpreadsheetContent[] inputSheets)
        {
            try
            {
                // Build the StaticGameConfig ConfigArchive
                DefaultGameConfigBuildParameters buildParams = new DefaultGameConfigBuildParameters()
                {
                    DefaultSource = new StaticDataDictionaryBuildSource(inputSheets.Select(sheet => (sheet.Name, (object)sheet)))
                };

                GameConfigBuild build = integration.MakeGameConfigBuild(null, new GameConfigBuildDebugOptions());
                ConfigArchive staticArchive = await build.CreateArchiveAsync(MetaTime.Now, buildParams, MetaGuid.None, null);

                // Extract SharedGameConfig from StaticGameConfig
                ReadOnlyMemory<byte> sharedArchiveBytes = staticArchive.GetEntryBytes("Shared.mpa");
                ConfigArchive sharedArchive = ConfigArchive.FromBytes(sharedArchiveBytes);

                // Import the shared game config (without variants)
                // \note Must provide the game config type explicitly as it's not the default in the tests
                GameConfigImportParams importParams = GameConfigImportParams.CreateSoloUnpatched(typeof(TestSharedGameConfig), sharedArchive, isBuildingConfigs: false, isConfigBuildParent: false);
                TestSharedGameConfig gameConfig = (TestSharedGameConfig)GameConfigFactory.Instance.ImportGameConfig(importParams);

                // Extract build report from archive
                ReadOnlyMemory<byte> metaDataBytes = staticArchive.GetEntryBytes("_metadata");
                GameConfigMetaData metadata = MetaSerialization.DeserializeTagged<GameConfigMetaData>(metaDataBytes.ToArray(), MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null); // \todo Optimize ToArray() alloc

                return (gameConfig, metadata.BuildReport);
            }
            catch (GameConfigBuildFailed failed)
            {
                return (null, failed.BuildReport);
            }
        }

        [Test]
        public async Task SuccessTest()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key", "Name"    },
                    new() { "Left1",   "Left 1"  },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key", "Name",    "Left"     },
                    new() { "Right1",  "Right 1", "Left1"    },
                })
            );

            Assert.AreEqual(0, report.GetMessageCountForLevel(GameConfigLogLevel.Warning));
            Assert.AreEqual(0, report.GetMessageCountForLevel(GameConfigLogLevel.Error));

            LeftId left1Id = LeftId.FromString("Left1");
            LeftInfo left1 = gameConfig.Left[left1Id];

            Assert.AreEqual(
                new LeftInfo[]
                {
                    new LeftInfo { Id = LeftId.FromString("Left1"), Name = "Left 1" },
                },
                gameConfig.Left.Values);

            Assert.AreEqual(
                new RightInfo[]
                {
                    new RightInfo { Id = RightId.FromString("Right1"), Name = "Right 1", Left = MetaRef<LeftInfo>.FromItem(left1) },
                },
                gameConfig.Right.Values);
        }

        [Test]
        public async Task ParserErrorsTest()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key", "Name"    },
                    new() { "Left1",   "Left 1"  },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key", "Name",    "Left"     },
                    new() { "***",     "Right 1", "Left1"    }, // bad ids
                    new() { "###",     "Right 1", "Left1"    },
                })
            );

            // Fail with two build errors
            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(2, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            // Check build error messages
            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Right.csv:A2", msg0.LocationUrl);
            StringAssert.Contains("ParseError", msg0.Exception);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            Assert.AreEqual("Right.csv:A3", msg1.LocationUrl);
            StringAssert.Contains("ParseError", msg1.Exception);
        }

        [Test]
        public async Task EmptySheetTest()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    // empty sheet
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { }, // empty header
                    new() { "Right1", "Right 1", "Left1"    },
                })
            );

            //Console.WriteLine("BUILD LOG:\n{0}", string.Join("\n", report.BuildMessages.Select(msg => msg.ToString())));

            // Fail with two build errors
            Assert.IsNull(gameConfig);
            Assert.AreEqual(2, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            // Check build error messages
            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            StringAssert.Contains("Left.csv", msg0.SourceInfo);
            Assert.AreEqual("Left.csv:", msg0.LocationUrl); // \todo is format for files good?
            StringAssert.Contains("Input sheet is completely empty", msg0.Message);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            StringAssert.Contains("Right.csv", msg1.SourceInfo);
            Assert.AreEqual("Right.csv:1:1", msg1.LocationUrl); // \todo is format for files good?
            StringAssert.Contains("Input sheet header row is empty", msg1.Message);
        }

        [Test]
        public async Task DuplicateKeyTest()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "/Variant", "Id #key", "Name"        },
                    new() { "",         "Left1",   "Left 1a"     },
                    new() { "",         "Left2",   "Left 2a"     },
                    new() { "",         "Left1",   "Left 1b"     }, // duplicate key
                    new() { "",         "Left2",   "Left 2b"     }, // duplicate key
                    new() { "A/X",      "Left1",   "Left 1 Xa"   },
                    new() { "A/Y",      "",        "Left 1 Y"    },
                    new() { "A/X",      "",        "Left 1 Xb"   }, // duplicate key/variant
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key", "Name"    },
                    new() { "Right1",  "Right 1" },
                })
            );

            //Console.WriteLine("BUILD LOG:\n{0}", string.Join("\n", report.BuildMessages.Select(msg => msg.ToString())));

            // Check build report
            Assert.IsNull(gameConfig);
            Assert.AreEqual(3, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            // Check build error messages
            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:4:4", msg0.LocationUrl);
            StringAssert.Contains("Duplicate object with key 'Left1', other copy is at ", msg0.Message);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            Assert.AreEqual("Left.csv:5:5", msg1.LocationUrl);
            StringAssert.Contains("Duplicate object with key 'Left2', other copy is at ", msg1.Message);

            GameConfigBuildMessage msg2 = report.BuildMessages[2];
            Assert.AreEqual("Left.csv:8:8", msg2.LocationUrl);
            StringAssert.Contains("Duplicate object with key 'Left1' (variant 'A/X'), other copy is at ", msg2.Message);
        }

        [Test]
        public async Task BadReferencesTest()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key", "Name"    },
                    new() { "Left1",   "Left 1"  },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key", "Name",    "Left"     },
                    new() { "Right1",  "Right 1", "Left_"    }, // bad references to Left library
                    new() { "Right2",  "Right 2", "Left_2"   },
                })
            );

            // Fail with two build errors
            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error)); // \todo Only one error reported for now, fixed in another PR

            // Check build error messages
            // \todo Add checks for the error messages
            //GameConfigBuildMessage msg0 = report.BuildMessages[0];
            //Assert.AreEqual(GameConfigLogLevel.Error, msg0.Level);
            //Assert.AreEqual("Right.csv:A2", msg0.LocationUrl);
            //Assert.True(msg0.Exception.Contains("ParseError: Invalid input '***'"));
            //
            //GameConfigBuildMessage msg1 = report.BuildMessages[1];
            //Assert.AreEqual(GameConfigLogLevel.Error, msg1.Level);
            //Assert.AreEqual("Right.csv:A3", msg1.LocationUrl);
            //Assert.True(msg0.Exception.Contains("ParseError: Invalid input '###'"));
        }

        [Test]
        public async Task MissingId()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Name"      },
                    new() { "Left 1"    },
                    new() { "Left 2"    },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("SpreadsheetFile:Left.csv", msg0.SourceInfo);
            StringAssert.Contains("No key columns were specified", msg0.Message);
        }

        [TestCase(null)] // \note Specifically tests that default handling is Error
        [TestCase(UnknownConfigMemberHandling.Error)]
        [TestCase(UnknownConfigMemberHandling.Warning)]
        [TestCase(UnknownConfigMemberHandling.Ignore)]
        public async Task UnknownMemberTest_Simple(UnknownConfigMemberHandling? unknownMemberHandlingOverride)
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                new TestConfigBuildIntegration
                {
                    UnknownMemberHandlingOverride = unknownMemberHandlingOverride
                },
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key", "Name",   "NonexistentMember" },
                    new() { "Left1",   "Left 1", "abc"               },
                    new() { "Left2",   "Left 2", ""                  }, // No value on this row -> no error. It's questionable if this is desirable, but this is natural for the current implementation.
                    new() { "Left3",   "Left 3", "def"               },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            // \ntoe When unknownMemberHandlingOverride is not given, we're using default, which is Error.
            UnknownConfigMemberHandling unknownMemberHandling = unknownMemberHandlingOverride ?? UnknownConfigMemberHandling.Error;

            // Check resulting game config.
            // - On Warning and Ignore, build should succeed despite possible build messages.
            // - On Error, build should not succeed.
            if (unknownMemberHandling == UnknownConfigMemberHandling.Warning || unknownMemberHandling == UnknownConfigMemberHandling.Ignore)
            {
                Assert.AreEqual(
                    new LeftInfo[]
                    {
                        new LeftInfo { Id = LeftId.FromString("Left1"), Name = "Left 1" },
                        new LeftInfo { Id = LeftId.FromString("Left2"), Name = "Left 2" },
                        new LeftInfo { Id = LeftId.FromString("Left3"), Name = "Left 3" },
                    },
                    gameConfig.Left.Values);

                Assert.AreEqual(new RightInfo[] {}, gameConfig.Right.Values);
            }
            else
            {
                Assert.IsNull(gameConfig);
            }

            // Check build messages.
            // - On Ignore, should at most have Information messages (which we don't check here as they're unrelated).
            // - On Error and Warning, should have Error or Warning messages respectively.
            if (unknownMemberHandling == UnknownConfigMemberHandling.Ignore)
            {
                Assert.LessOrEqual(report.HighestMessageLevel, GameConfigLogLevel.Information);
            }
            else
            {
                GameConfigLogLevel expectedLogLevel;

                if (unknownMemberHandling == UnknownConfigMemberHandling.Error)
                    expectedLogLevel = GameConfigLogLevel.Error;
                else
                    expectedLogLevel = GameConfigLogLevel.Warning;

                Assert.AreEqual(expectedLogLevel, report.HighestMessageLevel);
                Assert.AreEqual(2, report.GetMessageCountForLevel(expectedLogLevel));

                GameConfigBuildMessage msg0 = report.BuildMessages[0];
                Assert.AreEqual(expectedLogLevel, msg0.Level);
                Assert.AreEqual("Left.csv:C2", msg0.LocationUrl);
                StringAssert.Contains("No member 'NonexistentMember' found in GameConfigPipelineTests.LeftInfo", msg0.Message);

                GameConfigBuildMessage msg1 = report.BuildMessages[1];
                Assert.AreEqual(expectedLogLevel, msg1.Level);
                Assert.AreEqual("Left.csv:C4", msg1.LocationUrl);
                StringAssert.Contains("No member 'NonexistentMember' found in GameConfigPipelineTests.LeftInfo", msg1.Message);
            }
        }

        [Test]
        public async Task UnknownMemberTest_NestedMember()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key", "Name",   "Nested.StringMember",  "Nested.NonexistentMember"  },
                    new() { "Left1",   "Left 1", "a",                     "abc"                      },
                    new() { "Left2",   "Left 2", "b",                     ""                         }, // No value on this row -> no error. It's questionable if this is desirable, but this is natural for the current implementation.
                    new() { "Left3",   "Left 3", "c",                     "def"                      },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(2, report.GetMessageCountForLevel(GameConfigLogLevel.Error));

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual(GameConfigLogLevel.Error, msg0.Level);
            Assert.AreEqual("Left.csv:D2", msg0.LocationUrl);
            StringAssert.Contains("No member 'NonexistentMember' found in GameConfigPipelineTests.Nested", msg0.Message);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            Assert.AreEqual(GameConfigLogLevel.Error, msg1.Level);
            Assert.AreEqual("Left.csv:D4", msg1.LocationUrl);
            StringAssert.Contains("No member 'NonexistentMember' found in GameConfigPipelineTests.Nested", msg1.Message);
        }

        [Test]
        public async Task UnknownMemberTest_ListMember()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key", "Name",   "IntList.NonexistentMember" },
                    new() { "Left1",   "Left 1", "abc"                       },
                    new() { "Left2",   "Left 2", ""                          }, // No value on this row -> no error. It's questionable if this is desirable, but this is natural for the current implementation.
                    new() { "Left3",   "Left 3", "def"                       },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(2, report.GetMessageCountForLevel(GameConfigLogLevel.Error));

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual(GameConfigLogLevel.Error, msg0.Level);
            Assert.AreEqual("Left.csv:C2", msg0.LocationUrl);
            StringAssert.Contains("No member 'NonexistentMember' found in List<Int32>", msg0.Message);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            Assert.AreEqual(GameConfigLogLevel.Error, msg1.Level);
            Assert.AreEqual("Left.csv:C4", msg1.LocationUrl);
            StringAssert.Contains("No member 'NonexistentMember' found in List<Int32>", msg1.Message);
        }

        [Test]
        public async Task UnknownMemberTest_NonScalarNode()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key", "Name",   "NonexistentObject.A", "NonExistentArrayIndexed[0]", "NonExistentArrayVertical[]" },
                    new() { "Left1",   "Left 1", "abc",                 "def",                        "ghi" },
                    new() { "Left2",   "Left 2", "jkl",                 "",                           "" },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(6, report.GetMessageCountForLevel(GameConfigLogLevel.Error));

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual(GameConfigLogLevel.Error, msg0.Level);
            Assert.AreEqual("Left.csv:C2", msg0.LocationUrl);
            StringAssert.Contains("No member 'NonexistentObject' found in GameConfigPipelineTests.LeftInfo", msg0.Message);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            Assert.AreEqual(GameConfigLogLevel.Error, msg1.Level);
            Assert.AreEqual("Left.csv:D2", msg1.LocationUrl);
            StringAssert.Contains("No member 'NonExistentArrayIndexed' found in GameConfigPipelineTests.LeftInfo", msg1.Message);

            GameConfigBuildMessage msg2 = report.BuildMessages[2];
            Assert.AreEqual(GameConfigLogLevel.Error, msg2.Level);
            Assert.AreEqual("Left.csv:E2", msg2.LocationUrl);
            StringAssert.Contains("No member 'NonExistentArrayVertical' found in GameConfigPipelineTests.LeftInfo", msg2.Message);

            GameConfigBuildMessage msg3 = report.BuildMessages[3];
            Assert.AreEqual(GameConfigLogLevel.Error, msg3.Level);
            Assert.AreEqual("Left.csv:C3", msg3.LocationUrl);
            StringAssert.Contains("No member 'NonexistentObject' found in GameConfigPipelineTests.LeftInfo", msg3.Message);

            GameConfigBuildMessage msg4 = report.BuildMessages[4];
            Assert.AreEqual(GameConfigLogLevel.Error, msg4.Level);
            // \todo Empty collections don't have location info available. Fix this test if that's ever fixed.
            Assert.IsNull(msg4.LocationUrl);
            StringAssert.Contains("No member 'NonExistentArrayIndexed' found in GameConfigPipelineTests.LeftInfo", msg4.Message);

            GameConfigBuildMessage msg5 = report.BuildMessages[5];
            Assert.AreEqual(GameConfigLogLevel.Error, msg5.Level);
            // \todo Empty collections don't have location info available. Fix this test if that's ever fixed.
            Assert.IsNull(msg5.LocationUrl);
            StringAssert.Contains("No member 'NonExistentArrayVertical' found in GameConfigPipelineTests.LeftInfo", msg5.Message);
        }

        [Test]
        public async Task EmptyHeaderCell()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "",                     "Name",     ""          },
                    new() { "Left1",    "stray content",        "Left 1",   ""          },
                    new() { "Left2",    "more stray content",   "Left 2",   "stray"     },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(2, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:B:B", msg0.LocationUrl);
            StringAssert.Contains("This column contains nonempty cells, but its header cell is empty", msg0.Message);
            StringAssert.Contains("Nonempty content exists at: Left.csv:B2", msg0.Message);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            Assert.AreEqual("Left.csv:D:D", msg1.LocationUrl);
            StringAssert.Contains("This column contains nonempty cells, but its header cell is empty", msg1.Message);
            StringAssert.Contains("Nonempty content exists at: Left.csv:D3", msg1.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_Duplicate_Simple()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "Name",     },
                    new() { "Left1",    "Left 1",               },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:C:C", msg0.LocationUrl);
            StringAssert.Contains("Duplicate header cell for 'Name'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_Duplicate_ObjectMember()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "Nested.StringMember",  "Nested.StringMember"    },
                    new() { "Left1",    "Left 1",                                                    },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Duplicate header cell for 'Nested.StringMember'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_Duplicate_IndexedElement()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "IntList[0]", "IntList[0]", },
                    new() { "Left1",    "Left 1",                               },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Duplicate header cell for 'IntList[0]'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_Duplicate_IndexedElement_ObjectMember()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "IntList[0].Test", "IntList[0].Test", },
                    new() { "Left1",    "Left 1",                                         },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Duplicate header cell for 'IntList[0].Test'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_Duplicate_VerticalArray()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "IntList[]", "IntList[]",   },
                    new() { "Left1",    "Left 1",                               },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Duplicate header cell for 'IntList[]'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_Duplicate_VerticalArray_ObjectMember()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "IntList[].Test", "IntList[].Test", },
                    new() { "Left1",    "Left 1",                                       },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Duplicate header cell for 'IntList[].Test'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_ScalarVsObject()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "Nested",  "Nested.StringMember"    },
                    new() { "Left1",    "Left 1",                                       },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Conflicting header cells 'Nested' and 'Nested.StringMember'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_VerticalArray_ScalarVsObject()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "IntList[]", "IntList[].Test" },
                    new() { "Left1",    "Left 1",                                 },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Conflicting header cells 'IntList[]' and 'IntList[].Test'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_IndexedElement_ScalarVsObject()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "IntList[0]", "IntList[0].Test" },
                    new() { "Left1",    "Left 1",                                   },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Conflicting header cells 'IntList[0]' and 'IntList[0].Test'", msg0.Message);
        }

        [Test]
        public async Task ConflictingHeaderNode_Duplicate_And_ScalarVsObject()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "Nested",   "Nested",  "Nested.StringMember"    },
                    new() { "Left1",    "Left 1",                                                   },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(2, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:D:D", msg0.LocationUrl);
            StringAssert.Contains("Duplicate header cell for 'Nested'", msg0.Message);

            GameConfigBuildMessage msg1 = report.BuildMessages[1];
            Assert.AreEqual("Left.csv:E:E", msg1.LocationUrl);
            StringAssert.Contains("Conflicting header cells 'Nested' and 'Nested.StringMember'", msg1.Message);
        }

        [Test]
        public async Task VerticalArrayOfDeeplyNestedObject()
        {
            (TestSharedGameConfig gameConfig, GameConfigBuildReport report) = await BuildSharedGameConfigAsync(
                GameConfigTestHelper.ParseSpreadsheet("Left", new List<string>[]
                {
                    new() { "Id #key",  "Name",     "Array[].Deeply.Nested" },
                    new() { "Left1",    "Left 1",                           },
                }),
                GameConfigTestHelper.ParseSpreadsheet("Right", new List<string>[]
                {
                    new() { "Id #key" },
                })
            );

            Assert.IsNull(gameConfig);
            Assert.AreEqual(GameConfigLogLevel.Error, report.HighestMessageLevel);
            Assert.AreEqual(1, report.GetMessageCountForLevel(GameConfigLogLevel.Error));
            Assert.AreEqual(0, report.ValidationMessages.Length);

            GameConfigBuildMessage msg0 = report.BuildMessages[0];
            Assert.AreEqual("Left.csv:C:C", msg0.LocationUrl);
            StringAssert.Contains("At most 1 level of object nesting inside a vertical collection is currently supported.", msg0.Message);
        }
    }
}
