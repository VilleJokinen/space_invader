// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    [MetaSerializable]
    public class TestStringId : StringId<TestStringId> { }

    // Hierarchy of StringId classes
    public abstract class BaseStringId<TId> : StringId<TId> where TId : StringId<TId>, new() { }
    [MetaSerializable] public class DerivedStringIdA : BaseStringId<DerivedStringIdA> { }
    [MetaSerializable] public class DerivedStringIdB : BaseStringId<DerivedStringIdB> { }

    // Not MetaSerializable
    public class NonSerializableStringId : StringId<NonSerializableStringId> { }


    [TestFixture]
    public class StringIdTests
    {
        [Test]
        public void StringIdConversion()
        {
            // StringId to-string conversion test
            string val = "KVHAEKVHYAN__4GNA.3947A8O47TNAO87TAO74TAt_A34TA34T_A34T";
            TestStringId stringId = TestStringId.FromString(val);
            Assert.AreEqual(val, stringId.ToString());
        }

        [Test]
        public void StringIdInterning()
        {
            TestStringId a = TestStringId.FromString("abba");
            TestStringId b = TestStringId.FromString("abba");
            TestStringId c = TestStringId.FromString("cd");

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.IsTrue(ReferenceEquals(a, b));
            Assert.IsFalse(ReferenceEquals(a, c));
        }

        [Test]
        public void InternedAfterSerialize()
        {
            TestStringId a = TestStringId.FromString("dingdong");
            TestStringId b = MetaSerialization.CloneTagged(a, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

            Assert.IsTrue(ReferenceEquals(a, b));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void DynamicCreate()
        {
            TestStringId a = TestStringId.FromString("zoop");
            TestStringId b = (TestStringId)StringIdUtil.CreateDynamic(typeof(TestStringId), "zoop");

            Assert.IsTrue(ReferenceEquals(a, b));
        }

        [Test]
        public void CompareTo()
        {
            Assert.AreEqual(null, TestStringId.FromString(null));
            Assert.AreEqual(0, TestStringId.FromString("foo").CompareTo(TestStringId.FromString("foo")));
            Assert.Greater(TestStringId.FromString("A").CompareTo(null), 0);
            Assert.Greater(((IComparable)TestStringId.FromString("A")).CompareTo(null), 0);
            Assert.Less(TestStringId.FromString("A").CompareTo(TestStringId.FromString("B")), 0);
            Assert.Less(((IComparable)TestStringId.FromString("A")).CompareTo(TestStringId.FromString("B")), 0);
            Assert.Less(TestStringId.FromString("A"), TestStringId.FromString("B"));
        }

        [Test]
        public void EmptyStringThrows()
        {
            Assert.Throws<ArgumentException>(() => TestStringId.FromString(""));
        }

        [TestCase("testId")]
        [TestCase("test_456")]
        [TestCase("test_")]
        [TestCase("_-.")]
        [TestCase("  testId   ", "testId")]
        [TestCase("test_Id")]
        [TestCase("test.Id")]
        [TestCase("test._123")]
        [TestCase("test-Id")]
        [TestCase("test.Id-foo-bar.ipsum")]
        [TestCase("test-Id.foo.bar-ipsum")]
        [TestCase("test.1")]
        [TestCase("test-1")]
        [TestCase("test.123")]
        [TestCase("test-123")]
        [TestCase("test.123-456")]
        [TestCase("test-123.456")]
        [TestCase("test.")]
        [TestCase("test.Id.")]
        [TestCase("test..")]
        [TestCase("test..Id")]
        [TestCase("test--Id")]
        [TestCase("test.-Id")]
        [TestCase("test-.Id")]
        [TestCase("test..123")]
        public void ConfigParsing(string input, string expectedStrParam = null)
        {
            string expectedStr = expectedStrParam ?? input; // Default to reference being same as input (holds when no whitespace is involved)

            TestStringId expected = TestStringId.FromString(expectedStr);

            ConfigLexer lexer = new ConfigLexer(input);
            TestStringId result = (TestStringId)StringIdUtil.ConfigParse(typeof(TestStringId), lexer);
            Assert.AreEqual(expected, result);
            Assert.True(lexer.IsAtEnd);
        }

        [TestCase("test Id", "test", "Id")]
        [TestCase("test Id ipsum", "test", "Id ipsum")]
        [TestCase("test,Id", "test", ",Id")]
        [TestCase("test-.,Id", "test-.", ",Id")]
        [TestCase("test ,Id", "test", ",Id")]
        [TestCase("test, Id", "test", ", Id")]
        [TestCase("test _456", "test", "_456")]
        [TestCase("test_ 456", "test_", "456")]
        [TestCase("test-Id.foo bar-ipsum", "test-Id.foo", "bar-ipsum")]
        [TestCase("test . Id", "test", ". Id")]
        [TestCase("test .Id", "test", ".Id")]
        [TestCase("testId. 123", "testId.", "123")]
        [TestCase("testId- 123", "testId-", "123")]
        [TestCase("test-Id.foo.bar- ipsum", "test-Id.foo.bar-", "ipsum")]
        public void ConfigParsingWithRemainingInput(string input, string expectedStr, string expectedRemainingInput)
        {
            TestStringId expected = TestStringId.FromString(expectedStr);

            ConfigLexer lexer = new ConfigLexer(input);
            TestStringId result = (TestStringId)StringIdUtil.ConfigParse(typeof(TestStringId), lexer);
            Assert.AreEqual(expected, result);
            Assert.False(lexer.IsAtEnd);
            Assert.AreEqual(expectedRemainingInput, lexer.Input.Substring(lexer.CurrentToken.StartOffset));
        }

        [TestCase("")]
        [TestCase(",")]
        [TestCase("123")]
        [TestCase(".test")]
        [TestCase(".test.Id")]
        [TestCase("-test")]
        [TestCase("-test.Id")]
        public void ConfigParsingInvalid(string input)
        {
            Assert.Throws<ParseError>(() =>
            {
                ConfigLexer lexer = new ConfigLexer(input);
                StringIdUtil.ConfigParse(typeof(TestStringId), lexer);
            });
        }

        /// <summary>
        /// Test multiple StringId tokens in the same string, testing that
        /// the lexer advances correctly over these custom tokens.
        /// </summary>
        public void ConfigParsingMultipleStringIds()
        {
            ConfigLexer lexer = new ConfigLexer("  abc-  abc-d  abc-123  abc-1  abc.  abc.d  abc.123  abc.1  ");
            Assert.AreEqual(TestStringId.FromString("abc-"), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.AreEqual(TestStringId.FromString("abc-d"), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.AreEqual(TestStringId.FromString("abc-123"), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.AreEqual(TestStringId.FromString("abc-1"), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.AreEqual(TestStringId.FromString("abc."), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.AreEqual(TestStringId.FromString("abc.d"), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.AreEqual(TestStringId.FromString("abc.123"), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.AreEqual(TestStringId.FromString("abc.1"), StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            Assert.IsTrue(lexer.IsAtEnd);
        }

        /// <summary>
        /// Test that the lexer's position is left as is after trying
        /// to parse an invalid StringId.
        /// </summary>
        public void ConfigParsingPositionAfterInvalid()
        {
            ConfigLexer lexer = new ConfigLexer("  .test  -id");
            int offsetBefore = lexer.Offset;
            Assert.Throws<ParseError>(() => StringIdUtil.ConfigParse(typeof(TestStringId), lexer));
            int offsetAfter = lexer.Offset;
            Assert.AreEqual(offsetBefore, offsetAfter);
        }

        [TestCase]
        public void TestHierarchical()
        {
            // Differing StringId types, but equal values (still should not match)
            DerivedStringIdA a = DerivedStringIdA.FromString("test");
            DerivedStringIdB b = DerivedStringIdB.FromString("test");
            Assert.AreEqual("test", a.Value);
            Assert.AreEqual("test", b.Value);
            Assert.AreNotEqual(a, b);
        }

        [TestCase]
        public void TestNonSerializableDynamicCreation()
        {
            IStringId a = StringIdUtil.CreateDynamic(typeof(NonSerializableStringId), "test");
            NonSerializableStringId typed = (NonSerializableStringId)a;
            Assert.AreEqual("test", typed.Value);
        }

    }
}
