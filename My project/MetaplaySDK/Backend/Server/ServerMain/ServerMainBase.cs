// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Metaplay.Cloud;
using Metaplay.Cloud.Analytics;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Metrics;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Cloud.Services.Geolocation;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Server.Database;
using Metaplay.Server.EntityArchive;
using Metaplay.Server.Forms;
using Metaplay.Server.GameConfig;
using Metaplay.Server.MaintenanceJob;
using Metaplay.Server.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    public abstract class ServerMainBase : Application
    {
        // Node-global actors
        IActorRef   _actorMessageCollector;
        IActorRef   _analyticsDispatcher;

        protected ServerMainBase()
        {
            // \note These are rather opinionated changes and we might want to allow the application to override these.

            // Use InvariantCulture as default current culture.
            // We try to use explicit CultureInfo/IFormatProvider arguments instead of relying on current culture.
            // However, some usage of current culture may accidentally remain, so this is used as a best-effort failsafe.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            // Force worker threads to at least 2*numCPUs to see if that affects ThreadPool starvation
            ThreadPool.GetMinThreads(out int _workerThreads, out int completionPortThreads);
            ThreadPool.SetMinThreads(2 * Environment.ProcessorCount, completionPortThreads);
        }

        public Task<int> RunServerAsync(string[] cmdLineArgs) => RunApplicationMainAsync(applicationSymbolicName: "Server", cmdLineArgs);

        async Task InitializeFirebasePushNotificationsAsync()
        {
            // Initialize FCM
            PushNotificationOptions pushOpts = RuntimeOptionsRegistry.Instance.GetCurrent<PushNotificationOptions>();
            if (pushOpts.Enabled)
            {
                // Resolve credentials (from Secrets Manager or file)
                _logger.Information("Initializing Firebase with credentials from {FirebaseCredentialsPath}", pushOpts.FirebaseCredentialsPath);
                string firebaseCredentials = await SecretUtil.ResolveSecretAsync(_logger, pushOpts.FirebaseCredentialsPath).ConfigureAwait(false);

                // Initialize Firebase
                FirebaseApp _ = FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromJson(firebaseCredentials)
                });
            }
        }

        async Task InitializePublicBlobStoreConnectivityTests()
        {
            try
            {
                using (IBlobStorage storage = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>().CreatePublicBlobStorage("Connectivity"))
                {
                    await storage.PutAsync("connectivity-test", Encoding.UTF8.GetBytes("y"));
                }
            }
            catch (Exception ex)
            {
                // Failure is not critical, tolerate.
                _logger.Error("Failed while writing connectivity-test file: {Exception}", ex);
            }
        }

        protected async Task StartCoreServiceAsync()
        {
            // Initialize GameConfig providers
            ServerConfigDataProvider.Initialize();

            // Initialize database
            MetaDatabase.Initialize();

            // Ensure database is migrated to latest schema (only on leader node)
            if (ClusterCoordinatorActor.IsLeaderNode)
            {
                DatabaseMigrator migrator = new DatabaseMigrator();
                await migrator.EnsureMigratedAsync();
            }

            // Initialize Firebase push notifications
            await InitializeFirebasePushNotificationsAsync().ConfigureAwait(false);

            // Start Akka.net actor message metrics collector
            _actorMessageCollector = _actorSystem.ActorOf(Props.Create<ActorMessageCollector>(), "actor-message-collector");

            // Start analytics dispatcher (with enabled sinks)
            IEnumerable<AnalyticsDispatcherSinkBase> analyticsSinks = await IntegrationRegistry.Get<AnalyticsDispatcherSinkFactory>().CreateSinksAsync();
            _analyticsDispatcher = _actorSystem.ActorOf(Props.Create<AnalyticsDispatcherActor>(analyticsSinks), "analytics-dispatcher");

            // Start Google sign-in keycache autoupdater
            GooglePlayStoreOptions playStoreOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();
            if (playStoreOpts.EnableGoogleAuthentication)
                GoogleOAuth2PublicKeyCache.Instance.RenewAutomatically();

            // Start Apple sign-in keycache autoupdater
            AppleStoreOptions appStoreOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AppleStoreOptions>();
            if (appStoreOpts.EnableAppleAuthentication)
                AppleSignInPublicKeyCache.Instance.RenewAutomatically();

            // Fetch Facebook login access token and keycache autoupdater
            FacebookOptions facebookOptions = RuntimeOptionsRegistry.Instance.GetCurrent<FacebookOptions>();
            if (facebookOptions.LoginEnabled)
            {
                FacebookAppService.Instance.PrefetchAppAccessToken();
                FacebookLoginPublicKeyCache.Instance.RenewAutomatically();
            }

            // Start AndroidPublisherService instance
            if (playStoreOpts.EnableAndroidPublisherApi)
                AndroidPublisherServiceSingleton.Initialize();

            // Initialize geolocation.
            // Loads initial geolocation database if available, and starts database auto-updater.
            await Geolocation.InitializeAsync(
                replicaBlobStorage: RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>().CreatePrivateBlobStorage("Geolocation"),
                isLeader:           ClusterCoordinatorActor.IsLeaderNode)
                .ConfigureAwait(false);

            // Prepare Public BlobStore
            if (ClusterCoordinatorActor.IsLeaderNode)
            {
                await InitializePublicBlobStoreConnectivityTests();
            }

            // On leader node, periodically purge old PlayerIncidents (doing this fairly frequently to avoid purging huge swathes of reports at a time)
            // \todo [petri] refactor this to a better place, wrap inside an actor?
            if (ClusterCoordinatorActor.IsLeaderNode)
            {
                _ = Task.Run(async () =>
                {
                    while (true)
                    {
                        // When DB goes to null, the local service is dead
                        SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
                        MetaDatabase db = MetaDatabase.Get(QueryPriority.Lowest);
                        if (db == null)
                        {
                            _logger.Information("Player incidents clean-up sweeper stopping.");
                            return;
                        }

                        // Purge reports older than retention period
                        try
                        {
                            Stopwatch sw = Stopwatch.StartNew();
                            DateTime removeUntil = DateTime.UtcNow - systemOpts.IncidentReportRetentionPeriod;
                            int numPurged = await db.PurgePlayerIncidentsAsync(removeUntil, systemOpts.IncidentReportPurgeMaxItems).ConfigureAwait(false);
                            if (numPurged > 0)
                                _logger.Information("Purged {NumPurgedReports} player incident reports ({Duration:0.00}s elapsed)", numPurged, sw.ElapsedMilliseconds / 1000.0);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Failed to purge PlayerIncidents: {Exception}", ex);
                        }

                        // Purge audit log events older than retention period
                        try
                        {
                            MetaDuration retentionPeriod = MetaDuration.FromTimeSpan(systemOpts.AuditLogRetentionPeriod);
                            int numPurged = await db.PurgeAuditLogEventsAsync(MetaTime.Now - retentionPeriod).ConfigureAwait(false);
                            if (numPurged > 0)
                                _logger.Information("Purged {NumPurgedEvents} audit log events", numPurged);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Failed to purge AuditLogEvents: {Exception}", ex);
                        }

                        // Wait a while until purging again
                        await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                    }
                });
            }
        }

        protected async Task StopCoreServiceAsync()
        {
            _logger.Information("Stopping CoreService..");
            await _analyticsDispatcher.Ask<ShutdownComplete>(ShutdownSync.Instance, TimeSpan.FromSeconds(60));
            _actorMessageCollector.Tell(PoisonPill.Instance);
        }

        protected override Task PreInitializeAsync()
        {
            // Initialize Maintenance Jobs
            EntityMaintenanceJobRegistry.Initialize();

            // Initialize EntityArchiveRegistry
            EntityArchiveUtils.Initialize();

            // Initialize MetaFormTypeRegistry
            MetaFormTypeRegistry.Initialize();

            return Task.CompletedTask;
        }
    }
}
