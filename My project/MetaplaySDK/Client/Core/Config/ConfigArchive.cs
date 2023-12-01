// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

using Metaplay.Core.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Core.Config
{
    public class ConfigArchiveEntry
    {
        public string             Name  { get; }
        public ContentHash        Hash  { get; }
        public ReadOnlyMemory<byte> Bytes { get; }

        public ConfigArchiveEntry(string name, ContentHash hash, ReadOnlyMemory<byte> bytes)
        {
            Name  = name ?? throw new ArgumentNullException(nameof(name));
            Hash  = hash;
            Bytes = bytes;
        }

        public static ConfigArchiveEntry FromBlob(string name, byte[] bytes)
        {
            ContentHash hash = ContentHash.ComputeFromBytes(bytes);
            return new ConfigArchiveEntry(name, hash, bytes);
        }

        public override int GetHashCode() => Hash.GetHashCode();

        public override string ToString() => $"{Name}#{Hash}";
    }

    /// <summary>
    /// A config archive is a collection of <see cref="ConfigArchiveEntry"/> and provides a read-only view over the entries,
    /// these entries are nested <see cref="ConfigArchive"/>s, or MetaSerialized blobs of config libraries.
    /// Upon load, entries are decompressed if necessary.
    /// </summary>
    public class ConfigArchive
    {
        readonly Dictionary<string, ConfigArchiveEntry> _entries;
        public   ContentHash                             Version   { get; }
        public   MetaTime                                CreatedAt { get; }
        public IReadOnlyList<ConfigArchiveEntry> Entries { get; }

        public ConfigArchive(
            MetaTime createdAt,
            IEnumerable<ConfigArchiveEntry> entries)
        {
            CreatedAt = createdAt;
            Entries   = entries.OrderBy(x => x.Name, StringComparer.InvariantCulture).ToList();
            _entries  = Entries.ToDictionary(x=>x.Name);
            Version   = ConfigArchiveBuildUtility.ComputeVersionHashForEntries(Entries);
        }

        public ConfigArchive(ContentHash version, MetaTime createdAt, IEnumerable<ConfigArchiveEntry> entries)
        {
            Version   = version;
            CreatedAt = createdAt;
            Entries   = entries.OrderBy(x => x.Name, StringComparer.InvariantCulture).ToList();
            _entries  = Entries.ToDictionary(x=>x.Name);
        }

        public IOReader ReadEntry(string name)
        {
            ReadOnlyMemory<byte> entry = GetEntryBytes(name);
            return new IOReader(entry);
        }

        public Stream ReadEntryStream(string name)
        {
            return ReadEntry(name).ConvertToStream();
        }

        public ReadOnlyMemory<byte> GetEntryBytes(string name)
        {
            return _entries[name].Bytes;
        }

        public bool ContainsEntryWithName(string name)
        {
            return _entries.ContainsKey(name);
        }

        public ConfigArchiveEntry GetEntryByName(string name)
        {
            if (!_entries.TryGetValue(name, out ConfigArchiveEntry entry))
                throw new KeyNotFoundException($"Entry '{name}' not found in ConfigArchive");
            return entry;
        }

        /// Creates a read-only ConfigArchive view over the serialized representation. The contents of <paramref name="bytes"/> are
        /// NOT COPIED and must not be modified as long as the loaded ConfigArchive is in use.
        public static ConfigArchive FromBytes(byte[] bytes)
        {
            return Load(new ReadOnlyMemory<byte>(bytes));
        }

        /// Creates a read-only ConfigArchive view over the serialized representation. The contents of <paramref name="entry"/> are
        /// NOT COPIED and must not be modified as long as the loaded ConfigArchive is in use.
        public static ConfigArchive FromBytes(ReadOnlyMemory<byte> entry, List<string> libraryFilters = null)
        {
            return Load(entry, libraryFilters);
        }

        public static async Task<ConfigArchive> FromFileAsync(string fileName)
        {
            byte[] bytes = await FileUtil.ReadAllBytesAsync(fileName);
            return FromBytes(bytes);
        }

#if !UNITY_WEBGL_BUILD
        public static ConfigArchive FromFile(string fileName)
        {
            byte[] bytes = FileUtil.ReadAllBytes(fileName);
            return FromBytes(bytes);
        }
#endif

        static ConfigArchive Load(ReadOnlyMemory<byte> bytes, List<string> libraryFilters = null)
        {
            using (IOReader reader = new IOReader(bytes))
            {
                (int schemaVersion, ContentHash archiveVersion, MetaTime timestamp, int numEntries) = ConfigArchiveBuildUtility.ReadArchiveHeader(reader);

                // Read entry headers
                List<(string entryName, ContentHash entryHash, CompressionAlgorithm compression, int length)> headers = new List<(string, ContentHash, CompressionAlgorithm, int)>(numEntries);
                for (int ndx = 0; ndx < numEntries; ndx++)
                    headers.Add(ConfigArchiveBuildUtility.ReadEntryHeader(reader, schemaVersion));

                if (schemaVersion < 4)
                    archiveVersion = ConfigArchiveBuildUtility.ComputeVersionHashForEntries(headers.Select(e => (e.entryName, e.entryHash)));

                // Read entry payloads
                List<ConfigArchiveEntry> entries = new List<ConfigArchiveEntry>();
                for (int ndx = 0; ndx < numEntries; ndx++)
                {
                    var entry = headers[ndx];

                    ReadOnlyMemory<byte> entryBuf = bytes.Slice(reader.Offset, entry.length);

                    reader.SkipBytes(entry.length);

                    if (libraryFilters != null && !libraryFilters.Any(x => entry.entryName.StartsWith(x, StringComparison.InvariantCultureIgnoreCase)))
                        continue;

                    if (entry.compression != CompressionAlgorithm.None)
                    {
                        if (entry.compression == CompressionAlgorithm.Deflate)
                            entryBuf = CompressUtil.DeflateDecompress(entryBuf);
                        else
                            throw new NotSupportedException($"Unsupported compression method {entry.compression}");
                    }

                    entries.Add(new ConfigArchiveEntry(entry.entryName, entry.entryHash, entryBuf));
                }

                return new ConfigArchive(archiveVersion, timestamp, entries);
            }
        }
    }
}
