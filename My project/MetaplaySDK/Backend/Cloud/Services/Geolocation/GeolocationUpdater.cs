// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services.Geolocation
{
    /// <summary>
    /// Periodically (in the background) polls a <see cref="IGeolocationUpdateSource"/>
    /// for geolocation database updates and stores the updated databases in a
    /// <see cref="IGeolocationUpdateDestination"/>.
    /// </summary>
    internal class GeolocationUpdater
    {
        IMetaLogger                     _logger;
        IGeolocationUpdateSource        _source;
        IGeolocationUpdateDestination   _destination;
        TimeSpan                        _updateCheckInterval;

        DateTime?                       _lastUpdateCheckAt;

        /// <summary>
        /// Construct and start the updater.
        /// </summary>
        public GeolocationUpdater(IMetaLogger logger, IGeolocationUpdateSource source, IGeolocationUpdateDestination destination, TimeSpan updateCheckInterval)
        {
            if (updateCheckInterval < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(updateCheckInterval), "Update check interval cannot be negative");

            _logger                 = logger ?? throw new ArgumentNullException(nameof(logger));
            _source                 = source ?? throw new ArgumentNullException(nameof(source));
            _destination            = destination ?? throw new ArgumentNullException(nameof(destination));
            _updateCheckInterval    = updateCheckInterval;

            // \note No cancellation implemented for now. After initialization, geolocation system remains active until app shuts down.
            // \todo [nuutti] Could implement cancellation anyway. Would be more proper in situations
            //                where a static Geolocation singleton isn't desired, like maybe in tests.
            _ = Task.Run(MainLoopAsync);
        }

        static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

        async Task MainLoopAsync()
        {
            while (true)
            {
                // \note Keep ticking even on errors.
                try
                {
                    await TickAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error("Error in tick: {Error}", ex);
                }

                await Task.Delay(TickInterval).ConfigureAwait(false);
            }
        }

        async Task TickAsync()
        {
            GeolocationOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<GeolocationOptions>();

            if (!options.Enabled)
            {
                await _destination.OnGeolocationDisabledAsync(options).ConfigureAwait(false);
                return;
            }

            // Check if there's already a database in the destination (by checking its metadata),
            // and act accordingly.

            GeolocationDatabaseMetadata? destinationMetadataMaybe = await _destination.TryFetchMetadataAsync(options).ConfigureAwait(false);

            if (!destinationMetadataMaybe.HasValue)
            {
                // No database yet in destination.
                // Try to get it from source.

                GeolocationDatabase? sourceDatabaseMaybe = await _source.TryFetchDatabaseAsync(options).ConfigureAwait(false);
                if (sourceDatabaseMaybe.HasValue)
                {
                    GeolocationDatabase sourceDatabase = sourceDatabaseMaybe.Value;
                    await _destination.StoreDatabaseAsync(options, sourceDatabase).ConfigureAwait(false);
                    _logger.Information("Installed initial database (build date: {BuildDate})", sourceDatabase.Metadata.BuildDate);

                    _lastUpdateCheckAt = DateTime.Now; // Just got the initial database, so don't do the update check until _updateCheckInterval from now.
                }
            }
            else
            {
                // There's a database currently in destination.
                // Occasionally check if there's an update in source, and if so, fetch it and store to destination.

                GeolocationDatabaseMetadata destinationMetadata = destinationMetadataMaybe.Value;

                DateTime currentTime = DateTime.Now;

                if (!_lastUpdateCheckAt.HasValue || currentTime >= _lastUpdateCheckAt.Value + _updateCheckInterval)
                {
                    _lastUpdateCheckAt = currentTime;

                    GeolocationDatabaseMetadata? sourceMetadataMaybe = await _source.TryFetchMetadataAsync(options).ConfigureAwait(false);
                    if (!sourceMetadataMaybe.HasValue)
                        return;

                    GeolocationDatabaseMetadata sourceMetadata = sourceMetadataMaybe.Value;

                    if (sourceMetadata.BuildDate > destinationMetadata.BuildDate)
                    {
                        GeolocationDatabase sourceDatabase = await _source.TryFetchDatabaseAsync(options).ConfigureAwait(false)
                                                             ?? throw new InvalidOperationException("Geolocation database metadata was found in source, but database itself wasn't");

                        await _destination.StoreDatabaseAsync(options, sourceDatabase).ConfigureAwait(false);
                        _logger.Information("Installed updated database (old={OldBuildDate}, new={NewBuildDate})", destinationMetadata.BuildDate, sourceDatabase.Metadata.BuildDate);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Represents a source of geolocation database updates.
    /// Supports fetching a database, as well as fetching the
    /// metadata of the current database (by <see cref="IGeolocationDatabaseMetadataProvider"/>).
    /// In practice, we have two different types of source:
    /// - MaxMind's servers (<see cref="GeolocationUpdateSourceMaxMind"/>)
    /// - Replica storage, e.g. S3 (<see cref="GeolocationReplicaStorage"/>)
    ///
    /// Caveat:
    ///
    /// Due to technical limitations in certain implementations of this
    /// interface (namely, <see cref="GeolocationReplicaStorage"/>), the
    /// database returned by <see cref="TryFetchDatabaseAsync"/>
    /// and the metadata returned by <see cref="IGeolocationDatabaseMetadataProvider.TryFetchMetadataAsync"/>
    /// can rarely be out-of-sync such that the metadata is older than
    /// the database.
    ///
    /// Thus, users should *not* assume that the metadata is up-to-date
    /// with the database.
    ///
    /// Users *can* assume that the database is never older than the metadata.
    /// (And implementers should take care that this is the case.)
    ///
    /// This flavor of (rare/unlikely) out-of-sync is probably acceptable for
    /// the geolocation use-case. Explanation:
    ///
    /// Because <see cref="GeolocationUpdater"/> checks the availability of
    /// an update by polling the metadata from the update source, this kind
    /// of potential out-of-sync implies that an updater might simply end up
    /// not noticing that there's an update.
    ///
    /// The opposite cannot happen; i.e. an updater cannot end up thinking
    /// that there's an update when there actually isn't.
    ///
    /// In effect, this means that the active in-memory geolocation databases
    /// might get updated with more delay than ideal (in the event of such an
    /// incomplete write to <see cref="GeolocationReplicaStorage"/>, another
    /// update from MaxMind will be attempted again after
    /// <see cref="Geolocation.OriginUpdateCheckInterval"/>).
    /// This is not fatal, as there is no strict requirement on the update delay.
    /// Note that for use-cases other than geolocation, this might not be
    /// acceptable.
    ///
    /// \todo [nuutti] Even if the out-of-sync is practically harmless in the
    ///                current use-case, it:
    ///                - is not very clean
    ///                - requires the above explanation
    ///                - might be a footgun if the code is modified (or
    ///                  adapted to different use-cases) in ways that
    ///                  don't expect the out-of-sync
    ///                There are probably alternative designs where such
    ///                out-of-sync cannot happen, such as having a
    ///                "last-modified" value on IBlobStorage blobs.
    /// </summary>
    internal interface IGeolocationUpdateSource : IGeolocationDatabaseMetadataProvider
    {
        /// <summary> Get the current database, if any. </summary>
        public Task<GeolocationDatabase?> TryFetchDatabaseAsync(GeolocationOptions options);
    }

    /// <summary>
    /// Represents a destination of geolocation database updates.
    /// Supports storing a database, as well as fetching the
    /// metadata of the current database (by <see cref="IGeolocationDatabaseMetadataProvider"/>),
    /// so that the metadata can be compared with that in the source of the update.
    /// In practice, we have two different types of destination:
    /// - Replica storage, e.g. S3 (<see cref="GeolocationReplicaStorage"/>)
    /// - Resident storage, i.e. currently active in-memory database (<see cref="GeolocationResidentStorage"/>)
    /// </summary>
    internal interface IGeolocationUpdateDestination : IGeolocationDatabaseMetadataProvider
    {
        /// <summary> Assign the given database as the current database. </summary>
        public Task StoreDatabaseAsync          (GeolocationOptions options, GeolocationDatabase database);
        /// <summary> Geolocation is disabled; do any appropriate actions. </summary>
        /// <remarks> This may be called repeatedly and should be idempotent. </remarks>
        public Task OnGeolocationDisabledAsync  (GeolocationOptions options);
    }

    /// <summary>
    /// Common part of both <see cref="IGeolocationUpdateSource"/> and <see cref="IGeolocationUpdateDestination"/>.
    /// </summary>
    internal interface IGeolocationDatabaseMetadataProvider
    {
        /// <summary> Get the metadata of the current database, if any. </summary>
        public Task<GeolocationDatabaseMetadata?> TryFetchMetadataAsync(GeolocationOptions options);
    }
}
