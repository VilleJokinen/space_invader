// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Cloud.Tests
{
    [TestFixture]
    public class RandomTests
    {
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(7)]
        [TestCase(11)]
        [TestCase(128)]
        public void TestSmallValueDistribution(int numBuckets)
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(12345);
            int[] samples = new int[numBuckets];

            int numIters = numBuckets * 500;
            for (int ndx = 0; ndx < numIters; ndx++)
                samples[rnd.NextInt(numBuckets)]++;

            int expected = numIters / numBuckets;
            int min = expected * 85 / 100;
            int max = expected * 115 / 100;
            for (int ndx = 0; ndx < numBuckets; ndx++)
            {
                Assert.GreaterOrEqual(samples[ndx], min);
                Assert.LessOrEqual(samples[ndx], max);
            }
        }

        [Test]
        public void TestRandomIntExclusive()
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(1234);
            for (int iter = 0; iter < 100; iter++)
            {
                for (int max = 1; max < 1000; max++)
                {
                    int v = rnd.NextInt(max);
                    Assert.GreaterOrEqual(v, 0);
                    Assert.Less(v, max);
                }
            }
        }

        [Test]
        public void TestRandomFloatRange()
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(1234);
            for (int max = 1; max < 10000; max++)
            {
                float v = rnd.NextFloat();
                Assert.GreaterOrEqual(v, 0.0f);
                Assert.Less(v, 1.0f);
            }
        }

        [Test]
        public void TestRandomDoubleRange()
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(4321);
            for (int max = 1; max < 10000; max++)
            {
                double v = rnd.NextDouble();
                Assert.GreaterOrEqual(v, 0.0);
                Assert.Less(v, 1.0);
            }
        }

        [Test]
        public void TestRandomBool()
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(1234);
            int numTrue = 0;
            for (int ndx = 0; ndx < 1000; ndx++)
                numTrue += rnd.NextBool() ? 1 : 0;
            Assert.GreaterOrEqual(numTrue, 450);
            Assert.LessOrEqual(numTrue, 550);
        }

        [Test]
        public void TestListChoiceEmpty()
        {
            RandomPCG rnd = RandomPCG.CreateNew();
            int[] values = new int[] { };
            Assert.AreEqual(0, rnd.Choice(values));
        }

        [Test]
        public void TestEnumerableChoiceEmpty()
        {
            RandomPCG rnd = RandomPCG.CreateNew();
            List<int> values = new List<int>();
            Assert.AreEqual(0, rnd.Choice((IEnumerable<int>)values));
        }

        [TestCase(new int[] { 1 })]
        [TestCase(new int[] { 1, 2, 3 })]
        [TestCase(new int[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
        public void TestListChoice(int[] values)
        {
            int[] samples = new int[values.Length];

            RandomPCG rnd = RandomPCG.CreateFromSeed(1234);
            int numIters = values.Length * 500;
            for (int ndx = 0; ndx < numIters; ndx++)
                samples[rnd.Choice(values) - 1]++;

            int expected = numIters / values.Length;
            int min = expected * 85 / 100;
            int max = expected * 115 / 100;
            for (int ndx = 0; ndx < values.Length; ndx++)
            {
                Assert.GreaterOrEqual(samples[ndx], min);
                Assert.LessOrEqual(samples[ndx], max);
            }
        }

        [TestCase(new int[] { 1 })]
        [TestCase(new int[] { 1, 2, 3 })]
        [TestCase(new int[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
        public void TestEnumerableChoice(int[] values)
        {
            int[] samples = new int[values.Length];

            RandomPCG rnd = RandomPCG.CreateFromSeed(1234);
            int numIters = values.Length * 500;
            for (int ndx = 0; ndx < numIters; ndx++)
                samples[rnd.Choice((IEnumerable<int>)values) - 1]++;

            int expected = numIters / values.Length;
            int min = expected * 85 / 100;
            int max = expected * 115 / 100;
            for (int ndx = 0; ndx < values.Length; ndx++)
            {
                Assert.GreaterOrEqual(samples[ndx], min);
                Assert.LessOrEqual(samples[ndx], max);
            }
        }

        [Test]
        public void TestWeightedIndexEmpty()
        {
            RandomPCG rnd = RandomPCG.CreateNew();
            Assert.AreEqual(-1, rnd.GetWeightedIndex(new int[] { }));
        }

        [TestCase(new int[] { 1 })]
        [TestCase(new int[] { 1, 2, 3 })]
        [TestCase(new int[] { 5, 20, 60 })]
        [TestCase(new int[] { 1, 1000 })]
        [TestCase(new int[] { 100_000, 1_000_000 })]
        public void TestWeightedIndexInt(int[] weights)
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(1234);
            int[] samples = new int[weights.Length];

            for (int ndx = 0; ndx < 10000; ndx++)
                samples[rnd.GetWeightedIndex(weights)]++;

            int totalWeight = weights.Sum();
            for (int ndx = 0; ndx < weights.Length; ndx++)
            {
                int expected = (int)(weights[ndx] * 10000.0 / totalWeight);
                Assert.GreaterOrEqual(samples[ndx], expected * 90 / 100);
                Assert.LessOrEqual(samples[ndx], expected * 110 / 100);
            }
        }

        [Test]
        public void TestWeightedIndexF32()
        {
            F32[] weights = new F32[] { F32.Ratio100(5), F32.Ratio100(20), F32.Ratio100(60) };

            RandomPCG rnd = RandomPCG.CreateFromSeed(1234);
            int[] samples = new int[weights.Length];

            for (int ndx = 0; ndx < 10000; ndx++)
                samples[rnd.GetWeightedIndex(weights)]++;

            double totalWeight = weights.Select(w => w.Double).Sum();
            for (int ndx = 0; ndx < weights.Length; ndx++)
            {
                int expected = (int)(weights[ndx].Double * 10000 / totalWeight);
                Assert.GreaterOrEqual(samples[ndx], expected * 90 / 100);
                Assert.LessOrEqual(samples[ndx], expected * 110 / 100);
            }
        }

        [Test]
        public void ShuffleEmptyArray()
        {
            RandomPCG rnd = RandomPCG.CreateNew();
            int[] values = new int[0];
            rnd.ShuffleInPlace(values);
        }

        [Test]
        public void ShuffleEmptyList()
        {
            RandomPCG rnd = RandomPCG.CreateNew();
            List<int> values = new List<int>();
            rnd.ShuffleInPlace(values);
        }

        [Test]
        public void ShuffleArrayDistribution()
        {
            const int NumIters = 10000;
            const int Length = 6;
            int[,] counts = new int[Length, Length];
            IEnumerable<int> elements = Enumerable.Range(0, Length);

            RandomPCG rnd = RandomPCG.CreateFromSeed(123);
            for (int iter = 0; iter < NumIters; iter++)
            {
                int[] values = elements.ToArray();
                rnd.ShuffleInPlace(values);
                for (int ndx = 0; ndx < Length; ndx++)
                    counts[ndx, values[ndx]]++;

                int[] sorted = values.OrderBy(v => v).ToArray();
                if (!sorted.SequenceEqual(elements))
                    Assert.Fail($"Sorted sequence no longer matches original values: {sorted}");
            }

            for (int outer = 0; outer < Length; outer++)
            {
                for (int inner = 0; inner < Length; inner++)
                {
                    int expected = NumIters / Length;
                    Assert.GreaterOrEqual(counts[outer, inner], expected * 9 / 10);
                    Assert.LessOrEqual(counts[outer, inner], expected * 11 / 10);
                }
            }
        }

        [Test]
        public void ShuffleListDistribution()
        {
            const int NumIters = 10000;
            const int Length = 6;
            int[,] counts = new int[Length,Length];
            IEnumerable<int> elements = Enumerable.Range(0, Length);

            RandomPCG rnd = RandomPCG.CreateFromSeed(123);
            for (int iter = 0; iter < NumIters; iter++)
            {
                List<int> values = elements.ToList();
                rnd.ShuffleInPlace(values);
                for (int ndx = 0; ndx < Length; ndx++)
                    counts[ndx, values[ndx]]++;

                int[] sorted = values.OrderBy(v => v).ToArray();
                if (!sorted.SequenceEqual(elements))
                    Assert.Fail($"Sorted sequence no longer matches original values: {sorted}");
            }

            for (int outer = 0; outer < Length; outer++)
            {
                for (int inner = 0; inner < Length; inner++)
                {
                    int expected = NumIters / Length;
                    Assert.GreaterOrEqual(counts[outer, inner], expected * 9 / 10);
                    Assert.LessOrEqual(counts[outer, inner], expected * 11 / 10);
                }
            }
        }

        [Test]
        public void ConstantResults()
        {
            // Check operations make blessed results
            Assert.AreEqual("6B6FE4B1B7C17576675095C3DE1D7DF8356FE058", ComputeStreamChecksum((rng) => rng.NextInt().ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("AA13E77696435A8C320F668E2FE0A042B140B6B2", ComputeStreamChecksum((rng) => rng.NextUInt().ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("79479543DB17198F36E8BFD394F4F83609C7A4AB", ComputeStreamChecksum((rng) => rng.NextLong().ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("BAA3B13EC5769E7C859900A00D02801EF287F037", ComputeStreamChecksum((rng) => rng.NextULong().ToString(CultureInfo.InvariantCulture)));

            Assert.AreEqual("A009DA315F9CB0FEF9B785DB95DAD7F808C95753", ComputeStreamChecksum((rng) => rng.NextInt(maxExclusive: (int)(rng.NextUInt() & int.MaxValue)).ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("096BCC6AFDCD4AD5766C54972CE513DC2855B5B6", ComputeStreamChecksum((rng) => BitConverter.SingleToUInt32Bits(rng.NextFloat()).ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("18EFC70A5338EC22EA73807BE8704BE479A19338", ComputeStreamChecksum((rng) => BitConverter.DoubleToUInt64Bits(rng.NextDouble()).ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("3DF00C1BC38A5D40E3079A538AE4D679CEF40FCC", ComputeStreamChecksum((rng) =>
            {
                int a = rng.NextInt();
                int b = rng.NextInt();
                return rng.NextIntMinMax(minInclusive: System.Math.Min(a, b), maxExclusive: System.Math.Max(a, b)).ToString(CultureInfo.InvariantCulture);
            }));

            Assert.AreEqual("5E3B70B252BE8F497804FD4260CEFC3E63AD4682", ComputeStreamChecksum((rng) => rng.NextF32().Raw.ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("AA13E77696435A8C320F668E2FE0A042B140B6B2", ComputeStreamChecksum((rng) => rng.NextF64().Raw.ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("E8B8DCA03F41CA0DAB945D4B08CF03AA83C24D22", ComputeStreamChecksum((rng) =>
            {
                F64Vec2 v = rng.NextInsideUnitCircle();
                return v.X.Raw.ToString(CultureInfo.InvariantCulture) + ";" + v.Y.Raw.ToString(CultureInfo.InvariantCulture);
            }));
            Assert.AreEqual("A838A016AE7BE00683ED39513D638A9E939A3E91", ComputeStreamChecksum((rng) => rng.NextBool().ToString(CultureInfo.InvariantCulture)));

            Assert.AreEqual("D79E812CC8360E80C1CABE9C6B25627FEB5A08F5", ComputeStreamChecksum((rng) => rng.Choice(new int[] {1, 2, 3}).ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("8DB5F8141E988F0CBDB1DA8B48823CAD9AD8DBCA", ComputeStreamChecksum((rng) => rng.Choice((new int[] {1, 2, 3}).Select(x => x)).ToString(CultureInfo.InvariantCulture)));

            Assert.AreEqual("15B5313BFF01EB839F528DA0BF1AFA91A6BC2353", ComputeStreamChecksum((rng) => rng.GetWeightedIndex(new int[] {1, 2, 3}).ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual("EAAABE283B880808627F97BF3E4B43F3604213E9", ComputeStreamChecksum((rng) => rng.GetWeightedIndex(new F32[] {F32.One, F32.FromInt(2), F32.FromInt(3)}).ToString(CultureInfo.InvariantCulture)));

            Assert.AreEqual("0E66F6377B789EE3145E0A02D5D40A1A6CE87BF6", ComputeStreamChecksum((rng) =>
            {
                int[] arr = new int[] {0, 1, 2, 3};
                rng.ShuffleInPlace(arr);
                return arr[0].ToString(CultureInfo.InvariantCulture) + arr[1].ToString(CultureInfo.InvariantCulture);
            }));
            Assert.AreEqual("0E66F6377B789EE3145E0A02D5D40A1A6CE87BF6", ComputeStreamChecksum((rng) =>
            {
                List<int> list = new List<int>() {0, 1, 2, 3};
                rng.ShuffleInPlace(list);
                return list[0].ToString(CultureInfo.InvariantCulture) + list[1].ToString(CultureInfo.InvariantCulture);
            }));
            Assert.AreEqual("0E66F6377B789EE3145E0A02D5D40A1A6CE87BF6", ComputeStreamChecksum((rng) =>
            {
                List<int> list = new List<int>() {0, 1, 2, 3};
                int[] shuffled = list.Shuffle(rng).ToArray();
                return shuffled[0].ToString(CultureInfo.InvariantCulture) + shuffled[1].ToString(CultureInfo.InvariantCulture);
            }));
        }

        static string ComputeStreamChecksum(Func<RandomPCG, string> generator)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter w = new BinaryWriter(ms, System.Text.Encoding.UTF8);

            RandomPCG rng = RandomPCG.CreateFromSeed(12345);
            for (int i = 0; i < 10_000; ++i)
            {
                w.Write(generator(rng));
            }
            w.Flush();

            byte[] hash = SHA1.HashData(ms.GetBuffer());
            return Convert.ToHexString(hash);
        }
    }
}
