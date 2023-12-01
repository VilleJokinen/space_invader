// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Metaplay.Core.IO
{
    public class IODecodingError : Exception
    {
        public IODecodingError() { }
        public IODecodingError(string message) : base(message) { }
        public IODecodingError(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class IOReader : IDisposable
    {
        // unlike Encoding.UTF8, this has throwOnInvalidBytes = true
        static readonly UTF8Encoding s_encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        LowLevelIOReader    _reader;
        Decoder             _decoder;

        public int  Offset      => _reader.TotalOffset;
        public int  Remaining   => _reader.Remaining;
        public bool IsFinished  => _reader.IsFinished;

        public IOReader(byte[] bytes)
        {
            _reader = new LowLevelIOReader(
                buffer:                     null,
                segmentData:                bytes ?? throw new ArgumentNullException(nameof(bytes)),
                segmentOffset:              0,
                segmentEndOffset:           bytes.Length,
                segmentIndex:               0,
                previousSegmentsTotalSize:  0);
        }

        public IOReader(ReadOnlyMemory<byte> bytes)
        {
            _reader = new LowLevelIOReader(
                buffer:                     null,
                segmentData:                bytes,
                segmentOffset:              0,
                segmentEndOffset:           bytes.Length,
                segmentIndex:               0,
                previousSegmentsTotalSize:  0);
        }

        public IOReader(byte[] bytes, int offset, int size)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (offset + size > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(size));

            _reader = new LowLevelIOReader(
                buffer:                     null,
                segmentData:                bytes,
                segmentOffset:              offset,
                segmentEndOffset:           offset + size,
                segmentIndex:               0,
                previousSegmentsTotalSize:  0);
        }

        public IOReader(IOBuffer buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.BeginRead();
            IOBufferSegment segment = buffer.GetSegment(0);

            _reader = new LowLevelIOReader(
                buffer:                     buffer,
                segmentIndex:               0,
                segmentData:                segment.Buffer,
                segmentOffset:              0,
                segmentEndOffset:           segment.Size,
                previousSegmentsTotalSize:  0);
        }

        public void Dispose()
        {
            _reader.Dispose();
            _decoder = null;
        }

        /// <summary> Creates a decoder or recycles an old instance </summary>
        Decoder GetDecoder()
        {
            if (_decoder == null)
                _decoder = s_encoding.GetDecoder();
            else
                _decoder.Reset();
            return _decoder;
        }

        public void Seek(int targetOffset) => _reader.Seek(targetOffset);

        public void SkipBytes(int numBytes) => _reader.SkipBytes(numBytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadByte() => _reader.ReadByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadBytes(Span<byte> bytes) => _reader.ReadAll(bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadBytes(byte[] bytes) => ReadBytes(bytes, 0, bytes.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadBytes(byte[] outBytes, int outOffset, int numBytes) => _reader.ReadAll(outBytes, outOffset, numBytes);

        public uint ReadVarUInt()
        {
            uint value = 0;
            int shift = 0;
            int ndx;
            for (ndx = 0; ndx < 5; ndx++)
            {
                int b = ReadByte();
                if ((b & 0x80) != 0)
                {
                    value = value | ((uint)(b & 0x7F) << shift);
                    shift += 7;
                }
                else
                {
                    value = value | ((uint)b << shift);
                    return value;
                }
            }

            throw new IODecodingError($"Invalid varint found in IOBuffer (at offset {Offset - ndx})");
        }

        public ulong ReadVarULong()
        {
            ulong value = 0;
            int shift = 0;
            for (int ndx = 0; ndx < 10; ndx++)
            {
                int b = ReadByte();
                if ((b & 0x80) != 0)
                {
                    value = value | ((ulong)(b & 0x7F) << shift);
                    shift += 7;
                }
                else
                {
                    value = value | ((ulong)b << shift);
                    return value;
                }
            }

            throw new IODecodingError($"Invalid varlong found in IOBuffer (at offset {Offset})");
        }

        public MetaUInt128 ReadVarUInt128()
        {
            MetaUInt128 value = MetaUInt128.Zero;
            int shift = 0;
            for (int ndx = 0; ndx < 19; ndx++)
            {
                int b = ReadByte();
                if ((b & 0x80) != 0)
                {
                    value = value | (MetaUInt128.FromUInt((uint)(b & 0x7F)) << shift);
                    shift += 7;
                }
                else
                {
                    value = value | (MetaUInt128.FromUInt((uint)b) << shift);
                    return value;
                }
            }

            throw new IODecodingError($"Invalid VarUInt128 found in IOBuffer (at offset {Offset})");
        }

        public int ReadVarInt()
        {
            // Lowest bit is sign, flip all other bits with it
            uint v = ReadVarUInt();
            return (int)(v >> 1) ^ -(int)(v & 1);
        }

        public long ReadVarLong()
        {
            // Lowest bit is sign, flip all other bits with it
            ulong v = ReadVarULong();
            return (long)(v >> 1) ^ -(long)(v & 1);
        }

        public int ReadInt32()
        {
            Span<byte> buffer = stackalloc byte[4];
            ReadBytes(buffer);
            return (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64()
        {
            Span<byte> buffer = stackalloc byte[8];
            ReadBytes(buffer);
            return ((long)buffer[0] << 56) | ((long)buffer[1] << 48) | ((long)buffer[2] << 40) | ((long)buffer[3] << 32)
                 | ((long)buffer[4] << 24) | ((long)buffer[5] << 16) | ((long)buffer[6] << 8) | (long)buffer[7];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            return (ulong)ReadInt64();
        }

        public MetaUInt128 ReadUInt128()
        {
            ulong high = ReadUInt64();
            ulong low = ReadUInt64();
            return new MetaUInt128(high, low);
        }

        public F32 ReadF32() => F32.FromRaw(ReadInt32());
        public F32Vec2 ReadF32Vec2() => new F32Vec2(ReadF32(), ReadF32());
        public F32Vec3 ReadF32Vec3() => new F32Vec3(ReadF32(), ReadF32(), ReadF32());

        public F64 ReadF64() => F64.FromRaw(ReadInt64());
        public F64Vec2 ReadF64Vec2() => new F64Vec2(ReadF64(), ReadF64());
        public F64Vec3 ReadF64Vec3() => new F64Vec3(ReadF64(), ReadF64(), ReadF64());

        public float ReadFloat()
        {
            Span<byte> span = stackalloc byte[4];
            ReadBytes(span);
            return BitConverter.ToSingle(span);
        }

        public double ReadDouble()
        {
            Span<byte> span = stackalloc byte[8];
            ReadBytes(span);
            return BitConverter.ToDouble(span);
        }

        public string ReadString(int maxSize)
        {
            int numBytes = ReadVarInt();
            if (numBytes == -1)
                return null;
            else if (numBytes < 0)
                throw new IODecodingError($"Invalid String size: {numBytes}");
            else if (numBytes > maxSize)
                throw new IODecodingError($"String size too large: {numBytes} (max={maxSize})");

            // Fast path if in a flat area
            if (_reader._segmentOffset + numBytes <= _reader._segmentEndOffset)
            {
                string str = s_encoding.GetString(_reader._segmentData.Slice(_reader._segmentOffset, numBytes).Span);
                _reader._segmentOffset += numBytes;
                return str;
            }

            // Check there is enough data to complete the request before trying to read.
            // \note Redundant, would fail while reading anyway. Done early to avoid unnecessary allocation.
            // \note Done only after fast-path. This cannot fail in fast path since if there is sufficienly large contiguous segment, then
            //       we must have enough data in the whole buffer.
            if (numBytes > Remaining)
                throw new IODecodingError($"String size larger than remaining input: size={numBytes}, remaining={Remaining})");

            // Segmented access.
            Decoder         decoder             = GetDecoder();
            char[]          charBuffer          = new char[s_encoding.GetMaxCharCount(numBytes)]; // rent todo
            int             charOffset          = 0;
            int             numBytesRemaining   = numBytes;

            while (numBytesRemaining > 0)
            {
                int spaceInSegment =  _reader._segmentEndOffset - _reader._segmentOffset;
                if (spaceInSegment == 0)
                {
                    if (!_reader.NextSegment())
                        throw new IODecodingError($"ReadString(): reading past end-of-buffer");
                }
                else
                {
                    int     numToReadInSegment  = System.Math.Min(numBytesRemaining, spaceInSegment);
                    bool    isLastSegment       = numToReadInSegment == numBytesRemaining;
                    bool    segmentCompleted    = false;

                    decoder.Convert(_reader._segmentData.Slice(_reader._segmentOffset, numToReadInSegment).Span,
                        new Span<char>(charBuffer, charOffset, charBuffer.Length - charOffset),
                        isLastSegment, out int _, out int charsUsed, out segmentCompleted);

                    _reader._segmentOffset += numToReadInSegment;
                    numBytesRemaining -= numToReadInSegment;
                    charOffset += charsUsed;

                    if (isLastSegment && !segmentCompleted)
                        throw new IODecodingError($"ReadString(): string did not terminate properly");
                }
            }

            return new string(charBuffer, 0, charOffset);
        }

        public byte[] ReadByteString(int maxLength)
        {
            int length = ReadVarInt();
            if (length < 0)
                return null;
            else if (length > Remaining) // \note Redundant, would fail while reading anyway. Done early to avoid unnecessary allocation.
                throw new IODecodingError($"Byte array larger than remaining input: length={length}, remaining={Remaining})");
            else if (length > maxLength)
                throw new IODecodingError($"Deserialized byte array too large: {length} (max={maxLength})");
            else
            {
                byte[] result = new byte[length];
                ReadBytes(result, 0, length);
                return result;
            }
        }

        /// <summary>
        /// Converts the IOReader into a IOReaderStream. The ownership is moved
        /// and the IOReader is left in a Disposed state.
        /// </summary>
        public IOReaderStream ConvertToStream()
        {
            IOReaderStream stream = new IOReaderStream(_reader);
            _reader = default;
            return stream;
        }
    }
}
