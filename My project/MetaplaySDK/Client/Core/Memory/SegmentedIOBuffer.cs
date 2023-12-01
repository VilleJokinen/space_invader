// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using Metaplay.Core.IO;

namespace Metaplay.Core.Memory
{
    /// <summary>
    /// IOBuffer backed by fixed-size MemoryPool allocations.
    /// </summary>
    public sealed class SegmentedIOBuffer : RWIOBufferBase
    {
        public override int Count => _totalBytes;
        public override int NumSegments => _segments.Count;

        readonly struct Segment
        {
            public readonly MemoryAllocation    Allocation;
            public readonly int                 Size;

            public int BytesAvailable => Allocation.Bytes.Length - Size;

            public Segment(MemoryAllocation allocation, int size = 0)
            {
                Allocation = allocation;
                Size = size;
            }
        }

        readonly int                    _segmentSize;
        List<Segment>                   _segments;
        int                             _totalBytes;

        static IMemoryAllocator Allocator => PoolAllocator.Shared;

        public SegmentedIOBuffer(int segmentSize = 4096)
        {
            if (segmentSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(segmentSize));

            _segmentSize = segmentSize;
            _segments = new List<Segment>();
            _totalBytes = 0;
        }

        void AllocateSegment(int minBytes)
        {
            int allocSize = System.Math.Max(minBytes, _segmentSize);
            Segment segment = new Segment(Allocator.Allocate(allocSize));
            _segments.Add(segment);
        }

        public override MetaMemory<byte> GetMemory(int minBytes)
        {
            base.EnsureWriteLock();

            // If no segments allocated, allocate an initial one
            if (_segments.Count == 0)
                AllocateSegment(minBytes);

            // If not enough space in current segment, allocate a new one
            Segment activeSegment = _segments[_segments.Count - 1];
            if (activeSegment.BytesAvailable < minBytes)
            {
                AllocateSegment(minBytes);
                activeSegment = _segments[_segments.Count - 1];
            }

            // Return the latest segment as MetaMemory
            return new MetaMemory<byte>(activeSegment.Allocation.Bytes, activeSegment.Size);
        }

        public override void CommitMemory(int numBytes)
        {
            base.EnsureWriteLock();

            if (numBytes == 0)
                return;

            Segment activeSegment = _segments[_segments.Count - 1];
            _segments[_segments.Count - 1] = new Segment(activeSegment.Allocation, activeSegment.Size + numBytes);
            _totalBytes += numBytes;
        }

        public override IOBufferSegment GetSegment(int segmentIndex)
        {
            base.EnsureReadOrWriteLock();

            // Allocate initial segment here in case it hasn't yet been allocated. This is because IOReader expects to always have
            // at least one segment, even if the buffer is completely empty.
            if (_segments.Count == 0)
                AllocateSegment(0);

            if (segmentIndex < 0 || segmentIndex >= _segments.Count)
                throw new InvalidOperationException($"Illegal segment: {segmentIndex}");

            Segment segment = _segments[segmentIndex];
            return new IOBufferSegment() { Buffer = segment.Allocation.Bytes, Size = segment.Size };
        }

        public override void Dispose()
        {
            Clear();
        }

        public override void Clear()
        {
            // Release all allocated segments & reset size
            BeginWrite();
            for (int ndx = 0; ndx < NumSegments; ++ndx)
            {
                MemoryAllocation allocation = _segments[ndx].Allocation;
                Allocator.Deallocate(ref allocation);
            }
            _segments.Clear();
            _totalBytes = 0;
            EndWrite();
        }
    }
}
