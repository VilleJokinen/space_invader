// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Cloud.Tests
{
    class PrettyPrintTests
    {
        public class TimeContainer
        {
            public MetaTime Certainly;
            public MetaTime? Maybe;
        }

        [Test]
        public void TestPrettyPrintMetaTime()
        {
            // Check that pretty-prints and differences with MetaTimes are legible
            // \note: the exact formatting does not matter - If formatting has changed, just change the expected values.

            Assert.AreEqual("1970-01-01 00:02:03.456 Z", PrettyPrinter.Compact(MetaTime.FromMillisecondsSinceEpoch(123456)));
            Assert.AreEqual("PrettyPrintTests.TimeContainer{ Certainly=2021-01-04 15:54:57.000 Z, Maybe=null }", PrettyPrinter.Compact(new TimeContainer() { Certainly = MetaTime.FromMillisecondsSinceEpoch(1609775697000) }));
            Assert.AreEqual("PrettyPrintTests.TimeContainer{ Certainly=1970-01-01 00:00:00.000 Z, Maybe=2021-01-04 15:54:57.000 Z }", PrettyPrinter.Compact(new TimeContainer() { Maybe = MetaTime.FromMillisecondsSinceEpoch(1609775697000) }));

            Assert.AreEqual("", PrettyPrinter.Difference(MetaTime.FromMillisecondsSinceEpoch(123456), MetaTime.FromMillisecondsSinceEpoch(123456)));
            Assert.AreEqual("", PrettyPrinter.Difference<MetaTime?>(MetaTime.FromMillisecondsSinceEpoch(123456), MetaTime.FromMillisecondsSinceEpoch(123456)));
            Assert.AreEqual("1970-01-01 00:02:03.456 Z vs 1970-01-01 00:02:03.457 Z", PrettyPrinter.Difference(MetaTime.FromMillisecondsSinceEpoch(123456), MetaTime.FromMillisecondsSinceEpoch(123457)));
            Assert.AreEqual("1970-01-01 00:02:03.456 Z vs 1970-01-01 00:02:03.457 Z", PrettyPrinter.Difference<MetaTime?>(MetaTime.FromMillisecondsSinceEpoch(123456), MetaTime.FromMillisecondsSinceEpoch(123457)));

            Assert.AreEqual("", PrettyPrinter.Difference(
                new TimeContainer() { Certainly = MetaTime.FromMillisecondsSinceEpoch(1609775697000) },
                new TimeContainer() { Certainly = MetaTime.FromMillisecondsSinceEpoch(1609775697000) } ));
            Assert.AreEqual("TimeContainer {\n  Certainly = 2021-01-04 15:54:57.000 Z vs 2021-01-04 15:54:57.001 Z\n}\n", PrettyPrinter.Difference(
                new TimeContainer() { Certainly = MetaTime.FromMillisecondsSinceEpoch(1609775697000) },
                new TimeContainer() { Certainly = MetaTime.FromMillisecondsSinceEpoch(1609775697001) } ));
            Assert.AreEqual("", PrettyPrinter.Difference(
                new TimeContainer() { Maybe = MetaTime.FromMillisecondsSinceEpoch(1609775697000) },
                new TimeContainer() { Maybe = MetaTime.FromMillisecondsSinceEpoch(1609775697000) } ));
            Assert.AreEqual("TimeContainer {\n  Maybe = 2021-01-04 15:54:57.000 Z vs 2021-01-04 15:54:57.001 Z\n}\n", PrettyPrinter.Difference(
                new TimeContainer() { Maybe = MetaTime.FromMillisecondsSinceEpoch(1609775697000) },
                new TimeContainer() { Maybe = MetaTime.FromMillisecondsSinceEpoch(1609775697001) } ));
        }

        public class GameConfigImplicitKeyTypeId : StringId<GameConfigImplicitKeyTypeId> { }
        public class GameConfigExplicitKeyTypeId : StringId<GameConfigExplicitKeyTypeId> { }

        public class GameConfigImplicitKey : IGameConfigData<GameConfigImplicitKeyTypeId>
        {
            public GameConfigImplicitKeyTypeId  Id      { get; set; }
            public string                       Content { get; set; }

            public GameConfigImplicitKeyTypeId ConfigKey => Id;
        }
        public class GameConfigExplicitKey : IGameConfigData<GameConfigExplicitKeyTypeId>
        {
            public GameConfigExplicitKeyTypeId  Id      { get; set; }
            public string                       Content { get; set; }

            GameConfigExplicitKeyTypeId IGameConfigKey<GameConfigExplicitKeyTypeId>.ConfigKey => Id;
        }

        public class GameConfigImplicitKeyWrapper
        {
            public GameConfigImplicitKey Wrapped;
            public GameConfigImplicitKeyWrapper(GameConfigImplicitKey wrapped)
            {
                Wrapped = wrapped;
            }
        }
        public class GameConfigExplicitKeyWrapper
        {
            public GameConfigExplicitKey Wrapped;
            public GameConfigExplicitKeyWrapper(GameConfigExplicitKey wrapped)
            {
                Wrapped = wrapped;
            }
        }

        [Test]
        public void TestGameConfigConfigKey()
        {
            // implicitly defined ConfigKey gets pretty-printed
            Assert.AreEqual(
                "PrettyPrintTests.GameConfigImplicitKey{ Id=&GameConfigImplicitKeyTypeId.MyKey, Content=MyValue, ConfigKey=&GameConfigImplicitKeyTypeId.MyKey }",
                PrettyPrinter.Compact(new GameConfigImplicitKey() { Id=GameConfigImplicitKeyTypeId.FromString("MyKey"), Content="MyValue" }));

            // explicitly defined IGameConfigData<>.ConfigKey is omitted
            Assert.AreEqual(
                "PrettyPrintTests.GameConfigExplicitKey{ Id=&GameConfigExplicitKeyTypeId.MyKey, Content=MyValue }",
                PrettyPrinter.Compact(new GameConfigExplicitKey() { Id=GameConfigExplicitKeyTypeId.FromString("MyKey"), Content="MyValue" }));

            Assert.AreEqual(
                "PrettyPrintTests.GameConfigImplicitKeyWrapper{ Wrapped=&GameConfigImplicitKeyTypeId.MyKey }",
                PrettyPrinter.Compact(new GameConfigImplicitKeyWrapper(new GameConfigImplicitKey() { Id=GameConfigImplicitKeyTypeId.FromString("MyKey"), Content="MyValue" })));

            Assert.AreEqual(
                "PrettyPrintTests.GameConfigExplicitKeyWrapper{ Wrapped=&GameConfigExplicitKeyTypeId.MyKey }",
                PrettyPrinter.Compact(new GameConfigExplicitKeyWrapper(new GameConfigExplicitKey() { Id=GameConfigExplicitKeyTypeId.FromString("MyKey"), Content="MyValue" })));
        }

        public class ObjectWithIndexer
        {
            public int Value = 22;
            public int Prop => 22;
            public int this[int ndx] => ndx;
        }

        [Test]
        public void TestObjectWithIndexer()
        {
            // indexer not used
            Assert.AreEqual("PrettyPrintTests.ObjectWithIndexer{ Prop=22, Value=22 }", PrettyPrinter.Compact(new ObjectWithIndexer()));
        }

        public class ObjectWithCollections
        {
            public int[] A0 = Array.Empty<int>();
            public int[] A3 = new int[3] { 1, 2, 3 };

            public List<int> L0 = new List<int>();
            public List<int> L3 = new List<int>() { 1, 2, 3 };

            public IEnumerable<int> E0 = new List<int>();
            public IEnumerable<int> E3 = new List<int>() { 1, 2, 3 };
        }
        public class ObjectWithCollectionsSizeOnly
        {
            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public int[] A0 = Array.Empty<int>();
            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public int[] A3 = new int[3] { 1, 2, 3 };

            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public List<int> L0 = new List<int>();
            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public List<int> L3 = new List<int>() { 1, 2, 3 };

            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public IEnumerable<int> E0 = new List<int>();
            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public IEnumerable<int> E3 = new List<int>() { 1, 2, 3 };
        }

        public class ObjectWithCollections2
        {
            public IEnumerable<int> G3 => Generate3();
            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public IEnumerable<int> G3s => Generate3();

            public byte[] B3 = new byte[] { 0, 1, 2 };
            [PrettyPrint(PrettyPrintFlag.SizeOnly)]
            public byte[] B3s = new byte[] { 0, 1, 2 };

            static IEnumerable<int> Generate3()
            {
                yield return 1;
                yield return 2;
                yield return 3;
            }
        }

        [Test]
        public void TestObjectWithCollections()
        {
            Assert.AreEqual("PrettyPrintTests.ObjectWithCollections{ A0=[], A3=[ 1, 2, 3 ], L0=[], L3=[ 1, 2, 3 ], E0=[], E3=[ 1, 2, 3 ] }", PrettyPrinter.Compact(new ObjectWithCollections()));
            Assert.AreEqual(
@"PrettyPrintTests.ObjectWithCollections {
  A0 = []
  A3 = [
    [0] = 1
    [1] = 2
    [2] = 3
  ]
  L0 = []
  L3 = [
    [0] = 1
    [1] = 2
    [2] = 3
  ]
  E0 = []
  E3 = [
    [0] = 1
    [1] = 2
    [2] = 3
  ]
}
", PrettyPrinter.Verbose(new ObjectWithCollections()));

            Assert.AreEqual("PrettyPrintTests.ObjectWithCollectionsSizeOnly{ A0=[], A3=[ 3 elems ], L0=[], L3=[ 3 elems ], E0=[], E3=[ 3 elems ] }", PrettyPrinter.Compact(new ObjectWithCollectionsSizeOnly()));
            Assert.AreEqual(
@"PrettyPrintTests.ObjectWithCollectionsSizeOnly {
  A0 = []
  A3 = [ 3 elems ]
  L0 = []
  L3 = [ 3 elems ]
  E0 = []
  E3 = [ 3 elems ]
}
", PrettyPrinter.Verbose(new ObjectWithCollectionsSizeOnly()));

            Assert.AreEqual("PrettyPrintTests.ObjectWithCollections2{ G3=[ 1, 2, 3 ], G3s=[ 3 elems ], B3=[ 3 bytes ], B3s=[ 3 bytes ] }", PrettyPrinter.Compact(new ObjectWithCollections2()));
            Assert.AreEqual(
@"PrettyPrintTests.ObjectWithCollections2 {
  G3 = [
    [0] = 1
    [1] = 2
    [2] = 3
  ]
  G3s = [ 3 elems ]
  B3 = [ 3 bytes ]
  B3s = [ 3 bytes ]
}
", PrettyPrinter.Verbose(new ObjectWithCollections2()));
        }
    }
}
