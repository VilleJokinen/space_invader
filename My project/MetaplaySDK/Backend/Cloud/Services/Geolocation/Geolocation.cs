// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services.Geolocation
{
    /// <summary>
    /// Main public interface to IP geolocation.
    /// </summary>
    public class Geolocation
    {
        public static Geolocation Instance { get; private set; }

        /// <summary> How often to check whether there are updates in the origin, i.e. MaxMind's servers. </summary>
        static readonly TimeSpan OriginUpdateCheckInterval  = TimeSpan.FromHours(1);
        /// <summary> How often to check whether there are updates in the replica storage (e.g. S3). </summary>
        static readonly TimeSpan ReplicaUpdateCheckInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Initialize the geolocation utility default instance (<see cref="Instance"/>).
        /// </summary>
        /// <param name="isLeader">
        /// Should be true for exactly one node in the cluster; can be e.g. <see cref="Cluster.ClusterCoordinatorActor.IsLeaderNode"/>.
        /// The leader will be downloading updates directly from MaxMind and storing to a replica storage,
        /// which the other nodes will access.
        /// </param>
        public static async Task InitializeAsync(IBlobStorage replicaBlobStorage, bool isLeader)
        {
            if (Instance != null)
                throw new InvalidOperationException("Already initialized");

            IMetaLogger                 logger              = MetaLogger.ForContext<Geolocation>();
            GeolocationOptions          options             = RuntimeOptionsRegistry.Instance.GetCurrent<GeolocationOptions>();
            GeolocationReplicaStorage   replicaStorage      = new GeolocationReplicaStorage(replicaBlobStorage);
            GeolocationDatabase?        initialDatabase     = await TryGetInitialDatabaseAsync(logger, options, replicaStorage).ConfigureAwait(false);
            GeolocationResidentStorage  residentStorage     = new GeolocationResidentStorage(initialDatabase);
            List<GeolocationUpdater>    updaters            = new List<GeolocationUpdater>();

            // On leader, start an updater which gets updates from MaxMind's server and puts them to the replica storage.
            if (isLeader)
            {
                GeolocationUpdateSourceMaxMind sourceMaxMind = new GeolocationUpdateSourceMaxMind();

                updaters.Add(new GeolocationUpdater(
                    logger:                 MetaLogger.ForContext("GeolocationUpdaterFromOrigin"),
                    source:                 sourceMaxMind,
                    destination:            replicaStorage,
                    updateCheckInterval:    OriginUpdateCheckInterval));
            }

            // On all nodes, start an updater which gets updates from the replica storage and puts them to the resident storage.
            // \note This is also done on leader. Thus, on leader, an update is first copied from MaxMind to replica,
            //       and then from replica to resident. We could remove that extra fetch from the replica,
            //       but this way code is simpler and gets exercised better also on single-node setups.
            updaters.Add(new GeolocationUpdater(
                logger:                 MetaLogger.ForContext("GeolocationUpdaterFromReplica"),
                source:                 replicaStorage,
                destination:            residentStorage,
                updateCheckInterval:    ReplicaUpdateCheckInterval));

            GeolocationMetricsReporter metricsReporter = new GeolocationMetricsReporter(residentStorage);

            Instance = new Geolocation(residentStorage, updaters, metricsReporter);
        }

        GeolocationResidentStorage  _residentStorage;
        List<GeolocationUpdater>    _updaters;          // \todo [nuutti] Not used for anything at the moment. Should stop each updater when disposing Geolocation.
        GeolocationMetricsReporter  _metricsReporter;   // \todo [nuutti] Not used for anything at the moment. Should stop reporter when disposing Geolocation.

        Geolocation(GeolocationResidentStorage residentStorage, List<GeolocationUpdater> updaters, GeolocationMetricsReporter metricsReporter)
        {
            _residentStorage = residentStorage ?? throw new ArgumentNullException(nameof(residentStorage));
            _updaters = updaters ?? throw new ArgumentNullException(nameof(updaters));
            _metricsReporter = metricsReporter ?? throw new ArgumentNullException(nameof(metricsReporter));
        }

        /// <summary>
        /// Get location info corresponding to a player's ip address, if available.
        /// The info may be unavailable for various reasons:
        /// - No info found for the IP address in the geolocation database
        /// - Geolocation database hasn't been downloaded yet
        /// - Geolocation is disabled in <see cref="GeolocationOptions"/>
        /// - Geolocation database is over 30 days old
        ///   (probably due to updates being disabled due to <see cref="GeolocationOptions.MaxMindLicenseKeyPath"/> not being set)
        /// </summary>
        public PlayerLocation? TryGetPlayerLocation(IPAddress ipAddress)
        {
            DatabaseReader databaseReader = TryGetDatabaseReader();
            if (databaseReader == null)
                return null;

            if (databaseReader.TryCountry(ipAddress, out CountryResponse response))
            {
                string countryIsoCode = response.Country.IsoCode;

                if (countryIsoCode != null)
                    return new PlayerLocation(new CountryId(countryIsoCode));
            }

            return null;
        }

        /// <summary>
        /// Helper to get the current resident DatabaseReader if it's available and not too old.
        /// </summary>
        DatabaseReader TryGetDatabaseReader()
        {
            GeolocationResidentStorage.ResidentDatabase resident = _residentStorage.ResidentDatabaseMaybe;
            if (resident == null)
                return null;

            // \note According to license, don't use too old database.
            //       Shouldn't happen if things are properly configured;
            //       GeolocationUpdater should take care of keeping it up to date.
            if (MetaTime.Now > resident.Metadata.BuildDate + MetaDuration.FromDays(30))
                return null;

            return resident.Reader;
        }

        static async Task<GeolocationDatabase?> TryGetInitialDatabaseAsync(IMetaLogger logger, GeolocationOptions options, GeolocationReplicaStorage replicaStorage)
        {
            GeolocationDatabase? initialDatabase;

            if (options.Enabled)
            {
                initialDatabase = await replicaStorage.TryFetchDatabaseAsync(options).ConfigureAwait(false);

                if (initialDatabase.HasValue)
                    logger.Information("Initial geolocation database found from replica. Build date: {BuildDate}", initialDatabase.Value.Metadata.BuildDate);
                else
                    logger.Information("Initial geolocation database not available in replica");
            }
            else
            {
                initialDatabase = null;
                logger.Information("Geolocation is disabled, not fetching initial database");
            }

            return initialDatabase;
        }
    }
}
