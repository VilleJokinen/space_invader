// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Json;
using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Cloud.Tests
{
    class MetaGuidTests
    {
        [Test]
        public void NoneValueTests()
        {
            Assert.AreEqual(MetaUInt128.Zero, MetaGuid.None.Value);
            Assert.AreEqual(MetaGuid.None, MetaGuid.Parse("000000000000000-0-0000000000000000"));
            Assert.AreEqual("000000000000000-0-0000000000000000", MetaGuid.None.ToString());
        }

        [Test]
        public void InvalidValueTests()
        {
            Assert.Throws<ArgumentNullException>(() => MetaGuid.Parse(null)); // null
            Assert.Throws<FormatException>(() => MetaGuid.Parse("00000000000000A-0-0000000000000000")); // uppercase
            Assert.Throws<FormatException>(() => MetaGuid.Parse("00000000000000-0-0000000000000000"));  // too short timestamp
            Assert.Throws<FormatException>(() => MetaGuid.Parse("0000000000000000-0-0000000000000000")); // too long timestamp
            Assert.Throws<FormatException>(() => MetaGuid.Parse("000000000000000--0000000000000000")); // no reserved value
            Assert.Throws<FormatException>(() => MetaGuid.Parse("000000000000000-0-000000000000000")); // too short random
            Assert.Throws<FormatException>(() => MetaGuid.Parse("000000000000000-0-00000000000000000")); // too long random
            Assert.Throws<FormatException>(() => MetaGuid.Parse("000000000000000-1-0000000000000000")); // invalid reserved value
        }

        [Test]
        public void TimeRangeTest()
        {
            // Min value
            Assert.Throws<ArgumentException>(() => MetaGuid.NewWithTime(MetaGuid.MinDateTime - TimeSpan.FromTicks(1)));
            Assert.AreEqual(1970, MetaGuid.NewWithTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).GetDateTime().Year);
            Assert.AreEqual(1970, MetaGuid.NewWithTime(MetaGuid.MinDateTime).GetDateTime().Year);

            // Max value
            Assert.AreEqual(5623, MetaGuid.NewWithTime(new DateTime(5623, 1, 1, 0, 0, 0, DateTimeKind.Utc)).GetDateTime().Year);
            Assert.AreEqual(5623, MetaGuid.NewWithTime(MetaGuid.MaxDateTime).GetDateTime().Year);
            Assert.Throws<ArgumentException>(() => MetaGuid.NewWithTime(MetaGuid.MaxDateTime + TimeSpan.FromTicks(1)));
        }

        [Test]
        public void BasicStringConversionTest()
        {
            MetaGuid guid = MetaGuid.New();
            Assert.AreEqual(guid, MetaGuid.Parse(guid.ToString()));
            MetaGuid anotherGuid = MetaGuid.New();
            Assert.AreNotEqual(guid, anotherGuid);
            Assert.AreNotEqual(guid.ToString(), anotherGuid.ToString());
        }

        // \note Doesn't work as top-level entry, serialization tested in SerializationTests
        //[Test]
        //public void BasicSerializationTest()
        //{
        //    MetaGuid guid = MetaGuid.New();
        //    byte[] serialized = MetaSerialization.SerializeTagged(guid, MetaSerializationFlags.IncludeAll, null);
        //    MetaGuid deserialized = MetaSerialization.DeserializeTagged<MetaGuid>(serialized, MetaSerializationFlags.IncludeAll, null, null);
        //    Assert.AreEqual(guid, deserialized);
        //    MetaGuid another = MetaGuid.New();
        //    byte[] serializedOther = MetaSerialization.SerializeTagged(another, MetaSerializationFlags.IncludeAll, null);
        //    Assert.AreNotEqual(serialized, serializedOther);
        //    MetaGuid deserializedOther = MetaSerialization.DeserializeTagged<MetaGuid>(serializedOther, MetaSerializationFlags.IncludeAll, null, null);
        //    Assert.AreNotEqual(deserialized, deserializedOther);
        //}

        // Test that timestamp ordering works with CompareTo()
        [Test]
        public void TimeOrderTest()
        {
            Random rng = new Random(12345);
            for (int iter = 0; iter < 10_000; iter++)
            {
                DateTime ts0 = DateTime.UtcNow;
                DateTime ts1 = ts0 + TimeSpan.FromTicks(rng.Next(-100, 100));
                int cmpTs = ts0.CompareTo(ts1);
                if (ts0 != ts1)
                {
                    MetaGuid guid0 = MetaGuid.NewWithTime(ts0);
                    MetaGuid guid1 = MetaGuid.NewWithTime(ts1);
                    int cmpGuid = guid0.CompareTo(guid1);

                    Assert.AreEqual(ts0, guid0.GetDateTime());
                    Assert.AreEqual(ts1, guid1.GetDateTime());

                    Assert.AreEqual(cmpTs, cmpGuid);
                    Assert.IsFalse(guid0 == guid1);
                    Assert.IsTrue(guid0 != guid1);

                    // Check that ordering matches that of timestamp
                    if (cmpGuid < 0)
                    {
                        Assert.IsTrue(guid0 < guid1);
                        Assert.IsFalse(guid0 > guid1);
                        Assert.LessOrEqual(string.CompareOrdinal(guid0.ToString(), guid1.ToString()), -1);
                    }
                    else
                    {
                        Assert.IsTrue(guid0 > guid1);
                        Assert.IsFalse(guid0 < guid1);
                        Assert.GreaterOrEqual(string.CompareOrdinal(guid0.ToString(), guid1.ToString()), +1);
                    }

                    // Compare against none
                    Assert.IsTrue(guid0 > MetaGuid.None);
                    Assert.IsFalse(guid0 < MetaGuid.None);
                    Assert.IsTrue(guid0 != MetaGuid.None);
                    Assert.IsFalse(guid0 == MetaGuid.None);
                }
            }
        }

        [Test]
        public void BasicJsonTest()
        {
            MetaGuid guid = MetaGuid.New();
            string json = JsonSerialization.SerializeToString(guid);
            MetaGuid deserialized = JsonSerialization.Deserialize<MetaGuid>(json);
            Assert.AreEqual(guid, deserialized);
        }

        [Test]
        public void NoCollisionsTest()
        {
            // Check that no collisions are generated
            HashSet<MetaGuid> uids = new HashSet<MetaGuid>();
            for (int ndx = 0; ndx < 1_000_000; ndx++)
            {
                bool wasAdded = uids.Add(MetaGuid.New());
                Assert.IsTrue(wasAdded);
            }
        }

        [Test]
        public void FromTimeAndValueTest()
        {
            DateTime timePoint = DateTime.UtcNow;
            Assert.AreEqual(MetaGuid.FromTimeAndValue(timePoint, value: 123), MetaGuid.FromTimeAndValue(timePoint, value: 123));
            Assert.AreNotEqual(MetaGuid.FromTimeAndValue(timePoint, value: 123), MetaGuid.FromTimeAndValue(timePoint, value: 124));
            Assert.AreNotEqual(MetaGuid.FromTimeAndValue(timePoint + TimeSpan.FromMilliseconds(1), value: 123), MetaGuid.FromTimeAndValue(timePoint, value: 123));
            Assert.AreEqual(MetaGuid.FromTimeAndValue(timePoint, value: 123).GetDateTime(), timePoint);

            MetaTime metaTimePoint = MetaTime.Now;
            Assert.AreEqual(MetaGuid.FromTimeAndValue(metaTimePoint, value: 123), MetaGuid.FromTimeAndValue(metaTimePoint, value: 123));
            Assert.AreNotEqual(MetaGuid.FromTimeAndValue(metaTimePoint, value: 123), MetaGuid.FromTimeAndValue(metaTimePoint, value: 124));
            Assert.AreNotEqual(MetaGuid.FromTimeAndValue(metaTimePoint + MetaDuration.FromMilliseconds(1), value: 123), MetaGuid.FromTimeAndValue(metaTimePoint, value: 123));
            Assert.AreEqual(MetaGuid.FromTimeAndValue(metaTimePoint, value: 123).GetDateTime(), metaTimePoint.ToDateTime());
        }
        [Test]
        public void TestIComparable()
        {
            MetaGuid some = MetaGuid.New();
            Assert.Greater(some, MetaGuid.None);
            Assert.Less(MetaGuid.None, some);
            Assert.IsTrue(((IComparable)some).CompareTo(MetaGuid.None) > 0);
            Assert.IsTrue(((IComparable)MetaGuid.None).CompareTo(some) < 0);
            Assert.IsTrue(some.CompareTo(MetaGuid.None) > 0);
            Assert.IsTrue(MetaGuid.None.CompareTo(some) < 0);
            Assert.Greater(MetaGuid.NewWithTime(MetaGuid.MaxDateTime), MetaGuid.NewWithTime(MetaGuid.MinDateTime));
        }
    }
}
