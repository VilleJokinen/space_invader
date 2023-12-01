// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Metaplay.Core.IO
{
    public enum VarIntConst
    {
        Zero = 0,
        MinusOne = 1,
        One = 2,
        Sixteen = 32,
    }

    public static class VarIntUtils
    {
        // Maximum number of bytes required for writing a 32-bit integer as Var(U)Int
        public const int VarIntMaxBytes = 5;

        // Maximum number of bytes required for writing a 64-bit integer as Var(U)Long
        public const int VarLongMaxBytes = 10;

        // Maximum number of bytes required for writing a 128-bit integer as Var(U)Int128
        public const int VarInt128MaxBytes = 19;

        public static int CountVarIntBytes(int value)
        {
            uint v = (uint)((value >> 31) ^ (value << 1));
            int bytes = 1;
            while (v >= 128)
            {
                bytes++;
                v >>= 7;
            }
            return bytes;
        }

        public static int FormatVarInt(byte[] bytes, int value)
        {
            uint v = (uint)((value >> 31) ^ (value << 1));
            int pos = 0;
            while (v >= 128)
            {
                bytes[pos++] = (byte)(0x80 | (v & 0x7F));
                v >>= 7;
            }
            bytes[pos++] = (byte)(v & 0x7F);
            return pos;
        }
    }

    public class IOWriter : IDisposable
    {
        internal LowLevelIOWriter _writer;
        public int Offset => _writer.TotalOffset;

        public enum Mode
        {
            /// <summary> Data is written to the beginning of the buffer. The given IOBuffer must be empty. </summary>
            WriteNew,

            /// <summary> Written data is to be appended into the supplied IOBuffer. </summary>
            Append,

            /// <summary> The buffer is cleared on open. Data is written to the beginning of the buffer. </summary>
            Truncate,
        };

        public IOWriter(IOBuffer buffer, Mode mode = Mode.WriteNew)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (mode == Mode.WriteNew)
            {
                if (!buffer.IsEmpty)
                    throw new InvalidOperationException("IOWriter buffer must be empty");
            }
            else if (mode == Mode.Append)
            {
                // leave buffer as-is
            }
            else if (mode == Mode.Truncate)
            {
                buffer.Clear();
            }
            else
                throw new NotImplementedException();

            _writer = new LowLevelIOWriter(buffer);
        }

        public void Dispose()
        {
            _writer.Dispose();
            _writer = default;
        }

        public IOWriterStream ConvertToStream()
        {
            IOWriterStream stream = new IOWriterStream(_writer);
            _writer = default;
            return stream;
        }

        public SpanWriter GetSpanWriter()
        {
            return new SpanWriter(this);
        }

        public void ReleaseSpanWriter(ref SpanWriter writer)
        {
            writer.Flush();
        }

        public void WriteByte(byte value)
        {
            _writer.WriteByte(value);
        }

        public void WriteBytes(byte[] bytes, int offset, int numBytes)
        {
            _writer.WriteBytes(bytes, offset, numBytes);
        }

        public void WriteSpan(ReadOnlySpan<byte> bytes)
        {
            _writer.WriteSpan(bytes);
        }

        public void WriteGuid(Guid guid) { SpanWriter        s = GetSpanWriter(); s.WriteGuidAsByteString(guid); ReleaseSpanWriter(ref s); }
        public void WriteUInt32(uint v) { SpanWriter         s = GetSpanWriter(); s.WriteUInt32(v); ReleaseSpanWriter(ref s); }
        public void WriteInt32(int v) { SpanWriter           s = GetSpanWriter(); s.WriteInt32(v); ReleaseSpanWriter(ref s); }
        public void WriteInt64(long v) { SpanWriter          s = GetSpanWriter(); s.WriteInt64(v); ReleaseSpanWriter(ref s); }
        public void WriteVarInt(int v) { SpanWriter          s = GetSpanWriter(); s.WriteVarInt(v); ReleaseSpanWriter(ref s); }
        public void WriteUInt128(MetaUInt128 v) { SpanWriter s = GetSpanWriter(); s.WriteUInt128(v); ReleaseSpanWriter(ref s); }
        public void WriteString(string v) { SpanWriter       s = GetSpanWriter(); s.WriteString(v); ReleaseSpanWriter(ref s); }

        public void WriteVarUInt(uint value) { SpanWriter s = GetSpanWriter(); s.WriteVarUInt(value); ReleaseSpanWriter(ref s); }

        public void WriteVarULong(ulong value) { SpanWriter s = GetSpanWriter(); s.WriteVarULong(value); ReleaseSpanWriter(ref s); }
        public void WriteVarLong(long value) { SpanWriter s = GetSpanWriter(); s.WriteVarLong(value); ReleaseSpanWriter(ref s); }
        public void WriteVarUInt128(MetaUInt128 value) { SpanWriter s = GetSpanWriter(); s.WriteVarUInt128(value); ReleaseSpanWriter(ref s); }

        public void WriteUInt64(ulong value) { SpanWriter s = GetSpanWriter(); s.WriteUInt64(value); ReleaseSpanWriter(ref s); }

        public void WriteVarIntConst(VarIntConst value) { SpanWriter s = GetSpanWriter(); s.WriteVarIntConst(value); ReleaseSpanWriter(ref s); }

        public void WriteFloat(float value) { SpanWriter s = GetSpanWriter(); s.WriteFloat(value); ReleaseSpanWriter(ref s); }

        public void WriteDouble(double value) { SpanWriter s = GetSpanWriter(); s.WriteDouble(value); ReleaseSpanWriter(ref s); }

        public void WriteF32(F32 value) => WriteInt32(value.Raw);

        public void WriteF32Vec2(F32Vec2 value) { SpanWriter s = GetSpanWriter(); s.WriteF32Vec2(value); ReleaseSpanWriter(ref s); }

        public void WriteF32Vec3(F32Vec3 value) { SpanWriter s = GetSpanWriter(); s.WriteF32Vec3(value); ReleaseSpanWriter(ref s); }

        public void WriteF64(F64 value) => WriteInt64(value.Raw);

        public void WriteF64Vec2(F64Vec2 value) { SpanWriter s = GetSpanWriter(); s.WriteF64Vec2(value); ReleaseSpanWriter(ref s); }

        public void WriteF64Vec3(F64Vec3 value) { SpanWriter s = GetSpanWriter(); s.WriteF64Vec3(value); ReleaseSpanWriter(ref s); }

        public void WriteByteString(byte[] value) { SpanWriter s = GetSpanWriter(); s.WriteByteString(value); ReleaseSpanWriter(ref s); }
    }

    public ref struct SpanWriter
    {
        readonly IOWriter _owner;
        Span<Byte> _span;
        int _pos;

        ref LowLevelIOWriter Writer => ref _owner._writer;

        public int Offset => Writer.TotalOffset + _pos;
        public int Capacity => _span.Length - _pos;
#if DEBUG
        int _ensuredSpaceRemaining;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SpanWriter(IOWriter writer)
        {
            _owner = writer;
            _span = _owner._writer.GetSpan(1);
            _pos = 0;
#if DEBUG
            _ensuredSpaceRemaining = 0;
#endif
        }

#if DEBUG
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DebugCheckSpaceEnsured(int numBytes)
        {
            _ensuredSpaceRemaining -= numBytes;
            MetaDebug.Assert(_ensuredSpaceRemaining >= 0, "Unchecked write exceeds ensured space!");
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Span<Byte> SliceUnchecked(int numBytes)
        {
#if DEBUG
            DebugCheckSpaceEnsured(numBytes);
#endif
            Span<Byte> span = _span.Slice(_pos);
            _pos += numBytes;
            return span;
        }

        // Flush bytes written to current span to LowLevelIOWriter and release the memory
        // buffer. Next write operation will need to obtain a new span.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flush()
        {
            Writer.Advance(_pos);
            Writer.Flush();
            _span = Span<Byte>.Empty;
            _pos = 0;
#if DEBUG
            _ensuredSpaceRemaining = 0;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureSpace(int minSize)
        {
#if DEBUG
            _ensuredSpaceRemaining = minSize;
#endif
            if (Capacity >= minSize)
                return;
            Writer.Advance(_pos);
            _span = Writer.GetSpan(minSize);
            _pos = 0;
        }

        #region "Unchecked" methods, space in span is not checked.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByteUnchecked(byte value)
        {
#if DEBUG
            DebugCheckSpaceEnsured(1);
#endif
            _span[_pos++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesUnchecked2(byte value1, byte value2)
        {
#if DEBUG
            DebugCheckSpaceEnsured(2);
#endif
            _span[_pos++] = value1;
            _span[_pos++] = value2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesUnchecked3(byte value1, byte value2, byte value3)
        {
#if DEBUG
            DebugCheckSpaceEnsured(3);
#endif
            _span[_pos++] = value1;
            _span[_pos++] = value2;
            _span[_pos++] = value3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesUnchecked4(byte value1, byte value2, byte value3, byte value4)
        {
#if DEBUG
            DebugCheckSpaceEnsured(4);
#endif
            _span[_pos++] = value1;
            _span[_pos++] = value2;
            _span[_pos++] = value3;
            _span[_pos++] = value4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesUnchecked5(byte value1, byte value2, byte value3, byte value4, byte value5)
        {
#if DEBUG
            DebugCheckSpaceEnsured(5);
#endif
            _span[_pos++] = value1;
            _span[_pos++] = value2;
            _span[_pos++] = value3;
            _span[_pos++] = value4;
            _span[_pos++] = value5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesUnchecked6(byte value1, byte value2, byte value3, byte value4, byte value5, byte value6)
        {
#if DEBUG
            DebugCheckSpaceEnsured(6);
#endif
            _span[_pos++] = value1;
            _span[_pos++] = value2;
            _span[_pos++] = value3;
            _span[_pos++] = value4;
            _span[_pos++] = value5;
            _span[_pos++] = value6;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUIntUnchecked(uint value)
        {
#if DEBUG
            DebugCheckSpaceEnsured(VarIntUtils.VarIntMaxBytes);
#endif
            while (value >= 128)
            {
                _span[_pos++] = (byte)(0x80 | (value & 0x7F));
                value >>= 7;
            }
            _span[_pos++] = (byte)(value & 0x7F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarIntUnchecked(int value)
        {
            // Flip all other bits with sign, store sign in lowest bit
            uint v = (uint)((value >> 31) ^ (value << 1));
            WriteVarUIntUnchecked(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarULongUnchecked(ulong value)
        {
#if DEBUG
            DebugCheckSpaceEnsured(VarIntUtils.VarLongMaxBytes);
#endif
            while (value >= 128)
            {
                _span[_pos++] = (byte)(0x80 | (value & 0x7F));
                value >>= 7;
            }
            _span[_pos++] = (byte)(value & 0x7F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarLongUnchecked(long value)
        {
            // Flip all other bits with sign, store sign in lowest bit
            ulong v = (ulong)((value >> 63) ^ (value << 1));
            WriteVarULongUnchecked(v);
        }

        public void WriteVarUInt128Unchecked(MetaUInt128 value)
        {
#if DEBUG
            DebugCheckSpaceEnsured(VarIntUtils.VarInt128MaxBytes);
#endif
            do
            {
                if (value < MetaUInt128.FromUInt(128))
                {
                    _span[_pos++] = (byte)(value.Low & 0x7F);
                    break;
                }
                else
                {
                    _span[_pos++] = (byte)(0x80 | (value.Low & 0x7F));
                    value >>= 7;
                }
            } while (value != MetaUInt128.Zero);
        }

        public void WriteInt32Unchecked(int value)
        {
            WriteByteUnchecked((byte)(value >> 24));
            WriteByteUnchecked((byte)(value >> 16));
            WriteByteUnchecked((byte)(value >> 8));
            WriteByteUnchecked((byte)(value >> 0));
        }

        public void WriteUInt32Unchecked(uint value) => WriteInt32Unchecked((int)value);

        public void WriteInt64Unchecked(long value)
        {
            WriteByteUnchecked((byte)(value >> 56));
            WriteByteUnchecked((byte)(value >> 48));
            WriteByteUnchecked((byte)(value >> 40));
            WriteByteUnchecked((byte)(value >> 32));
            WriteByteUnchecked((byte)(value >> 24));
            WriteByteUnchecked((byte)(value >> 16));
            WriteByteUnchecked((byte)(value >> 8));
            WriteByteUnchecked((byte)(value >> 0));
        }

        public void WriteUInt64Unchecked(ulong value) => WriteInt64Unchecked((long)value);

        public void WriteUInt128Unchecked(MetaUInt128 value)
        {
            WriteUInt64Unchecked(value.High);
            WriteUInt64Unchecked(value.Low);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarIntConstUnchecked(VarIntConst v)
        {
            WriteByteUnchecked((byte)v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void WriteShortUTF8Unchecked(string value)
        {
            int numBytes = EncodeStringIntoSpan(value, _span.Slice(_pos + 1));
#if DEBUG
            DebugCheckSpaceEnsured(numBytes + 1);
#endif
            // Write single-byte array length to start of span as VarInt
            _span[_pos] = (byte)(numBytes << 1);
            _pos += numBytes + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteF32Unchecked(F32 value) => WriteInt32Unchecked(value.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteF32Vec2Unchecked(F32Vec2 value)
        {
            WriteInt32Unchecked(value.RawX);
            WriteInt32Unchecked(value.RawY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteF32Vec3Unchecked(F32Vec3 value)
        {
            WriteInt32Unchecked(value.RawX);
            WriteInt32Unchecked(value.RawY);
            WriteInt32Unchecked(value.RawZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteF64Unchecked(F64 value) => WriteInt64Unchecked(value.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteF64Vec2Unchecked(F64Vec2 value)
        {
            WriteInt64Unchecked(value.RawX);
            WriteInt64Unchecked(value.RawY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteF64Vec3Unchecked(F64Vec3 value)
        {
            WriteInt64Unchecked(value.RawX);
            WriteInt64Unchecked(value.RawY);
            WriteInt64Unchecked(value.RawZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloatUnchecked(float val)
        {
            BitConverter.TryWriteBytes(SliceUnchecked(4), val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDoubleUnchecked(double val)
        {
            BitConverter.TryWriteBytes(SliceUnchecked(8), val);
        }

        #endregion
        #region Write API

        public void WriteByte(byte value)
        {
            EnsureSpace(1);
            WriteByteUnchecked(value);
        }

        public void WriteVarUInt(uint value)
        {
            EnsureSpace(VarIntUtils.VarIntMaxBytes);
            WriteVarUIntUnchecked(value);
        }

        public void WriteVarInt(int value)
        {
            // Flip all other bits with sign, store sign in lowest bit&
            uint v = (uint)((value >> 31) ^ (value << 1));
            WriteVarUInt(v);
        }

        public void WriteVarULong(ulong value)
        {
            EnsureSpace(VarIntUtils.VarLongMaxBytes);
            WriteVarULongUnchecked(value);
        }

        public void WriteVarLong(long value)
        {
            // Flip all other bits with sign, store sign in lowest bit
            ulong v = (ulong)((value >> 63) ^ (value << 1));
            WriteVarULong(v);
        }

        public void WriteVarUInt128(MetaUInt128 value)
        {
            EnsureSpace(VarIntUtils.VarInt128MaxBytes);
            WriteVarUInt128Unchecked(value);
        }

        public void WriteInt32(int value)
        {
            EnsureSpace(4);
            WriteInt32Unchecked(value);
        }

        public void WriteUInt32(uint value)
        {
            WriteInt32((int)value);
        }

        public void WriteInt64(long value)
        {
            EnsureSpace(8);
            WriteInt64Unchecked(value);
        }

        public void WriteUInt64(ulong value)
        {
            WriteInt64((long)value);
        }

        public void WriteUInt128(MetaUInt128 value)
        {
            WriteUInt64(value.High);
            WriteUInt64(value.Low);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarIntConst(VarIntConst v)
        {
            EnsureSpace(1);
            WriteByteUnchecked((byte)v);
        }

        public void WriteFloat(float val)
        {
            EnsureSpace(4);
            WriteFloatUnchecked(val);
        }

        public void WriteDouble(double val)
        {
            EnsureSpace(8);
            WriteDoubleUnchecked(val);
        }

        public void WriteSpan(ReadOnlySpan<byte> bytes)
        {
            if (Capacity >= bytes.Length)
            {
#if DEBUG
                _ensuredSpaceRemaining = bytes.Length;
#endif
                bytes.CopyTo(SliceUnchecked(bytes.Length));
            }
            else
            {
                Flush();
                Writer.WriteSpan(bytes);
            }
        }

        public void WriteBytes(byte[] bytes, int offset, int numBytes)
        {
            WriteSpan(new ReadOnlySpan<byte>(bytes, offset, numBytes));
        }

        public void WriteF32(F32 value) => WriteInt32(value.Raw);

        public void WriteF32Vec2(F32Vec2 value)
        {
            EnsureSpace(8);
            WriteF32Vec2Unchecked(value);
        }

        public void WriteF32Vec3(F32Vec3 value)
        {
            EnsureSpace(12);
            WriteF32Vec3Unchecked(value);
        }

        public void WriteF64(F64 value) => WriteInt64(value.Raw);

        public void WriteF64Vec2(F64Vec2 value)
        {
            EnsureSpace(16);
            WriteF64Vec2Unchecked(value);
        }

        public void WriteF64Vec3(F64Vec3 value)
        {
            EnsureSpace(24);
            WriteF64Vec3Unchecked(value);
        }

        public void WriteString(string value)
        {
            if (value == null)
                WriteVarIntConst(VarIntConst.MinusOne);
            else if (value.Length == 0)
                WriteVarIntConst(VarIntConst.Zero);
            else
            {
                // Try to use conservative upper bound first to prevent need to count actual number of bytes.
                // The byte array length is encoded as signed VarInt which means that there are 6 bytes available
                // for it (1 bit for sign, 1 bit for VarInt continuation bit).
                int maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
                if (maxBytes < 64)
                {
                    // size as VarInt fits in single byte
                    EnsureSpace(maxBytes + 1);
                    WriteShortUTF8Unchecked(value);
                    return;
                }

                int exactBytes = Encoding.UTF8.GetByteCount(value);
                WriteVarInt(exactBytes);
                if (exactBytes <= 128 || Capacity >= exactBytes)
                {
                    EnsureSpace(exactBytes);
                }
                else
                {
                    // \todo[jarkko]: don't allocate here. Encode directly to the buffer.
                    byte[] bytes = Encoding.UTF8.GetBytes(value);
                    Flush();
                    Writer.WriteBytes(bytes, 0, bytes.Length);
                    return;
                }

                EncodeStringIntoSpan(value, SliceUnchecked(exactBytes));
            }
        }

        public void WriteByteString(byte[] value)
        {
            if (value == null)
                WriteVarIntConst(VarIntConst.MinusOne);
            else
            {
                WriteVarInt(value.Length);
                WriteBytes(value, 0, value.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int EncodeStringIntoSpan(string s, Span<Byte> dst)
        {
            return Encoding.UTF8.GetBytes(s, dst);
        }

        #endregion

        public void WriteGuidAsByteString(Guid guid)
        {
            EnsureSpace(17);
            WriteGuidAsByteStringUnchecked(guid);
        }

        public void WriteGuidAsByteStringUnchecked(Guid guid)
        {
            WriteVarIntConstUnchecked(VarIntConst.Sixteen);
            guid.TryWriteBytes(SliceUnchecked(16));
        }

        public void WriteNullableGuidAsByteString(Guid? guid)
        {
            EnsureSpace(17);
            WriteNullableGuidAsByteStringUnchecked(guid);
        }

        public void WriteNullableGuidAsByteStringUnchecked(Guid? guid)
        {
            if(guid.HasValue)
                WriteGuidAsByteStringUnchecked(guid.Value);
            else
                WriteVarIntConstUnchecked(VarIntConst.MinusOne);
        }
    }
}
