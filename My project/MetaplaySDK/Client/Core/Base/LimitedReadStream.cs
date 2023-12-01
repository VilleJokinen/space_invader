// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.IO;

namespace Metaplay.Core
{
    /// <summary>
    /// Read-only wrapper stream, such that the total number of bytes read from it
    /// will be at most a specified limit.
    /// Throws <see cref="LimitExceededException"/> if a read from the underlying stream
    /// gives out more than the remaining limit.
    ///
    /// <para>
    /// Can be used to guard against large inputs from untrusted sources.
    /// </para>
    /// </summary>
    public class LimitedReadStream : Stream
    {
        public class LimitExceededException : Exception
        {
            public LimitExceededException(string message) : base(message) { }
        }

        Stream  _underlying;
        int     _limit;
        int     _numReadSoFar = 0;

        public LimitedReadStream(Stream underlying, int limit)
        {
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
            _limit      = limit >= 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit cannot be negative");
        }

        public override long Position   { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Length     => throw new NotSupportedException();
        public override bool CanWrite   => false;
        public override bool CanSeek    => false;
        public override bool CanRead    => _underlying.CanRead;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // \note Relying on underlying's Read for parameter validation

            int numReadNow = _underlying.Read(buffer, offset, count);

            if (numReadNow < 0)
                throw new InvalidOperationException($"Read from underlying stream (type {_underlying.GetType()}) returned a negative value {numReadNow} (requested {count})");
            if (numReadNow > count)
                throw new InvalidOperationException($"Read from underlying stream (type {_underlying.GetType()}) returned {numReadNow}, more than the requested {count}");

            int numRemainingUntilLimit = _limit - _numReadSoFar;
            if (numReadNow > numRemainingUntilLimit)
                throw new LimitExceededException($"Reading from underlying stream (type {_underlying.GetType()}) gave {numReadNow} bytes, but we have only {numRemainingUntilLimit} remaining until limit (read {_numReadSoFar} so far out of the limit of {_limit})");

            _numReadSoFar += numReadNow;

            return numReadNow;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
