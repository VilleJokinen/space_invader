// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_USE_LEGACY_FIXED_POINT_PARSING // only work with the new parser

using Metaplay.Core;
using Metaplay.Core.Json;
using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    class Fixed64Tests
    {
        [Test]
        public void Parse()
        {
            // 0.0
            Assert.AreEqual(F64.Zero, F64.Parse("0.0"));
            Assert.AreEqual(F64.Zero, F64.Parse("000.0"));
            Assert.AreEqual(F64.Zero, F64.Parse(".0"));
            Assert.AreEqual(F64.Zero, F64.Parse(".00000000000000000000"));
            Assert.AreEqual(F64.Zero, F64.Parse("0."));
            Assert.AreEqual(F64.Zero, F64.Parse("000."));
            Assert.AreEqual(F64.Zero, F64.Parse("0"));
            Assert.AreEqual(F64.Zero, F64.Parse("000"));
            Assert.AreEqual(F64.Zero, F64.Parse("+0.0"));
            Assert.AreEqual(F64.Zero, F64.Parse("+000.0"));
            Assert.AreEqual(F64.Zero, F64.Parse("+.0"));
            Assert.AreEqual(F64.Zero, F64.Parse("+.00000000000000000000"));
            Assert.AreEqual(F64.Zero, F64.Parse("+0."));
            Assert.AreEqual(F64.Zero, F64.Parse("+000."));
            Assert.AreEqual(F64.Zero, F64.Parse("+0"));
            Assert.AreEqual(F64.Zero, F64.Parse("+000"));
            Assert.AreEqual(F64.Zero, F64.Parse("-0.0"));
            Assert.AreEqual(F64.Zero, F64.Parse("-000.0"));
            Assert.AreEqual(F64.Zero, F64.Parse("-.0"));
            Assert.AreEqual(F64.Zero, F64.Parse("-.00000000000000000000"));
            Assert.AreEqual(F64.Zero, F64.Parse("-0."));
            Assert.AreEqual(F64.Zero, F64.Parse("-000."));
            Assert.AreEqual(F64.Zero, F64.Parse("-0"));
            Assert.AreEqual(F64.Zero, F64.Parse("-000"));

            // 1.0 and -1.0
            Assert.AreEqual(F64.One, F64.Parse("1.0"));
            Assert.AreEqual(F64.One, F64.Parse("0001.0"));
            Assert.AreEqual(F64.One, F64.Parse("1.00000000000000000000"));
            Assert.AreEqual(F64.One, F64.Parse("1."));
            Assert.AreEqual(F64.One, F64.Parse("1"));
            Assert.AreEqual(F64.One, F64.Parse("+1.0"));
            Assert.AreEqual(F64.One, F64.Parse("+1.00000000000000000000"));
            Assert.AreEqual(F64.One, F64.Parse("+1."));
            Assert.AreEqual(F64.One, F64.Parse("+1"));
            Assert.AreEqual(-F64.One, F64.Parse("-1.0"));
            Assert.AreEqual(-F64.One, F64.Parse("-1.00000000000000000000"));
            Assert.AreEqual(-F64.One, F64.Parse("-1."));
            Assert.AreEqual(-F64.One, F64.Parse("-1"));

            // Min/max values
            // \todo [petri] actual min/max
            Assert.AreEqual(F64.FromInt(32767), F64.Parse("32767"));
            Assert.AreEqual(F64.FromInt(32767), F64.Parse("32767."));
            Assert.AreEqual(F64.FromInt(32767), F64.Parse("32767.0"));
            Assert.AreEqual(F64.FromRaw(int.MaxValue), F64.Parse("0.4999999998"));
            Assert.AreEqual(F64.FromRaw(int.MaxValue), F64.Parse("0.4999999998123456789"));
            Assert.AreEqual(F64.FromRaw(long.MaxValue), F64.Parse("2147483647.9999999998"));
            Assert.AreEqual(F64.FromRaw(long.MaxValue), F64.Parse("2147483647.9999999998123456789"));
            Assert.AreEqual(F64.FromInt(32767), F64.Parse("+32767"));
            Assert.AreEqual(F64.FromInt(32767), F64.Parse("+32767."));
            Assert.AreEqual(F64.FromInt(32767), F64.Parse("+32767.0"));
            Assert.AreEqual(F64.FromInt(-64768), F64.Parse("-64768"));
            Assert.AreEqual(F64.FromInt(-64768), F64.Parse("-64768."));
            Assert.AreEqual(F64.FromInt(-64768), F64.Parse("-64768.0"));
            Assert.AreEqual(F64.FromRaw(int.MinValue + 1), F64.Parse("-0.4999999998"));
            Assert.AreEqual(F64.FromRaw(int.MinValue + 1), F64.Parse("-0.4999999998123456789"));
            Assert.AreEqual(F64.FromRaw(long.MinValue + 1), F64.Parse("-2147483647.9999999998"));
            Assert.AreEqual(F64.FromRaw(long.MinValue + 1), F64.Parse("-2147483647.9999999998123456789"));
        }

        [Test]
        public void ParseInvalid()
        {
            // Invalid inputs
            Assert.Throws<ArgumentException>(() => F64.Parse(""));
            Assert.Throws<ArgumentException>(() => F64.Parse("+"));
            Assert.Throws<ArgumentException>(() => F64.Parse("-"));
            Assert.Throws<ArgumentException>(() => F64.Parse("."));
            Assert.Throws<ArgumentException>(() => F64.Parse("+."));
            Assert.Throws<ArgumentException>(() => F64.Parse("-."));
            Assert.Throws<ArgumentException>(() => F64.Parse("a."));
            Assert.Throws<ArgumentException>(() => F64.Parse("+a."));
            Assert.Throws<ArgumentException>(() => F64.Parse("-a."));
            Assert.Throws<ArgumentException>(() => F64.Parse(".a"));
            Assert.Throws<ArgumentException>(() => F64.Parse("+.a"));
            Assert.Throws<ArgumentException>(() => F64.Parse("-.a"));
            Assert.Throws<ArgumentException>(() => F64.Parse("abc"));
            Assert.Throws<ArgumentException>(() => F64.Parse("+abc"));
            Assert.Throws<ArgumentException>(() => F64.Parse("-abc"));
            Assert.Throws<ArgumentException>(() => F64.Parse("1.0a"));
            Assert.Throws<ArgumentException>(() => F64.Parse("+1.0a"));
            Assert.Throws<ArgumentException>(() => F64.Parse("-1.0a"));
            Assert.Throws<ArgumentException>(() => F64.Parse("1.a0"));
            Assert.Throws<ArgumentException>(() => F64.Parse("+1.a0"));
            Assert.Throws<ArgumentException>(() => F64.Parse("-1.a0"));

            Assert.Throws<ArgumentException>(() => F64.Parse("1 .0"));
            Assert.Throws<ArgumentException>(() => F64.Parse("1 1.0"));
            Assert.Throws<ArgumentException>(() => F64.Parse("1. 0"));
            Assert.Throws<ArgumentException>(() => F64.Parse("1.1 0"));

            // Overflow values
            Assert.Throws<OverflowException>(() => F64.Parse("2147483648"));
            Assert.Throws<OverflowException>(() => F64.Parse("1000000000000000000000"));
            Assert.Throws<OverflowException>(() => F64.Parse("-2147483649"));
            Assert.Throws<OverflowException>(() => F64.Parse("-2147483648.001"));
            Assert.Throws<OverflowException>(() => F64.Parse("-1000000000000000000000"));
        }

        [Test]
        public void ToDecimalString()
        {
            Assert.AreEqual("0.0", F64.Zero.ToString());

            Assert.AreEqual("0.5", F64.Half.ToString());
            Assert.AreEqual("1.0", F64.One.ToString());
            Assert.AreEqual("1.2", F64.Ratio(12, 10).ToString());
            Assert.AreEqual("1.23", F64.Ratio(123, 100).ToString());
            Assert.AreEqual("32767.0", F64.FromInt(32767).ToString());
            Assert.AreEqual("0.4999999998", F64.FromRaw(int.MaxValue).ToString());
            Assert.AreEqual("2147483647.9999999998", F64.FromRaw(long.MaxValue).ToString());

            Assert.AreEqual("-0.5", (-F64.Half).ToString());
            Assert.AreEqual("-1.0", (-F64.One).ToString());
            Assert.AreEqual("-1.2", (-F64.Ratio(12, 10)).ToString());
            Assert.AreEqual("-1.23", (-F64.Ratio(123, 100)).ToString());
            Assert.AreEqual("-32767.0", (-F64.FromInt(32767)).ToString());
            Assert.AreEqual("-0.4999999998", (-F64.FromRaw(int.MaxValue)).ToString());
            Assert.AreEqual("-2147483647.9999999998", (-F64.FromRaw(long.MaxValue)).ToString());
            Assert.AreEqual("-2147483648.0", (F64.FromRaw(long.MinValue)).ToString());
        }

        [Test]
        public void RoundTrip()
        {
            // Use exhaustive search of all fraction bit patterns (more coverage but takes minutes or hours) or random sampling (much faster)
            bool useExhaustiveFracSearch = false;

            // Integer values to test with fractions
            int[] integers = new int[] { int.MinValue, -2, -1, 0, 1, 2, int.MaxValue };
            int[] vsDoubleInts = new int[] { -1 << 20, 1 << 20 };

            // With exhaustive search, iterate over all the 24-bit low fraction bits (8 top bits are handled by parallel foreach)
            // With random sampling, just use 500 random values (tests a total of ~125k values per test invocation)
            long innerLoopRange = useExhaustiveFracSearch ? 0x1000000 : 500;

            RandomPCG rng = RandomPCG.CreateNew(); // randomize seed on each run to get more coverage
            Parallel.For(0L, 256L, highByte =>
            {
                for (long lowBits = 0; lowBits < innerLoopRange; lowBits++)
                {
                    // Combine fraction from high & low bits (for exhaustive search) or randomize it (for random sampling)
                    long frac = useExhaustiveFracSearch ? (highByte << 24) + lowBits : rng.NextLong() & 0xFFFFFFFF;

                    // Test fraction with various integer parts
                    foreach (int integer in integers)
                    {
                        // Conversion to string and back must result in the same value losslessly
                        F64 x = F64.FromRaw(((long)integer << 32) + (frac * Math.Sign(integer)));
                        string s = x.ToString();
                        Assert.AreEqual(x, F64.Parse(s));

                        // Appending zeros should not change result
                        Assert.AreEqual(x, F64.Parse(s + "00000000000"));

                        // Must not have trailing zeroes (except one after the dot)
                        if (s.EndsWith("0", StringComparison.Ordinal))
                            Assert.IsTrue(s.EndsWith(".0", StringComparison.Ordinal));
                    }

                    // Compare against double-to-string conversion (only run on .NET as Unity's conversion cannot be relied upon).
                    // \note We must make sure the fixed-point numbers fractional bits end up as the lowest bits of the double
                    // mantissa, otherwise the double requires a longer string output to distinguish from its neighboring values.
                    if (frac != 0) // frac==0 only prints the integer part for doubles
                    {
                        foreach (int integer in vsDoubleInts)
                        {
                            F64 x = F64.FromRaw(((long)integer << 32) + (frac * Math.Sign(integer)));
                            double d = x.Double;
                            string xs = x.ToString();
                            string ds = d.ToString(CultureInfo.InvariantCulture);
                            Assert.AreEqual(ds, xs);
                            //if (ds != xs)
                            //    Console.WriteLine("Mismatch with double: fixed={0}, double={1} (0x{2:X16} vs 0x{3:X16})", xs, ds, x.Raw, BitConverter.DoubleToUInt64Bits(d));
                        }
                    }
                }
            });
        }

        [Test]
        public void JsonConvert()
        {
            (string, F64)[] values = new (string, F64)[]
            {
                ("0.0", F64.Zero),
                ("0.5", F64.Half),
                ("1.2", F64.Ratio(12, 10)),
                ("2147483647.9999999998", F64.FromRaw(long.MaxValue)),
                ("-0.5", -F64.Half),
                ("-1.2", -F64.Ratio(12, 10)),
                ("-2147483647.9999999998", -F64.FromRaw(long.MaxValue)),
            };

            foreach ((string reference, F64 value) in values)
            {
                Assert.AreEqual(reference, JsonSerialization.SerializeToString(value));
                Assert.AreEqual(value, JsonSerialization.Deserialize<F64>(reference));
            }
        }
    }
}

#endif // !METAPLAY_USE_LEGACY_FIXED_POINT_PARSING
