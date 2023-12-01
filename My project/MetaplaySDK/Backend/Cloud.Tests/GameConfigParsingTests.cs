// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloud.Tests
{
    [TestFixture]
    public class GameConfigParsingTests
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
        public class TestId : StringId<TestId> { }

        [MetaSerializable]
        public class DeeplyNestedInfo
        {
            [MetaMember(1)] public int      Int;
            [MetaMember(2)] public string   Str;

            public override bool Equals(object obj)
            {
                if (obj is not DeeplyNestedInfo other)
                    return false;

                return Equals(Int, other.Int)
                    && Equals(Str, other.Str);
            }

            public override int GetHashCode() => throw new NotImplementedException("do not use");
        }

        [MetaSerializable]
        public class NestedInfo
        {
            [MetaMember(1)] public int              Amount  = -1;
            [MetaMember(2)] public string           Type;
            [MetaMember(3)] public IntVector2       Coords;
            [MetaMember(4)] public DeeplyNestedInfo Deeply;
            [MetaMember(5)] public int[]            IntArray;

            public override bool Equals(object obj)
            {
                if (obj is not NestedInfo other)
                    return false;

                return Equals(Amount, other.Amount)
                    && Equals(Type, other.Type)
                    && Equals(Coords, other.Coords)
                    && Equals(Deeply, other.Deeply)
                    && Helper.SequenceEquals(IntArray, other.IntArray);
            }

            public override int GetHashCode() => throw new NotImplementedException("do not use");
        }

        [MetaSerializable]
        public class TestInfo : IGameConfigData<TestId>
        {
            public TestId ConfigKey => Id;

            [MetaMember(1)] public TestId                           Id          { get; set; }
            [MetaMember(2)] public string                           Name        { get; set; }
            [MetaMember(3)] public MetaRef<TestInfo>                Ref         { get; set; }
            [MetaMember(4)] public List<int>                        IntList     { get; set; }
            [MetaMember(5)] public int[]                            IntArray    { get; set; }
            [MetaMember(6)] public NestedInfo                       Nested      { get; set; }
            [MetaMember(7)] public List<NestedInfo>                 NestedList  { get; set; }
            [MetaMember(8)] public List<IntVector2>                 CoordList   { get; set; }
            [MetaMember(9)] public List<string>                     StringList  { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is not TestInfo other)
                    return false;

                return Equals(Id, other.Id)
                    && Equals(Name, other.Name)
                    && Equals(Ref, other.Ref)
                    && Helper.SequenceEquals(IntList, other.IntList)
                    && Helper.SequenceEquals(IntArray, other.IntArray)
                    && Equals(Nested, other.Nested)
                    && Helper.SequenceEquals(NestedList, other.NestedList)
                    && Helper.SequenceEquals(CoordList, other.CoordList)
                    && Helper.SequenceEquals(StringList, other.StringList);
            }

            public override int GetHashCode() => throw new NotImplementedException("do not use");

            // ToString for debugging convenience
            public override string ToString() => PrettyPrint.Compact(this).ToString();
        }

        [MetaSerializable]
        public class TestKeyValue : GameConfigKeyValue
        {
            [MetaMember(1)] public TestId           TestId;
            [MetaMember(2)] public int              Int0;
            [MetaMember(3)] public int              Int1;
            [MetaMember(5)] public string           Str0;
            [MetaMember(6)] public string           Str1;
            [MetaMember(7)] public string           Str2;
        }

        // \note Requires for TestId to be a valid MetaRef
        public class TestSharedGameConfig : SharedGameConfigBase
        {
            [GameConfigEntry("TestItems")]
            public GameConfigLibrary<TestId, TestInfo> TestItems { get; set; }
        }

        // BASIC PARSING

        // \todo [petri] error case: cell parse errors
        // \todo [petri] error case: non-existent members
        // \todo [petri] error case: missing Id (what should this actually do?)

        [Test]
        public void BasicTest()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "Name",                "Ref"      },
                    new() { "Item0",   "Some Name",           "RefA"     },
                    new() { "Item1",   "\tAnother Name '!/ ", "OtherRef" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), Name = "Some Name",             Ref = MetaRef<TestInfo>.FromKey(TestId.FromString("RefA"))     },
                    new TestInfo { Id = TestId.FromString("Item1"), Name = "\tAnother Name '!/ ",   Ref = MetaRef<TestInfo>.FromKey(TestId.FromString("OtherRef")) }
                }, items);
        }

        [Test]
        public void SlashSlashCommentTest()
        {
            // Test named ('// Name') and anonymous comments ('//') -- '// Name' must not parse into the Name member
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "// Name",             "Ref",      "//"                           },
                    new() { "Item0",   "Some Name",           "RefA",     "This is the first item"       },
                    new() { "Item1",   "\tAnother Name '!/ ", "OtherRef", "Comment for the second item"  },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), Ref = MetaRef<TestInfo>.FromKey(TestId.FromString("RefA"))     },
                    new TestInfo { Id = TestId.FromString("Item1"), Ref = MetaRef<TestInfo>.FromKey(TestId.FromString("OtherRef")) }
                }, items);
        }

        [Test]
        public void CommentTagTest()
        {
            // Test named ('#comment:Name') and anonymous comments ('#comment') -- #comment:Name must not parse into the Name member
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "#comment:Name",       "Ref",      "#comment"                     },
                    new() { "Item0",   "Some Name",           "RefA",     "This is the first item"       },
                    new() { "Item1",   "\tAnother Name '!/ ", "OtherRef", "Comment for the second item"  },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), Ref = MetaRef<TestInfo>.FromKey(TestId.FromString("RefA"))     },
                    new TestInfo { Id = TestId.FromString("Item1"), Ref = MetaRef<TestInfo>.FromKey(TestId.FromString("OtherRef")) }
                }, items);
        }

        [Test]
        public void EmptyRow()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "Name"    },
                    new() {},
                    new() { "Item0",   "Item 0"  },
                    new() { "" },
                    new() { "Item1",   "Item 1"  },
                    new() { "",        ""  },
                    new() { "Item2",   "Item 2"  },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), Name = "Item 0" },
                    new TestInfo { Id = TestId.FromString("Item1"), Name = "Item 1" },
                    new TestInfo { Id = TestId.FromString("Item2"), Name = "Item 2" },
                }, items);
        }

        /// <summary>
        /// Fully-empty columns are ok.
        /// In contrast, empty header cells in a non-empty column are not ok (see <see cref="GameConfigPipelineTests.EmptyHeaderCell"/>).
        /// </summary>
        [Test]
        public void EmptyColumn()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key",  "", "Name"    },
                    new() { "Item0",    "", "Item 0"  },
                    new() { "Item1",    "", "Item 1"  },
                    new() { "Item2",    "", "Item 2"  },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), Name = "Item 0" },
                    new TestInfo { Id = TestId.FromString("Item1"), Name = "Item 1" },
                    new TestInfo { Id = TestId.FromString("Item2"), Name = "Item 2" },
                }, items);
        }

        // IMPLICIT COLLECTIONS

        [Test]
        public void ImplicitCollections()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "IntList[0]", "IntArray[]" }, // both indexed and vertical collections must produce an empty array/list
                    new() { "Item0",   "",           ""  },
                    new() { "Item1",   "",           ""  },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), IntList = new List<int>(), IntArray = Array.Empty<int>() },
                    new TestInfo { Id = TestId.FromString("Item1"), IntList = new List<int>(), IntArray = Array.Empty<int>() }
                }, items);
        }

        [Test]
        public void ImplicitCollectionsInObjects()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "Nested.Type", "Nested.IntArray[]"    },
                    new() { "Item0",   "Some",        ""                     }, // Type triggers Nested to be created -> Nested.IntArray should be empty
                    new() { "Item1",   "",            ""                     }, // Empty collection doesn't trigger Nested to get allocated
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), Nested = new NestedInfo { Type = "Some", IntArray = Array.Empty<int>() } },
                    new TestInfo { Id = TestId.FromString("Item1"), Nested = null },
                }, items);
        }

        [Test]
        public void InheritVariantImplicitCollections()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant", "Id #key", "IntList[0]", "IntArray[]" },
                    new() { "",         "Item0",   "10",         "20"         },
                    new() { "A/X",      "Item0",   "",           ""           }, // empty collections inherit from baseline
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), IntList = new List<int>{ 10 }, IntArray = new int[] { 20 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), IntList = new List<int>{ 10 }, IntArray = new int[] { 20 } }),
                }, items);
        }

        // MULTI-REPRESENTATION COLLECTIONS

        [Test]
        public void CollectionMultiRepresentation()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "IntArray", "IntArray[]", "IntArray[0]",  "IntArray[1]"   },
                    new() { "Item0",   "",         "",           "",             ""              }, // no representation used
                    new() { "Item1",   "[]",       "",           "",             ""              }, // scalar representation used (empty)
                    new() { "Item2",   "",         "10",         "",             ""              }, // vertical representation used
                    new() { "",        "",         "20",         "",             ""              }, // vertical representation used
                    new() { "Item3",   "",         "",           "30",           "40"            }, // indexed representation used
                    new() { "Item4",   "1,2,3",    "",           "",             ""              }, // scalar representation
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), IntArray = new int[] { } },
                    new TestInfo { Id = TestId.FromString("Item1"), IntArray = new int[] { } },
                    new TestInfo { Id = TestId.FromString("Item2"), IntArray = new int[] { 10, 20 } },
                    new TestInfo { Id = TestId.FromString("Item3"), IntArray = new int[] { 30, 40 } },
                    new TestInfo { Id = TestId.FromString("Item4"), IntArray = new int[] { 1, 2, 3 } },
                }, items);
        }

        [Test]
        public void CollectionVariantOverrides()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant", "Id #key", "IntArray[]", "IntArray", "/:B/X" },
                    new() { "",         "Item0",   "5",          "",         ""      }, // Item0: row variant A/X overrides array with empty one
                    new() { "A/X",      "",        "",           "[]",       ""      },
                    new() { "",         "Item1",   "1",          "",         "[]"    }, // Item1: column variant B/X overrides array with empty one
                    new() { "",         "",        "2",          "",         ""      },
                    new() { "",         "Item2",   "5",          "",         ""      }, // Item2: row variant A/X but no override
                    new() { "",         "",        "6",          "",         ""      },
                    new() { "A/X",      "",        "",           "",         ""      },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), IntArray = new int[] { 5 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), IntArray = new int[] { } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), IntArray = new int[] { 1, 2 } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item1"), IntArray = new int[] { } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item2"), IntArray = new int[] { 5, 6 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item2"), IntArray = new int[] { 5, 6 } }),
                }, items);
        }

        // NESTED TYPES

        [Test]
        public void NestedTypes()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "Nested.Amount", "Nested.Type", "Nested.Coords", "Nested.Deeply.Int" },
                    new() { "Item0",   "10",            "Foo",         "(5, 1)",        "42" },
                    new() { "Item1",   "",              "",            "(1, 3)" },
                    new() { "Item2",   "" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), Nested = new NestedInfo { Amount = 10, Type = "Foo", Coords = new IntVector2(5, 1), Deeply = new DeeplyNestedInfo { Int = 42 } } },
                    new TestInfo { Id = TestId.FromString("Item1"), Nested = new NestedInfo { Amount = -1, Type = null,  Coords = new IntVector2(1, 3),Deeply = null } },
                    new TestInfo { Id = TestId.FromString("Item2"), Nested = null },
                }, items);
        }

        // SINGLE-CELL ARRAYS

        [Test]
        public void SingleCellArray()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "IntList", "IntArray"      },
                    new() { "Item0",   "10,20",   "5"             },
                    new() { "Item1",   "50",      "100,101,102"   },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), IntList = new List<int> { 10, 20 }, IntArray = new int[] { 5 } },
                    new TestInfo { Id = TestId.FromString("Item1"), IntList = new List<int> { 50 },     IntArray = new int[] { 100, 101, 102 } },
                    new TestInfo { Id = TestId.FromString("Item2"), IntList = null,                     IntArray = null },
                }, items);
        }

        // HORIZONTAL ARRAYS

        // \todo [petri] error case: duplicate indexes: IntList[0], IntList[1], IntList[0]
        // \todo [petri] error case: negative index: IntList[-1]
        // \todo [petri] edge case: missing/non-ordered indexes: IntList[2], IntList[0]
        // \todo [petri] error case: empty element followed by non-empty
        // \todo [petri] nested arrays/indexes (eg, NestedList[0].NestedList[1].Member) -- should this be supported?

        [Test]
        public void HorizontalArray_Simple()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "IntList[0]", "IntList[1]", "IntArray[0]", "IntArray[1]", "IntArray[2]", },
                    new() { "Item0",   "10",         "20",         "5",           "" },
                    new() { "Item1",   "50",         "",           "100",         "101",         "102" },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), IntList = new List<int> { 10, 20 }, IntArray = new int[] { 5 } },
                    new TestInfo { Id = TestId.FromString("Item1"), IntList = new List<int> { 50 },     IntArray = new int[] { 100, 101, 102 } },
                    new TestInfo { Id = TestId.FromString("Item2"), IntList = new List<int>(),          IntArray = Array.Empty<int>() },
                }, items);
        }

        [Test]
        public void HorizontalArray_Length1()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "StringList[0]" },
                    new() { "Item0",   "abc  , def" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), StringList = new List<string> { "abc  , def" } },
                }, items);
        }

        [Test]
        public void HorizontalArray_Complex()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "CoordList[0]", "CoordList[1]", "CoordList[2]" },
                    new() { "Item0",   "(5, 2)",       "(4, 4)" },
                    new() { "Item1",   "(9, 9)" },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), CoordList = new List<IntVector2>{ new IntVector2(5, 2), new IntVector2(4, 4) } },
                    new TestInfo { Id = TestId.FromString("Item1"), CoordList = new List<IntVector2>{ new IntVector2(9, 9) } },
                    new TestInfo { Id = TestId.FromString("Item2"), CoordList = new List<IntVector2>() },
                }, items);
        }

        // \note Not yet implemented
        [Test]
        public void HorizontalArray_ComplexMembers()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "NestedList[0].Amount", "NestedList[0].Type", "NestedList[1].Amount", "NestedList[1].Type" },
                    new() { "Item0",   "20",                   "Gold",               "5",                    "Gems"               },
                    new() { "Item1",   "100",                  "Gold",               "",                     "Sword"              },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), NestedList = new List<NestedInfo> { new NestedInfo { Amount = 20, Type = "Gold" }, new NestedInfo { Amount = 5, Type = "Gems" } } },
                    new TestInfo { Id = TestId.FromString("Item1"), NestedList = new List<NestedInfo> { new NestedInfo { Amount = 100, Type = "Gold" }, new NestedInfo { Type = "Sword" } } },
                    new TestInfo { Id = TestId.FromString("Item2"), NestedList = new List<NestedInfo>() },
                }, items);
        }

        // \note Not yet implemented
        [Test]
        public void HorizontalArray_ComplexNestedMembers()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "NestedList[0].Amount", "NestedList[0].Type", "NestedList[0].Deeply.Int", "NestedList[1].Amount", "NestedList[1].Type", "NestedList[1].Deeply.Int" },
                    new() { "Item0",   "20",                   "Gold",               "-10",                      "5",                    "Gems",               "-5" },
                    new() { "Item1",   "100",                  "Gold",               "-20",                      "",                     "Sword" },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), NestedList = new List<NestedInfo> { new NestedInfo { Amount = 20, Type = "Gold", Deeply = new() { Int = -10 } }, new NestedInfo { Amount = 5, Type = "Gems", Deeply = new() { Int = -5 } } } },
                    new TestInfo { Id = TestId.FromString("Item1"), NestedList = new List<NestedInfo> { new NestedInfo { Amount = 100, Type = "Gold", Deeply = new() { Int = -20 } }, new NestedInfo { Type = "Sword" } } },
                    new TestInfo { Id = TestId.FromString("Item2"), NestedList = new List<NestedInfo>() },
                }, items);
        }

        // VERTICAL ARRAYS

        // \todo [petri] error case: conflicting columns: NestedList[0].Type, NestedList[0].Type
        // \todo [petri] error case: empty element followed by non-empty

        [Test]
        public void VerticalArray_Simple()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "IntList[]", "IntArray[]"  },
                    new() { "Item0",   "10",        "5"           },
                    new() { "",        "20",        ""            },
                    new() { "Item1",   "50",        "100"         },
                    new() { "",        "",          "101"         },
                    new() { "",        "",          "102"         },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), IntList = new List<int> { 10, 20 }, IntArray = new int[] { 5 } },
                    new TestInfo { Id = TestId.FromString("Item1"), IntList = new List<int> { 50 },     IntArray = new int[] { 100, 101, 102 } },
                    new TestInfo { Id = TestId.FromString("Item2"), IntList = new List<int>(),          IntArray = new int[] { } },
                }, items);
        }

        [Test]
        public void VerticalArray_ComplexMembers()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "NestedList[].Amount", "NestedList[].Type" },
                    new() { "Item0",   "20",                  "Gold" },
                    new() { "",        "5",                   "Gems" },
                    new() { "Item1",   "100",                 "Gold" },
                    new() { "",        "",                    "Sword" },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), NestedList = new List<NestedInfo> { new() { Amount = 20, Type = "Gold" }, new() { Amount = 5, Type = "Gems" } } },
                    new TestInfo { Id = TestId.FromString("Item1"), NestedList = new List<NestedInfo> { new() { Amount = 100, Type = "Gold", }, new() { Type = "Sword" } } },
                    new TestInfo { Id = TestId.FromString("Item2"), NestedList = new List<NestedInfo>() },
                }, items);
        }

        [Test]
        [Ignore("\\todo: Feature not yet supported")]
        public void VerticalArray_ComplexNestedMembers()
        {
            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "NestedList[].Amount", "NestedList[].Type", "NestedList[].Deeply.Int" },
                    new() { "Item0",   "20",                  "Gold",              "-10" },
                    new() { "",        "5",                   "Gems",              "-20" },
                    new() { "Item1",   "100",                 "Gold",              "" },
                    new() { "",        "",                    "Sword",             "" },
                    new() { "Item2" },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo { Id = TestId.FromString("Item0"), NestedList = new List<NestedInfo> { new() { Amount = 20, Type = "Gold", Deeply = new() { Int = -10 } }, new() { Amount = 5, Type = "Gems", Deeply = new() { Int = -20 } } } },
                    new TestInfo { Id = TestId.FromString("Item1"), NestedList = new List<NestedInfo> { new() { Amount = 100, Type = "Gold", }, new() { Type = "Sword" } } },
                    new TestInfo { Id = TestId.FromString("Item2"), NestedList = null },
                }, items);
        }

        #region Variants

        [Test]
        public void Variant_Basic_RowOverride()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",             "Nested.Type"                   },
                    new() { "",             "Item0",        "item 0",           "item 0 nested type"            },
                    new() { "A/X",          "",             "item 0 modified",  ""                              },
                    new() { "",             "Item1",        "item 1",           "item 1 nested type"            },
                    new() { "A/Y",          "Item1",        "item 1 modified",  "item 1 nested type modified"   },
                    new() { "A/Y",          "Item0",        "",                 "item 0 nested type modified"   },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Type = "item 1 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type modified" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type modified" } }),
                }, items);
        }

        [Test]
        public void Variant_Basic_RowOverride_VariantBeforeBaseline()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",             "Nested.Type"                   },
                    new() { "A/Y",          "Item1",        "item 1 modified",  "item 1 nested type modified"   },
                    new() { "A/X",          "Item0",        "item 0 modified",  ""                              },
                    new() { "",             "Item0",        "item 0",           "item 0 nested type"            },
                    new() { "A/Y",          "",             "",                 "item 0 nested type modified"   },
                    new() { "",             "Item1",        "item 1",           "item 1 nested type"            },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type modified" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type modified" } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Type = "item 1 nested type" } }),
                }, items);
        }

        [Test]
        public void Variant_Basic_Append()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",             "Nested.Type"                   },
                    new() { "",             "Item0",        "item 0",           "item 0 nested type"            },
                    new() { "A/X",          "Item1",        "appended item",    ""                              },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "appended item",     Nested = null }),
                }, items);
        }

        [Test]
        public void Variant_Basic_ColumnOverride()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key",      "Name",     "/:A/X",            "/:A/Y",            "Nested.Type",          "/:A/Y" },
                    new() { "Item0",        "item 0",   "item 0 modified",  "",                 "item 0 nested type",   "item 0 nested type modified" },
                    new() { "Item1",        "item 1",   "",                 "item 1 modified",  "item 1 nested type",   "item 1 nested type modified" },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type modified" } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Type = "item 1 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type modified" } }),
                }, items);
        }

        [Test]
        public void Variant_Basic_RowOverrideWithMultipleVariantsPerRow()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",             "Nested.Type"                   },
                    new() { "",             "Item0",        "item 0",           "item 0 nested type"            },
                    new() { "A/X, A/Y",     "",             "item 0 modified",  ""                              },
                    new() { "",             "Item1",        "item 1",           "item 1 nested type"            },
                    new() { "A/Y, A/X",     "Item1",        "item 1 modified",  "item 1 nested type modified"   },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Type = "item 1 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type modified" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type modified" } }),
                }, items);
        }

        [Test]
        public void Variant_Basic_ColumnOverrideWithMultipleVariantsPerColumn()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key",      "Name",     "/:A/X, A/Y",       "Nested.Type",          "/:A/Y" },
                    new() { "Item0",        "item 0",   "item 0 modified",  "item 0 nested type",   "item 0 nested type modified" },
                    new() { "Item1",        "item 1",   "item 1 modified",  "item 1 nested type",   "item 1 nested type modified" },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type modified" } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Type = "item 1 nested type" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type modified" } }),
                }, items);
        }

        [Test]
        public void Variant_Basic_RowAndColumnOverride()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",             "/:A/X",            "Nested.Type"                   },
                    new() { "",             "Item0",        "item 0",           "item 0 modified",  "item 0 nested type"            },
                    new() { "",             "Item1",        "item 1",           "",                 "item 1 nested type"            },
                    new() { "A/Y",          "Item1",        "item 1 modified",  "",                 "item 1 nested type modified"   },
                    new() { "A/Y",          "Item0",        "",                 "",                 "item 0 nested type modified"   },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0 modified",   Nested = new NestedInfo { Type = "item 0 nested type" } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Type = "item 1 nested type" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1 modified",   Nested = new NestedInfo { Type = "item 1 nested type modified" } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Type = "item 0 nested type modified" } }),
                }, items);
        }

        [Test]
        public void Variant_OverrideNestedPartially()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",     "Nested.Amount",    "Nested.Type",                  "/:B/X" },
                    new() { "",             "Item0",        "item 0",   "123",              "item 0 nested type",           "item 0 nested type modified B" },
                    new() { "A/X",          "Item0",        "",         "",                 "item 0 nested type modified A",  "" },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 123, Type = "item 0 nested type" } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 123, Type = "item 0 nested type modified B" } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 123, Type = "item 0 nested type modified A" } }),
                }, items);
        }

        [Test]
        public void Variant_OverrideDeeplyNestedPartially()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",     "Nested.Amount",    "Nested.Deeply.Int",    "Nested.Deeply.Str",        "/:B/X" },
                    new() { "",             "Item0",        "item 0",   "123",              "456",                  "item 0 deep",              "item 0 deep modified B" },
                    new() { "A/X",          "Item0",        "",         "",                 "",                     "item 0 deep modified A",   "" },
                    new() { "",             "Item1",        "item 1",   "",                 "",                     "item 1 deep",              "item 1 deep modified B" },
                    new() { "A/X",          "Item1",        "",         "",                 "",                     "item 1 deep modified A",   "" },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 123, Deeply = new DeeplyNestedInfo { Int = 456, Str = "item 0 deep" } } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 123, Deeply = new DeeplyNestedInfo { Int = 456, Str = "item 0 deep modified B" } } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 123, Deeply = new DeeplyNestedInfo { Int = 456, Str = "item 0 deep modified A" } } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Deeply = new DeeplyNestedInfo { Str = "item 1 deep" } } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Deeply = new DeeplyNestedInfo { Str = "item 1 deep modified B" } } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            Nested = new NestedInfo { Deeply = new DeeplyNestedInfo { Str = "item 1 deep modified A" } } }),
                }, items);
        }

        [Test]
        public void Variant_OverrideNestedThatIsNotInBaseline()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",     "Nested.Amount",    "/:B/X" },
                    new() { "",             "Item0",        "item 0",   "",                 "456" },
                    new() { "A/X",          "Item0",        "",         "123",              "" },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = null }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 456 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            Nested = new NestedInfo { Amount = 123 } }),
                }, items);
        }

        [Test]
        public void Variant_OverrideArray_Simple_Vertical()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",     "IntArray[]",   "/:B/X",    "/:B/Y",    "/:B/Z",    "/:B/W" },
                    new() { "",             "Item0",        "item 0",   "1",            "110",      "1110",     "",         "111110" },
                    new() { "",             "",             "",         "2",            "220",      "",         "22220",    "222220" },
                    new() { "",             "",             "",         "3",            "",         "",         "",         "333330" },
                    new() { "",             "",             "",         "",             "",         "",         "",         "444440" },
                    new() { "A/X",          "Item0",        "",         "11",           "",         "",         "",         "" },
                    new() { "",             "",             "",         "22",           "",         "",         "",         "" },
                    new() { "A/Y",          "Item0",        "",         "111",          "",         "",         "",         "" },
                    new() { "",             "",             "",         "",             "",         "",         "",         "" },
                    new() { "A/Z",          "Item0",        "",         "",             "",         "",         "",         "" },
                    new() { "",             "",             "",         "2222",         "",         "",         "",         "" },
                    new() { "",             "",             "",         "",             "",         "",         "",         "" },
                    new() { "A/W",          "Item0",        "",         "11111",        "",         "",         "",         "" },
                    new() { "",             "",             "",         "22222",        "",         "",         "",         "" },
                    new() { "",             "",             "",         "33333",        "",         "",         "",         "" },
                    new() { "",             "",             "",         "44444",        "",         "",         "",         "" },
                    new() { "",             "Item1",        "item 1",   "",             "1230",     "",         "",         "" },
                    new() { "",             "",             "",         "",             "4560",     "",         "",         "" },
                    new() { "A/X",          "Item1",        "",         "123",          "",         "",         "",         "" },
                    new() { "",             "",             "",         "456",          "",         "",         "",         "" },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 1, 2, 3 } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 110, 220 } }),
                    ("B/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 1110 } }),
                    ("B/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 0, 22220 } }),
                    ("B/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 111110, 222220, 333330, 444440 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 11, 22 } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 111 } }),
                    ("A/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 0, 2222 } }),
                    ("A/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 11111, 22222, 33333, 44444 } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            IntArray = new int[] { } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            IntArray = new int[] { 1230, 4560 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            IntArray = new int[] { 123, 456 } }),
                }, items);
        }

        [Test]
        public void Variant_OverrideArray_Simple_Horizontal()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",     "IntArray[0]",  "/:B/X","/:B/Y","/:B/W",    "IntArray[1]",  "/:B/X","/:B/Z","/:B/W",    "IntArray[2]",  "/:B/W",    "IntArray[3]",  "/:B/W"     },
                    new() { "",             "Item0",        "item 0",   "1",            "110","1110","111110",      "2",            "220","22220","222220",     "3",            "333330",   "",             "444440"    },
                    new() { "A/X",          "Item0",        "",         "11",           "","","",                   "22",           "","","",                   "",             "",         "",             ""          },
                    new() { "A/Y",          "Item0",        "",         "111",          "","","",                   "",             "","","",                   "",             "",         "",             ""          },
                    new() { "A/Z",          "Item0",        "",         "",             "","","",                   "2222",         "","","",                   "",             "",         "",             ""          },
                    new() { "A/W",          "Item0",        "",         "11111",        "","","",                   "22222",        "","","",                   "33333",        "",         "44444",        ""          },
                    new() { "",             "Item1",        "item 1",   "",             "1230","","",               "",             "4560","","",               "",             "",         "",             ""          },
                    new() { "A/X",          "Item1",        "",         "123",          "","","",                   "456",          "","","",                   "",             "",         "",             ""          },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 1, 2, 3 } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 110, 220 } }),
                    ("B/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 1110 } }),
                    ("B/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 111110, 222220, 333330, 444440 } }),
                    ("B/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 0, 22220 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 11, 22 } }),
                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 111 } }),
                    ("A/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 0, 2222 } }),
                    ("A/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            IntArray = new int[] { 11111, 22222, 33333, 44444 } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            IntArray = new int[] { } }),
                    ("B/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            IntArray = new int[] { 1230, 4560 } }),
                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            IntArray = new int[] { 123, 456 } }),
                }, items);
        }

        [Test]
        public void Variant_OverrideArray_Complex_Vertical()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",     "NestedList[].Amount",  "/:B/X", "/:B/Y", "/:B/Z", "/:B/W", "NestedList[].Type",    "/:B/X", "/:B/Y", "/:B/Z", "/:B/W"  },
                    new() { "",             "Item0",        "item 0",   "1",                    "110","1110","","111110",           "a",                    "xx0","","","xxxxx0"                 },
                    new() { "",             "",             "",         "2",                    "220","","22220","222220",          "b",                    "","","",""                         },
                    new() { "",             "",             "",         "3",                    "","","","",                        "c",                    "zz0","","zzzz0","zzzzz0"            },
                    new() { "",             "",             "",         "",                     "","","","444440",                  "",                     "","","",""                         },
                    new() { "A/X",          "Item0",        "",         "11",                   "","","","",                        "xx",                   "","","",""                         },
                    new() { "",             "",             "",         "22",                   "","","","",                        "",                     "","","",""                         },
                    new() { "",             "",             "",         "",                     "","","","",                        "zz",                   "","","",""                         },
                    new() { "A/Y",          "Item0",        "",         "111",                  "","","","",                        "",                     "","","",""                         },
                    new() { "",             "",             "",         "",                     "","","","",                        "",                     "","","",""                         },
                    new() { "A/Z",          "Item0",        "",         "",                     "","","","",                        "",                     "","","",""                         },
                    new() { "",             "",             "",         "2222",                 "","","","",                        "",                     "","","",""                         },
                    new() { "",             "",             "",         "",                     "","","","",                        "zzzz",                 "","","",""                         },
                    new() { "",             "",             "",         "",                     "","","","",                        "",                     "","","",""                         },
                    new() { "A/W",          "Item0",        "",         "11111",                "","","","",                        "xxxxx",                "","","",""                         },
                    new() { "",             "",             "",         "22222",                "","","","",                        "",                     "","","",""                         },
                    new() { "",             "",             "",         "",                     "","","","",                        "zzzzz",                "","","",""                         },
                    new() { "",             "",             "",         "44444",                "","","","",                        "",                     "","","",""                         },
                    new() { "",             "Item1",        "item 1",   "",                     "1230","","","",                    "",                     "","","",""                         },
                    new() { "",             "",             "",         "",                     "","","","",                        "",                     "xyz0","","",""                     },
                    new() { "A/X",          "Item1",        "",         "123",                  "","","","",                        "",                     "","","",""                         },
                    new() { "",             "",             "",         "",                     "","","","",                        "xyz",                  "","","",""                         },
                }));

            // \note Config handling may output the variants in non-intuitive order
            //       when a column override has an empty value and a following column override has a nonempty value,
            //       such as B/Z and B/W here.
            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 1, Type = "a" },
                                                                                                                                            new NestedInfo{ Amount = 2, Type = "b" },
                                                                                                                                            new NestedInfo{ Amount = 3, Type = "c" } } }),

                    ("B/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 110, Type = "xx0" },
                                                                                                                                            new NestedInfo{ Amount = 220             },
                                                                                                                                            new NestedInfo{               Type = "zz0" } } }),

                    ("B/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 1110 } } }),

                    ("B/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 111110, Type = "xxxxx0" },
                                                                                                                                            new NestedInfo{ Amount = 222220                 },
                                                                                                                                            new NestedInfo{                  Type = "zzzzz0" },
                                                                                                                                            new NestedInfo{ Amount = 444440                 } } }),

                    ("B/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { null,
                                                                                                                                            new NestedInfo{ Amount = 22220              },
                                                                                                                                            new NestedInfo{               Type = "zzzz0" } } }),

                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 11, Type = "xx" },
                                                                                                                                            new NestedInfo{ Amount = 22             },
                                                                                                                                            new NestedInfo{              Type = "zz" } } }),

                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 111 } } }),

                    ("A/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { null,
                                                                                                                                            new NestedInfo{ Amount = 2222              },
                                                                                                                                            new NestedInfo{              Type = "zzzz" } } }),

                    ("A/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 11111, Type = "xxxxx" },
                                                                                                                                            new NestedInfo{ Amount = 22222                 },
                                                                                                                                            new NestedInfo{                 Type = "zzzzz" },
                                                                                                                                            new NestedInfo{ Amount = 44444                 } } }),

                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            NestedList = null }),

                    ("B/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 1230              },
                                                                                                                                            new NestedInfo{              Type = "xyz0" } } }),

                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 123              },
                                                                                                                                            new NestedInfo{              Type = "xyz" } } }),

                }, items);
        }

        [Test]
        public void Variant_OverrideArray_Complex_Horizontal()
        {
            (string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Variant",     "Id #key",      "Name",     "NestedList[0].Amount", "/:B/X","/:B/Y","/:B/W",    "NestedList[0].Type", "/:B/X","/:B/W",
                                                                        "NestedList[1].Amount", "/:B/X","/:B/Z","/:B/W",    "NestedList[1].Type", "/:B/X",
                                                                        "NestedList[2].Amount",                             "NestedList[2].Type", "/:B/X","/:B/Z","/:B/W",
                                                                        "NestedList[3].Amount", "/:B/W",                    "NestedList[3].Type" },

                    new() { "",             "Item0",        "item 0",   "1", "110","1110","111110",                         "a", "xx0","xxxxx0",
                                                                        "2", "220", "22220", "222220",                      "b", "",
                                                                        "3",                                                "c", "zz0", "zzzz0", "zzzzz0",
                                                                        "", "444440",                                       "" },

                    new() { "A/X",          "Item0",        "",         "11", "","","",                                     "xx", "","",
                                                                        "22", "","","",                                     "", "",
                                                                        "",                                                 "zz", "","","" },

                    new() { "A/Y",          "Item0",        "",         "111", "","","",                                    "", "","",
                                                                        "", "","","",                                       "", "" },

                    new() { "A/Z",          "Item0",        "",         "", "","","",                                       "", "","",
                                                                        "2222", "","","",                                   "", "",
                                                                        "",                                                 "zzzz", "","","",
                                                                        "", "",                                             "" },

                    new() { "A/W",          "Item0",        "",         "11111", "","","",                                  "xxxxx", "","",
                                                                        "22222", "","","",                                  "", "",
                                                                        "",                                                 "zzzzz", "","","",
                                                                        "44444", "",                                        "" },

                    new() { "",             "Item1",        "item 1",   "", "1230","","",                                   "", "","",
                                                                        "", "","","",                                       "", "xyz0" },

                    new() { "A/X",          "Item1",        "",         "123", "","","",                                    "", "","",
                                                                        "", "","","",                                       "xyz", "" },
                }));

            Assert.AreEqual(
                new (string, TestInfo)[]
                {
                    (null,  new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 1, Type = "a" },
                                                                                                                                            new NestedInfo{ Amount = 2, Type = "b" },
                                                                                                                                            new NestedInfo{ Amount = 3, Type = "c" } } }),

                    ("B/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 110, Type = "xx0" },
                                                                                                                                            new NestedInfo{ Amount = 220             },
                                                                                                                                            new NestedInfo{               Type = "zz0" } } }),

                    ("B/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 1110 } } }),

                    ("B/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 111110, Type = "xxxxx0" },
                                                                                                                                            new NestedInfo{ Amount = 222220                 },
                                                                                                                                            new NestedInfo{                  Type = "zzzzz0" },
                                                                                                                                            new NestedInfo{ Amount = 444440                 } } }),

                    ("B/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { null,
                                                                                                                                            new NestedInfo{ Amount = 22220              },
                                                                                                                                            new NestedInfo{               Type = "zzzz0" } } }),


                    ("A/X", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 11, Type = "xx" },
                                                                                                                                            new NestedInfo{ Amount = 22             },
                                                                                                                                            new NestedInfo{              Type = "zz" } } }),

                    ("A/Y", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 111 } } }),

                    ("A/Z", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { null,
                                                                                                                                            new NestedInfo{ Amount = 2222              },
                                                                                                                                            new NestedInfo{              Type = "zzzz" } } }),

                    ("A/W", new TestInfo { Id = TestId.FromString("Item0"), Name = "item 0",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 11111, Type = "xxxxx" },
                                                                                                                                            new NestedInfo{ Amount = 22222                 },
                                                                                                                                            new NestedInfo{                 Type = "zzzzz" },
                                                                                                                                            new NestedInfo{ Amount = 44444                 } } }),
                    (null,  new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            NestedList = null }),

                    ("B/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 1230              },
                                                                                                                                            new NestedInfo{              Type = "xyz0" } } }),

                    ("A/X", new TestInfo { Id = TestId.FromString("Item1"), Name = "item 1",            NestedList = new List<NestedInfo> { new NestedInfo{ Amount = 123              },
                                                                                                                                            new NestedInfo{              Type = "xyz" } } }),

                }, items);
        }

        #endregion

        #region GameConfigKeyValue

        [Test]
        public void KeyValue()
        {
            (string MemberName, object Value)[] members = GameConfigTestHelper.ParseNonVariantKeyValueMembers<TestKeyValue>(
                new GameConfigParseKeyValuePipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestKeyValue", new List<string>[]
                {
                    new() { "Str1",     "test string 1" },
                    new() { "Str2" },
                    new() { "Int0",     "123" },
                    new() { "Int1",     "456" },
                    new() { "TestId",   "TestIdValue" },
                }));

            Assert.AreEqual(
                new (string, object)[]
                {
                    ("Str1",    "test string 1"),
                    ("Str2",    ""),
                    ("Int0",    123),
                    ("Int1",    456),
                    ("TestId",  TestId.FromString("TestIdValue")),
                }, members);
        }

        [Test]
        public void KeyValue_Variant()
        {
            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo("Test.csv");
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);

            (string VariantIdMaybe, string MemberName, object Value)[] members = GameConfigTestHelper.ParseKeyValueMembers<TestKeyValue>(
                buildLog,
                new GameConfigParseKeyValuePipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestKeyValue", new List<string>[]
                {
                    new() { "/Variant",     "",         "" },

                    new() { "",             "Str1",     "test string 1" },
                    new() { "A/X",          "",         "test string 1 x" },
                    new() { "A/Y",          "Str1",     "test string 1 y" },
                    new() { "A/Z",          "Str1" },

                    new() { "A/X",          "Int0",     "1230" },
                    new() { "",             "Int0",     "123" },
                    new() { "A/Y",          "",         "12300" },

                    new() { "",             "Int1",     "456" },

                    new() { "A/W",          "Str1",     "test string 1 w" },

                    new() { "A/Z",          "Int0",     "123000" },
                    new() { "A/W",          "Int0" },

                    new() { "A/X",          "Str0",     "test string 0 x" },

                    new() { "A/X",          "Str2" },
                    new() { "",             "Str2" },
                }));

            Assert.AreEqual(
                new (string, string, object)[]
                {
                    (null,      "Str1",     "test string 1"),
                    ("A/X",     "Str1",     "test string 1 x"),
                    ("A/Y",     "Str1",     "test string 1 y"),
                    ("A/Z",     "Str1",     "test string 1"),

                    ("A/X",     "Int0",     1230),
                    (null,      "Int0",     123),
                    ("A/Y",     "Int0",     12300),

                    (null,      "Int1",     456),

                    ("A/W",     "Str1",     "test string 1 w"),

                    ("A/Z",     "Int0",     123000),
                    ("A/W",     "Int0",     123),

                    ("A/X",     "Str0",     "test string 0 x"),

                    ("A/X",     "Str2",     ""),
                    (null,      "Str2",     ""),
                }, members);
        }

        #endregion

        [Test]
        public void Aliases()
        {
            (List<TestId> Aliases, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithAliases<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Aliases",                     "Id #key",  "Name"    },
                    new() { "Item0Alias",                   "Item0",    "item 0", },
                    new() { "",                             "Item1",    "item 1"  },
                    new() { "Item2Alias, Item2OtherAlias",  "Item2",    "item 2"  },
                }));

            Assert.AreEqual(
                new (List<TestId>, TestInfo)[]
                {
                    (new List<TestId>{ TestId.FromString("Item0Alias") },
                     new TestInfo{ Id = TestId.FromString("Item0"), Name = "item 0" }),

                    (null,
                     new TestInfo{ Id = TestId.FromString("Item1"), Name = "item 1" }),

                    (new List<TestId>{ TestId.FromString("Item2Alias"), TestId.FromString("Item2OtherAlias") },
                     new TestInfo{ Id = TestId.FromString("Item2"), Name = "item 2" }),
                }, items);
        }

        [Test]
        public void AliasesAndVariants()
        {
            // \note Aliases defined in variant rows are not a very useful feature since it has the same effect
            //       as just defining them in the baseline row (aliases are not per-variant).
            //       Supporting it anyway for now, for compatibility.

            (List<TestId> Aliases, string VariantIdMaybe, TestInfo Item)[] items = GameConfigTestHelper.ParseItemsWithAliasesAndVariants<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "/Aliases",                                         "/Variant",     "Id #key",  "Name"    },
                    new() { "Item0Alias",                                       "",             "Item0",    "item 0", },
                    new() { "Item0AliasInVariant",                              "X/Y",          "",         "item 0 modified", },
                    new() { "",                                                 "",             "Item1",    "item 1"  },
                    new() { "Item1AliasInVariant,Item1OtherAliasInVariant",     "X/Y",          "Item1",    "item 1 modified"  },
                    new() { "Item2Alias, Item2OtherAlias",                      "",             "Item2",    "item 2"  },
                    new() { "",                                                 "X/Y",          "",         "item 2 modified"  },
                }));

            Assert.AreEqual(
                new (List<TestId>, string, TestInfo)[]
                {
                    (new List<TestId>{ TestId.FromString("Item0Alias") },
                     null,
                     new TestInfo{ Id = TestId.FromString("Item0"), Name = "item 0" }),

                    (new List<TestId>{ TestId.FromString("Item0AliasInVariant") },
                     "X/Y",
                     new TestInfo{ Id = TestId.FromString("Item0"), Name = "item 0 modified" }),

                    (null,
                     null,
                     new TestInfo{ Id = TestId.FromString("Item1"), Name = "item 1" }),

                    (new List<TestId>{ TestId.FromString("Item1AliasInVariant"), TestId.FromString("Item1OtherAliasInVariant") },
                     "X/Y",
                     new TestInfo{ Id = TestId.FromString("Item1"), Name = "item 1 modified" }),

                    (new List<TestId>{ TestId.FromString("Item2Alias"), TestId.FromString("Item2OtherAlias") },
                     null,
                     new TestInfo{ Id = TestId.FromString("Item2"), Name = "item 2" }),

                    (null,
                     "X/Y",
                     new TestInfo{ Id = TestId.FromString("Item2"), Name = "item 2 modified" }),
                }, items);
        }

        // \todo [petri] add more tests: from-above inheritance? others?
    }
}
