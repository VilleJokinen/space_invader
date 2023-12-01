// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.IO;

namespace Metaplay.Core.IO
{
    /// <summary>
    /// Stream interface into an IOReader.
    /// </summary>
    public sealed class IOReaderStream : Stream
    {
        LowLevelIOReader _reader;

        internal IOReaderStream(LowLevelIOReader reader)
        {
            _reader = reader;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _reader.TotalLength;

        public override long Position { get => _reader.TotalOffset; set => _reader.Seek((int)value); }

        public override int Read(byte[] buffer, int offset, int count) => _reader.ReadSome(buffer, offset, count);

#if NETCOREAPP
        public override int Read(Span<byte> buffer) => _reader.ReadSome(buffer);
#endif

        public override int ReadByte()
        {
            if (_reader.IsFinished)
                return -1;
            else
                return _reader.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _reader.Seek((int)offset);
                    break;

                case SeekOrigin.Current:
                    _reader.SkipBytes((int)offset);
                    break;

                case SeekOrigin.End:
                    _reader.Seek(_reader.TotalLength - (int)offset);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }

            return _reader.TotalOffset;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() =>  throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    }
}
