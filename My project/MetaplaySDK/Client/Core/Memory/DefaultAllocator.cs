// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Memory
{
    /// <summary>
    /// Allocator backed by the default platform allocator.
    /// </summary>
    public sealed class DefaultAllocator : IMemoryAllocator
    {
        static readonly DefaultAllocator _shared = new DefaultAllocator();

        public MemoryAllocation Allocate(int size)
        {
            byte[] allocBacking = new byte[size];
            MemoryAllocation alloc = new MemoryAllocation(bytes: allocBacking);
            return alloc;
        }

        public void Deallocate(ref MemoryAllocation allocation)
        {
            allocation = MemoryAllocation.Empty;
        }

        /// <summary>
        /// Shared default allocator.
        /// </summary>
        public static DefaultAllocator Shared => _shared;
    };
}
