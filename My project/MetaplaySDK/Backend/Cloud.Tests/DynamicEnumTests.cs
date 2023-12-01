// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    [TestFixture]
    public class DynamicEnumTests
    {
        [MetaSerializable]
        public class TestDynamicEnum : DynamicEnum<TestDynamicEnum>
        {
            public static readonly TestDynamicEnum TestValue0 = new TestDynamicEnum(0, nameof(TestValue0));
            public static readonly TestDynamicEnum TestValue1 = new TestDynamicEnum(1, nameof(TestValue1));

            public static TestDynamicEnum CreateTestValue(int value)
            {
                return new TestDynamicEnum(value, "dummy");
            }

            public TestDynamicEnum(int value, string name) : base(value, name, isValid: true)
            {
            }
        }

        [Test]
        public void Comparisons()
        {
            #pragma warning disable CS1718 // "Comparison made to same variable; did you mean to compare something else?"
            Assert.True(TestDynamicEnum.TestValue0 == TestDynamicEnum.TestValue0);
            Assert.True(TestDynamicEnum.TestValue1 == TestDynamicEnum.TestValue1);
            #pragma warning restore CS1718
            Assert.True(TestDynamicEnum.TestValue0 == TestDynamicEnum.CreateTestValue(0));
            Assert.True(TestDynamicEnum.CreateTestValue(0) == TestDynamicEnum.TestValue0);
            Assert.False(TestDynamicEnum.TestValue0 == TestDynamicEnum.TestValue1);
            Assert.True(TestDynamicEnum.TestValue1 == TestDynamicEnum.CreateTestValue(1));
            Assert.True(TestDynamicEnum.CreateTestValue(1) == TestDynamicEnum.TestValue1);
            Assert.False(TestDynamicEnum.TestValue1 == TestDynamicEnum.TestValue0);
            Assert.False(TestDynamicEnum.TestValue0 == null);
            Assert.False(null == TestDynamicEnum.TestValue0);
            Assert.True((TestDynamicEnum)null == (TestDynamicEnum)null);

            #pragma warning disable CS1718 // "Comparison made to same variable; did you mean to compare something else?"
            Assert.False(TestDynamicEnum.TestValue0 != TestDynamicEnum.TestValue0);
            Assert.False(TestDynamicEnum.TestValue1 != TestDynamicEnum.TestValue1);
            #pragma warning restore CS1718
            Assert.True(TestDynamicEnum.TestValue0 != TestDynamicEnum.TestValue1);
            Assert.True(TestDynamicEnum.TestValue1 != TestDynamicEnum.TestValue0);
            Assert.True(TestDynamicEnum.TestValue0 != null);
            Assert.True(null != TestDynamicEnum.TestValue0);
            Assert.False((TestDynamicEnum)null != (TestDynamicEnum)null);

            Assert.True(((object)TestDynamicEnum.TestValue0).Equals(TestDynamicEnum.TestValue0));
            Assert.False(((object)TestDynamicEnum.TestValue0).Equals(TestDynamicEnum.TestValue1));
            Assert.False(((object)TestDynamicEnum.TestValue1).Equals(TestDynamicEnum.TestValue0));
            Assert.False(((object)TestDynamicEnum.TestValue0).Equals(null));

            Assert.True(((IEquatable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue0).Equals(TestDynamicEnum.TestValue0));
            Assert.False(((IEquatable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue0).Equals(TestDynamicEnum.TestValue1));
            Assert.False(((IEquatable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue1).Equals(TestDynamicEnum.TestValue0));
            Assert.False(((IEquatable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue0).Equals(null));

            Assert.True(((IComparable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue0).CompareTo(TestDynamicEnum.TestValue0) == 0);
            Assert.True(((IComparable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue0).CompareTo(TestDynamicEnum.TestValue1) < 0);
            Assert.True(((IComparable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue0).CompareTo(null) > 0);
            Assert.True(((IComparable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue1).CompareTo(TestDynamicEnum.TestValue1) == 0);
            Assert.True(((IComparable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue1).CompareTo(TestDynamicEnum.TestValue0) > 0);
            Assert.True(((IComparable<DynamicEnum<TestDynamicEnum>>)TestDynamicEnum.TestValue1).CompareTo(null) > 0);

            Assert.True(((IComparable)TestDynamicEnum.TestValue0).CompareTo(TestDynamicEnum.TestValue0) == 0);
            Assert.True(((IComparable)TestDynamicEnum.TestValue0).CompareTo(TestDynamicEnum.TestValue1) < 0);
            Assert.True(((IComparable)TestDynamicEnum.TestValue0).CompareTo(null) > 0);
            Assert.True(((IComparable)TestDynamicEnum.TestValue1).CompareTo(TestDynamicEnum.TestValue1) == 0);
            Assert.True(((IComparable)TestDynamicEnum.TestValue1).CompareTo(TestDynamicEnum.TestValue0) > 0);
            Assert.True(((IComparable)TestDynamicEnum.TestValue1).CompareTo(null) > 0);
        }
    }
}
