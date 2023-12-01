// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.IO
{
    /// <summary>
    /// A single buffer segment. The content lays on range [0..size[. The buffer might have larger capacity.
    /// </summary>
    public struct IOBufferSegment
    {
        /// <summary>
        /// The view into the segment contents.
        /// </summary>
        public byte[] Buffer;

        /// <summary>
        /// The number of bytes of content this segment contains. The content lays on range [0, size[.
        /// </summary>
        public int Size;

        public static IOBufferSegment Empty
        {
            get
            {
                return new IOBufferSegment()
                {
                    Buffer = Array.Empty<byte>(),
                    Size = 0
                };
            }
        }

#if NETCOREAPP
        public Span<byte> AsSpan() => Buffer.AsSpan(0, Size);
#endif
    };

    /// <summary>
    /// Backing storage of various IO buffers.
    ///
    /// <para>
    /// To support buffers of various natures, the storage is represented in Segments. For convenience,
    /// there is always at least one segment. This segment however may be zero-sized and empty (IOBufferSegment.Empty).
    /// </para>
    ///
    /// <para>
    /// To write into the buffer, the writer is expected to fetch a segment or segments, edit them and flush the changed
    /// segments.
    /// </para>
    /// </summary>
    public interface IOBuffer : IDisposable
    {
        /// <summary>
        /// Is the buffer empty, i.e. has no content. Behavior is undefined if a write is ongoing (BeginWrite/EndWrite).
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        /// The number of bytes of content in the buffer. Behavior is undefined if a write is ongoing (BeginWrite/EndWrite).
        /// </summary>
        int Count { get; }

        /// <summary>
        /// The number of Segments in the buffer. Greater or equal to 0.
        /// </summary>
        int NumSegments { get; }

        /// <summary>
        /// Returns the segment. <paramref name="segmentIndex"/> should be in range [0, NumSegments[.
        /// <para>
        /// Notably <c>GetSegment(0)</c> is always valid. However, the returned segment might be 0-sized.
        /// </para>
        /// </summary>
        IOBufferSegment GetSegment(int segmentIndex);

        #region Write

        /// <summary>
        /// Locks the buffer for writing.
        /// </summary>
        void BeginWrite();

        /// <summary>
        /// Request a new block of memory for writing of at least <paramref name="minBytes"/> bytes.
        /// The requests are typically small so it's up to the implementation of the buffer to ensure
        /// reasonably large blocks are allocated. The writes into the requested block need to be
        /// flushed by a call to <see cref="CommitMemory(int)"/>.
        /// </summary>
        /// <param name="minBytes"></param>
        /// <returns></returns>
        MetaMemory<byte> GetMemory(int minBytes);

        /// <summary>
        /// Commits a sequence of bytes written into a block of memory requested with <see cref="GetMemory(int)"/>.
        /// </summary>
        /// <param name="numBytes"></param>
        void CommitMemory(int numBytes);

        /// <summary>
        /// Unlocks the buffer after writing.
        /// </summary>
        void EndWrite();

        /// <summary>
        /// Marks buffer as empty. The underlying memory may be retained or release, depending on
        /// the implementation.
        /// </summary>
        void Clear();

        #endregion

        #region Read

        /// <summary>
        /// Locks the buffer for reading.
        /// </summary>
        void BeginRead();

        /// <summary>
        /// Unlocks the buffer after reading.
        /// </summary>
        void EndRead();

        #endregion
    };
}
