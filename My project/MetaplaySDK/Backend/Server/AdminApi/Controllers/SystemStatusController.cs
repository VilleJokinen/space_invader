// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Options;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Guild;
using Metaplay.Core.Json;
using Metaplay.Core.League.Player;
using Metaplay.Server.GameConfig;
using Metaplay.Server.Matchmaking;
using Metaplay.Server.Web3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK routes that return information about the deployment.
    /// Note that all of this data is available without any authentication, so be careful not
    /// to accidentally leak any secure data in here
    /// </summary>
    public class SystemStatusController : GameAdminApiController
    {
        public SystemStatusController(ILogger<SystemStatusController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// API endpoint to return just enough configuration for the dashboard to initialize itself
        /// Note that this endpoint is totally unauthorized - everyone has access to it. Because of this,
        /// be very careful not to accidentally leak any sensitive data here!
        /// Usage:  GET /api/hello
        /// Test:   curl http://localhost:5550/api/hello
        /// </summary>
        [HttpGet("hello")]
        [AllowAnonymous]
        public ActionResult GetHello()
        {
            // Fetch all options
            (EnvironmentOptions envOpts, AdminApiOptions adminApiOpts, DeploymentOptions deployOpts, SystemOptions systemOpts, PushNotificationOptions pushOpts, GooglePlayStoreOptions playStoreOpts) =
                RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions, AdminApiOptions, DeploymentOptions, SystemOptions, PushNotificationOptions, GooglePlayStoreOptions>();

            // Figure out the appropriate auth configuration
            Dictionary<string, string> authConfig = new Dictionary<string, string>();
            authConfig.Add("type", adminApiOpts.Type.ToString());
            switch (adminApiOpts.Type)
            {
                case AuthenticationType.None:
                    break;

                case AuthenticationType.JWT:
                    authConfig.Add("rolePrefix", adminApiOpts.JwtConfiguration.RolePrefix);
                    authConfig.Add("bearerToken", Request.Headers[adminApiOpts.JwtConfiguration.BearerTokenSource]);
                    authConfig.Add("logoutUri", adminApiOpts.JwtConfiguration.LogoutUri);
                    authConfig.Add("userInfoUri", adminApiOpts.JwtConfiguration.UserInfoUri);
                    break;

                default:
                    throw new InvalidOperationException("Invalid or incomplete auth type chosen.");
            }

            // \todo [petri] taking copies of various RuntimeOptions, should ultimately find a better solution
            return Ok(new
            {
                ProjectName                          = MetaplayCore.Options.ProjectName,
                Environment                          = envOpts.Environment,
                IsProductionEnvironment              = RuntimeOptionsRegistry.Instance.EnvironmentFamily == EnvironmentFamily.Production,
                BuildNumber                          = CloudCoreVersion.BuildNumber,
                CommitId                             = string.IsNullOrEmpty(CloudCoreVersion.CommitId) ? "not available" : CloudCoreVersion.CommitId,
                GrafanaUri                           = deployOpts.GrafanaUri,
                KubernetesNamespace                  = deployOpts.KubernetesNamespace,
                PlayerDeletionDefaultDelay           = systemOpts.PlayerDeletionDefaultDelay,
                EnableGooglePlayInAppPurchaseRefunds = playStoreOpts.EnableGooglePlayInAppPurchaseRefunds,
                EnableRemoveIapSubscriptions         = envOpts.EnableDevelopmentFeatures,
                AuthConfig                           = authConfig,

                FeatureFlags = new
                {
                    PushNotifications   = pushOpts.Enabled,
                    Guilds              = new GuildsEnabledCondition().IsEnabled,
                    AsyncMatchmaker     = new AsyncMatchmakingEnabledCondition().IsEnabled,
                    Web3                = new Web3EnabledCondition().IsEnabled,
                    PlayerLeagues       = new PlayerLeaguesEnabledCondition().IsEnabled,
                    Localization        = new LocalizationsEnabledCondition().IsEnabled,
                }
            });
        }

        /// <summary>
        /// API endpoint to return information about the deployment that doesn't change during runtime
        /// Usage:  GET /api/staticConfig
        /// Test:   curl http://localhost:5550/api/staticConfig
        /// </summary>
        [HttpGet("staticConfig")]
        [RequirePermission(MetaplayPermissions.ApiGeneralView)]
        public object GetStaticConfig()
        {
            return new
            {
                ClusterConfig = Application.Instance.ClusterConfig,
                DefaultLanguage = MetaplayCore.Options.DefaultLanguage,
                SupportedLogicVersions = MetaplayCore.Options.SupportedLogicVersions,
                ServerReflection = new
                {
                    ActivablesMetadata = ActivablesUtil.GetMetadata(),
                },
                ConfigBuildInfo = new ServerConfigBuildInfo()
            };
        }

        /// <summary>
        /// API endpoint to return potentially large game configuration
        /// Usage:  GET /api/gamedata
        /// Test:   curl http://localhost:5550/api/gamedata
        /// </summary>
        [HttpGet("gamedata")]
        [RequirePermission(MetaplayPermissions.ApiGeneralView)]
        public IActionResult GetGameData()
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            ISharedGameConfig sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;
            IServerGameConfig serverGameConfig = activeGameConfig.BaselineGameConfig.ServerConfig;
            Dictionary<string, object> sharedEntries = GetGameConfigEntries(sharedGameConfig);
            Dictionary<string, object> serverEntries = GetGameConfigEntries(serverGameConfig);

            return Ok(new
            {
                // \todo Should shared and server configs be separate top-level entries like this, or should they be merged into one?
                //       If separate, then consider renaming GameConfig to SharedGameConfig (including in dashboard).

                GameConfig = sharedEntries,

                ServerGameConfig = serverEntries,
            });
        }

        public static Dictionary<string, object> GetGameConfigEntries<TGameConfig>(TGameConfig gameConfig)
            where TGameConfig : IGameConfig
        {
            GameConfigRepository.GameConfigTypeInfo gameConfigTypeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(gameConfig.GetType());
            Dictionary<string, object> entries = gameConfigTypeInfo.Entries.Values.ToDictionary(
                entryInfo => entryInfo.MemberInfo.Name, // \todo [nuutti] Use member name, or the name specified in the GameConfigEntryAttribute? #config-entry-name
                entryInfo => entryInfo.MemberInfo.GetDataMemberGetValueOnDeclaringType()(gameConfig));

            // Add SDK-declared IGameConfigLibrary<> interface properties even if they don't exist as actual game config entries.
            // This exposes the SDK-provided stub libraries to the dashboard.
            foreach (PropertyInfo interfaceProp in typeof(TGameConfig).EnumerateInstancePropertiesInUnspecifiedOrder())
            {
                if (!interfaceProp.PropertyType.IsGenericTypeOf(typeof(IGameConfigLibrary<,>)))
                    continue;
                if (!entries.ContainsKey(interfaceProp.Name))
                    entries.Add(interfaceProp.Name, interfaceProp.GetDataMemberGetValueOnDeclaringType()(gameConfig));
            }
            return entries;
        }

        /// <summary>
        /// API endpoint to return frequently updated information about the deployment
        /// Usage:  GET /api/status
        /// Test:   curl http://localhost:5550/api/status
        /// </summary>
        [HttpGet("status")]
        [RequirePermission(MetaplayPermissions.ApiGeneralView)]
        public async Task<ActionResult<object>> GetStatus()
        {
            GlobalStatusResponse                    globalStatus        = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            StatsCollectorLiveEntityCountResponse   liveEntityCounts    = await AskEntityAsync<StatsCollectorLiveEntityCountResponse>(StatsCollectorManager.EntityId, StatsCollectorLiveEntityCountRequest.Instance);
            StatsCollectorNumConcurrentsResponse    stats               = await AskEntityAsync<StatsCollectorNumConcurrentsResponse>(StatsCollectorManager.EntityId, StatsCollectorNumConcurrentsRequest.Instance);
            DatabaseOptions                         databaseOptions     = RuntimeOptionsRegistry.Instance.GetCurrent<DatabaseOptions>();

            return new
            {
                ClientCompatibilitySettings = globalStatus.ClientCompatibilitySettings,
                MaintenanceStatus = globalStatus.MaintenanceStatus,
                LiveEntityCounts = liveEntityCounts.LiveEntityCounts,
                DatabaseStatus = new
                {
                    Backend = databaseOptions.Backend,
                    ActiveShards = databaseOptions.NumActiveShards,
                    TotalShards = databaseOptions.Shards.Length,
                },
                NumConcurrents = stats.NumConcurrents,
            };
        }

        /// <summary>
        /// API endpoint to return total item counts of all item types (Players, Guilds, etc.) across
        /// all database shards.
        /// Usage:  GET /api/databaseItemCounts
        /// Test:   curl http://localhost:5550/api/databaseItemCounts
        /// </summary>
        [HttpGet("databaseItemCounts")]
        [RequirePermission(MetaplayPermissions.ApiGeneralView)]
        public async Task<ActionResult<object>> GetDatabaseItemCounts()
        {
            StatsCollectorStateResponse response    = await AskEntityAsync<StatsCollectorStateResponse>(StatsCollectorManager.EntityId, StatsCollectorStateRequest.Instance);
            StatsCollectorState         state       = response.State.Deserialize(resolver: null, logicVersion: null);

            OrderedDictionary<string, int> totalItemCounts = new OrderedDictionary<string, int>();
            foreach ((string tableName, int[] shardItemCounts) in state.DatabaseShardItemCounts)
                totalItemCounts.Add(tableName, shardItemCounts.Sum());
            return new { totalItemCounts };
        }

        [HttpGet("databaseStatus")]
        [RequirePermission(MetaplayPermissions.ApiDatabaseStatus)]
        public async Task<ActionResult<object>> GetDatabaseStatus()
        {
            StatsCollectorStateResponse response    = await AskEntityAsync<StatsCollectorStateResponse>(StatsCollectorManager.EntityId, StatsCollectorStateRequest.Instance);
            StatsCollectorState         state       = response.State.Deserialize(resolver: null, logicVersion: null);
            return new
            {
                Options         = RuntimeOptionsRegistry.Instance.GetOptions<DatabaseOptions>(),
                NumShards       = state.DatabaseShardItemCounts.First().Value.Count(),
                ShardItemCounts = state.DatabaseShardItemCounts,
            };
        }

        /// <summary>
        /// API endpoint to get all current RuntimeOptions information
        /// Usage:  GET /api/runtimeOptions
        /// Test:   curl http://localhost:5550/api/runtimeOptions
        /// </summary>
        [HttpGet("runtimeOptions")]
        [RequirePermission(MetaplayPermissions.ApiRuntimeOptionsView)]
        public object GetRuntimeOptions()
        {
            RuntimeOptionsRegistry registry = RuntimeOptionsRegistry.Instance;

            return new
            {
                AllSources  = registry.GetAllSources(),
                Options     = registry.GetAllOptions()
            };
        }

        /// <summary>
        /// API endpoint to get all environment links from the runtime options.
        /// Usage:  GET /api/quickLinks
        /// Test:   curl http://localhost:5550/api/quickLinks
        /// </summary>
        [HttpGet("quickLinks")]
        [RequirePermission(MetaplayPermissions.ApiQuickLinksView)]
        public object GetQuickLinks()
        {
            EnvironmentOptions environmentOptions = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            return environmentOptions.QuickLinks;
        }

        /// <summary>
        /// API endpoint to get Dashboard specific configuration.
        /// Usage:  GET /api/dashboardOptions
        /// Test:   curl http://localhost:5550/api/dashboardOptions
        /// </summary>
        [HttpGet("dashboardOptions")]
        [RequirePermission(MetaplayPermissions.ApiGeneralView)]
        public object GetDashboardOptions()
        {
            LiveOpsDashboardOptions liveOpsDashboardOptions = RuntimeOptionsRegistry.Instance.GetCurrent<LiveOpsDashboardOptions>();
            return liveOpsDashboardOptions;
        }

        /// <summary>
        /// API endpoint for testing entity ask failures
        /// Usage:  GET /api/testEntityAskFailure?controlled={false/true}&async={false/true}
        /// Test:   curl http://localhost:5550/api/testEntityAskFailure?controlled=false&async=false
        /// </summary>
        [HttpGet("testEntityAskFailure")]
        [RequirePermission(MetaplayPermissions.ApiTestsAll)]
        public async Task TestEntityAskFailure([FromQuery] bool controlled = true, [FromQuery] bool async = false)
        {
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            if (!envOpts.EnableDevelopmentFeatures)
                return;

            await AskEntityAsync<EntityAskOk>(EntityId.Create(EntityKindCloudCore.DiagnosticTool, 0),
                async ? new TestAsyncHandlerEntityAskFailureRequest(controlled)
                : new TestEntityAskFailureRequest(controlled));
        }
    }
}
