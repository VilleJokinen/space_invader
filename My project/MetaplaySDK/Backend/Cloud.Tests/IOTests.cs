// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.IO;
using Metaplay.Core.Math;
using Metaplay.Core.Memory;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Cloud.Tests
{
    [TestFixture(WriteBufferType.WriteToFlatIOBuffer)]
    [TestFixture(WriteBufferType.WriteToSegmentedIOBuffer)]
    [TestFixture(ReadBufferType.ReadFromFlatArray)]
    [TestFixture(ReadBufferType.ReadFromFlatIOBuffer)]
    [TestFixture(ReadBufferType.ReadFromSegmentedIOBuffer)]
    public class IOTests
    {
        delegate void WriteFunc(ref SpanWriter writer);
        delegate void ReadFunc(IOReader reader);

        public enum WriteBufferType
        {
            WriteToFlatIOBuffer,
            WriteToSegmentedIOBuffer,
        }
        WriteBufferType _writeBufferType;

        public enum ReadBufferType
        {
            ReadFromFlatArray,
            ReadFromFlatIOBuffer,
            ReadFromSegmentedIOBuffer,
        }
        ReadBufferType _readBufferType;

        public IOTests(WriteBufferType writeBufferType)
        {
            _writeBufferType = writeBufferType;
            _readBufferType = ReadBufferType.ReadFromFlatArray;
        }
        public IOTests(ReadBufferType readBufferType)
        {
            _writeBufferType = WriteBufferType.WriteToFlatIOBuffer;
            _readBufferType = readBufferType;
        }

        void Test(WriteFunc writeFunc, ReadFunc readFunc)
        {
            byte[] serialized = DoWrite(writeFunc);
            using (IOReader reader = CreateReader(serialized))
            {
                readFunc(reader);

                Assert.AreEqual(serialized.Length, reader.Offset); // all bytes must be consumed
            }
        }

        byte[] DoWrite(WriteFunc writeFunc)
        {
            IOBuffer buffer;

            switch (_writeBufferType)
            {
                case WriteBufferType.WriteToFlatIOBuffer:
                {
                    buffer = new FlatIOBuffer(initialCapacity: 2);
                    break;
                }
                case WriteBufferType.WriteToSegmentedIOBuffer:
                {
                    buffer = new SegmentedIOBuffer(segmentSize: 2);
                    break;
                }
                default:
                    throw new InvalidOperationException();
            }

            using(buffer)
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    SpanWriter span = writer.GetSpanWriter();
                    writeFunc(ref span);
                    writer.ReleaseSpanWriter(ref span);
                }
                return buffer.ToArray();
            }
        }

        IOReader CreateReader(byte[] serialized)
        {
            switch (_readBufferType)
            {
                case ReadBufferType.ReadFromFlatArray:
                {
                    return new IOReader(serialized);
                }
                case ReadBufferType.ReadFromFlatIOBuffer:
                {
                    FlatIOBuffer buffer = new FlatIOBuffer();
                    using (IOWriter writer = new IOWriter(buffer))
                        writer.WriteBytes(serialized, 0, serialized.Length);
                    return new IOReader(buffer);
                }
                case ReadBufferType.ReadFromSegmentedIOBuffer:
                {
                    SegmentedIOBuffer buffer = new SegmentedIOBuffer(segmentSize: 2);
                    using (IOWriter writer = new IOWriter(buffer))
                        writer.WriteBytes(serialized, 0, serialized.Length);
                    return new IOReader(buffer);
                }
            }

            return null;
        }

        [Test]
        public void TestBasicTypes()
        {
            Test(
                (ref SpanWriter writer) => writer.WriteByte(98),
                (IOReader reader) => Assert.AreEqual(98, reader.ReadByte())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteVarInt(999999),
                (IOReader reader) => Assert.AreEqual(999999, reader.ReadVarInt())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteVarInt(-99999999),
                (IOReader reader) => Assert.AreEqual(-99999999, reader.ReadVarInt())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteVarUInt(uint.MaxValue),
                (IOReader reader) => Assert.AreEqual(uint.MaxValue, reader.ReadVarUInt())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteFloat(8.0f),
                (IOReader reader) => Assert.AreEqual(8.0f, reader.ReadFloat())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteDouble(-8.0),
                (IOReader reader) => Assert.AreEqual(-8.0, reader.ReadDouble())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteF32(F32.Pi),
                (IOReader reader) => Assert.AreEqual(F32.Pi, reader.ReadF32())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteF32Vec2(new F32Vec2(F32.Pi, F32.Neg1)),
                (IOReader reader) => Assert.AreEqual(new F32Vec2(F32.Pi, F32.Neg1), reader.ReadF32Vec2())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteF32Vec3(new F32Vec3(F32.Pi, F32.Neg1, F32.Zero)),
                (IOReader reader) => Assert.AreEqual(new F32Vec3(F32.Pi, F32.Neg1, F32.Zero), reader.ReadF32Vec3())
            );

            Test(
                (ref SpanWriter writer) => writer.WriteString(null),
                (IOReader reader) => Assert.AreEqual(null, reader.ReadString(1024))
            );

            Test(
                (ref SpanWriter writer) => writer.WriteString(""),
                (IOReader reader) => Assert.AreEqual("", reader.ReadString(1024))
            );

            Test(
                (ref SpanWriter writer) => writer.WriteString("���"),
                (IOReader reader) => Assert.AreEqual("���", reader.ReadString(1024))
            );

            Test(
                (ref SpanWriter writer) => writer.WriteByteString(null),
                (IOReader reader) => Assert.AreEqual(null, reader.ReadByteString(1024))
            );

            Test(
                (ref SpanWriter writer) => writer.WriteByteString(new byte[] { }),
                (IOReader reader) => Assert.AreEqual(new byte[] { }, reader.ReadByteString(1024))
            );

            Test(
                (ref SpanWriter writer) => writer.WriteByteString(new byte[] { 0, 1, 2, 255 }),
                (IOReader reader) => Assert.AreEqual(new byte[] { 0, 1, 2, 255 }, reader.ReadByteString(1024))
            );

            // \todo [petri] more types
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(128)]
        [TestCase(65535)]
        [TestCase(65536)]
        public void TestByteArray(int length)
        {
            byte[] bytes = new byte[length];
            for (int ndx = 0; ndx < length; ndx++)
                bytes[ndx] = (byte)((ndx + 1231487) * 9257983751);

            Test(
                (ref SpanWriter writer) => writer.WriteSpan(bytes),
                (IOReader reader) =>
                {
                    byte[] result = new byte[bytes.Length];
                    reader.ReadBytes(result);
                    Assert.AreEqual(bytes, result);
                }
            );
        }

        [TestCase(0)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(255)]
        public void TestByte(byte val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteByte(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadByte())
            );
        }

        [TestCase(0u)]
        [TestCase(127u)]
        [TestCase(128u)]
        [TestCase(16383u)]
        [TestCase(16384u)]
        [TestCase(0x7FFF_FFFFu)]
        [TestCase(0xFFFF_FFFFu)]
        public void TestVarUInt(uint val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteVarUInt(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadVarUInt())
            );
        }

        [TestCase(0)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(-127)]
        [TestCase(-128)]
        [TestCase(8191)]
        [TestCase(8192)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void TestVarInt(int val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteVarInt(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadVarInt())
            );
        }

        [TestCase(0)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(-127)]
        [TestCase(-128)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void TestInt32(int val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteInt32(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadInt32())
            );
        }

        [TestCase(0ul)]
        [TestCase(127ul)]
        [TestCase(128ul)]
        [TestCase(0x7FFF_FFFFul)]
        [TestCase(0xFFFF_FFFFul)]
        [TestCase(0x7FFF_FFFF_FFFF_FFFFul)]
        [TestCase(0xFFFF_FFFF_FFFF_FFFFul)]
        public void TestVarULong(ulong val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteVarULong(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadVarULong())
            );
        }

        [TestCase(0L)]
        [TestCase(127L)]
        [TestCase(128L)]
        [TestCase(0x7FFF_FFFFL)]
        [TestCase(0xFFFF_FFFFL)]
        [TestCase(-63L)]
        [TestCase(-64L)]
        [TestCase(-127L)]
        [TestCase(-128L)]
        [TestCase(-0x7FFF_FFFFL)]
        [TestCase(-0xFFFF_FFFFL)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void TestVarLong(long val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteVarLong(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadVarLong())
            );
        }

        [TestCase(0L)]
        [TestCase(127L)]
        [TestCase(128L)]
        [TestCase(0x7FFF_FFFFL)]
        [TestCase(0xFFFF_FFFFL)]
        [TestCase(-63L)]
        [TestCase(-64L)]
        [TestCase(-127L)]
        [TestCase(-128L)]
        [TestCase(-0x7FFF_FFFFL)]
        [TestCase(-0xFFFF_FFFFL)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void TestInt64(long val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteInt64(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadInt64())
            );
        }

        [TestCase(0ul, 0ul)]
        [TestCase(0ul, 127ul)]
        [TestCase(0ul, 128ul)]
        [TestCase(0ul, 65536ul)]
        [TestCase(0ul, ulong.MaxValue)]
        [TestCase(1ul, 0ul)]
        [TestCase(127ul, 0ul)]
        [TestCase(0x7FFF_FFFF_FFFF_FFFFul, 0x7FFF_FFFF_FFFF_FFFFul)]
        [TestCase(0xFFFF_FFFF_FFFF_FFFFul, 0xFFFF_FFFF_FFFF_FFFFul)]
        public void TestVarUInt128(ulong high, ulong low)
        {
            MetaUInt128 val = new MetaUInt128(high, low);
            Test(
                (ref SpanWriter writer) => writer.WriteVarUInt128(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadVarUInt128())
            );
        }

        [Test]
        public void TestVarUInt128()
        {
            MetaUInt128 val = new MetaUInt128(116, 14214069542612952311ul);

            Test(
                (ref SpanWriter writer) => writer.WriteVarUInt128(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadVarUInt128())
            );
        }

        [TestCase(0)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(-127)]
        [TestCase(-128)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void TestF32(int raw)
        {
            F32 val = F32.FromRaw(raw);
            Test(
                (ref SpanWriter writer) => writer.WriteF32(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadF32())
            );
        }

        [TestCase(0, 65536)]
        public void TestF32Vec2(int rawX, int rawY)
        {
            F32Vec2 val = F32Vec2.FromRaw(rawX, rawY);
            Test(
                (ref SpanWriter writer) => writer.WriteF32Vec2(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadF32Vec2())
            );
        }

        [TestCase(0, 65536, -65536)]
        public void TestF32Vec3(int rawX, int rawY, int rawZ)
        {
            F32Vec3 val = F32Vec3.FromRaw(rawX, rawY, rawZ);
            Test(
                (ref SpanWriter writer) => writer.WriteF32Vec3(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadF32Vec3())
            );
        }

        [TestCase(0L)]
        [TestCase(127L)]
        [TestCase(128L)]
        [TestCase(0x7FFF_FFFFL)]
        [TestCase(0xFFFF_FFFFL)]
        [TestCase(-63L)]
        [TestCase(-64L)]
        [TestCase(-127L)]
        [TestCase(-128L)]
        [TestCase(-0x7FFF_FFFFL)]
        [TestCase(-0xFFFF_FFFFL)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void TestF64(long raw)
        {
            F64 val = F64.FromRaw(raw);
            Test(
                (ref SpanWriter writer) => writer.WriteF64(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadF64())
            );
        }

        [TestCase(0L, 65536L)]
        public void TestF64Vec2(long rawX, long rawY)
        {
            F64Vec2 val = F64Vec2.FromRaw(rawX, rawY);
            Test(
                (ref SpanWriter writer) => writer.WriteF64Vec2(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadF64Vec2())
            );
        }

        [TestCase(0L, 65536L, -65536L)]
        public void TestF64Vec3(long rawX, long rawY, long rawZ)
        {
            F64Vec3 val = F64Vec3.FromRaw(rawX, rawY, rawZ);
            Test(
                (ref SpanWriter writer) => writer.WriteF64Vec3(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadF64Vec3())
            );
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("a")]
        [TestCase("ai7n83r7n9a87b9a874n9a874ng")]
        [TestCase("�������������")]
        public void TestString(string val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteString(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadString(maxSize: 4096))
            );
        }

        [TestCaseSource(nameof(GenerateLongTestStrings))]
        public void TestLongString(LongStringWrapper val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteString(val.Value),
                (IOReader reader) => Assert.AreEqual(val.Value, reader.ReadString(maxSize: val.Value.Length * 3))
            );
        }

        public class LongStringWrapper
        {
            public string Value;
            public LongStringWrapper(string v)
            {
                Value = v;
            }
            public override string ToString()
            {
                return Invariant($"<length {Value.Length,6}>");
            }
        }
        public static IEnumerable<LongStringWrapper> GenerateLongTestStrings()
        {
            string pattern = "nihao=\u4f60\u597dox=\ud83d\udc02tsha=\ud801\udccc";
            string compound = pattern;

            for (int i = 0; i < 5; ++i)
            {
                compound = compound + compound;
            }
            for (int i = 0; i < 10; ++i)
            {
                compound = compound + compound;
                yield return new LongStringWrapper(compound);
            }
        }

        [TestCase(0.0f)]
        [TestCase(0.0125f)]
        [TestCase(1.0f)]
        [TestCase(-123.5f)]
        [TestCase(1000000.0f)]
        [TestCase(float.Epsilon)]
        [TestCase(float.MaxValue)]
        [TestCase(float.MinValue)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void TestFloat(float val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteFloat(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadFloat())
            );
        }

        [TestCase(0.0f)]
        [TestCase(0.0125f)]
        [TestCase(1.0f)]
        [TestCase(-123.5f)]
        [TestCase(1000000.0f)]
        [TestCase(double.Epsilon)]
        [TestCase(double.MaxValue)]
        [TestCase(double.MinValue)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void TestDouble(double val)
        {
            Test(
                (ref SpanWriter writer) => writer.WriteDouble(val),
                (IOReader reader) => Assert.AreEqual(val, reader.ReadDouble())
            );
        }

        [Test]
        public void TestByteString()
        {
            const int MaxLength = 1024;

            Test(
                (ref SpanWriter writer) => writer.WriteByteString(null),
                (IOReader reader) => Assert.AreEqual(null, reader.ReadByteString(MaxLength))
            );

            Test(
                (ref SpanWriter writer) => writer.WriteByteString(new byte[] { }),
                (IOReader reader) => Assert.AreEqual(new byte[] { }, reader.ReadByteString(MaxLength))
            );

            Test(
                (ref SpanWriter writer) => writer.WriteByteString(new byte[] { 0, 1, 127, 255 }),
                (IOReader reader) => Assert.AreEqual(new byte[] { 0, 1, 127, 255 }, reader.ReadByteString(MaxLength))
            );
        }
    }

    // \todo [nuutti] Unify with IOTests?
    public class IOSeekTests
    {
        public enum ReadType
        {
            FlatArray,
            FlatArrayWithOffsetAndSize,
            SegmentedBuffer,
        }

        [TestCase(ReadType.FlatArray)]
        [TestCase(ReadType.FlatArrayWithOffsetAndSize)]
        [TestCase(ReadType.SegmentedBuffer)]
        public void TestSeek(ReadType readType)
        {
            byte[] properInput = CreateRandomBytes(count: 100, seed: 1);

            using (IOReader reader = CreateReader(readType, properInput, out int baseOffset))
                RunSeekTestsOn100SizedInput(reader, baseOffset, properInput);
        }

        static byte[] CreateRandomBytes(int count, int seed)
        {
            byte[] bytes = new byte[count];
            RandomPCG rnd = RandomPCG.CreateFromSeed((uint)seed);
            for (int i = 0; i < count; i++)
                bytes[i] = (byte)(rnd.NextUInt() & 0xff);
            return bytes;
        }

        static IOReader CreateReader(ReadType readType, byte[] properInput, out int baseOffset)
        {
            switch (readType)
            {
                case ReadType.FlatArray:
                    baseOffset = 0;
                    return new IOReader(properInput);

                case ReadType.FlatArrayWithOffsetAndSize:
                {
                    byte[] prefix = CreateRandomBytes(count: 13, seed: 2);
                    byte[] suffix = CreateRandomBytes(count: 17, seed: 3);
                    baseOffset = prefix.Length;
                    return new IOReader(Util.ConcatBytes(prefix, properInput, suffix), offset: prefix.Length, size: properInput.Length);
                }

                case ReadType.SegmentedBuffer:
                {
                    SegmentedIOBuffer segmented = new SegmentedIOBuffer(segmentSize: 30);
                    using (IOWriter writer = new IOWriter(segmented))
                        writer.WriteBytes(properInput, offset: 0, numBytes: properInput.Length);
                    baseOffset = 0;
                    return new IOReader(segmented);
                }

                default:
                    throw new Exception();
            }
        }

        static void RunSeekTestsOn100SizedInput(IOReader reader, int baseOffset, byte[] reference)
        {
            // Well-behaving tests
            RunOKSeekTestsOn100SizedInput(reader, baseOffset, reference);

            // Read too much
            reader.Seek(baseOffset+80);
            Assert.Catch<Exception>(() => reader.ReadBytes(new byte[30]));

            // Check the OK tests still succeed (i.e. IOReader isn't in a bad state)
            RunOKSeekTestsOn100SizedInput(reader, baseOffset, reference);

            // Seek to invalid (negative) position
            Assert.Catch<Exception>(() => reader.Seek(-1));

            // Check OK again
            RunOKSeekTestsOn100SizedInput(reader, baseOffset, reference);

            // Seek to invalid (too large) position
            Assert.Catch<Exception>(() => reader.Seek(baseOffset + 101));

            // Check OK again
            RunOKSeekTestsOn100SizedInput(reader, baseOffset, reference);

            // Seek to very end - that is still ok, but then reading fails
            reader.Seek(baseOffset + 100);
            Assert.Catch<Exception>(() => reader.ReadByte());

            // Check OK again
            RunOKSeekTestsOn100SizedInput(reader, baseOffset, reference);
        }

        static void RunOKSeekTestsOn100SizedInput(IOReader reader, int baseOffset, byte[] reference)
        {
            reader.Seek(baseOffset+65);
            Assert.AreEqual(reference[65], reader.ReadByte());

            reader.Seek(baseOffset+10);
            Assert.AreEqual(reference[10], reader.ReadByte());
            Assert.AreEqual(reference[11], reader.ReadByte());
            reader.Seek(baseOffset+10);
            Assert.AreEqual(reference[10], reader.ReadByte());
            reader.Seek(baseOffset+10);
            Assert.AreEqual(reference[10], reader.ReadByte());

            reader.Seek(baseOffset+99);
            Assert.AreEqual(reference[99], reader.ReadByte());

            {
                reader.Seek(baseOffset+10);
                byte[] output = new byte[80];
                reader.ReadBytes(output);
                Assert.AreEqual(reference.Skip(10).Take(80), output);
            }
            {
                reader.Seek(baseOffset+0);
                byte[] output = new byte[100];
                reader.ReadBytes(output);
                Assert.AreEqual(reference, output);
            }
        }
    }

    public class IOWriterTests
    {
        [Test]
        public void TestIOWriterModeWriteNew()
        {
            using SegmentedIOBuffer buffer = new SegmentedIOBuffer(1);
            using (IOWriter writer = new IOWriter(buffer, IOWriter.Mode.WriteNew))
                writer.WriteInt32(123);
            Assert.Catch<InvalidOperationException>(() => new IOWriter(buffer, IOWriter.Mode.WriteNew));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestIOWriterModeAppend(bool clearBetween)
        {
            using SegmentedIOBuffer buffer = new SegmentedIOBuffer(1);
            using (IOWriter writer = new IOWriter(buffer))
                writer.WriteInt32(123);
            if (clearBetween)
                buffer.Clear();
            using (IOWriter writer = new IOWriter(buffer, IOWriter.Mode.Append))
                writer.WriteInt32(456);
            using (IOReader reader = new IOReader(buffer))
            {
                if (!clearBetween)
                    Assert.AreEqual(123, reader.ReadInt32());
                Assert.AreEqual(456, reader.ReadInt32());
                Assert.IsTrue(reader.IsFinished);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestIOWriterModeTruncate(bool clearBetween)
        {
            using SegmentedIOBuffer buffer = new SegmentedIOBuffer(1);
            using (IOWriter writer = new IOWriter(buffer))
                writer.WriteInt32(123);
            if (clearBetween)
                buffer.Clear();
            using (IOWriter writer = new IOWriter(buffer, IOWriter.Mode.Truncate))
                writer.WriteInt32(456);
            using (IOReader reader = new IOReader(buffer))
            {
                Assert.AreEqual(456, reader.ReadInt32());
                Assert.IsTrue(reader.IsFinished);
            }
        }

        // IOWriter.Offset

        [Test]
        public void TestIOWriterOffsetFlatIOBuffer() => DoTestIOWriterOffset(new FlatIOBuffer(initialCapacity: 32), IOWriter.Mode.WriteNew);

        [Test]
        public void TestIOWriterOffsetSegmentedIOBuffer() => DoTestIOWriterOffset(new SegmentedIOBuffer(segmentSize: 1), IOWriter.Mode.WriteNew);

        [Test]
        public void TestIOWriterOffsetFlatIOBufferAppend() => DoTestIOWriterOffsetWithContent(new FlatIOBuffer(initialCapacity: 32), IOWriter.Mode.Append);

        [Test]
        public void TestIOWriterOffsetSegmentedIOBufferAppend() => DoTestIOWriterOffsetWithContent(new SegmentedIOBuffer(segmentSize: 3), IOWriter.Mode.Append);

        [Test]
        public void TestIOWriterOffsetFlatIOBufferTruncate() => DoTestIOWriterOffsetWithContent(new FlatIOBuffer(initialCapacity: 32), IOWriter.Mode.Truncate);

        [Test]
        public void TestIOWriterOffsetSegmentedIOBufferTruncate() => DoTestIOWriterOffsetWithContent(new SegmentedIOBuffer(segmentSize: 3), IOWriter.Mode.Truncate);

        public void DoTestIOWriterOffsetWithContent(IOBuffer buffer, IOWriter.Mode mode)
        {
            using (var writer = new IOWriter(buffer))
                writer.WriteString("foobarfoobarfoobarfoobar");
            DoTestIOWriterOffset(buffer, mode);
            buffer.Dispose();
        }

        public void DoTestIOWriterOffset(IOBuffer buffer, IOWriter.Mode mode)
        {
            int expectedOffset = 0;
            if (mode == IOWriter.Mode.Append)
                expectedOffset = buffer.Count;

            using (IOWriter writer = new IOWriter(buffer, mode))
            {
                Assert.AreEqual(expectedOffset, writer.Offset);

                for (int i = 0; i < 30; ++i)
                {
                    writer.WriteByte(1);
                    expectedOffset += 1;
                    Assert.AreEqual(expectedOffset, writer.Offset);
                }
                for (int i = 0; i < 30; ++i)
                {
                    writer.WriteBytes(new byte[13], 1, 11);
                    expectedOffset += 11;
                    Assert.AreEqual(expectedOffset, writer.Offset);
                }

                writer.Dispose();
                Assert.AreEqual(0, writer.Offset);
            }
            buffer.Dispose();
        }

        // IOReader.Offset && Remaining

        [Test]
        public void TestIOReaderOffsetAndRemainingByteArray()
        {
            DoTestIOReaderOffsetAndRemaining(new IOReader(new byte[1234]), 1234);
            DoTestIOReaderOffsetAndRemaining(new IOReader(new byte[1234], 14, 1199), 1199, baseOffset: 14);
        }

        [Test]
        public void TestIOReaderOffsetAndRemainingFlatIOBuffer()
        {
            var buffer = new FlatIOBuffer(initialCapacity: 32);
            using (var writer = new IOWriter(buffer))
                writer.WriteBytes(new byte[1255], 0, 1255);
            DoTestIOReaderOffsetAndRemaining(new IOReader(buffer), 1255);
        }

        [Test]
        public void TestIOReaderOffsetAndRemainingSegmentedIOBuffer()
        {
            using var buffer = new SegmentedIOBuffer(1);
            using (var writer = new IOWriter(buffer))
                writer.WriteBytes(new byte[1255], 0, 1255);
            DoTestIOReaderOffsetAndRemaining(new IOReader(buffer), 1255);
        }

        void DoTestIOReaderOffsetAndRemaining(IOReader reader, int count, int baseOffset = 0)
        {
            int expectedOffset = baseOffset;
            int expectedRemaining = count;

            Assert.AreEqual(expectedOffset, reader.Offset);
            Assert.AreEqual(expectedRemaining, reader.Remaining);

            while (true)
            {
                for (int i = 0; i < 5; ++i)
                {
                    if (expectedRemaining < 1)
                        break;

                    reader.ReadByte();
                    expectedOffset += 1;
                    expectedRemaining -= 1;
                    Assert.AreEqual(expectedOffset, reader.Offset);
                    Assert.AreEqual(expectedRemaining, reader.Remaining);
                }
                for (int i = 0; i < 5; ++i)
                {
                    if (expectedRemaining < 11)
                        break;

                    reader.ReadBytes(new byte[13], 1, 11);
                    expectedOffset += 11;
                    expectedRemaining -= 11;
                    Assert.AreEqual(expectedOffset, reader.Offset);
                    Assert.AreEqual(expectedRemaining, reader.Remaining);
                }

                if (expectedRemaining == 0)
                    break;
            }

            reader.Dispose();
            Assert.AreEqual(0, reader.Remaining);

        }

    }
    public class IOBufferUtilTests
    {
        [Test]
        public void TestContentsEqual()
        {
            using SegmentedIOBuffer a = new SegmentedIOBuffer(1);
            using FlatIOBuffer b = new FlatIOBuffer();

            using (IOWriter writer = new IOWriter(a))
                writer.WriteInt32(1234567);
            using (IOWriter writer = new IOWriter(b))
                writer.WriteInt32(1234567);

            Assert.IsTrue(IOBufferUtil.ContentsEqual(a, b));

            using (IOWriter writer = new IOWriter(b, IOWriter.Mode.Truncate))
                writer.WriteInt32(1234568);

            Assert.IsFalse(IOBufferUtil.ContentsEqual(a, b));
        }

        [Test]
        public void TestToArray()
        {
            foreach (int segmentSize in new int[] { 1, 3, 4 })
            {
                using SegmentedIOBuffer a = new SegmentedIOBuffer(segmentSize);
                using FlatIOBuffer b = new FlatIOBuffer();

                using (IOWriter writer = new IOWriter(a))
                    writer.WriteInt32(1234567);
                using (IOWriter writer = new IOWriter(b))
                    writer.WriteInt32(1234567);

                byte[] arra = IOBufferUtil.ToArray(a);
                byte[] arrb = IOBufferUtil.ToArray(b);

                Assert.IsTrue(arra.Length > 0);
                Assert.AreEqual(arra, arrb);
            }
        }

        [Test]
        public void TestAppendTo()
        {
            using SegmentedIOBuffer a = new SegmentedIOBuffer(segmentSize: 1);
            using (IOWriter writer = new IOWriter(a))
                writer.WriteInt32(1234567);

            using SegmentedIOBuffer b = new SegmentedIOBuffer(segmentSize: 2);
            using (IOWriter writer = new IOWriter(b))
                writer.WriteInt32(234568);

            using SegmentedIOBuffer c = new SegmentedIOBuffer(segmentSize: 3);
            IOBufferUtil.AppendTo(src: a, dst: c);
            IOBufferUtil.AppendTo(src: b, dst: c);

            FlatIOBuffer reference = new FlatIOBuffer();
            using (IOWriter writer = new IOWriter(reference))
            {
                writer.WriteInt32(1234567);
                writer.WriteInt32(234568);
            }

            Assert.AreEqual(IOBufferUtil.ToArray(c), IOBufferUtil.ToArray(reference));
        }

        [Test]
        public void TestCopyTo()
        {
            using SegmentedIOBuffer b = new SegmentedIOBuffer(segmentSize: 2);
            using (IOWriter writer = new IOWriter(b))
                writer.WriteBytes(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);

            {
                byte[] target = new byte[6];
                IOBufferUtil.CopyTo(b, target, 0);
                Assert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 0}, target);
            }
            {
                byte[] target = new byte[6];
                b.CopyTo(target, 1);
                Assert.AreEqual(new byte[] { 0, 1, 2, 3, 4, 5}, target);
            }
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => b.CopyTo(new byte[6], 2));
                Assert.Throws<ArgumentOutOfRangeException>(() => b.CopyTo(new byte[6], -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => b.CopyTo(new byte[4], 0));
            }
        }
    }
    public class IOBufferStreamTests
    {
        // Naked, Flat, SegmentedLong, SegmentedShort
        public enum IOBufferType
        {
            Naked,
            Flat,
            SegmentedLong,
            SegmentedShort,
        }

        IOBuffer CreateReadBuffer(IOBufferType bufferType)
        {
            IOBuffer buffer;
            switch (bufferType)
            {
                case IOBufferType.Flat:
                    buffer = new FlatIOBuffer(4);
                    break;

                case IOBufferType.SegmentedLong:
                    buffer = new SegmentedIOBuffer(segmentSize: 1024);
                    break;

                case IOBufferType.SegmentedShort:
                    buffer = new SegmentedIOBuffer(segmentSize: 2);
                    break;

                default:
                    return null;
            }

            using (IOWriter writer = new IOWriter(buffer))
            {
                writer.WriteByte(1);
                writer.WriteByte(2);
                writer.WriteByte(3);
                writer.WriteByte(4);
            }
            return buffer;
        }

        IOBuffer CreateWriteBuffer(IOBufferType bufferType)
        {
            switch (bufferType)
            {
                case IOBufferType.Flat:
                    return new FlatIOBuffer(4);

                case IOBufferType.SegmentedLong:
                    return new SegmentedIOBuffer(segmentSize: 1024);

                case IOBufferType.SegmentedShort:
                    return new SegmentedIOBuffer(segmentSize: 2);

                default:
                    return null;
            }
        }

        IOReaderStream CreateReaderStream(IOBufferType bufferType, IOBuffer buffer)
        {
            switch (bufferType)
            {
                case IOBufferType.Naked:
                    return new IOReader(new byte[] {1, 2, 3, 4}).ConvertToStream();
            }

            return new IOReader(buffer).ConvertToStream();
        }

        void CheckWriteBuffer(IOBuffer buffer)
        {
            Assert.AreEqual(new byte[] { 1, 2, 3, 4 }, buffer.ToArray());
        }

        [TestCase(IOBufferType.Naked)]
        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public void TestReadByte(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateReadBuffer(bufferType);
            using IOReaderStream stream = CreateReaderStream(bufferType, buffer);
            Assert.AreEqual(1, stream.ReadByte());
            Assert.AreEqual(2, stream.ReadByte());
            Assert.AreEqual(3, stream.ReadByte());
            Assert.AreEqual(4, stream.ReadByte());
            Assert.AreEqual(-1, stream.ReadByte());
        }

        [TestCase(IOBufferType.Naked)]
        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public void TestReadByteArray(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateReadBuffer(bufferType);
            using IOReaderStream stream = CreateReaderStream(bufferType, buffer);
            byte[] buf = new byte[6];
            int numRead = stream.Read(buf, 1, 5);
            Assert.AreEqual(4, numRead);
            Assert.AreEqual(1, buf[1]);
            Assert.AreEqual(2, buf[2]);
            Assert.AreEqual(3, buf[3]);
            Assert.AreEqual(4, buf[4]);
        }

        [TestCase(IOBufferType.Naked)]
        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public void TestReadSpan(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateReadBuffer(bufferType);
            using IOReaderStream stream = CreateReaderStream(bufferType, buffer);
            byte[] buf = new byte[6];
            int numRead = stream.Read(buf.AsSpan(1, 5));
            Assert.AreEqual(4, numRead);
            Assert.AreEqual(1, buf[1]);
            Assert.AreEqual(2, buf[2]);
            Assert.AreEqual(3, buf[3]);
            Assert.AreEqual(4, buf[4]);
        }

        [TestCase(IOBufferType.Naked)]
        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public async Task TestReadByteArrayAsync(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateReadBuffer(bufferType);
            using IOReaderStream stream = CreateReaderStream(bufferType, buffer);
            byte[] buf = new byte[6];
            int numRead = await stream.ReadAsync(buf, 1, 5);
            Assert.AreEqual(4, numRead);
            Assert.AreEqual(1, buf[1]);
            Assert.AreEqual(2, buf[2]);
            Assert.AreEqual(3, buf[3]);
            Assert.AreEqual(4, buf[4]);
        }

        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public void TestWriteByte(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateWriteBuffer(bufferType);
            using (IOWriterStream stream = new IOWriter(buffer).ConvertToStream())
            {
                stream.WriteByte(1);
                stream.WriteByte(2);
                stream.WriteByte(3);
                stream.WriteByte(4);
            }
            CheckWriteBuffer(buffer);
        }

        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public void TestWriteByteArray(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateWriteBuffer(bufferType);
            using (IOWriterStream stream = new IOWriter(buffer).ConvertToStream())
            {
                stream.Write(new byte[] { 0, 1, 2, 3, 4 }, 1, 4);
            }
            CheckWriteBuffer(buffer);
        }

        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public async Task TestWriteByteArrayAsync(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateWriteBuffer(bufferType);
            using (IOWriterStream stream = new IOWriter(buffer).ConvertToStream())
            {
                await stream.WriteAsync(new byte[] { 0, 1, 2, 3, 4 }, 1, 4);
            }
            CheckWriteBuffer(buffer);
        }

        [TestCase(IOBufferType.Flat)]
        [TestCase(IOBufferType.SegmentedLong)]
        [TestCase(IOBufferType.SegmentedShort)]
        public void TestWriteByteSpan(IOBufferType bufferType)
        {
            using IOBuffer buffer = CreateWriteBuffer(bufferType);
            using (IOWriterStream stream = new IOWriter(buffer).ConvertToStream())
            {
                stream.Write(new byte[] { 0, 1, 2, 3, 4 }.AsSpan(1, 4));
            }
            CheckWriteBuffer(buffer);
        }
    }

    public class AllocatorTests
    {
        [Test]
        public void TestAllocators()
        {
            TestAllocator(DefaultAllocator.Shared);
            TestAllocator(PoolAllocator.Shared);
        }

        static void TestAllocator(IMemoryAllocator allocator)
        {
            MemoryAllocation alloc1 = allocator.Allocate(10);
            Assert.GreaterOrEqual(alloc1.Bytes.Length, 10);

            MemoryAllocation alloc2 = allocator.Allocate(20);
            Assert.GreaterOrEqual(alloc2.Bytes.Length, 20);

            Assert.IsFalse(ReferenceEquals(alloc1.Bytes, alloc2.Bytes));

            allocator.Deallocate(ref alloc1);
            allocator.Deallocate(ref alloc2);
        }
    }
}
