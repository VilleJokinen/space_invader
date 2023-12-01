// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    public class SerializedObjectComparerTest
    {
        [MetaSerializable]
        public abstract class BaseObject
        {
            [MetaMember(1)] public string BaseField = "Base";
        }

        [MetaSerializableDerived(1)]
        public class DerivedObject : BaseObject
        {
            [MetaMember(101)]  public string DerivedField = "Derived";
            [MetaMember(102)]  public OrderedDictionary<string, string> DerivedDict = new OrderedDictionary<string, string>() { { "1", "1" }, { "2", "2" } };
        }

        [MetaSerializable]
        public class Root
        {
            [MetaMember(1)] public MetaTime Time = MetaTime.FromMillisecondsSinceEpoch(123456780);
            [MetaMember(2)] public MetaTime? TimeMaybe = MetaTime.FromMillisecondsSinceEpoch(223456780);
            [MetaMember(3)] public int Integer = 123;
            [MetaMember(4)] public string[] Array = new string[] { "A", "B", "C" };
            [MetaMember(5)] public BaseObject BaseObject = new DerivedObject();
        }

        [Test]
        public void TestCompare()
        {
            ExpectSame(new Root(), new Root());
            ExpectDifference(new Root() { Integer = 1 }, new Root(),
@"SerializedObjectComparerTest.Root {
  Integer: 1 vs 123
}");

            ExpectDifference(new Root() { BaseObject = null }, new Root(),
@"SerializedObjectComparerTest.Root {
  BaseObject: <null> vs <not-null>
}");

            {
                Root obj = new Root();
                ((DerivedObject)obj.BaseObject).DerivedField = "123";
                ExpectDifference(obj, new Root(),
@"SerializedObjectComparerTest.Root {
  BaseObject {
    (SerializedObjectComparerTest.DerivedObject) {
      DerivedField: 123 vs Derived
    }
  }
}");
            }

            {
                Root obj = new Root();
                ((DerivedObject)obj.BaseObject).DerivedDict.Add("Extra", "3");
                ExpectDifference(obj, new Root(),
@"SerializedObjectComparerTest.Root {
  BaseObject {
    (SerializedObjectComparerTest.DerivedObject) {
      DerivedDict {
        [2]: <value> vs <missing>
      }
    }
  }
}");
            }

            {
                Root obj = new Root();
                ((DerivedObject)obj.BaseObject).DerivedDict.Remove("2");
                ((DerivedObject)obj.BaseObject).DerivedDict.Add("Extra", "ExtraValue");
                ExpectDifference(obj, new Root(),
@"SerializedObjectComparerTest.Root {
  BaseObject {
    (SerializedObjectComparerTest.DerivedObject) {
      DerivedDict {
        [1] {
          <Key>: Extra vs 2
          <Value>: ExtraValue vs 2
        }
      }
    }
  }
}");
            }
        }

        void ExpectSame(Root a, Root b)
        {
            ExpectResult(a, b, "Objects First and Second are bit-exact identical");
        }

        void ExpectDifference(Root a, Root b, string difference, bool useTypeHint = true)
        {
            ExpectResult(a, b, "Objects First and Second differ:\n" + difference, useTypeHint);
        }

        void ExpectResult(Root a, Root b, string difference, bool useTypeHint = true)
        {
            byte[] aBytes = MetaSerialization.SerializeTagged(a, MetaSerializationFlags.IncludeAll, logicVersion: null);
            byte[] bBytes = MetaSerialization.SerializeTagged(b, MetaSerializationFlags.IncludeAll, logicVersion: null);

            SerializedObjectComparer comparer = new SerializedObjectComparer();
            comparer.FirstName = "First";
            comparer.SecondName = "Second";
            comparer.Type = useTypeHint ? typeof(Root) : null;
            string result = comparer.Compare(aBytes, bBytes).Description;
            Assert.AreEqual(difference, result);
        }

        [Test]
        public void TestNoThrow()
        {
            byte[] cleanBytes = MetaSerialization.SerializeTagged(new Root(), MetaSerializationFlags.IncludeAll, logicVersion: null);
            byte[] dirtyBytes = new byte[cleanBytes.Length];

            for (int ndx = 0; ndx < cleanBytes.Length; ++ndx)
            {
                cleanBytes.AsSpan().CopyTo(dirtyBytes.AsSpan());
                dirtyBytes[ndx] = (byte)(dirtyBytes[ndx] + 2);

                SerializedObjectComparer comparer = new SerializedObjectComparer();
                _ = comparer.Compare(cleanBytes, dirtyBytes);
            }
        }
    }
}
