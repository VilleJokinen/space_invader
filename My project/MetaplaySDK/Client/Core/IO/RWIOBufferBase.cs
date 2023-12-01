// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.IO
{
    /// <summary>
    /// Base class for Read-Write IOBuffers.
    ///
    /// <para>
    /// Maintains a (unsynchronized) reader and writer counters, and allows concurrently either 1 writer or N readers.
    /// </para>
    /// </summary>
    abstract public class RWIOBufferBase : IOBuffer
    {
        protected bool _writeLock { get; private set; } = false;
        protected int _numReaders { get; private set; } = 0;

        protected void EnsureWriteLock()
        {
            if (!_writeLock)
                throw new InvalidOperationException("Operation requires write lock to the buffer but doesn't have it");
        }

        protected void EnsureReadOrWriteLock()
        {
            if (!_writeLock && _numReaders == 0)
                throw new InvalidOperationException("Operation requires read or write lock to the buffer but has neither");
        }

        public virtual void BeginWrite()
        {
            if (_writeLock)
                throw new InvalidOperationException("Cannot acquire a write lock to the buffer due to an ongoing write lock");
            if (_numReaders != 0)
                throw new InvalidOperationException("Cannot acquire a write lock to the buffer due to an ongoing read");
            _writeLock = true;
        }

        public virtual void EndWrite()
        {
            _writeLock = false;
        }

        public virtual void BeginRead()
        {
            if (_writeLock)
                throw new InvalidOperationException("Cannot read the buffer due to an ongoing write");
            _numReaders++;
        }

        public virtual void EndRead()
        {
            _numReaders--;
        }

        public virtual bool IsEmpty => Count == 0;

        public abstract int Count { get; }
        public abstract int NumSegments { get; }
        public abstract IOBufferSegment GetSegment(int segmentIndex);
        public abstract MetaMemory<byte> GetMemory(int minBytes);
        public abstract void CommitMemory(int numBytes);
        public abstract void Clear();
        public abstract void Dispose();
    }
}
