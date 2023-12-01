// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Json;
using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Cloud.Tests
{
    public class ImportTest
    {
        public string           String      { get; set; }
        public int              Integer     { get; set; }
        public F64              Fixed64     { get; set; }
        public F32              Fixed32     { get; set; }
        public TestStringId     StringId    { get; set; }
    }

    [TestFixture]
    public class JsonTests
    {
#if !METAPLAY_USE_LEGACY_FIXED_POINT_PARSING // only work with the new parser
        [Test]
        public void JsonImport()
        {
            F64 f64 = F64.Ratio100(12_52);
            F32 f32 = F32.Ratio(-1, 1000);
            string input = $"{{\"String\":\"SomeString\",\"Integer\":123,\"Fixed64\":{f64},\"Fixed32\":{f32},\"StringId\":\"SomeStringId\"}}";
            ImportTest test = JsonSerialization.Deserialize<ImportTest>(input);

            Assert.AreEqual("SomeString", test.String);
            Assert.AreEqual(123, test.Integer);
            Assert.AreEqual(f64, test.Fixed64);
            Assert.AreEqual(f32, test.Fixed32);
            Assert.AreEqual("SomeStringId", test.StringId.ToString());
        }
#endif

        [Test]
        public void TestF32()
        {
            void Test(F32 value)
            {
                string str = value.ToString();
                F32 parsed = JsonSerialization.Deserialize<F32>(str);
                Assert.AreEqual(value, parsed);
            }

            Test(F32.Ratio100(12_34));
            Test(F32.Ratio100(-12_34));
            Test(F32.Ratio100(12_00));
            Test(F32.Ratio100(-12_00));
            Test(F32.Ratio100(12_00));
            Test(F32.Ratio100(-12_00));
            Test(F32.Zero);
            Test(F32.Zero);

            Assert.AreEqual(F32.Zero, JsonSerialization.Deserialize<F32>("0"));
            Assert.AreEqual(F32.Zero, JsonSerialization.Deserialize<F32>("0.0"));
            Assert.AreEqual(F32.Zero, JsonSerialization.Deserialize<F32>("0.0000"));
        }

        [Test]
        public void TestF64()
        {
            void Test(F64 reference)
            {
                string str = reference.ToString();
                F64 parsed = JsonSerialization.Deserialize<F64>(str);
                Assert.AreEqual(reference, parsed);
            }

            Test(F64.Ratio100(12_34));
            Test(F64.Ratio100(-12_34));
            Test(F64.Ratio100(12_00));
            Test(F64.Ratio100(-12_00));
            Test(F64.Ratio100(12_00));
            Test(F64.Ratio100(-12_00));
            Test(F64.Zero);

            Assert.AreEqual(F64.Zero, JsonSerialization.Deserialize<F64>("0"));
            Assert.AreEqual(F64.Zero, JsonSerialization.Deserialize<F64>("0.0"));
            Assert.AreEqual(F64.Zero, JsonSerialization.Deserialize<F64>("0.0000"));
        }

        [Test]
        public void SerializeStringId()
        {
            Assert.AreEqual("\"SomeStringId\"", JsonSerialization.SerializeToString(TestStringId.FromString("SomeStringId")));
        }

        [Test]
        public void SerializeMetaTime()
        {
            Assert.AreEqual("\"1970-01-01T00:00:00.0000000Z\"", JsonSerialization.SerializeToString(MetaTime.Epoch));
            Assert.AreEqual("\"2021-12-31T23:59:59.9990000Z\"", JsonSerialization.SerializeToString(MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc))));
        }

        [Test]
        public void DeserializeMetaTime()
        {
            Assert.AreEqual(MetaTime.Epoch, JsonSerialization.Deserialize<MetaTime>("\"1970-01-01T00:00:00.0000000Z\""));
            Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)), JsonSerialization.Deserialize<MetaTime>("\"2021-12-31T23:59:59.9990000Z\""));
        }

        [Test]
        public void SerializeMetaDuration()
        {
            Assert.AreEqual("\"0.00:00:00.0000000\"", JsonSerialization.SerializeToString(MetaDuration.Zero));
            Assert.AreEqual("\"0.00:00:00.0010000\"", JsonSerialization.SerializeToString(MetaDuration.FromMilliseconds(1)));
            Assert.AreEqual("\"0.00:00:01.0000000\"", JsonSerialization.SerializeToString(MetaDuration.FromSeconds(1)));
            Assert.AreEqual("\"0.00:01:00.0000000\"", JsonSerialization.SerializeToString(MetaDuration.FromMinutes(1)));
            Assert.AreEqual("\"0.01:00:00.0000000\"", JsonSerialization.SerializeToString(MetaDuration.FromHours(1)));
            Assert.AreEqual("\"111.01:00:00.0000000\"", JsonSerialization.SerializeToString(MetaDuration.FromHours(24 * 111 + 1)));
        }

        [Test]
        public void DeserializeMetaDuration()
        {
            Assert.AreEqual(MetaDuration.Zero, JsonSerialization.Deserialize<MetaDuration>("\"0.00:00:00.0000000\""));
            Assert.AreEqual(MetaDuration.FromMilliseconds(1), JsonSerialization.Deserialize<MetaDuration>("\"0.00:00:00.0010000\""));
            Assert.AreEqual(MetaDuration.FromSeconds(1), JsonSerialization.Deserialize<MetaDuration>("\"0.00:00:01.0000000\""));
            Assert.AreEqual(MetaDuration.FromMinutes(1), JsonSerialization.Deserialize<MetaDuration>("\"0.00:01:00.0000000\""));
            Assert.AreEqual(MetaDuration.FromHours(1), JsonSerialization.Deserialize<MetaDuration>("\"0.01:00:00.0000000\""));
            Assert.AreEqual(MetaDuration.FromHours(24 * 111 + 1), JsonSerialization.Deserialize<MetaDuration>("\"111.01:00:00.0000000\""));
        }

        public class NullCollectionSerializationTestType
        {
            public int[] IntArr = new int[] { 1, 2, 3 };
            [JsonSerializeNullCollectionAsEmpty]
            public int[] IntArrNcae = new int[] { 1, 2, 3 };

            public int[] EmptyIntArr = new int[] { };
            [JsonSerializeNullCollectionAsEmpty]
            public int[] EmptyIntArrNcae = new int[] { };

            public int[] NullIntArr = null;
            [JsonSerializeNullCollectionAsEmpty]
            public int[] NullIntArrNcae = null;

            public List<int> NullIntList = null;
            [JsonSerializeNullCollectionAsEmpty]
            public List<int> NullIntListNcae = null;

            public HashSet<int> IntSet = new() { 5 }; // just one as the order might change
            [JsonSerializeNullCollectionAsEmpty]
            public HashSet<int> IntSetNcae = new() { 5 };

            public HashSet<int> NullIntSet = null;
            [JsonSerializeNullCollectionAsEmpty]
            public HashSet<int> NullIntSetNcae = null;

            public OrderedDictionary<string, string> Str2StrDict = new() { { "foo", "bar" },  { "fiz", "buz" } };
            [JsonSerializeNullCollectionAsEmpty]
            public OrderedDictionary<string, string> Str2StrDictNcae = new() { { "foo", "bar" },  { "fiz", "buz" } };

            public OrderedDictionary<string, string> EmptyStr2StrDict { get; } = new();
            [JsonSerializeNullCollectionAsEmpty]
            public OrderedDictionary<string, string> EmptyStr2StrDictNcae { get; } = new();

            public OrderedDictionary<string, string> NullStr2StrDict { get; } = null;
            [JsonSerializeNullCollectionAsEmpty]
            public OrderedDictionary<string, string> NullStr2StrDictNcae { get; } = null;
        }

        [Test]
        public void SerializeJsonSerializeNullCollectionAsEmpty()
        {
            const string expected =
                "{\"intArr\":[1,2,3],"
                + "\"intArrNcae\":[1,2,3],"
                + "\"emptyIntArr\":[],"
                + "\"emptyIntArrNcae\":[],"
                + "\"nullIntArr\":null,"
                + "\"nullIntArrNcae\":[],"
                + "\"nullIntList\":null,"
                + "\"nullIntListNcae\":[],"
                + "\"intSet\":[5],"
                + "\"intSetNcae\":[5],"
                + "\"nullIntSet\":null,"
                + "\"nullIntSetNcae\":[],"
                + "\"str2StrDict\":{\"foo\":\"bar\",\"fiz\":\"buz\"},"
                + "\"str2StrDictNcae\":{\"foo\":\"bar\",\"fiz\":\"buz\"},"
                + "\"emptyStr2StrDict\":{},"
                + "\"emptyStr2StrDictNcae\":{},"
                + "\"nullStr2StrDict\":null,"
                + "\"nullStr2StrDictNcae\":{}"
                + "}";
            string actual = JsonSerialization.SerializeToString(new NullCollectionSerializationTestType());
            Assert.AreEqual(expected, actual);
        }

        public class NullCollectionDeserializationTestType
        {
            [JsonSerializeNullCollectionAsEmpty]
            public int[] Arr = new int[] {1, 2, 3};

            [JsonSerializeNullCollectionAsEmpty]
            public List<int> List = new() {1, 2, 3};

            [JsonSerializeNullCollectionAsEmpty]
            public OrderedDictionary<string, string> Dict = new() { {"1", "2"} };
        }

        [Test]
        public void DeserializeJsonSerializeNullCollectionAsEmpty()
        {
            // Null-into-empty serialization does not affect deserialization
            NullCollectionDeserializationTestType testValue = JsonSerialization.Deserialize<NullCollectionDeserializationTestType>("{\"Arr\":null,\"List\":null,\"Dict\":null}");
            Assert.IsNull(testValue.Arr);
            Assert.IsNull(testValue.List);
            Assert.IsNull(testValue.Dict);
        }

        class SensitiveAttributeSerializationTestType
        {
            [Sensitive]
            public string A = "secret1";

            [Sensitive]
            public string B = null;

            [Sensitive]
            public string C { get; } = "secret2";
        }

        [Test]
        public void SerializeSensitiveAttribute()
        {
            Assert.AreEqual("{\"a\":\"XXX\",\"b\":null,\"c\":\"XXX\"}", JsonSerialization.SerializeToString(new SensitiveAttributeSerializationTestType()));
        }

        class SensitiveAttributeDeserializationTestType
        {
            [Sensitive]
            public string A = "default";
        }

        [Test]
        public void DeserializeSensitiveAttribute()
        {
            SensitiveAttributeDeserializationTestType testValue = JsonSerialization.Deserialize<SensitiveAttributeDeserializationTestType>("{\"A\":\"secret\"}");
            Assert.AreEqual("secret", testValue.A);
        }

    }
}
