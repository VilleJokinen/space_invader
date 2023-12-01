// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Metaplay.Cloud.Utility
{
    public static class MiniMD5
    {
        const int ExtraSize = 9;  // min. 1 padding byte + 8 for length
        const int BlockSize = 64;

        /// <summary>
        /// Return first 32 bits of a full MD5. Useful for hashing strings when same hashing is needed in application and database.
        /// Note: it's possible to compute the same value in MySql using: 'conv(substr(md5(input), 1, 8), 16, 10)'. The testing on
        /// this was fairly limited, though: non-ASCII characters were tested, but not ones that don't fit in 16 bits. SQLite doesn't
        /// include hashing functions, but it should be possible to add them fairly easily, see:
        /// https://stackoverflow.com/questions/3179021/sha1-hashing-in-sqlite-how/3179047#3179047.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static uint ComputeMiniMD5(string input)
        {
            int numBytes = Encoding.UTF8.GetByteCount(input);
            int allocSize = (numBytes + ExtraSize + BlockSize - 1) & ~(BlockSize - 1);
            Span<byte> bytes = (allocSize <= 256) ? stackalloc byte[allocSize] : new byte[allocSize];
            Encoding.UTF8.GetBytes(input, bytes);
            return ComputeMD5(bytes, numBytes);
        }

        // MD5 constants
        private const int S11 = 7;
        private const int S12 = 12;
        private const int S13 = 17;
        private const int S14 = 22;
        private const int S21 = 5;
        private const int S22 = 9;
        private const int S23 = 14;
        private const int S24 = 20;
        private const int S31 = 4;
        private const int S32 = 11;
        private const int S33 = 16;
        private const int S34 = 23;
        private const int S41 = 6;
        private const int S42 = 10;
        private const int S43 = 15;
        private const int S44 = 21;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateLeft(uint x, int n) { return (x << n) | (x >> (32 - n)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FF(ref uint a, uint b, uint c, uint d, uint x, int s, uint ac)
        {
            a += ((b & c) | (~b & d)) + x + ac;
            a = RotateLeft(a, s);
            a += b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GG(ref uint a, uint b, uint c, uint d, uint x, int s, uint ac)
        {
            a += ((b & d) | (c & ~d)) + x + ac;
            a = RotateLeft(a, s);
            a += b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void HH(ref uint a, uint b, uint c, uint d, uint x, int s, uint ac)
        {
            a += (b ^ c ^ d) + x + ac;
            a = RotateLeft(a, s);
            a += b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void II(ref uint a, uint b, uint c, uint d, uint x, int s, uint ac)
        {
            a += (c ^ (b | ~d)) + x + ac;
            a = RotateLeft(a, s);
            a += b;
        }

        /// <summary>
        /// Computes MD5 of a buffer of bytes. Assumes that buffer is aligned to 64-byte boundary and has
        /// at least 9 bytes free for padding (min. 1 byte) and payload length (8 bytes).
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="inputSize"></param>
        /// <returns></returns>
        private static uint ComputeMD5(Span<byte> buffer, int inputSize)
        {
            // Initialize state
            Span<uint> ctxState = stackalloc uint[4];
            ctxState[0] = 0x67452301;
            ctxState[1] = 0xefcdab89;
            ctxState[2] = 0x98badcfe;
            ctxState[3] = 0x10325476;

            // Pad input to 56 bytes (modulo 64)
            int padNdx = inputSize & 0x3f;
            int padLen = (padNdx < 56) ? (56 - padNdx) : (120 - padNdx);
            buffer[inputSize] = 0x80;
            for (int ndx = 1; ndx < padLen; ndx++)
                buffer[inputSize + ndx] = 0;

            // Append number of bits
            int bitsOffset = inputSize + padLen;
            uint numBits = (uint)inputSize << 3;
            buffer[bitsOffset] = (byte)numBits;
            buffer[bitsOffset + 1] = (byte)(numBits >> 8);
            buffer[bitsOffset + 2] = (byte)(numBits >> 16);
            buffer[bitsOffset + 3] = (byte)(numBits >> 24);
            buffer[bitsOffset + 4] = 0; // \note only supports inputs up to 512MB
            buffer[bitsOffset + 5] = 0;
            buffer[bitsOffset + 6] = 0;
            buffer[bitsOffset + 7] = 0;

            // Transform all blocks of 64 bytes
            int totalSize = inputSize + padLen + 8;
            TransformBlocks(ctxState, buffer, totalSize);

            // Return first 32 bits
            return Util.ByteSwap(ctxState[0]);
        }

        // Transform number of MD5 blocks
        private static void TransformBlocks(Span<uint> ctxState, ReadOnlySpan<byte> buffer, int totalSize)
        {
            Span<uint> block = stackalloc uint[16];

            for (int blockOffset = 0; blockOffset < totalSize; blockOffset += 64)
            {
                ConvertBlock(block, buffer, blockOffset);
                //ReadOnlySpan<uint> block = MemoryMarshal.Cast<byte, uint>(buffer.Slice(blockOffset, 64)); // \todo [petri] are there alignment or endianess issues here?

                uint a = ctxState[0], b = ctxState[1], c = ctxState[2], d = ctxState[3];

                // Round 1
                FF(ref a, b, c, d, block[0], S11, 0xd76aa478); // 1
                FF(ref d, a, b, c, block[1], S12, 0xe8c7b756); // 2
                FF(ref c, d, a, b, block[2], S13, 0x242070db); // 3
                FF(ref b, c, d, a, block[3], S14, 0xc1bdceee); // 4
                FF(ref a, b, c, d, block[4], S11, 0xf57c0faf); // 5
                FF(ref d, a, b, c, block[5], S12, 0x4787c62a); // 6
                FF(ref c, d, a, b, block[6], S13, 0xa8304613); // 7
                FF(ref b, c, d, a, block[7], S14, 0xfd469501); // 8
                FF(ref a, b, c, d, block[8], S11, 0x698098d8); // 9
                FF(ref d, a, b, c, block[9], S12, 0x8b44f7af); // 10
                FF(ref c, d, a, b, block[10], S13, 0xffff5bb1); // 11
                FF(ref b, c, d, a, block[11], S14, 0x895cd7be); // 12
                FF(ref a, b, c, d, block[12], S11, 0x6b901122); // 13
                FF(ref d, a, b, c, block[13], S12, 0xfd987193); // 14
                FF(ref c, d, a, b, block[14], S13, 0xa679438e); // 15
                FF(ref b, c, d, a, block[15], S14, 0x49b40821); // 16

                // Round 2
                GG(ref a, b, c, d, block[1], S21, 0xf61e2562); // 17
                GG(ref d, a, b, c, block[6], S22, 0xc040b340); // 18
                GG(ref c, d, a, b, block[11], S23, 0x265e5a51); // 19
                GG(ref b, c, d, a, block[0], S24, 0xe9b6c7aa); // 20
                GG(ref a, b, c, d, block[5], S21, 0xd62f105d); // 21
                GG(ref d, a, b, c, block[10], S22, 0x02441453); // 22
                GG(ref c, d, a, b, block[15], S23, 0xd8a1e681); // 23
                GG(ref b, c, d, a, block[4], S24, 0xe7d3fbc8); // 24
                GG(ref a, b, c, d, block[9], S21, 0x21e1cde6); // 25
                GG(ref d, a, b, c, block[14], S22, 0xc33707d6); // 26
                GG(ref c, d, a, b, block[3], S23, 0xf4d50d87); // 27
                GG(ref b, c, d, a, block[8], S24, 0x455a14ed); // 28
                GG(ref a, b, c, d, block[13], S21, 0xa9e3e905); // 29
                GG(ref d, a, b, c, block[2], S22, 0xfcefa3f8); // 30
                GG(ref c, d, a, b, block[7], S23, 0x676f02d9); // 31
                GG(ref b, c, d, a, block[12], S24, 0x8d2a4c8a); // 32

                // Round 3
                HH(ref a, b, c, d, block[5], S31, 0xfffa3942); // 33
                HH(ref d, a, b, c, block[8], S32, 0x8771f681); // 34
                HH(ref c, d, a, b, block[11], S33, 0x6d9d6122); // 35
                HH(ref b, c, d, a, block[14], S34, 0xfde5380c); // 36
                HH(ref a, b, c, d, block[1], S31, 0xa4beea44); // 37
                HH(ref d, a, b, c, block[4], S32, 0x4bdecfa9); // 38
                HH(ref c, d, a, b, block[7], S33, 0xf6bb4b60); // 39
                HH(ref b, c, d, a, block[10], S34, 0xbebfbc70); // 40
                HH(ref a, b, c, d, block[13], S31, 0x289b7ec6); // 41
                HH(ref d, a, b, c, block[0], S32, 0xeaa127fa); // 42
                HH(ref c, d, a, b, block[3], S33, 0xd4ef3085); // 43
                HH(ref b, c, d, a, block[6], S34, 0x04881d05); // 44
                HH(ref a, b, c, d, block[9], S31, 0xd9d4d039); // 45
                HH(ref d, a, b, c, block[12], S32, 0xe6db99e5); // 46
                HH(ref c, d, a, b, block[15], S33, 0x1fa27cf8); // 47
                HH(ref b, c, d, a, block[2], S34, 0xc4ac5665); // 48

                // Round 4
                II(ref a, b, c, d, block[0], S41, 0xf4292244); // 49
                II(ref d, a, b, c, block[7], S42, 0x432aff97); // 50
                II(ref c, d, a, b, block[14], S43, 0xab9423a7); // 51
                II(ref b, c, d, a, block[5], S44, 0xfc93a039); // 52
                II(ref a, b, c, d, block[12], S41, 0x655b59c3); // 53
                II(ref d, a, b, c, block[3], S42, 0x8f0ccc92); // 54
                II(ref c, d, a, b, block[10], S43, 0xffeff47d); // 55
                II(ref b, c, d, a, block[1], S44, 0x85845dd1); // 56
                II(ref a, b, c, d, block[8], S41, 0x6fa87e4f); // 57
                II(ref d, a, b, c, block[15], S42, 0xfe2ce6e0); // 58
                II(ref c, d, a, b, block[6], S43, 0xa3014314); // 59
                II(ref b, c, d, a, block[13], S44, 0x4e0811a1); // 60
                II(ref a, b, c, d, block[4], S41, 0xf7537e82); // 61
                II(ref d, a, b, c, block[11], S42, 0xbd3af235); // 62
                II(ref c, d, a, b, block[2], S43, 0x2ad7d2bb); // 63
                II(ref b, c, d, a, block[9], S44, 0xeb86d391); // 64

                ctxState[0] += a;
                ctxState[1] += b;
                ctxState[2] += c;
                ctxState[3] += d;
            }
        }

        /// <summary>
        /// Convert a block of 64 bytes to 16 uints for processing.
        /// </summary>
        /// <param name="output"></param>
        /// <param name="input"></param>
        private static void ConvertBlock(Span<uint> output, ReadOnlySpan<byte> input, int srcOffset)
        {
            for (int ndx = 0; ndx < 16; ndx++)
                output[ndx] = BitConverter.ToUInt32(input.Slice(srcOffset + ndx * 4, 4));
        }
    }
}
