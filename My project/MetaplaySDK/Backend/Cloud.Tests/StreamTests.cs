// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Cloud.Tests
{
    /// <summary>
    /// Test cases in an attempt to test feature parity between MemoryStream and ReadOnlyMemoryStream
    /// </summary>
    [TestFixture(StreamType.MemoryStream)]
    [TestFixture(StreamType.ReadOnlyMemoryStream)]
    public class StreamTests
    {
        public enum StreamType
        {
            MemoryStream,
            ReadOnlyMemoryStream
        }

        static readonly byte[] _bytes = Enumerable.Range(0, 200).Select(x=> (byte)x).ToArray();

        readonly Stream _stream;

        public StreamTests(StreamType type)
        {
            switch (type)
            {
                case StreamType.MemoryStream:
                    _stream = new MemoryStream(_bytes.ToArray());
                    break;
                case StreamType.ReadOnlyMemoryStream:
                    _stream = new ReadOnlyMemoryStream(new ReadOnlyMemory<byte>(_bytes.ToArray()));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        [Test]
        public void TestRead()
        {
            byte[] readBytes = new byte[10];
            int    readCount = _stream.Read(readBytes, 0, 10);

            Assert.AreEqual(10, readCount);
            Assert.AreEqual(10, _stream.Position);
            Assert.AreEqual(
                new byte[10]
                {
                    0,
                    1,
                    2,
                    3,
                    4,
                    5,
                    6,
                    7,
                    8,
                    9
                }, readBytes);
        }

        [Test]
        public void TestSeekRead()
        {
            byte[] readBytes = new byte[10];
            _stream.Seek(100, SeekOrigin.Begin);
            int    readCount = _stream.Read(readBytes, 0, 10);

            Assert.AreEqual(10, readCount);
            Assert.AreEqual(110, _stream.Position);
            Assert.AreEqual(
                new byte[10]
                {
                    100,
                    101,
                    102,
                    103,
                    104,
                    105,
                    106,
                    107,
                    108,
                    109
                }, readBytes);
        }

        [Test]
        public void TestSeek()
        {
            long seek = _stream.Seek(100, SeekOrigin.Begin);
            Assert.AreEqual(100, _stream.Position);
            Assert.AreEqual(100, seek);

            seek = _stream.Seek(50, SeekOrigin.Current);
            Assert.AreEqual(150, _stream.Position);
            Assert.AreEqual(150, seek);

            seek = _stream.Seek(-90, SeekOrigin.End);
            Assert.AreEqual(110, _stream.Position);
            Assert.AreEqual(110, seek);
        }
    }
}
