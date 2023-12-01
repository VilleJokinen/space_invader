// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using IronCompress;
using Metaplay.Core;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using System;
using System.Buffers;
using System.IO.Compression;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Helpers for compressing anonymous byte[] blobs using IronCompress. There is no support
    /// for compressing multiple files or archives. Only compression of buffers is supported,
    /// i.e., streaming compression is not supported. Supports the LZ4 and Zstandard compression
    /// algorithms.
    /// <para>
    /// Compressed buffers use the format:
    /// - Magic: 4 bytes
    /// - Version: 1 byte
    /// - Flags: 1 byte (0 for now)
    /// - Algorith: 1 byte (CompressionAlgorithm)
    /// - UncompressedSize: VarInt
    /// - CompressedBytes: byte[]
    /// </para>
    /// </summary>
    public static class BlobCompress
    {
        // \note The object is thread-safe
        static Iron s_iron = new Iron(ArrayPool<byte>.Shared);

        public const int FormatVersion = 1;

        // Use identifiable starting characters in magic, but non-UTF8 latter part to avoid collisions with text files
        public const byte MagicPrefixByte0 = (byte)'M';
        public const byte MagicPrefixByte1 = (byte)'P';
        public const byte MagicPrefixByte2 = 0xe6;  // small latin 'ae'
        public const byte MagicPrefixByte3 = 0xfe;  // small latin 'thorn'

        const int MaximumHeaderSize = 16; // Maximum header size in bytes, must include all possible payloads: 4 header, 1 version, 1 flags, 1 algorithm, 5 size, 4 "extra" for future

        static (Codec, CompressionLevel) GetIronCompressCodec(CompressionAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case CompressionAlgorithm.None:
                    throw new ArgumentException("CompressionAlgorithm.None not supported in this code path!", nameof(algorithm));

                case CompressionAlgorithm.Deflate:
                    throw new ArgumentException("CompressionAlgorithm.Deflate compression not supported in this code path!", nameof(algorithm));

                case CompressionAlgorithm.LZ4:
                    // \note Use CompressionLevel.Optimal other levels lose compression ratio fast but only give modest speed ups.
                    // \note The compression level handling for LZ4 was fixed in v1.5.1.
                    // See: https://github.com/aloneguid/ironcompress/pull/19
                    // See: The bug has been reported here: https://github.com/aloneguid/ironcompress/issues/17
                    return (Codec.LZ4, CompressionLevel.Optimal);

                case CompressionAlgorithm.Zstandard:
                    // \note CompressionLevel.Fastest a good trade-off between speed and compression ratio for Zstandard.
                    return (Codec.Zstd, CompressionLevel.Fastest);

                default:
                    throw new ArgumentException($"Unknown CompressionAlgorithm {algorithm}", nameof(algorithm));
            }
        }

        /// <summary>
        /// Compress the bytes in a buffer with the specified algorithm. Includes a header in the buffer
        /// which identifies the compression format and contains the relevant details needed to decompress
        /// the data, mainly the uncompressed size of the buffer.
        /// Note: CompressionAlgorithm.None must be handled on the outside
        /// Note: Caller must Dispose of returned buffer!
        /// </summary>
        /// <param name="uncompressed">Buffer with the bytes to compress</param>
        /// <param name="algorithm">Algorithm to use for compressing the data</param>
        /// <returns>Buffer with the compressed bytes, including the header</returns>
        public static FlatIOBuffer CompressBlob(FlatIOBuffer uncompressed, CompressionAlgorithm algorithm)
        {
            uncompressed.BeginRead(); // \note only needed for the s_iron.Compress() call
            try
            {
                // Compress with IronCompress (using the specified algorithm)
                (Codec codec, CompressionLevel compressionLevel) = GetIronCompressCodec(algorithm);
                using IronCompressResult compressed = s_iron.Compress(codec, uncompressed.AsSpan(), compressionLevel: compressionLevel);

                // Write header and compressed payload to final buffer & return
                FlatIOBuffer compressedBuffer = new FlatIOBuffer(initialCapacity: MaximumHeaderSize + compressed.Length);
                using (IOWriter writer = new IOWriter(compressedBuffer))
                {
                    writer.WriteByte(MagicPrefixByte0);
                    writer.WriteByte(MagicPrefixByte1);
                    writer.WriteByte(MagicPrefixByte2);
                    writer.WriteByte(MagicPrefixByte3);
                    writer.WriteByte(FormatVersion);
                    writer.WriteByte((byte)0); // flags
                    writer.WriteByte((byte)algorithm);
                    writer.WriteVarInt((int)uncompressed.Count);
                    MetaDebug.Assert(writer.Offset <= MaximumHeaderSize, "ConservativeHeaderSize ({0} bytes) not large enough to contain header ({1} bytes)", MaximumHeaderSize, writer.Offset);
                    writer.WriteSpan(compressed.AsSpan());
                }

                return compressedBuffer;
            }
            finally
            {
                uncompressed.EndRead();
            }
        }

        public static bool IsCompressed(byte[] buffer)
        {
            // If the magic bytes don't match, assume legacy non-compressed payload
            return (buffer.Length >= 4) &&
                (buffer[0] == MagicPrefixByte0) &&
                (buffer[1] == MagicPrefixByte1) &&
                (buffer[2] == MagicPrefixByte2) &&
                (buffer[3] == MagicPrefixByte3);
        }

        public static FlatIOBuffer DecompressBlob(byte[] compressed)
        {
            // Check magic bytes
            using IOReader reader = new IOReader(compressed);
            int magic0 = reader.ReadByte();
            int magic1 = reader.ReadByte();
            int magic2 = reader.ReadByte();
            int magic3 = reader.ReadByte();
            if (magic0 != MagicPrefixByte0 || magic1 != MagicPrefixByte1 || magic2 != MagicPrefixByte2 || magic3 != MagicPrefixByte3)
                throw new InvalidOperationException($"Invalid compression magic found: {magic0:X2} {magic1:X2} {magic2:X2} {magic3:X2}");

            // Handle format version
            int formatVersion = reader.ReadByte();
            if (formatVersion != 1)
                throw new InvalidOperationException($"Unsupported compressed format version {formatVersion}");

            // Read flags (unused)
            int flags = reader.ReadByte();
            if (flags != 0)
                throw new InvalidOperationException($"Unsupported compressed format flags 0x{flags:X2}, must be zero");

            // Read rest of header
            CompressionAlgorithm algorithm = (CompressionAlgorithm)reader.ReadByte();
            int uncompressedSize = reader.ReadVarInt();

            // Decompress using IronCompress (\note deflate is not supported with the new compression)
            (Codec codec, CompressionLevel _) = GetIronCompressCodec(algorithm);
            using (IronCompressResult uncompressed = s_iron.Decompress(codec, compressed.AsSpan().Slice(reader.Offset), uncompressedSize))
            {
                //DebugLog.Info("Decompressed {UncompressedBytes} bytes from {CompressedBytes} with {Algorithm] ({CompressRatio:0.00}%)", uncompressed.Length, compressed.Length, algorithm, compressed.Length * 100.0 / uncompressed.Length);
                // \todo [petri] could avoid the copy if IronCompress allowed decompressing to an existing buffer
                return FlatIOBuffer.CopyFromSpan(uncompressed.AsSpan());
            }
        }
    }
}
