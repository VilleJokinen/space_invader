// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using System;
#if NETCOREAPP
using System.Numerics;
#endif

namespace Metaplay.Core
{
    public static class MurmurHash
    {
        public static uint MurmurHash2(IOBuffer buffer, uint seed = 0xc58f1a7b)
        {
            // \note[jarkko]: Reimplemented based on MurmurHash2 spec and the public domain reference implementation (trailing byte mixing, choice of M).
            const uint m = 0x5bd1e995;
            const int r = 24;

            buffer.BeginRead();

            try
            {
                // compat
                if (buffer.Count == 0)
                    return 0;

                // Hash is computed in words. If a buffer or segments is not multiple of a word,
                // the remainder, which we call here a Spill, needs to be handled separately.

                uint h          = seed ^ (uint)buffer.Count;
                uint spill      = 0;
                uint spillSize  = 0;

                for (int segmentIndex = 0; segmentIndex < buffer.NumSegments; ++segmentIndex)
                {
                    IOBufferSegment segment     = buffer.GetSegment(segmentIndex);
                    int             offset      = 0;
                    int             remaining   = segment.Size;

                    // Spill
                    for (;;)
                    {
                        if (spillSize == 0)
                            break;
                        if (remaining == 0)
                            break;

                        byte piece = segment.Buffer[offset];
                        offset++;
                        remaining--;

                        spill = (spill) | ((uint)piece << (int)(8 * spillSize));
                        spillSize++;

                        if (spillSize == 4)
                        {
                            // spill round like a normal
                            uint k = spill;
                            k *= m;
                            k ^= k >> r;
                            k *= m;

                            h *= m;
                            h ^= k;


                            // reset spill
                            spill = 0;
                            spillSize = 0;
                        }
                    }

#if NETCOREAPP
                    // Word vectors
                    //
                    // Reading Vectors<> allows us to reinterpret bytes as uints without copies, use of unsafe
                    // pointers, or worrying about alignment, while being about as fast as unsafe pointer
                    // access. Win.
                    //
                    // We don't compute the mixing with SIMD as because the H mixing is a serial XOR MUL over
                    // all words and I couldn't figure out a how to simd it efficiently. Mixing the K is trivially
                    // parallelizable but it does not help much. Also it would be fragile -- using some of
                    // System.Runtime.Intrinsics causes compiler to spill a hot variable (h) to stack, tanking
                    // performance.
                    if (remaining >= 4 * Vector<uint>.Count && BitConverter.IsLittleEndian)
                    {
                        Span<byte>  vecStartSpan    = segment.Buffer.AsSpan(start: offset);
                        int         vecByteSize     = 4 * Vector<uint>.Count;
                        int         numVecs         = (int)((uint)remaining / (uint)vecByteSize);

                        for (int vecNdx = 0; vecNdx < numVecs; ++vecNdx)
                        {
                            Vector<uint> kv = new Vector<uint>(vecStartSpan.Slice(vecNdx * vecByteSize));
                            for (int e = 0; e < Vector<uint>.Count; ++e)
                            {
                                uint k = kv[e];
                                k *= m;
                                k ^= k >> r;
                                k *= m;

                                h *= m;
                                h ^= k;
                            }
                        }

                        offset += numVecs * vecByteSize;
                        remaining -= numVecs * vecByteSize;
                    }
#endif
                    // Words
                    int numWordsRemaining = remaining / 4;
                    if (BitConverter.IsLittleEndian)
                    {
                        for (int wordNdx = 0; wordNdx < numWordsRemaining; ++wordNdx)
                        {
                            uint k = BitConverter.ToUInt32(segment.Buffer, offset + wordNdx * 4);

                            // Round
                            k *= m;
                            k ^= k >> r;
                            k *= m;

                            h *= m;
                            h ^= k;
                        }
                    }
                    else
                    {
                        for (int wordNdx = 0; wordNdx < numWordsRemaining; ++wordNdx)
                        {
                            uint k  = ((uint)segment.Buffer[offset + wordNdx * 4])
                                    | (((uint)segment.Buffer[offset + wordNdx * 4 + 1]) << 8)
                                    | (((uint)segment.Buffer[offset + wordNdx * 4 + 2]) << 16)
                                    | (((uint)segment.Buffer[offset + wordNdx * 4 + 3]) << 24);

                            // Round
                            k *= m;
                            k ^= k >> r;
                            k *= m;

                            h *= m;
                            h ^= k;
                        }
                    }
                    offset += numWordsRemaining * 4;
                    remaining -= numWordsRemaining * 4;

                    // New spill
                    for (;;)
                    {
                        if (remaining == 0)
                            break;

                        byte piece = segment.Buffer[offset];
                        offset++;
                        remaining--;

                        spill = (spill) | ((uint)piece << (int)(8 * spillSize));
                        spillSize++;
                    }
                }

                // final spill
                if (spillSize != 0)
                {
                    uint k = spill;
                    h ^= k;
                    h *= m;
                }

                h ^= h >> 13;
                h *= m;
                h ^= h >> 15;

                return h;
            }
            finally
            {
                buffer.EndRead();
            }
        }

        public static uint MurmurHash2(byte[] data, uint seed = 0xc58f1a7b)
        {
            // \see MurmurHash2InlineBitConverter from http://landman-code.blogspot.com/2009/02/c-superfasthash-and-murmurhash2.html
            const uint M = 0x5bd1e995;
            const int R = 24;

            int length = data.Length;
            if (length == 0)
                return 0;
            uint h = seed ^ (uint)length;
            int currentIndex = 0;
            if (BitConverter.IsLittleEndian)
            {
                while (length >= 4)
                {
                    uint k = BitConverter.ToUInt32(data, currentIndex);
                    currentIndex += 4;

                    k *= M;
                    k ^= k >> R;
                    k *= M;

                    h *= M;
                    h ^= k;
                    length -= 4;
                }
            }
            else
            {
                while (length >= 4)
                {
                    uint k = (uint)(data[currentIndex++] | data[currentIndex++] << 8 | data[currentIndex++] << 16 | data[currentIndex++] << 24);

                    k *= M;
                    k ^= k >> R;
                    k *= M;

                    h *= M;
                    h ^= k;
                    length -= 4;
                }
            }
            switch (length)
            {
                case 3:
                    h ^= (ushort)(data[currentIndex++] | data[currentIndex++] << 8);
                    h ^= (uint)(data[currentIndex] << 16);
                    h *= M;
                    break;
                case 2:
                    h ^= (ushort)(data[currentIndex++] | data[currentIndex] << 8);
                    h *= M;
                    break;
                case 1:
                    h ^= data[currentIndex];
                    h *= M;
                    break;
                default:
                    break;
            }

            // Do a few final mixes of the hash to ensure the last few
            // bytes are well-incorporated.

            h ^= h >> 13;
            h *= M;
            h ^= h >> 15;

            return h;
        }
    }
}
