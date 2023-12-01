// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Cloud.Tests
{
    [TestFixture]
    public class LimitedReadStreamTests
    {
        #region Read tests

        /// <summary>
        /// Outcome of a read test
        /// </summary>
        public enum ReadOutcome
        {
            /// <summary> Finishes successfully </summary>
            Ok,
            /// <summary> <see cref="LimitedReadStream.LimitExceededException"/> is thrown during test </summary>
            LimitExceeded,
        }

        /// <summary>
        /// How to consume the input stream
        /// </summary>
        public enum ConsumeFlavor
        {
            /// <summary> Consume entire input, using multiple reads, each smaller than the entire input </summary>
            ReadInParts,
            /// <summary> Consume entire input, in one read big enough to consume it all </summary>
            OneFullRead,
            /// <summary> Consume entire input, using <see cref="Stream.CopyTo(Stream)"/> </summary>
            CopyTo,

            /// <summary> Just do one read, might not consume whole input </summary>
            ReadJustOnce,
        }

        // Input size 0
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      0, 0, 10)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      0, 1, 10)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           0, 0, 10)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           0, 1, 10)]
        // Input size 1
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      1, 1, 10)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      1, 2, 10)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           1, 1, 10)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           1, 2, 10)]
        // Limit is equal to input size
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.ReadInParts,      100, 100, 40)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      100, 100, 100)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      100, 100, 150)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 100, 40)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 100, 100)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 100, 150)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 100, -1)]
        // Limit is larger than input size
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.ReadInParts,      100, 200, 40)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      100, 200, 200)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.OneFullRead,      100, 200, 250)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 200, 40)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 200, 200)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 200, 250)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.CopyTo,           100, 200, -1)]
        // Limit is smaller than input size, but reads do not exceed limit.
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.ReadJustOnce,     1, 0, 0)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.ReadJustOnce,     2, 1, 1)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.ReadJustOnce,     100, 80, 79)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.ReadJustOnce,     100, 80, 80)]
        [TestCase(ReadOutcome.Ok,               ConsumeFlavor.ReadJustOnce,     100, 99, 99)]
        // Limit is smaller than input size, and reads exceed limit; expect LimitExceededException.
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadJustOnce,     1, 0, 1)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadJustOnce,     2, 0, 1)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadJustOnce,     2, 1, 2)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadInParts,      100, 20, 40)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadInParts,      100, 80, 40)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadInParts,      100, 99, 40)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.OneFullRead,      100, 20, 100)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.OneFullRead,      100, 99, 100)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.CopyTo,           100, 20, 40)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.CopyTo,           100, 80, 40)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.CopyTo,           100, 99, 40)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.CopyTo,           100, 20, -1)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.CopyTo,           100, 99, -1)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadJustOnce,     100, 80, 81)]
        [TestCase(ReadOutcome.LimitExceeded,    ConsumeFlavor.ReadJustOnce,     100, 99, 100)]
        public void TestRead(ReadOutcome expectedOutcome, ConsumeFlavor consumeFlavor, int inputSize, int limit, int readSize)
        {
            byte[] input = CreateBytes(inputSize);

            using (Stream inputStream = new MemoryStream(input))
            using (Stream limitedStream = new LimitedReadStream(inputStream, limit))
            {
                Action doConsume = () =>
                {
                    switch (consumeFlavor)
                    {
                        case ConsumeFlavor.ReadInParts:   TestReadInParts(input, readSize, limitedStream);                break;
                        case ConsumeFlavor.OneFullRead:   TestOneFullRead(input, readSize, limitedStream);                break;
                        case ConsumeFlavor.CopyTo:        TestCopyTo(input, copyToBufferSize: readSize, limitedStream);   break;
                        case ConsumeFlavor.ReadJustOnce:  TestReadJustOnce(input, readSize, limitedStream);               break;
                        default:
                            throw new InvalidEnumArgumentException();
                    }
                };

                // Depending on expected outcome, either call doConsume plainly, or wrap it in Assert.Throws
                switch (expectedOutcome)
                {
                    case ReadOutcome.Ok:
                        doConsume();

                        // For full-consume flavors, check that nothing is left in stream.
                        if (consumeFlavor != ConsumeFlavor.ReadJustOnce)
                        {
                            byte[] buffer = new byte[1];
                            Assert.AreEqual(0, limitedStream.Read(buffer, 0, 1));
                        }
                        break;

                    case ReadOutcome.LimitExceeded:
                        Assert.Throws<LimitedReadStream.LimitExceededException>(() => doConsume());
                        break;
                }
            }
        }

        public void TestReadInParts(byte[] input, int readSize, Stream limitedStream)
        {
            int numFullReads    = input.Length / readSize;
            int remainder       = input.Length % readSize;

            for (int i = 0; i < numFullReads; i++)
            {
                byte[] buffer = new byte[readSize];
                Assert.AreEqual(readSize, limitedStream.Read(buffer, 0, readSize));
                Assert.AreEqual(input.Skip(i*readSize).Take(readSize), buffer);
            }

            {
                byte[] buffer = new byte[remainder];
                Assert.AreEqual(remainder, limitedStream.Read(buffer, 0, remainder));
                Assert.AreEqual(input.Skip(numFullReads*readSize).Take(remainder), buffer);
            }
        }

        public void TestOneFullRead(byte[] input, int readSize, Stream limitedStream)
        {
            byte[] buffer = new byte[readSize];
            Assert.AreEqual(input.Length, limitedStream.Read(buffer, 0, readSize));
            Assert.AreEqual(input, buffer.Take(input.Length));
        }

        public void TestCopyTo(byte[] input, int copyToBufferSize, Stream limitedStream)
        {
            using (MemoryStream outMemoryStream = new MemoryStream())
            {
                if (copyToBufferSize <= 0)
                    limitedStream.CopyTo(outMemoryStream);
                else
                    limitedStream.CopyTo(outMemoryStream, copyToBufferSize);

                byte[] buffer = outMemoryStream.ToArray();
                Assert.AreEqual(input, buffer);
            }
        }

        public void TestReadJustOnce(byte[] input, int readSize, Stream limitedStream)
        {
            byte[] buffer = new byte[readSize];
            Assert.AreEqual(readSize, limitedStream.Read(buffer, 0, readSize));
            Assert.AreEqual(input.Take(readSize), buffer);
        }

        static byte[] CreateBytes(int size)
        {
            byte[] bytes = new byte[size];
            for (int i = 0; i < size; i++)
                bytes[i] = (byte)((i + 1) * 7 % 256);
            return bytes;
        }

        #endregion

        #region Misc

        [Test]
        public void TestInvalidConstruction()
        {
            Assert.Catch<Exception>(() =>
            {
                new LimitedReadStream(null, -1);
            });
            Assert.Throws<ArgumentNullException>(() =>
            {
                new LimitedReadStream(null, 42);
            });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                using (MemoryStream ms = new MemoryStream())
                    _ = new LimitedReadStream(ms, -1);
            });
        }

        #endregion
    }
}
