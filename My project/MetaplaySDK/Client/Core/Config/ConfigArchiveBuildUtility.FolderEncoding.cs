// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#define UNITY_WEBGL_BUILD
#endif

#if !UNITY_WEBGL_BUILD
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL" (regarding blocking file IO). False positive, this is non-WebGL.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Core.Config
{
    public static partial class ConfigArchiveBuildUtility
    {
        /// <summary>
        /// Tools for handling Folder-Encoded ConfigArchives. A folder-encoded
        /// config archive is folder on a file system, where each entry is written
        /// to a separate file. Additionally, an Index file is used to track metadata.
        /// </summary>
        public static class FolderEncoding
        {
            public struct DirectoryIndex
            {
                public struct Entry
                {
                    public readonly string      Path;
                    public readonly ContentHash Version;

                    public Entry(string path, ContentHash version)
                    {
                        Path    = path;
                        Version = version;
                    }
                }

                public readonly ContentHash Version;
                public readonly MetaTime    Timestamp;
                public readonly List<Entry> FileEntries;

                public DirectoryIndex(ContentHash version, MetaTime timestamp, List<Entry> fileEntries)
                {
                    Version     = version;
                    Timestamp   = timestamp;
                    FileEntries = fileEntries;
                }
            }

            public static void WriteToDirectory(ConfigArchive archive, string dirName)
            {
                // Make sure directory exists
                if (!File.Exists(dirName))
                    Directory.CreateDirectory(dirName);

                // \todo [petri] delete existing files?

                // Write out all files
                foreach (ConfigArchiveEntry entry in archive.Entries)
                {
                    string filePath = Path.Combine(dirName, entry.Name);
                    using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                        fileStream.Write(entry.Bytes.Span);
                }

                // Write Index.txt
                string fileIndex =
                    "MetaplayArchive " + archive.Version.ToString() + " " + archive.CreatedAt.MillisecondsSinceEpoch.ToString(CultureInfo.InvariantCulture) + "\n" +
                    string.Join("", archive.Entries.Select(entry => entry.Name + " " + entry.Hash.ToString() + "\n"));
                File.WriteAllText(Path.Combine(dirName, "Index.txt"), fileIndex);
            }

            public static ConfigArchive FromDirectory(string dirName)
            {
                DirectoryIndex index = ReadDirectoryIndex(dirName);

                List<ConfigArchiveEntry> entries = new List<ConfigArchiveEntry>();
                foreach (DirectoryIndex.Entry fileEntry in index.FileEntries)
                {
                    string name  = Path.GetFileName(fileEntry.Path);
                    byte[] bytes = FileUtil.ReadAllBytes(fileEntry.Path);
                    entries.Add(new ConfigArchiveEntry(name, fileEntry.Version, new Memory<byte>(bytes)));
                }

                return new ConfigArchive(index.Version, index.Timestamp, entries);
            }

            public static async Task<ConfigArchive> FromDirectoryAsync(string dirName)
            {
                DirectoryIndex index = await ReadDirectoryIndexAsync(dirName);

                List<ConfigArchiveEntry> entries = new List<ConfigArchiveEntry>();
                foreach (DirectoryIndex.Entry fileEntry in index.FileEntries)
                {
                    string name  = Path.GetFileName(fileEntry.Path);
                    byte[] bytes = await FileUtil.ReadAllBytesAsync(fileEntry.Path);
                    entries.Add(new ConfigArchiveEntry(name, fileEntry.Version, new Memory<byte>(bytes)));
                }

                return new ConfigArchive(index.Version, index.Timestamp, entries);
            }

            static DirectoryIndex ReadDirectoryIndexFile(string indexFilePath)
            {
                // Read Index.txt (with metadata and entry names)
                string   dirPath     = Path.GetDirectoryName(indexFilePath);
                string   archiveName = Path.GetFileName(dirPath);
                string[] indexFile   = FileUtil.ReadAllLines(indexFilePath);
                return ParseDirectoryIndexFile(dirPath, archiveName, indexFile);
            }

            static async Task<DirectoryIndex> ReadDirectoryIndexFileAsync(string indexFilePath)
            {
                // Read Index.txt (with metadata and entry names)
                string   dirPath     = Path.GetDirectoryName(indexFilePath);
                string   archiveName = Path.GetFileName(dirPath);
                string[] indexFile   = await FileUtil.ReadAllLinesAsync(indexFilePath);
                return ParseDirectoryIndexFile(dirPath, archiveName, indexFile);
            }

            static DirectoryIndex ParseDirectoryIndexFile(string dirPath, string archiveName, string[] indexFile)
            {
                // Parse header line: version hash & timestamp
                if (indexFile.Length == 0)
                    throw new ParseError($"Archive index file is empty {archiveName}/Index.txt");
                string   header      = indexFile[0];
                string[] headerParts = header.Split(' ');
                if (headerParts.Length != 3)
                    throw new ParseError($"Invalid archive {archiveName}/Index.txt header prefix");
                if (headerParts[0] != IndexFileHeaderMagic)
                    throw new ParseError($"Invalid archive {archiveName}/Index.txt header prefix");

                ContentHash version   = ContentHash.ParseString(headerParts[1]);
                MetaTime    timestamp = MetaTime.FromMillisecondsSinceEpoch(long.Parse(headerParts[2], CultureInfo.InvariantCulture));

                // Parse all entries
                List<DirectoryIndex.Entry> entries = new List<DirectoryIndex.Entry>();
                foreach (string fileEntry in indexFile.Skip(1))
                {
                    // Skip empty lines
                    if (string.IsNullOrEmpty(fileEntry.Trim()))
                        continue;

                    // Parse entry row: <fileName> <fileHash>
                    string[] parts = fileEntry.Split(' ');
                    if (parts.Length != 2)
                        throw new ParseError($"Invalid row in archive {archiveName}/Index.txt: '{fileEntry}'");
                    string      fileName = parts[0];
                    ContentHash hash     = ContentHash.ParseString(parts[1]);

                    // Read file and store entry
                    entries.Add(new DirectoryIndex.Entry(Path.Combine(dirPath, fileName), hash));
                }

                return new DirectoryIndex(version, timestamp, entries);
            }

            public static DirectoryIndex ReadDirectoryIndex(string directoryPath)
            {
                return ReadDirectoryIndexFile(Path.Combine(directoryPath, "Index.txt"));
            }

            public static Task<DirectoryIndex> ReadDirectoryIndexAsync(string directoryPath)
            {
                return ReadDirectoryIndexFileAsync(Path.Combine(directoryPath, "Index.txt"));
            }
        }
    }
}

#pragma warning restore MP_WGL_00
#endif // !UNITY_WEBGL_BUILD
