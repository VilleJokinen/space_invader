// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Cloud.Tests
{
    [TestFixture]
    public class KeyedStableWeightedCoinflipTests
    {
        List<uint> _testKeys = CreateTestKeys();
        List<uint> _testRollIds = CreateTestRolls();

        static List<uint> CreateTestKeys()
        {
            List<uint> list = new List<uint>()
            {
                0,
                1,
                2,
                0xFFFFFFFF,
                0x7FFFFFFF,
                12234,
                23474,
                99999,
            };

            RandomPCG rng = RandomPCG.CreateFromSeed(1234);
            for (int ndx = 0; ndx < 1000; ++ndx)
                list.Add(rng.NextUInt());

            return list;
        }
        static List<uint> CreateTestRolls()
        {
            List<uint> list = new List<uint>()
            {
                0,
                1,
                2,
                0xFFFFFFFF,
                0x7FFFFFFF,
                12234,
                23474,
                99999,
            };

            RandomPCG rng = RandomPCG.CreateFromSeed(54321);
            for (int ndx = 0; ndx < 10000; ++ndx)
                list.Add(rng.NextUInt());

            return list;
        }

        [Test]
        public void TestDistribution()
        {
            List<int> testWeights = new List<int> { 100, 300, 500, 700, 900 };
            foreach (uint key in _testKeys)
            {
                foreach (int weight in testWeights)
                {
                    int numPos = 0;
                    foreach (uint rollId in _testRollIds)
                        numPos += (KeyedStableWeightedCoinflip.FlipACoin(key, rollId, weight) ? 1 : 0);

                    int expected = (int)((weight / 1000.0f) * _testRollIds.Count);
                    int min = expected - (int)(0.05f * _testRollIds.Count); // -5%
                    int max = expected + (int)(0.05f * _testRollIds.Count); // +5%

                    Assert.GreaterOrEqual(numPos, min);
                    Assert.LessOrEqual(numPos, max);
                }
            }
        }

        [Test]
        public void TestWith1000Weight()
        {
            foreach (uint key in _testKeys)
            {
                foreach (uint rollId in _testRollIds)
                {
                    Assert.IsTrue(KeyedStableWeightedCoinflip.FlipACoin(key, rollId, trueWeightPermille: 1000));
                }
            }
        }

        [Test]
        public void TestWith0Weight()
        {
            foreach (uint key in _testKeys)
            {
                foreach (uint rollId in _testRollIds)
                {
                    Assert.IsFalse(KeyedStableWeightedCoinflip.FlipACoin(key, rollId, trueWeightPermille: 0));
                }
            }
        }

        [Test]
        public void TestRollAutocorrelation()
        {
            foreach (uint key in _testKeys)
            {
                const int NumSamples = 10000;
                int numSameDegree1 = 0;
                int numSameDegree2 = 0;
                for(uint rollId = 2; rollId < NumSamples; ++rollId)
                {
                    numSameDegree1 += (KeyedStableWeightedCoinflip.FlipACoin(key, rollId) == KeyedStableWeightedCoinflip.FlipACoin(key, rollId-1) ? 1 : 0);
                    numSameDegree2 += (KeyedStableWeightedCoinflip.FlipACoin(key, rollId) == KeyedStableWeightedCoinflip.FlipACoin(key, rollId-2) ? 1 : 0);
                }

                int exp = (int)(0.5f * (NumSamples - 2));
                int min = exp - (int)(0.05f * NumSamples); // -5%
                int max = exp + (int)(0.05f * NumSamples); // +5%

                Assert.GreaterOrEqual(numSameDegree1, min);
                Assert.LessOrEqual(numSameDegree1, max);

                Assert.GreaterOrEqual(numSameDegree2, min);
                Assert.LessOrEqual(numSameDegree2, max);
            }
        }

        [Test]
        public void TestKeyAutocorrelation()
        {
            // \note: take only a 1000 to limit run time
            foreach (uint rollId in _testRollIds.Take(1000))
            {
                const int NumSamples = 10000;
                int numSameDegree1 = 0;
                int numSameDegree2 = 0;
                for(uint key = 2; key < NumSamples; ++key)
                {
                    numSameDegree1 += (KeyedStableWeightedCoinflip.FlipACoin(key, rollId) == KeyedStableWeightedCoinflip.FlipACoin(key-1, rollId) ? 1 : 0);
                    numSameDegree2 += (KeyedStableWeightedCoinflip.FlipACoin(key, rollId) == KeyedStableWeightedCoinflip.FlipACoin(key-2, rollId) ? 1 : 0);
                }

                int expected = (int)(0.5f * (NumSamples - 2));
                int min = expected - (int)(0.05f * NumSamples); // -5%
                int max = expected + (int)(0.05f * NumSamples); // +5%

                Assert.GreaterOrEqual(numSameDegree1, min);
                Assert.LessOrEqual(numSameDegree1, max);

                Assert.GreaterOrEqual(numSameDegree2, min);
                Assert.LessOrEqual(numSameDegree2, max);
            }
        }
    }
}
