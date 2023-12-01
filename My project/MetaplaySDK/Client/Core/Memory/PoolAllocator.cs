// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Buffers;

namespace Metaplay.Core.Memory
{
    /// <summary>
    /// Allocator backed by a global memory pool
    /// </summary>
    public sealed class PoolAllocator : IMemoryAllocator
    {
        static readonly PoolAllocator _shared = new PoolAllocator();

        public MemoryAllocation Allocate(int size)
        {
            byte[] allocBacking = ArrayPool<byte>.Shared.Rent(size);
            MemoryAllocation alloc = new MemoryAllocation(bytes: allocBacking);
            return alloc;
        }

        public void Deallocate(ref MemoryAllocation allocation)
        {
            ArrayPool<byte>.Shared.Return(allocation.Bytes);
            allocation = MemoryAllocation.Empty;
        }

        /// <summary>
        /// Shared pool allocator.
        /// </summary>
        public static PoolAllocator Shared => _shared;
    };
}
