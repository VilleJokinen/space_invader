// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.IO;

namespace Metaplay.Core.IO
{
    /// <summary>
    /// Stream interface into an IOWriter.
    /// </summary>
    public sealed class IOWriterStream : Stream
    {
        LowLevelIOWriter _writer;

        internal IOWriterStream(LowLevelIOWriter writer)
        {
            _writer = writer;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _writer.TotalOffset;

        public override long Position { get => _writer.TotalOffset; set => throw new System.NotImplementedException(); }

        public override void Flush() => _writer.Flush();

        public override void Write(byte[] buffer, int offset, int count) => _writer.WriteBytes(buffer, offset, count);

        public override void WriteByte(byte value) => _writer.WriteByte(value);

#if NETCOREAPP
        public override void Write(ReadOnlySpan<byte> span) => _writer.WriteSpan(span);
#endif

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writer.Dispose();
            }
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new System.NotImplementedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotImplementedException();

        public override void SetLength(long value) => throw new System.NotImplementedException();
    }
}
