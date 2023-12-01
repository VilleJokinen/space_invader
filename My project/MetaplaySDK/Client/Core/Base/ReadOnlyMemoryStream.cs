// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.IO;

namespace Metaplay.Core
{
    /// <summary>
    /// Stream wrapper around <see cref="ReadOnlyMemory{T}"/>, <see cref="MemoryStream"/> does not support reading from a Span/Memory without creating a copy of the data.
    /// </summary>
    public class ReadOnlyMemoryStream : Stream
    {
        readonly ReadOnlyMemory<byte> _buffer;

        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> buffer)
        {
            _buffer = buffer;
            Length  = buffer.Length;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int clampedCount = (int)System.Math.Clamp(count, 0, _buffer.Length - Position);

            ReadOnlyMemory<byte> memory = _buffer.Slice((int)Position, clampedCount);
            memory.CopyTo(new Memory<byte>(buffer).Slice(offset));
            Position += memory.Length;
            return memory.Length;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = System.Math.Clamp(offset, 0, _buffer.Length);
                    break;
                case SeekOrigin.Current:
                    Position = System.Math.Clamp(Position + offset, 0, _buffer.Length);
                    break;
                case SeekOrigin.End:
                    Position = System.Math.Clamp(Length + offset, 0, _buffer.Length);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(origin), origin, null);
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override bool CanRead  { get; } = true;
        public override bool CanSeek  { get; } = true;
        public override bool CanWrite { get; } = false;
        public override long Length   { get; }
        public override long Position { get; set; }
    }
}
