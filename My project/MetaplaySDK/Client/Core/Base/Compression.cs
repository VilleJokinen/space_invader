// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using System.IO;
using System.IO.Compression;

namespace Metaplay.Core
{
    [MetaSerializable]
    public enum CompressionAlgorithm
    {
        None        = 0,    // Not compressed
        Deflate     = 1,    // Deflate (legacy format, using System.IO.Compression.DeflateStream)
        LZ4         = 2,    // LZ4 (via BlobCompress, using IronCompress)
        Zstandard   = 3,    // Zstandard (via BlobCompress, using IronCompress)
    }

    public class DecompressionSizeLimitExceeded : Exception
    {
        public DecompressionSizeLimitExceeded(string message, Exception innerException = null) : base(message, innerException) { }
    }

    public static class CompressUtil
    {
        public static byte[] DeflateCompress(byte[] input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return DeflateCompress(input.AsSpan());
        }

        public static byte[] DeflateCompress(ReadOnlySpan<byte> input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            // \todo [petri] optimize allocations by recycling buffers?
            using (MemoryStream outStream = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(outStream, CompressionLevel.Fastest))
                {
                    ds.Write(input);
                    ds.Flush();
                }
                return outStream.ToArray();
            }
        }

        public static byte[] DeflateDecompress(ReadOnlyMemory<byte> compressed, int maxDecompressedSize = -1)
        {
                using MemoryStream         outStream        = new MemoryStream();
                using ReadOnlyMemoryStream compressedStream = new ReadOnlyMemoryStream(compressed);
                using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                {
                    if (maxDecompressedSize < 0)
                        deflateStream.CopyTo(outStream);
                    else
                    {
                        using (LimitedReadStream limitedStream = new LimitedReadStream(deflateStream, maxDecompressedSize))
                        {
                            try
                            {
                                limitedStream.CopyTo(outStream);
                            }
                            catch (LimitedReadStream.LimitExceededException limitExceeded)
                            {
                                throw new DecompressionSizeLimitExceeded($"Deflate-decompressed size exceeds the limit of {maxDecompressedSize}", innerException: limitExceeded);
                            }
                        }
                    }
                }

                return outStream.ToArray();
        }

        public static byte[] DeflateDecompress(byte[] compressed, int offset = 0, int length = -1, int maxDecompressedSize = -1)
        {
            if (compressed == null)
                throw new ArgumentNullException(nameof(compressed));

            return DeflateDecompress(new Memory<byte>(compressed, offset, length < 0 ? (compressed.Length - offset) : length), maxDecompressedSize);
        }

        public static byte[] Decompress(byte[] payload, CompressionAlgorithm algorithm, int maxDecompressedSize = -1)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            switch (algorithm)
            {
                case CompressionAlgorithm.None:
                    if (maxDecompressedSize >= 0 && payload.Length > maxDecompressedSize)
                        throw new DecompressionSizeLimitExceeded($"None-compressed payload has length {payload.Length}, larger than maximum {maxDecompressedSize}");

                    return payload;

                case CompressionAlgorithm.Deflate:
                    return DeflateDecompress(payload, offset: 0, length: -1, maxDecompressedSize: maxDecompressedSize);

                default:
                    throw new InvalidOperationException($"Invalid CompressionAlgorithm {algorithm}");
            }
        }

        public static bool IsSupportedForDecompression(CompressionAlgorithm compression)
        {
            switch (compression)
            {
                case CompressionAlgorithm.None:
                case CompressionAlgorithm.Deflate:
                    return true;

                default:
                    return false;
            }
        }

        public static CompressionAlgorithmSet GetSupportedDecompressionAlgorithms()
        {
            CompressionAlgorithmSet set = new CompressionAlgorithmSet();
            set.Add(CompressionAlgorithm.Deflate);
            return set;
        }
    }

    [MetaSerializable]
    public struct CompressionAlgorithmSet
    {
        [MetaMember(1)] uint _flags;

        public bool Contains(CompressionAlgorithm algorithm) => (_flags & ToBit(algorithm)) != 0;
        public void Add(CompressionAlgorithm algorithm)
        {
            _flags |= ToBit(algorithm);
        }

        uint ToBit(CompressionAlgorithm algorithm)
        {
            if (algorithm == CompressionAlgorithm.None)
                throw new ArgumentOutOfRangeException(nameof(algorithm), "None is not an algorithm");

            uint algoNdx = (uint)algorithm - 1;
            if (algoNdx >= 32)
                throw new ArgumentOutOfRangeException(nameof(algorithm));

            return 1u << (int)algoNdx;
        }
    }
}
