// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using NUnit.Framework;
using System;
using Metaplay.Core;
using System.Collections.Generic;
using Metaplay.Core.Memory;
using Metaplay.Core.IO;

namespace Cloud.Tests
{
    class MurmurHashTests
    {
        static IEnumerable<(uint, byte[])> GetTestVectors()
        {
            byte[] GenVector(int size)
            {
                byte[] buf = new byte[size];
                for (int ndx = 0; ndx < size; ++ndx)
                    buf[ndx] = (byte)(size * 13 + ndx * 25547 + (ndx * 25547) >> 15);
                return buf;
            }

            yield return (0, new byte[] { });
            yield return (2777606018, new byte[] { 0 });
            yield return (2992382271, new byte[] { 0, 0 });
            yield return (116393284, new byte[] { 0, 0, 0 });
            yield return (2917101299, new byte[] { 0, 0, 0, 0 });
            yield return (2858403171, new byte[] { 0, 0, 0, 0, 0 });
            yield return (3648318840, new byte[] { 1, 2, 3, 4, 5 });
            yield return (2680867370, new byte[] { 1, 2, 3, 4, 5, 6 });
            yield return (627095955, new byte[] { 1, 2, 3, 4, 5, 6, 7 });
            yield return (429285715, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            yield return (1763516080, new byte[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11 });
            yield return (2179676110, new byte[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            yield return (3146544919, GenVector(255));
            yield return (2522549744, GenVector(256));
            yield return (4180392572, GenVector(257));
            yield return (655803644, GenVector(1023));
            yield return (1626577037, GenVector(1024));
            yield return (2219617791, GenVector(1025));
        }

        [Test]
        public void TestWithRawArray()
        {
            foreach ((uint result, byte[] bytes) in GetTestVectors())
                Assert.AreEqual(result, MurmurHash.MurmurHash2(bytes));
        }

        [Test]
        public void TestWithFlatIOBuffer()
        {
            foreach ((uint result, byte[] bytes) in GetTestVectors())
            {
                FlatIOBuffer buffer = new FlatIOBuffer();
                using (var writer = new IOWriter(buffer))
                    writer.WriteBytes(bytes, 0, bytes.Length);
                Assert.AreEqual(result, MurmurHash.MurmurHash2(buffer));
            }
        }

        [Test]
        public void TestWithSegmentedIOBuffer()
        {
            foreach ((uint result, byte[] bytes) in GetTestVectors())
            {
                using SegmentedIOBuffer buffer = new SegmentedIOBuffer(segmentSize: 13);
                using (var writer = new IOWriter(buffer))
                    writer.WriteBytes(bytes, 0, bytes.Length);
                Assert.AreEqual(result, MurmurHash.MurmurHash2(buffer));
            }
        }
    }
}
