// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Serialization;
using System;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services.Geolocation
{
    /// <summary>
    /// Represents persistent storage (e.g. S3) for a replica of a geolocation database.
    /// Purpose of the replica is to avoid having to download from MaxMind's servers more often than necessary.
    /// Implemented using a blob storage; two items are stored:
    /// - A <see cref="GeolocationDatabase"/> (serialized with <see cref="GeolocationDatabase.Serialize"/>).
    /// - A <see cref="GeolocationDatabaseMetadata"/> (MetaSerialized),
    ///   stored redundantly/separately, so that the metadata can be quickly accessed
    ///   without downloading the whole database.
    /// </summary>
    internal class GeolocationReplicaStorage : IGeolocationUpdateDestination, IGeolocationUpdateSource
    {
        IBlobStorage _blobStorage;

        public GeolocationReplicaStorage(IBlobStorage blobStorage)
        {
            _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        }

        const string DatabaseStorageBlobName = "GeoLite2";
        const string MetadataStorageBlobName = "GeoLite2Metadata";

        #region IGeolocationUpdateDestination and IGeolocationUpdateSource

        /// <summary>
        /// Store the given database (including its contained metadata) in one blob,
        /// and also store its metadata in a separate blob for faster access.
        ///
        /// Note that the two blobs are *not* stored in one atomic operation, so
        /// they can get out of sync. The database blob is stored before the
        /// metadata blob, so the metadata may be older than the database (but
        /// not the other way around).
        /// Please see the comment on <see cref="IGeolocationUpdateSource"/>
        /// for an explanation of this kind of out-of-sync.
        /// </summary>
        public async Task StoreDatabaseAsync(GeolocationOptions options, GeolocationDatabase database)
        {
            byte[] serializedDatabase = database.Serialize();
            byte[] serializedMetadata = MetaSerialization.SerializeTagged<GeolocationDatabaseMetadata>(database.Metadata, MetaSerializationFlags.Persisted, logicVersion: null);

            await _blobStorage.PutAsync(DatabaseStorageBlobName, serializedDatabase).ConfigureAwait(false);
            await _blobStorage.PutAsync(MetadataStorageBlobName, serializedMetadata).ConfigureAwait(false);
        }

        /// <summary>
        /// Do nothing.
        /// In particular, disabling geolocation doesn't remove an existing replica.
        /// </summary>
        public Task OnGeolocationDisabledAsync(GeolocationOptions options)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Get the database from the storage, or null if none.
        /// </summary>
        public async Task<GeolocationDatabase?> TryFetchDatabaseAsync(GeolocationOptions options)
        {
            byte[] serializedDatabase = await _blobStorage.GetAsync(DatabaseStorageBlobName).ConfigureAwait(false);
            if (serializedDatabase == null)
                return null;

            return GeolocationDatabase.Deserialize(serializedDatabase);
        }

        /// <summary>
        /// Get the database metadata from the storage, or null if none.
        /// </summary>
        /// <remarks>
        /// The metadata is stored as a blob separate from the database blob,
        /// and thus it's possible for those to go out of sync if storing
        /// the metadata fails.
        /// <see cref="StoreDatabaseAsync(GeolocationOptions, GeolocationDatabase)"/> stores
        /// the database blob before the metadata blob, so that the database
        /// will never be older than the metadata.
        /// </remarks>
        public async Task<GeolocationDatabaseMetadata?> TryFetchMetadataAsync(GeolocationOptions options)
        {
            byte[] serializedMetadata = await _blobStorage.GetAsync(MetadataStorageBlobName).ConfigureAwait(false);
            if (serializedMetadata == null)
                return null;

            return MetaSerialization.DeserializeTagged<GeolocationDatabaseMetadata>(serializedMetadata, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);
        }

        #endregion
    }
}
