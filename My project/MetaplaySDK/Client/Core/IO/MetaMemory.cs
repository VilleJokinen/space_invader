// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Metaplay.Core.IO
{
    public readonly struct MetaMemory<T>
    {
        public readonly T[] Buf;
        public readonly int Offset;
        public int Length => Buf.Length - Offset;

        public static MetaMemory<T> Empty = new MetaMemory<T>(Array.Empty<T>(), 0);

        public MetaMemory(T[] buf, int offset)
        {
            Buf = buf;
            Offset = offset;
        }

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Buf[Offset + index]; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { Buf[Offset + index] = value; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MetaMemory<T> Slice(int index)
        {
            return new MetaMemory<T>(Buf, Offset + index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<T> AsMemory()
        {
            return Buf.AsMemory(Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan()
        {
            return Buf.AsSpan(Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Span<T>(MetaMemory<T> s) => s.AsSpan();
    }
}
