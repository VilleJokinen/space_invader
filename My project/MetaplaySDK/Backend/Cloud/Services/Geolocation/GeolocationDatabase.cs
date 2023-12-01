// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using MaxMind.GeoIP2;
using Metaplay.Core;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Metaplay.Cloud.Services.Geolocation
{
    /// <summary>
    /// Some metadata about a database that we care about
    /// when we're checking for database updates.
    /// </summary>
    [MetaSerializable]
    public struct GeolocationDatabaseMetadata
    {
        /// <summary>
        /// Database build date/time (as in the HTTP Last-Modified header in MaxMind's geoip download).
        /// </summary>
        /// <remarks>
        /// This is specifically from the Last-Modified header.
        /// This is *not* exactly the same as <see cref="MaxMind.Db.Metadata.BuildDate"/>.
        /// </remarks>
        [MetaMember(1)] public MetaTime BuildDate { get; private set; }

        public GeolocationDatabaseMetadata(MetaTime buildDate)
        {
            BuildDate = buildDate;
        }
    }

    /// <summary>
    /// Holds a geolocation database payload and metadata.
    /// <see cref="Payload"/> contains the actual database
    /// (in practice, a GeoLite2 database in MaxMind's .mmdb format).
    /// </summary>
    /// <remarks>
    /// This type itself is not MetaSerialized, but instead has
    /// ad-hoc (de)serialization.
    /// <see cref="Payload"/> can be relatively large (megabytes)
    /// and MetaSerialization has some limits on byte arrays etc.
    /// \todo [nuutti] Make MetaSerialization limits customizable
    ///                per type or per use case?
    /// </remarks>
    internal struct GeolocationDatabase
    {
        public const int PayloadMaxSize = 50 * 1024 * 1024; // 50 MB, should be plenty. It was some 4 MB on 2021-01-22.

        public readonly GeolocationDatabaseMetadata Metadata;
        /// <summary> The contents of the .mmdb file. </summary>
        public readonly byte[]                      Payload;

        public GeolocationDatabase(GeolocationDatabaseMetadata metadata, byte[] payload)
        {
            Metadata = metadata;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public DatabaseReader CreateMaxMindDatabaseReader()
        {
            using (MemoryStream databaseStream = new MemoryStream(Payload))
                return new DatabaseReader(databaseStream);
        }

        #region Serialization

        const int CurrentSchemaVersion = 1;

        // Serialized format:
        // - Schema version (VarInt)
        // - Metadata in MetaSerialization tagged format (ByteString)
        // - Payload, deflate-compressed (ByteString)

        const int           SerializedMetadataMaxSize   = 1024; // Metadata should be quite small.
        public const int    CompressedPayloadMaxSize    = PayloadMaxSize;

        public byte[] Serialize()
        {
            byte[] serializedMetadata   = MetaSerialization.SerializeTagged<GeolocationDatabaseMetadata>(Metadata, MetaSerializationFlags.Persisted, logicVersion: null);
            byte[] compressedPayload    = CompressUtil.DeflateCompress(Payload);

            using (FlatIOBuffer buffer = new FlatIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    SpanWriter s = writer.GetSpanWriter();
                    s.WriteVarInt(CurrentSchemaVersion);
                    s.WriteByteString(serializedMetadata);
                    s.WriteByteString(compressedPayload);
                    writer.ReleaseSpanWriter(ref s);
                }

                return buffer.ToArray();
            }
        }

        public static GeolocationDatabase Deserialize(byte[] serialized)
        {
            byte[] serializedMetadata;
            byte[] compressedPayload;

            using (IOReader reader = new IOReader(serialized))
            {
                int schemaVersion = reader.ReadVarInt();
                if (schemaVersion != CurrentSchemaVersion)
                    throw new MetaSerializationException($"Unsupported {nameof(GeolocationDatabase)} schema version {schemaVersion}");

                serializedMetadata  = reader.ReadByteString(SerializedMetadataMaxSize);
                compressedPayload   = reader.ReadByteString(CompressedPayloadMaxSize);
            }

            GeolocationDatabaseMetadata metadata    = MetaSerialization.DeserializeTagged<GeolocationDatabaseMetadata>(serializedMetadata, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);
            byte[]                      payload     = CompressUtil.DeflateDecompress(compressedPayload);

            return new GeolocationDatabase(metadata, payload);
        }

        #endregion
    }
}
