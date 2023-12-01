// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Memory
{
    /// <summary>
    /// An allocation retrieved from an IMemoryAllocator.
    /// </summary>
    public readonly struct MemoryAllocation
    {
        public readonly byte[] Bytes;

        public MemoryAllocation(byte[] bytes)
        {
            Bytes = bytes;
        }

        public static MemoryAllocation Empty
        {
            get
            {
                return new MemoryAllocation
                (
                    bytes: Array.Empty<byte>()
                );
            }
        }
    }

    /// <summary>
    /// Provides <see cref="MemoryAllocation"/>s.
    /// </summary>
    public interface IMemoryAllocator
    {
        /// <summary>
        /// Allocates a <see cref="MemoryAllocation"/> with a length of at least <paramref name="size"/>.
        /// </summary>
        MemoryAllocation Allocate(int size);

        /// <summary>
        /// Returns a previously Allocated allocation back to the allocator.
        /// By calling this method, the caller relinquishes the ownership to the allocation and must
        /// no longer modify the allocation.
        /// </summary>
        void Deallocate(ref MemoryAllocation allocation);
    }
}
