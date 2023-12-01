// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Json;
using Metaplay.Core.Model;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Cloud.Tests
{
    [TestFixture]
    public class JsonSerializationTests
    {
        public class JsonTest
        {
            [Sensitive] public string Password = "supersecretpassword1234";
            [Sensitive] public int Age = 123;
            [Sensitive] public object NullableSecret = null;
            [Sensitive] public string ComputedSecret => Password;
        }

        [Test]
        public void SensitiveMember()
        {
            string json = JsonSerialization.SerializeToString(new JsonTest());
            Assert.AreEqual("{\"password\":\"XXX\",\"age\":\"XXX\",\"nullableSecret\":null,\"computedSecret\":\"XXX\"}", json);
        }

        #region Game config data serialization

        [MetaSerializable]
        public class TestGameConfigId : StringId<TestGameConfigId>
        {
        }

        [MetaSerializable]
        public class TestGameConfigData : IGameConfigData<TestGameConfigId>
        {
            [MetaMember(1)] public TestGameConfigId         Id              { get; private set; }
            [MetaMember(2)] public int                      Value           { get; private set; }

            public TestGameConfigId ConfigKey => Id;

            public TestGameConfigData(){ }
            public TestGameConfigData(TestGameConfigId id, int value)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
                Value = value;
            }
        }

        [MetaSerializable]
        public class TestGameConfigWithNestedPayloadId : StringId<TestGameConfigWithNestedPayloadId>
        {
        }

        [MetaSerializable]
        public class TestGameConfigDataWithNestedPayload : IGameConfigData<TestGameConfigWithNestedPayloadId>
        {
            [MetaSerializable]
            public class Subclass
            {
                [MetaMember(1)] public string Field = "Level2";
                [MetaMember(2)] public Subclass2 Sub = new Subclass2();
                [MetaMember(3)] public TestGameConfigData Ref = new TestGameConfigData(TestGameConfigId.FromString("R2"), 100);
            }
            [MetaSerializable]
            public class Subclass2
            {
                [MetaMember(1)] public string Field = "Level3";
                [MetaMember(2)] public TestGameConfigData Ref = new TestGameConfigData(TestGameConfigId.FromString("R3"), 100);
            }

            [MetaMember(1)] public TestGameConfigWithNestedPayloadId    Id              { get; private set; }
            [MetaMember(2)] public Subclass                             Value           { get; private set; } = new Subclass();
            [MetaMember(3)] public TestGameConfigData                   RefTop          { get; private set; } = new TestGameConfigData(TestGameConfigId.FromString("R1"), 100);

            public TestGameConfigWithNestedPayloadId ConfigKey => Id;

            public TestGameConfigDataWithNestedPayload(){ }
            public TestGameConfigDataWithNestedPayload(TestGameConfigWithNestedPayloadId id)
            {
                Id = id ?? throw new ArgumentNullException(nameof(id));
            }
        }

        public class TestGameConfigDataWrapper
        {
            public TestGameConfigData TestDataProperty { get; set; }
            public TestGameConfigData TestDataField;

            public IGameConfigData BaseDataProperty { get; set; }
            public IGameConfigData BaseDataField;

            [ForceSerializeByValue]
            public TestGameConfigData TestDataPayloadProperty { get; set; }
            [ForceSerializeByValue]
            public TestGameConfigData TestDataPayloadField;

            [ForceSerializeByValue]
            public IGameConfigData BaseDataPayloadProperty { get; set; }
            [ForceSerializeByValue]
            public IGameConfigData BaseDataPayloadField;

            [ForceSerializeByValue]
            public TestGameConfigDataWithNestedPayload DataPayloadWithNestedPayload { get; set; }
        }

        [Test]
        public void GameConfigData()
        {
            string normal = JsonSerialization.SerializeToString(new TestGameConfigDataWrapper
            {
                TestDataProperty        = new TestGameConfigData(TestGameConfigId.FromString("A"), 100),
                TestDataField           = new TestGameConfigData(TestGameConfigId.FromString("B"), 200),

                BaseDataProperty        = new TestGameConfigData(TestGameConfigId.FromString("C"), 300),
                BaseDataField           = new TestGameConfigData(TestGameConfigId.FromString("D"), 400),

                TestDataPayloadProperty = new TestGameConfigData(TestGameConfigId.FromString("E"), 500),
                TestDataPayloadField    = new TestGameConfigData(TestGameConfigId.FromString("F"), 600),

                BaseDataPayloadProperty = new TestGameConfigData(TestGameConfigId.FromString("G"), 700),
                BaseDataPayloadField    = new TestGameConfigData(TestGameConfigId.FromString("H"), 800),

                DataPayloadWithNestedPayload = new TestGameConfigDataWithNestedPayload(TestGameConfigWithNestedPayloadId.FromString("I")),
            });

            // \todo Allow any member order
            string reference =
                "{" +
                "\"testDataField\":\"B\"," +
                "\"baseDataField\":\"D\"," +
                "\"testDataPayloadField\":{\"id\":\"F\",\"value\":600,\"configKey\":\"F\"}," +
                "\"baseDataPayloadField\":{\"id\":\"H\",\"value\":800,\"configKey\":\"H\"}," +
                "\"testDataProperty\":\"A\"," +
                "\"baseDataProperty\":\"C\"," +
                "\"testDataPayloadProperty\":{\"id\":\"E\",\"value\":500,\"configKey\":\"E\"}," +
                "\"baseDataPayloadProperty\":{\"id\":\"G\",\"value\":700,\"configKey\":\"G\"}," +
                "\"dataPayloadWithNestedPayload\":{\"id\":\"I\",\"value\":{\"field\":\"Level2\",\"sub\":{\"field\":\"Level3\",\"ref\":\"R3\"},\"ref\":\"R2\"},\"refTop\":\"R1\",\"configKey\":\"I\"}" +
                "}";

            Assert.AreEqual(reference, normal);
        }

        #endregion

        class TestJsonExcludeReadOnlyProperties
        {
            public class Subclass2
            {
                public string F = "F";
                public string RW { get; set; } = "RW";
                public string R => "R";
            };

            public class Subclass1
            {
                public Subclass2 F = new Subclass2();
                public Subclass2 RW { get; set; } = new Subclass2();
                public Subclass2 R => new Subclass2();
            };

            [JsonExcludeReadOnlyProperties]
            public Subclass1 ExcludedRW { get; set; } = new Subclass1();

            [JsonExcludeReadOnlyProperties]
            public Subclass1 ExcludedR { get; } = new Subclass1();

            public Subclass1 IncludedRW { get; set; } = new Subclass1();
            public Subclass1 IncludedR { get; } = new Subclass1();
        }

        [Test]
        public void JsonExcludeReadOnlyProperties()
        {
            string normal = JsonSerialization.SerializeToString(new TestJsonExcludeReadOnlyProperties());
            string reference =
                @"
                {
                    ""excludedRW"": {
                            ""f"":{""f"":""F"",""rw"":""RW""},
                            ""rw"":{""f"":""F"",""rw"":""RW""}
                            },
                    ""excludedR"": {
                            ""f"":{""f"":""F"",""rw"":""RW""},
                            ""rw"":{""f"":""F"",""rw"":""RW""}
                            },

                    ""includedRW"": {
                            ""f"":{""f"":""F"",""rw"":""RW"",""r"":""R""},
                            ""rw"":{""f"":""F"",""rw"":""RW"",""r"":""R""},
                            ""r"":{""f"":""F"",""rw"":""RW"",""r"":""R""}
                            },

                    ""includedR"": {
                            ""f"":{""f"":""F"",""rw"":""RW"",""r"":""R""},
                            ""rw"":{""f"":""F"",""rw"":""RW"",""r"":""R""},
                            ""r"":{""f"":""F"",""rw"":""RW"",""r"":""R""}
                            }
                }".Replace(" ","").Replace("\n","").Replace("\r","").Replace("\t","");

            Assert.AreEqual(reference, normal);
        }

        class RecursiveType
        {
            public class Subclass
            {
                public RecursiveType Indirect;
            }

            public RecursiveType Direct;
            public Subclass Indirect = new Subclass();
        }

        [Test]
        public void RecursiveObject()
        {
            {
                RecursiveType obj = new RecursiveType();
                obj.Direct = obj;
                JsonSerializationException ex = Assert.Throws<JsonSerializationException>(() => JsonSerialization.SerializeToString(obj));
                Assert.IsTrue(ex.Message.Contains("self referencing loop", StringComparison.OrdinalIgnoreCase));
            }
            {
                RecursiveType obj = new RecursiveType();
                obj.Indirect.Indirect = obj;
                JsonSerializationException ex = Assert.Throws<JsonSerializationException>(() => JsonSerialization.SerializeToString(obj));
                Assert.IsTrue(ex.Message.Contains("self referencing loop", StringComparison.OrdinalIgnoreCase));
            }
        }

        class SerializeNullCollectionAsEmptyType
        {
            public class Subclass1
            {
                public int[] IntArr = null;
                public Dictionary<int, int> Dict = null;
            };

            [JsonSerializeNullCollectionAsEmpty]
            public int[] IntArr = null;

            [JsonSerializeNullCollectionAsEmpty]
            public Dictionary<int, int> Dict = null;

            [JsonSerializeNullCollectionAsEmpty]
            public Subclass1[] Nested = new Subclass1[] { new Subclass1() };
        }

        [Test]
        public void JsonSerializeNullCollectionAsEmpty()
        {
            string normal = JsonSerialization.SerializeToString(new SerializeNullCollectionAsEmptyType());
            string reference =
                @"
                {
                    ""intArr"": [],
                    ""dict"": {},
                    ""nested"": [{
                            ""intArr"": null,
                            ""dict"": null
                            }]
                }".Replace(" ","").Replace("\n","").Replace("\r","").Replace("\t","");

            Assert.AreEqual(reference, normal);
        }

        class Bug_2022_09_28
        {
            public class Outer
            {
                [ForceSerializeByValue] public Inner Member = new Inner();
            }

            public class Inner : IGameConfigData<int>
            {
                public MetaDuration WillTriggerHere = MetaDuration.Zero;
                public int ConfigKey => 123;
            }
        }

        [Test]
        public void FixBug_2022_09_28()
        {
            string normal = JsonSerialization.SerializeToString(new Bug_2022_09_28.Outer());
            string reference =
                @"
                {
                    ""member"":
                    {
                        ""willTriggerHere"":""0.00:00:00.0000000"",
                        ""configKey"": 123
                    }
                }".Replace(" ","").Replace("\n","").Replace("\r","").Replace("\t","");

            Assert.AreEqual(reference, normal);
        }
    }
}
