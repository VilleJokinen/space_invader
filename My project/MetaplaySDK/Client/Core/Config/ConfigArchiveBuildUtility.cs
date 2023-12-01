// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#define UNITY_WEBGL_BUILD
#endif

#if UNITY_2017_1_OR_NEWER && !UNITY_WEBGL_BUILD
#   define HAS_SYNCHRONOUS_IO
#endif

using Metaplay.Core.IO;
using Metaplay.Core.Math;
using Metaplay.Core.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Core.Config
{
    public static partial class ConfigArchiveBuildUtility
    {
        public const int    FileSchemaVersion    = 5;
        public const uint   FileHeaderMagic      = ((uint)'M' << 24) | ((uint)'C' << 16) | ((uint)'A' << 8) | ((uint)'!'); // "MCA!" (Metaplay Config Archive)
        public const int    MaxEntryNameLength   = 1024;
        public const string IndexFileHeaderMagic = "MetaplayArchive";

        /// <summary>
        /// Converts the <paramref name="archive"/> to a compressed byte array.
        /// If you are creating a nested ConfigArchive, we recommend that you disable compression (using <paramref name="compression"/> for the inner archives.
        /// </summary>
        public static byte[] ToBytes(ConfigArchive archive, CompressionAlgorithm compression = CompressionAlgorithm.Deflate, int minimumSizeForCompression = 32)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));

            (ContentHash hash, byte[] bytes) data = ToBytes(archive.CreatedAt, archive.Entries, compression, minimumSizeForCompression);

            if(compression != CompressionAlgorithm.None)
                MetaDebug.Assert(data.hash == archive.Version, "Archive content hash changed between creation and byte conversion, indicating that the underlying data was changed!");

            return data.bytes;
        }

        public static Task WriteToFileAsync(string path, ConfigArchive archive)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            byte[] bytes = ToBytes(archive);
            return FileUtil.WriteAllBytesAsync(path, bytes);
        }

#if HAS_SYNCHRONOUS_IO
        public static void WriteToFile(string path, ConfigArchive archive)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            byte[] bytes = ToBytes(archive);
            FileUtil.WriteAllBytes(path, bytes);
        }
#endif

        /// <summary>
        /// Converts the <paramref name="entries"/> to a compressed byte array.
        /// We recommend that you disable compression (using <paramref name="compression"/> if you are creating a nested ConfigArchive.
        /// </summary>
        internal static (ContentHash hash, byte[] bytes) ToBytes(MetaTime timestamp, IEnumerable<ConfigArchiveEntry> entries, CompressionAlgorithm compression = CompressionAlgorithm.Deflate, int minimumSizeForCompression = 32)
        {
            // Sort entries by name
            List<ConfigArchiveEntry> orderedEntries = entries.OrderBy(entry => entry.Name, StringComparer.InvariantCulture).ToList();
            ContentHash              version = ComputeVersionHashForEntries(orderedEntries.Select(x => (x.Name, x.Hash)));

            // Setup buffer & writer
            using (FlatIOBuffer buffer = new FlatIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    // Write header
                    writer.WriteUInt32(FileHeaderMagic);
                    writer.WriteInt32(FileSchemaVersion);
                    writer.WriteUInt128(version.Value); // added in schema version 4
                    writer.WriteInt64(timestamp.MillisecondsSinceEpoch);
                    writer.WriteInt32(orderedEntries.Count);

                    // Write entry headers (and calculate offsets)
                    List<ReadOnlyMemory<byte>> payloads = new List<ReadOnlyMemory<byte>>();
                    foreach (ConfigArchiveEntry entry in orderedEntries)
                    {
                        writer.WriteString(entry.Name);
                        writer.WriteUInt128(entry.Hash.Value);

                        ReadOnlyMemory<byte>   payload          = entry.Bytes;
                        CompressionAlgorithm entryCompression = CompressionAlgorithm.None;
                        if (compression != CompressionAlgorithm.None && payload.Length > minimumSizeForCompression)
                        {
                            if (compression != CompressionAlgorithm.Deflate)
                                throw new NotSupportedException($"Support for compression algorithm {compression} not implemented!");

                            byte[] compressedContent = CompressUtil.DeflateCompress(payload.Span);
                            if (compressedContent.Length < payload.Length)
                            {
                                payload          = new Memory<byte>(compressedContent);
                                entryCompression = CompressionAlgorithm.Deflate;
                            }
                        }

                        writer.WriteUInt32((uint)entryCompression);
                        writer.WriteInt32(payload.Length);
                        payloads.Add(payload);
                    }

                    // Write entry payloads.
                    // Note: there an extra copy here that could be avoided. Rather than writing the payloads using IOWriter
                    // and then doing the final copy in IOBuffer.ToArray we can just write the payloads to the output array directly.

                    foreach (ReadOnlyMemory<byte> payload in payloads)
                        writer.WriteSpan(payload.Span);
                }

                return (version, buffer.ToArray());
            }
        }

        public static ContentHash ComputeVersionHashForEntries(IEnumerable<ConfigArchiveEntry> entries)
        {
            return ComputeVersionHashForEntries(entries.Select(x => (x.Name, x.Hash)));
        }

        public static ContentHash ComputeVersionHashForEntries(IEnumerable<(string, ContentHash)> entries)
        {
            MetaUInt128 currentHash = MetaUInt128.Zero;
            foreach ((string Name, ContentHash Hash) entry in entries)
                currentHash = ModifyHash(currentHash, entry.Name, entry.Hash);
            // Hash of MetaUInt128.Zero is not acceptable, as that is interpreted as ContentHash.None. This would
            // be the case for a config archive with 0 entries.
            if (currentHash == MetaUInt128.Zero)
                currentHash = MetaUInt128.One;
            return new ContentHash(currentHash);
        }

        /// <summary>
        /// Checks the version (checksum) of the archive is correct. Throws on failure.
        /// </summary>
        public static void TestArchiveVersion(string archiveName, ConfigArchive archive)
        {
            // Check the content hash is valid
            ContentHash computedHash = ComputeVersionHashForEntries(archive.Entries.Select(e => (e.Name, e.Hash)));
            if (archive.Version != computedHash)
                throw new InvalidOperationException($"{archiveName} has invalid Version. Version was marked as {archive.Version} but version recomputation resulted in {computedHash} -- the archive contents are likely inconsistent with the hash");
        }

        static MetaUInt128 ModifyHash(MetaUInt128 current, string name, ContentHash hash)
        {
            current.Low  *= 7919;
            current.High *= 1797;
            current      ^= ContentHash.ComputeFromBytes(Encoding.UTF8.GetBytes(name)).Value;
            current      ^= hash.Value;
            return current;
        }

        public static (int, ContentHash, MetaTime, int) ReadArchiveHeader(IOReader reader)
        {
            uint magic = reader.ReadUInt32();
            if (magic != FileHeaderMagic)
            {
                if (magic == 0x76657273)
                    throw new SerializationException($"Invalid ConfigArchive file: unexpected magic 0x{magic:x8}. That's ASCII for \"vers\" - maybe a Git LFS pointer file?");
                else
                    throw new SerializationException($"Invalid ConfigArchive file: unexpected magic 0x{magic:x8}");
            }
            int schemaVersion = reader.ReadInt32();
            if (schemaVersion > FileSchemaVersion)
                throw new SerializationException($"ConfigArchive version ({schemaVersion}) is too new, latest supported is version {FileSchemaVersion}");
            if (schemaVersion < 2)
                throw new SerializationException($"Unsupported ConfigArchive version {schemaVersion}, oldest supported version is 2");
            ContentHash archiveVersion = (schemaVersion >= 4) ? new ContentHash(reader.ReadUInt128()) : ContentHash.None;
            MetaTime    timestamp      = (schemaVersion >= 3) ? MetaTime.FromMillisecondsSinceEpoch(reader.ReadInt64()) : MetaTime.Epoch;
            int         numEntries     = reader.ReadInt32();
            return (schemaVersion, archiveVersion, timestamp, numEntries);
        }

        internal static (string, ContentHash, CompressionAlgorithm, int) ReadEntryHeader(IOReader reader, int schemaVersion)
        {
            string               entryName = reader.ReadString(MaxEntryNameLength);
            ContentHash          entryHash = new ContentHash(reader.ReadUInt128());
            CompressionAlgorithm entryCompression;

            if (schemaVersion <= 4)
            {
                // flags, unused
                _ = reader.ReadUInt32();

                entryCompression = CompressionAlgorithm.None;
            }
            else
            {
                entryCompression = (CompressionAlgorithm)reader.ReadUInt32();
            }

            int entryLength = reader.ReadInt32();

            if (!CompressUtil.IsSupportedForDecompression(entryCompression))
                throw new SerializationException($"Unsupported compression format {(uint)entryCompression}");

            return (entryName, entryHash, entryCompression, entryLength);
        }

        #if !UNITY_WEBGL_BUILD

        #pragma warning restore MP_WGL_00
        #endif // !UNITY_WEBGL_BUILD
    }
}
