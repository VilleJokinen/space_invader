// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Cloud.Tests
{
    [TestFixture]
    public class GameConfigOutputItemParserTests
    {
        [MetaSerializable]
        public class ClassContainer
        {
            [MetaMember(1)] public int      ValueProperty { get; set; }
            [MetaMember(2)] public int      ValueField;
            [MetaMember(3)] public string   ReferenceProperty { get; set; }
            [MetaMember(4)] public string   ReferenceField;

            private ClassContainer() { }

            public ClassContainer(int integer, string str)
            {
                ValueProperty = integer;
                ValueField = integer;
                ReferenceProperty = str;
                ReferenceField = str;
            }

            public override bool Equals(object obj)
            {
                if (obj is not ClassContainer other)
                    return false;

                return Equals(ValueProperty, other.ValueProperty)
                    && Equals(ValueField, other.ValueField)
                    && Equals(ReferenceProperty, other.ReferenceProperty)
                    && Equals(ReferenceField, other.ReferenceField);
            }

            public override int GetHashCode() => throw new NotImplementedException("do not use");
        }

        [MetaSerializable]
        public struct StructContainer
        {
            [MetaMember(1)] public int      ValueProperty { get; set; }
            [MetaMember(2)] public int      ValueField;
            [MetaMember(3)] public string   ReferenceProperty { get; set; }
            [MetaMember(4)] public string   ReferenceField;

            public StructContainer(int integer, string str)
            {
                ValueProperty = integer;
                ValueField = integer;
                ReferenceProperty = str;
                ReferenceField = str;
            }

            public override bool Equals(object obj)
            {
                if (obj is not StructContainer other)
                    return false;

                return Equals(ValueProperty, other.ValueProperty)
                    && Equals(ValueField, other.ValueField)
                    && Equals(ReferenceProperty, other.ReferenceProperty)
                    && Equals(ReferenceField, other.ReferenceField);
            }

            public override int GetHashCode() => throw new NotImplementedException("do not use");
        }

        [MetaSerializable]
        public class TestId : StringId<TestId> { }

        [MetaSerializable]
        public class TestInfo : IGameConfigData<TestId>
        {
            public TestId ConfigKey => Id;

            [MetaMember(1)] public TestId          Id       { get; set; }
            [MetaMember(2)] public ClassContainer  Class    { get; set; }
            [MetaMember(3)] public StructContainer Struct   { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is not TestInfo other)
                    return false;

                return Equals(Id, other.Id)
                    && Equals(Class, other.Class)
                    && Equals(Struct, other.Struct);
            }

            public override int GetHashCode() => throw new NotImplementedException("do not use");
        }

        [Test]
        public void MemberSetters()
        {
            // Test combinations of: class vs struct (as container), prop vs field (as member), and value-type vs reference-type (as member)
            // GameConfigOutputItemParser has custom behavior for these in its generated SetMember() logic which we want to exercise.

            TestInfo[] items = GameConfigTestHelper.ParseNonVariantItems<TestId, TestInfo>(
                new GameConfigParseLibraryPipelineConfig(),
                GameConfigTestHelper.ParseSpreadsheet("TestItems", new List<string>[]
                {
                    new() { "Id #key", "Class.ValueProperty", "Class.ValueField", "Class.ReferenceProperty", "Class.ReferenceField", "Struct.ValueProperty", "Struct.ValueField", "Struct.ReferenceProperty", "Struct.ReferenceField", },
                    new() { "Item",    "10",                  "10",               "Foo",                     "Foo",                  "10",                   "10",                "Foo",                      "Foo"                    },
                }));

            Assert.AreEqual(
                new TestInfo[]
                {
                    new TestInfo {
                        Id = TestId.FromString("Item"),
                        Class = new ClassContainer(10, "Foo"),
                        Struct = new StructContainer(10, "Foo"),
                    },
                }, items);
        }
    }
}
