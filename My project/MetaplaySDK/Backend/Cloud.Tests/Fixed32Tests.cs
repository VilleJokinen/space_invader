// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_USE_LEGACY_FIXED_POINT_PARSING // only work with the new parser

using Metaplay.Core.Json;
using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    class Fixed32Tests
    {
        [Test]
        public void ParseValid()
        {
            // 0.0
            Assert.AreEqual(F32.Zero, F32.Parse("0.0"));
            Assert.AreEqual(F32.Zero, F32.Parse("000.0"));
            Assert.AreEqual(F32.Zero, F32.Parse(".0"));
            Assert.AreEqual(F32.Zero, F32.Parse(".00000000000000"));
            Assert.AreEqual(F32.Zero, F32.Parse("0."));
            Assert.AreEqual(F32.Zero, F32.Parse("000."));
            Assert.AreEqual(F32.Zero, F32.Parse("0"));
            Assert.AreEqual(F32.Zero, F32.Parse("000"));
            Assert.AreEqual(F32.Zero, F32.Parse("+0.0"));
            Assert.AreEqual(F32.Zero, F32.Parse("+000.0"));
            Assert.AreEqual(F32.Zero, F32.Parse("+.0"));
            Assert.AreEqual(F32.Zero, F32.Parse("+.00000000000000"));
            Assert.AreEqual(F32.Zero, F32.Parse("+0."));
            Assert.AreEqual(F32.Zero, F32.Parse("+000."));
            Assert.AreEqual(F32.Zero, F32.Parse("+0"));
            Assert.AreEqual(F32.Zero, F32.Parse("+000"));
            Assert.AreEqual(F32.Zero, F32.Parse("-0.0"));
            Assert.AreEqual(F32.Zero, F32.Parse("-000.0"));
            Assert.AreEqual(F32.Zero, F32.Parse("-.0"));
            Assert.AreEqual(F32.Zero, F32.Parse("-.00000000000000"));
            Assert.AreEqual(F32.Zero, F32.Parse("-0."));
            Assert.AreEqual(F32.Zero, F32.Parse("-000."));
            Assert.AreEqual(F32.Zero, F32.Parse("-0"));
            Assert.AreEqual(F32.Zero, F32.Parse("-000"));

            // 1.0 and -1.0
            Assert.AreEqual(F32.One, F32.Parse("1.0"));
            Assert.AreEqual(F32.One, F32.Parse("0001.0"));
            Assert.AreEqual(F32.One, F32.Parse("1.00000000000000"));
            Assert.AreEqual(F32.One, F32.Parse("1."));
            Assert.AreEqual(F32.One, F32.Parse("1"));
            Assert.AreEqual(F32.One, F32.Parse("+1.0"));
            Assert.AreEqual(F32.One, F32.Parse("+1.00000000000000"));
            Assert.AreEqual(F32.One, F32.Parse("+1."));
            Assert.AreEqual(F32.One, F32.Parse("+1"));
            Assert.AreEqual(-F32.One, F32.Parse("-1.0"));
            Assert.AreEqual(-F32.One, F32.Parse("-1.00000000000000"));
            Assert.AreEqual(-F32.One, F32.Parse("-1."));
            Assert.AreEqual(-F32.One, F32.Parse("-1"));

            // Min/max values
            Assert.AreEqual(F32.FromInt(32767), F32.Parse("32767"));
            Assert.AreEqual(F32.FromInt(32767), F32.Parse("32767."));
            Assert.AreEqual(F32.FromInt(32767), F32.Parse("32767.0"));
            Assert.AreEqual(F32.FromRaw(int.MaxValue), F32.Parse("32767.99998"));
            Assert.AreEqual(F32.FromRaw(int.MaxValue), F32.Parse("32767.99999"));
            Assert.AreEqual(F32.FromInt(32767), F32.Parse("+32767"));
            Assert.AreEqual(F32.FromInt(32767), F32.Parse("+32767."));
            Assert.AreEqual(F32.FromInt(32767), F32.Parse("+32767.0"));
            Assert.AreEqual(F32.FromInt(-32768), F32.Parse("-32768"));
            Assert.AreEqual(F32.FromInt(-32768), F32.Parse("-32768."));
            Assert.AreEqual(F32.FromInt(-32768), F32.Parse("-32768.0"));
            Assert.AreEqual(F32.FromRaw(int.MinValue + 1), F32.Parse("-32767.99998"));
            Assert.AreEqual(F32.FromRaw(int.MinValue + 1), F32.Parse("-32767.99999"));
            Assert.AreEqual(F32.FromRaw(int.MinValue), F32.Parse("-32768.0"));
        }

        [Test]
        public void ParseInvalid()
        {
            // Invalid inputs
            Assert.Throws<ArgumentException>(() => F32.Parse(""));
            Assert.Throws<ArgumentException>(() => F32.Parse("+"));
            Assert.Throws<ArgumentException>(() => F32.Parse("-"));
            Assert.Throws<ArgumentException>(() => F32.Parse("."));
            Assert.Throws<ArgumentException>(() => F32.Parse("+."));
            Assert.Throws<ArgumentException>(() => F32.Parse("-."));
            Assert.Throws<ArgumentException>(() => F32.Parse("a."));
            Assert.Throws<ArgumentException>(() => F32.Parse("+a."));
            Assert.Throws<ArgumentException>(() => F32.Parse("-a."));
            Assert.Throws<ArgumentException>(() => F32.Parse(".a"));
            Assert.Throws<ArgumentException>(() => F32.Parse("+.a"));
            Assert.Throws<ArgumentException>(() => F32.Parse("-.a"));
            Assert.Throws<ArgumentException>(() => F32.Parse("abc"));
            Assert.Throws<ArgumentException>(() => F32.Parse("+abc"));
            Assert.Throws<ArgumentException>(() => F32.Parse("-abc"));
            Assert.Throws<ArgumentException>(() => F32.Parse("1.0a"));
            Assert.Throws<ArgumentException>(() => F32.Parse("+1.0a"));
            Assert.Throws<ArgumentException>(() => F32.Parse("-1.0a"));
            Assert.Throws<ArgumentException>(() => F32.Parse("1.a0"));
            Assert.Throws<ArgumentException>(() => F32.Parse("+1.a0"));
            Assert.Throws<ArgumentException>(() => F32.Parse("-1.a0"));

            Assert.Throws<ArgumentException>(() => F32.Parse("1 .0"));
            Assert.Throws<ArgumentException>(() => F32.Parse("1 1.0"));
            Assert.Throws<ArgumentException>(() => F32.Parse("1. 0"));
            Assert.Throws<ArgumentException>(() => F32.Parse("1.1 0"));

            // Overflow values
            Assert.Throws<OverflowException>(() => F32.Parse("32768"));
            Assert.Throws<OverflowException>(() => F32.Parse("100000000000"));
            Assert.Throws<OverflowException>(() => F32.Parse("-32769"));
            Assert.Throws<OverflowException>(() => F32.Parse("-32768.001"));
            Assert.Throws<OverflowException>(() => F32.Parse("-100000000000"));
        }

        [Test]
        public void ToDecimalString()
        {
            Assert.AreEqual("0.0", F32.Zero.ToString());

            Assert.AreEqual("0.5", F32.Half.ToString());
            Assert.AreEqual("1.0", F32.One.ToString());
            Assert.AreEqual("1.2", F32.Ratio(12, 10).ToString());
            Assert.AreEqual("1.23", F32.Ratio(123, 100).ToString());
            Assert.AreEqual("32767.0", F32.FromInt(32767).ToString());
            Assert.AreEqual("32767.99998", F32.FromRaw(int.MaxValue).ToString());

            Assert.AreEqual("-0.5", (-F32.Half).ToString());
            Assert.AreEqual("-1.0", (-F32.One).ToString());
            Assert.AreEqual("-1.2", (-F32.Ratio(12, 10)).ToString());
            Assert.AreEqual("-1.23", (-F32.Ratio(123, 100)).ToString());
            Assert.AreEqual("-32767.0", (-F32.FromInt(32767)).ToString());
            Assert.AreEqual("-32767.99998", (-F32.FromRaw(int.MaxValue)).ToString());
        }

        [Test]
        public void RoundTrip()
        {
            // Integer values to test with fractions
            int[] integers = new int[] { -32876, -2, -1, 0, 1, 2, 32767 };
            int[] vsFloatInts = new int[] { -128, 128 };

            // Iterate over high 8 bits in parallel
            Parallel.For(0, 256, highByte =>
            {
                // Iterate over low 8 bits sequentially
                for (int lowByte = 0; lowByte < 256; lowByte++)
                {
                    // Combine high and low bytes to final fraction
                    int frac = (highByte << 8) + lowByte;

                    // Test fraction with various integer parts
                    foreach (int integer in integers)
                    {
                        // Conversion to string and back must result in the same value losslessly
                        F32 x = F32.FromRaw((integer << 16) + (frac * Math.Sign(integer)));
                        string s = x.ToString();
                        Assert.AreEqual(x, F32.Parse(s));

                        // Appending zeros should not change result
                        Assert.AreEqual(x, F32.Parse(s + "00000000000"));

                        // Must not have trailing zeroes (except one after the dot)
                        if (s.EndsWith("0", StringComparison.Ordinal))
                            Assert.IsTrue(s.EndsWith(".0", StringComparison.Ordinal));
                    }

                    // Compare against float-to-string conversion (only run on .NET as Unity's conversion cannot be relied upon).
                    // \note We must make sure the fixed-point numbers fractional bits end up as the lowest bits of the float
                    // mantissa, otherwise the float requires a longer string output to distinguish from its neighboring values.
                    if (frac != 0) // frac==0 only prints the integer part for doubles
                    {
                        foreach (int integer in vsFloatInts)
                        {
                            F32 x = F32.FromRaw((integer << 16) + (frac * Math.Sign(integer)));
                            float f = x.Float;
                            string xs = x.ToString();
                            string fs = f.ToString(CultureInfo.InvariantCulture);
                            Assert.AreEqual(fs, xs);
                            //if (fs != xs)
                            //    Console.WriteLine("Mismatch with float: fixed={0}, float={1} (0x{2:X8} vs 0x{3:X8})", xs, fs, x.Raw, BitConverter.SingleToUInt32Bits(f));
                        }
                    }
                }
            });
        }

        [Test]
        public void JsonConvert()
        {
            (string, F32)[] values = new (string, F32)[]
            {
                ("0.0", F32.Zero),
                ("0.5", F32.Half),
                ("1.2", F32.Ratio(12, 10)),
                ("32767.99998", F32.FromRaw(int.MaxValue)),
                ("-0.5", -F32.Half),
                ("-1.2", -F32.Ratio(12, 10)),
                ("-32767.99998", -F32.FromRaw(int.MaxValue)),
            };

            foreach ((string reference, F32 value) in values)
            {
                Assert.AreEqual(reference, JsonSerialization.SerializeToString(value));
                Assert.AreEqual(value, JsonSerialization.Deserialize<F32>(reference));
            }
        }
    }
}

#endif // !METAPLAY_USE_LEGACY_FIXED_POINT_PARSING
