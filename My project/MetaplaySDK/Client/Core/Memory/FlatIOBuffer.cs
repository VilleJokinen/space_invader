// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using Metaplay.Core.IO;

namespace Metaplay.Core.Memory
{
    /// <summary>
    /// IOBuffer backed by a single contiguous MemoryPool allocation.
    /// </summary>
    public sealed class FlatIOBuffer : RWIOBufferBase
    {
        public const int MinBufferSize = 32;
        public const int MaxBufferSize = 1 << 28;

        public override int Count => _count;
        public override int NumSegments => 1;

        MemoryAllocation _allocation;
        int _count;

        static IMemoryAllocator Allocator => PoolAllocator.Shared;

        public FlatIOBuffer(int initialCapacity = 128)
        {
            _allocation = Allocator.Allocate(System.Math.Max(initialCapacity, MinBufferSize));
            _count = 0;
        }

#if NETCOREAPP
        public Span<byte> AsSpan() => GetSegment(0).AsSpan();

        public static FlatIOBuffer CopyFromSpan(ReadOnlySpan<byte> span)
        {
            int spanSize = span.Length;
            FlatIOBuffer buffer = new FlatIOBuffer(spanSize);
            buffer.BeginWrite();
            MetaMemory<byte> mem = buffer.GetMemory(spanSize);
            span.CopyTo(mem.AsSpan());
            buffer.CommitMemory(spanSize);
            buffer.EndWrite();
            return buffer;
        }
#endif

        public override MetaMemory<byte> GetMemory(int minBytes)
        {
            base.EnsureWriteLock();

            // If not enough space left, re-size the allocation
            int spaceLeft = _allocation.Bytes.Length - _count;
            if (spaceLeft < minBytes)
            {
                int newCapacity = Util.CeilToPowerOfTwo(System.Math.Max(_allocation.Bytes.Length + minBytes, MinBufferSize));
                if (newCapacity > MaxBufferSize)
                    throw new InvalidOperationException($"FlatIOBuffer growing too large: has {_allocation.Bytes.Length} bytes, requesting {minBytes} more");

                MemoryAllocation oldAllocation = _allocation;
                _allocation = Allocator.Allocate(newCapacity);
                Buffer.BlockCopy(oldAllocation.Bytes, 0, _allocation.Bytes, 0, Count);
                Allocator.Deallocate(ref oldAllocation);
            }

            return new MetaMemory<byte>(_allocation.Bytes, _count);
        }

        public override void CommitMemory(int numBytes)
        {
            base.EnsureWriteLock();

            _count += numBytes;
        }

        public override IOBufferSegment GetSegment(int segmentIndex)
        {
            base.EnsureReadOrWriteLock();

            if (segmentIndex != 0)
                throw new InvalidOperationException($"Illegal segment: {segmentIndex}. Must be zero");

            return new IOBufferSegment() { Buffer = _allocation.Bytes, Size = _count };
        }

        public override void Dispose()
        {
            BeginWrite();
            Allocator.Deallocate(ref _allocation);
            _count = 0;
            EndWrite();
        }

        public override void Clear()
        {
            BeginWrite();
            _count = 0;
            EndWrite();
        }
    }
}
