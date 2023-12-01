// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    [TestFixture]
    public class MetaUInt128Tests
    {
        static readonly MetaUInt128 EmptyMask   = MetaUInt128.Zero;
        static readonly MetaUInt128 FullMask    = ~MetaUInt128.Zero;
        static readonly MetaUInt128 HighMask    = new MetaUInt128(ulong.MaxValue, 0ul);
        static readonly MetaUInt128 LowMask     = new MetaUInt128(0ul, ulong.MaxValue);
        static readonly MetaUInt128 EvenMask    = new MetaUInt128(0xAAAA_AAAA_AAAA_AAAAul, 0xAAAA_AAAA_AAAA_AAAAul);
        static readonly MetaUInt128 OddMask     = new MetaUInt128(0x5555_5555_5555_5555ul, 0x5555_5555_5555_5555ul);

        [Test]
        public void TestConstructors()
        {
            Assert.AreEqual(0ul, EmptyMask.High);
            Assert.AreEqual(0ul, EmptyMask.Low);
            Assert.AreEqual(ulong.MaxValue, FullMask.High);
            Assert.AreEqual(ulong.MaxValue, FullMask.Low);
            Assert.AreEqual(ulong.MaxValue, HighMask.High);
            Assert.AreEqual(0ul, HighMask.Low);
            Assert.AreEqual(0ul, LowMask.High);
            Assert.AreEqual(ulong.MaxValue, LowMask.Low);
        }

        [Test]
        public void TestComparisons()
        {
            Assert.AreEqual(MetaUInt128.Zero, new MetaUInt128());
            Assert.AreNotEqual(MetaUInt128.One, MetaUInt128.Zero);
            Assert.True(MetaUInt128.Zero >= MetaUInt128.Zero);
            Assert.True(MetaUInt128.Zero <= MetaUInt128.Zero);
            Assert.True(MetaUInt128.One > MetaUInt128.Zero);
            Assert.True(MetaUInt128.One >= MetaUInt128.Zero);
            Assert.False(MetaUInt128.One < MetaUInt128.Zero);
            Assert.False(MetaUInt128.One <= MetaUInt128.Zero);

            Assert.True(new MetaUInt128(1, 0) > new MetaUInt128(0, ulong.MaxValue));
            Assert.True(new MetaUInt128(1, 0) >= new MetaUInt128(0, ulong.MaxValue));
            Assert.False(new MetaUInt128(1, 0) < new MetaUInt128(0, ulong.MaxValue));
            Assert.False(new MetaUInt128(1, 0) <= new MetaUInt128(0, ulong.MaxValue));
        }

        [Test]
        public void TestBitwiseOperations()
        {
            Assert.AreEqual(new MetaUInt128(ulong.MaxValue, ulong.MaxValue), ~MetaUInt128.Zero);
            Assert.AreEqual(new MetaUInt128(ulong.MaxValue, ulong.MaxValue - 1), ~MetaUInt128.One);

            Assert.AreEqual(FullMask, HighMask | LowMask);
            Assert.AreEqual(FullMask, EvenMask | OddMask);
            Assert.AreEqual(HighMask, HighMask | EmptyMask);
            Assert.AreEqual(EvenMask, EvenMask | EmptyMask);

            Assert.AreEqual(HighMask, HighMask & FullMask);
            Assert.AreEqual(EmptyMask, HighMask & LowMask);
            Assert.AreEqual(EmptyMask, EvenMask & OddMask);
            Assert.AreEqual(EvenMask, EvenMask & FullMask);

            Assert.AreEqual(OddMask, EvenMask ^ FullMask);
            Assert.AreEqual(EmptyMask, EvenMask ^ EvenMask);
            Assert.AreEqual(FullMask, EvenMask ^ OddMask);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        [TestCase(63)]
        public void TestShortShifts(int shift)
        {
            Assert.AreEqual(ulong.MaxValue, (FullMask << shift).High);
            Assert.AreEqual(ulong.MaxValue << shift, (FullMask << shift).Low);

            Assert.AreEqual(ulong.MaxValue, (FullMask >> shift).Low);
            Assert.AreEqual(ulong.MaxValue >> shift, (FullMask >> shift).High);
        }

        [TestCase(64)]
        [TestCase(65)]
        [TestCase(127)]
        [TestCase(128)]
        public void TestLongShifts(int shift)
        {
            Assert.AreEqual(0ul, (FullMask << shift).Low);
            Assert.AreEqual((shift >= 128) ? 0ul : (ulong.MaxValue << (shift - 64)), (FullMask << shift).High);

            Assert.AreEqual(0ul, (FullMask >> shift).High);
            Assert.AreEqual((shift >= 128) ? 0ul : (ulong.MaxValue >> (shift - 64)), (FullMask >> shift).Low);
        }

        [Test]
        public void TestCompareTo()
        {
            Assert.IsTrue(MetaUInt128.Zero.CompareTo(MetaUInt128.Zero) == 0);
            Assert.IsTrue(MetaUInt128.Zero.CompareTo(MetaUInt128.One) < 0);
            Assert.IsTrue(MetaUInt128.One.CompareTo(MetaUInt128.Zero) > 0);

            Assert.IsTrue(((IComparable)MetaUInt128.Zero).CompareTo(MetaUInt128.Zero) == 0);
            Assert.IsTrue(((IComparable)MetaUInt128.Zero).CompareTo(MetaUInt128.One) < 0);
            Assert.IsTrue(((IComparable)MetaUInt128.One).CompareTo(MetaUInt128.Zero) > 0);

            Assert.Less(MetaUInt128.Zero, MetaUInt128.One);
            Assert.Greater(MetaUInt128.One, MetaUInt128.Zero);
        }
    }
}
