// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Checksum;
using NUnit.Framework;
using System.Collections.Generic;

namespace Cloud.Tests
{
    class Crc8Tests
    {
        static IEnumerable<(byte, byte[], Crc8.Polynomial)> GetTestVectors()
        {
            byte[] GenVector(int size)
            {
                byte[] buf = new byte[size];
                for (int ndx = 0; ndx < size; ++ndx)
                    buf[ndx] = (byte)(size * 13 + ndx * 25547 + (ndx * 25547) >> 15);
                return buf;
            }

            yield return (0x00, new byte[] { }, Crc8.Polynomial.SMBus);
            yield return (0x00, new byte[] { 0 }, Crc8.Polynomial.SMBus);
            yield return (0x00, new byte[] { 0, 0 }, Crc8.Polynomial.SMBus);
            yield return (0x00, new byte[] { 0, 0, 0 }, Crc8.Polynomial.SMBus);
            yield return (0x00, new byte[] { 0, 0, 0, 0 }, Crc8.Polynomial.SMBus);
            yield return (0x00, new byte[] { 0, 0, 0, 0, 0 }, Crc8.Polynomial.SMBus);
            yield return (0x07, new byte[] { 1 }, Crc8.Polynomial.SMBus);
            yield return (0xbc, new byte[] { 1, 2, 3, 4, 5 }, Crc8.Polynomial.SMBus);
            yield return (0x2f, new byte[] { 1, 2, 3, 4, 5, 6 }, Crc8.Polynomial.SMBus);
            yield return (0xd8, new byte[] { 1, 2, 3, 4, 5, 6, 7 }, Crc8.Polynomial.SMBus);
            yield return (0x3e, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Crc8.Polynomial.SMBus);
            yield return (0x25, new byte[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11 }, Crc8.Polynomial.SMBus);
            yield return (0x7e, new byte[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, Crc8.Polynomial.SMBus);
            yield return (0x1c, GenVector(255), Crc8.Polynomial.SMBus);
            yield return (0xfe, GenVector(256), Crc8.Polynomial.SMBus);
            yield return (0x50, GenVector(257), Crc8.Polynomial.SMBus);
            yield return (0xb2, GenVector(1023), Crc8.Polynomial.SMBus);
            yield return (0x9c, GenVector(1024), Crc8.Polynomial.SMBus);
            yield return (0x6e, GenVector(1025), Crc8.Polynomial.SMBus);
        }

        [Test]
        public void TestWithRawArray()
        {
            foreach ((uint result, byte[] bytes, Crc8.Polynomial poly) in GetTestVectors())
                Assert.AreEqual(result, Crc8.ComputeCrc8(bytes, poly));
        }
    }
}
