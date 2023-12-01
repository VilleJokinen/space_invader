// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Metaplay.Core.IO
{
    /// <summary>
    /// Low-level IOWriter. Requests segments of memory from an <see cref="IOBuffer"/> and
    /// serves them to IOWriter as <see cref="MetaSpan{T}"/>s. Essentially a cache for the
    /// active segment from the IOBuffer to avoid virtual method calls.
    /// </summary>
    struct LowLevelIOWriter : IDisposable
    {
        IOBuffer            _buffer;       // Buffer into which we are writing
        MetaMemory<byte>    _segment;      // Current segment from buffer
        int                 _bytesWritten; // Number of bytes written into current segment

        public LowLevelIOWriter(IOBuffer buffer)
        {
            _buffer = buffer;
            _buffer.BeginWrite();

            // Use an empty segment to indicate there's no active write segment
            _segment = MetaMemory<byte>.Empty;
            _bytesWritten = 0;
        }

        public int TotalOffset => (_buffer == null) ? 0 : _buffer.Count + _bytesWritten;
        public bool IsDisposed  => _buffer == null;
        int SpaceInSegment => _segment.Length - _bytesWritten;

        public void Dispose()
        {
            if (IsDisposed)
                return;
            Flush();
            _buffer.EndWrite();
            _buffer = null;
            _segment = MetaMemory<byte>.Empty;
            _bytesWritten = 0;
        }

        [Conditional("DEBUG")]
        void CheckIsDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(IOWriter));
        }

        [Conditional("DEBUG")]
        void CheckSpaceInSegment(int numBytes)
        {
            if (SpaceInSegment < numBytes)
                throw new InvalidOperationException($"Trying to Advance {numBytes} without corresponding call to {nameof(GetSpan)}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CommitSegment()
        {
            _buffer.CommitMemory(_bytesWritten);
            _bytesWritten = 0;
        }

        /// <summary>
        /// Make sure that the current active segment has at least <paramref name="minBytes"/> bytes.
        /// </summary>
        /// <param name="minBytes"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureSpaceInSegment(int minBytes)
        {
            // If already have enough space in segment, return
            if (SpaceInSegment < minBytes)
            {
                // Commit bytes written & allocate new segment
                CommitSegment();
                _segment = _buffer.GetMemory(minBytes);
            }
        }

        public void WriteByte(byte value)
        {
            CheckIsDisposed();

            // If active segment full, allocate a new one
            EnsureSpaceInSegment(1);

            // Write the byte
            _segment[_bytesWritten++] = value;
        }

        public void WriteSpan(ReadOnlySpan<byte> bytes)
        {
            CheckIsDisposed();

            while (true)
            {
                Span<byte> dst = GetSpan(1);
                if (bytes.Length <= dst.Length)
                {
                    bytes.CopyTo(dst);
                    Advance(bytes.Length);
                    break;
                }
                else
                {
                    int numToWrite = dst.Length;
                    bytes.Slice(0, numToWrite).CopyTo(dst);
                    bytes = bytes.Slice(numToWrite);
                    Advance(numToWrite);
                }
            }
        }

        /// <summary>
        /// Request a span with at least <paramref name="minBytes"/> bytes of memory.
        /// Should be followed by a call to <see cref="Advance(int)"/> to commit the
        /// actually written bytes.
        /// </summary>
        /// <param name="minBytes"></param>
        /// <returns></returns>
        public MetaMemory<byte> GetSpan(int minBytes)
        {
            EnsureSpaceInSegment(minBytes);
            return _segment.Slice(_bytesWritten);
        }

        /// <summary>
        /// Commit a sequence of bytes written to the span requested previously from
        /// <see cref="GetSpan(int)"/>.
        /// </summary>
        /// <param name="numBytes"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int numBytes)
        {
            CheckSpaceInSegment(numBytes);
            _bytesWritten += numBytes;
        }

        public void WriteBytes(byte[] bytes, int offset, int numBytes)
        {
            WriteSpan(new ReadOnlySpan<byte>(bytes, offset, numBytes));
        }

        public void Flush()
        {
            CheckIsDisposed();
            CommitSegment();

            // Switch to sentinel segment so next GetSpan() allocates another segment
            _segment = MetaMemory<byte>.Empty;
        }
    }
}
