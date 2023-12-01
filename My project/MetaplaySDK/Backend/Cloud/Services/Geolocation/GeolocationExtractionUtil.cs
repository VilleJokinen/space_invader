// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using SharpCompress.Readers.Tar;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Metaplay.Cloud.Services.Geolocation
{
    /// <summary>
    /// Utilities for extracting geolocation database blobs from the blobs downloaded from MaxMind.
    /// </summary>
    internal static class GeolocationExtractionUtil
    {
        public static byte[] ExtractGeolite2CountryDatabase(byte[] sourceArchiveTarGz)
        {
            // From the origin payload (which is a .tar.gz), extract just the part that we care about (which is the .mmdb)
            return ExtractFileByNameFromTarGz(sourceArchiveTarGz, "GeoLite2-Country.mmdb", maxSize: GeolocationDatabase.PayloadMaxSize);
        }

        /// <summary>
        /// Given the .tar.gz blob <paramref name="tarGzBytes"/>, extract a file with name <paramref name="wantedFileName"/>.
        /// Note that the file may be inside a directory. <paramref name="wantedFileName"/> is just the filename and not the path.
        /// This throws if the extracted file is larger than <paramref name="maxSize"/> bytes.
        ///
        /// \todo For less trusted input, more size checks should be done on the way from the .tar.gz to the final extracted payload.
        ///       In particular, the size of the decompressed .gz should be bounded.
        /// </summary>
        public static byte[] ExtractFileByNameFromTarGz(byte[] tarGzBytes, string wantedFileName, int maxSize)
        {
            using (Stream tarGzStream = new MemoryStream(tarGzBytes))
            using (Stream tarStream = new GZipStream(tarGzStream, CompressionMode.Decompress))
            using (TarReader tarReader = TarReader.Open(tarStream))
            {
                while (tarReader.MoveToNextEntry())
                {
                    if (tarReader.Entry.IsDirectory)
                        continue;

                    string entryFileName = tarReader.Entry.Key.Split("/").Last();

                    if (entryFileName == wantedFileName)
                    {
                        if (tarReader.Entry.Size > maxSize)
                            throw new InvalidOperationException($"Archive entry has size {tarReader.Entry.Size}, given maximum is {maxSize}");
                        if (tarReader.Entry.Size < 0)
                            throw new InvalidOperationException($"Archive entry has negative size {tarReader.Entry.Size}");
                        int entrySize = (int)tarReader.Entry.Size;

                        using (Stream underlyingEntryStream = tarReader.OpenEntryStream())
                        using (Stream entryStream = new LimitedReadStream(underlyingEntryStream, limit: entrySize))
                        using (MemoryStream outStream = new MemoryStream(capacity: entrySize))
                        {
                            entryStream.CopyTo(outStream);

                            byte[] entryContent = outStream.ToArray();
                            if (entryContent.Length != entrySize)
                                throw new InvalidOperationException($"Archive entry has declared size {entrySize}, but read {entryContent.Length} bytes");

                            return entryContent;
                        }
                    }
                }

                throw new InvalidOperationException($"Archive does not contain {wantedFileName}");
            }
        }
    }
}
