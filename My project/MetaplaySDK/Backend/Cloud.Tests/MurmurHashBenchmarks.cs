// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using NUnit.Framework;
using System;
using Metaplay.Core;
using System.Collections.Generic;
using Metaplay.Core.Memory;
using Metaplay.Core.IO;
using static System.FormattableString;

namespace Cloud.Tests
{
    [Ignore("Benchmarks are disabled by default (they cannot fail)")]
    [NonParallelizable]
    class MurmurHashBenchmarks
    {
        static IEnumerable<(uint, byte[])> GetTestVectors()
        {
            byte[] GenVector(int size)
            {
                byte[] buf = new byte[size];
                for (int ndx = 0; ndx < size; ++ndx)
                    buf[ndx] = (byte)(size*13 + ndx*25547 + (ndx * 25547) >> 15);
                return buf;
            }

            yield return (0, new byte[] { });
            yield return (4067711262, GenVector(123));
            yield return (2219617791, GenVector(1025));
            yield return (67956905, GenVector(4025));
            yield return (1532393486, GenVector(9025));
            yield return (3101862390, GenVector(129025));
            yield return (33771106, GenVector(12_456_789));
        }

        [Test]
        public void BenchmarkWithRawArray()
        {
            foreach ((uint result, byte[] bytes) in GetTestVectors())
            {
                Assert.AreEqual(result, MurmurHash.MurmurHash2(bytes));
                TinyBenchmark.Execute(Invariant($"{bytes.Length} bytes"), numRepeats: 15, () => MurmurHash.MurmurHash2(bytes));
            }
        }

        [Test]
        public void BenchmarkWithFlatIOBuffer()
        {
            foreach ((uint result, byte[] bytes) in GetTestVectors())
            {
                FlatIOBuffer buffer = new FlatIOBuffer();
                using (var writer = new IOWriter(buffer))
                    writer.WriteBytes(bytes, 0, bytes.Length);
                Assert.AreEqual(result, MurmurHash.MurmurHash2(buffer));
                TinyBenchmark.Execute(Invariant($"{bytes.Length} bytes"), numRepeats: 15, () => MurmurHash.MurmurHash2(buffer));
            }
        }

        [Test]
        public void BenchmarkWith256BSegmentedIOBuffer()
        {
            foreach ((uint result, byte[] bytes) in GetTestVectors())
            {
                using SegmentedIOBuffer buffer = new SegmentedIOBuffer(segmentSize: 256);
                using (var writer = new IOWriter(buffer))
                    writer.WriteBytes(bytes, 0, bytes.Length);
                Assert.AreEqual(result, MurmurHash.MurmurHash2(buffer));
                TinyBenchmark.Execute(Invariant($"{bytes.Length} bytes"), numRepeats: 15, () => MurmurHash.MurmurHash2(buffer));
            }
        }
    }
}
