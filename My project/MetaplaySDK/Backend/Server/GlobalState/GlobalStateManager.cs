// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud;
using Metaplay.Cloud.Analytics;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Math;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using Metaplay.Server.ServerAnalyticsEvents;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    /// <summary>
    /// Request the current status of <see cref="GlobalStateManager"/> (maintenance status, logic version config, etc.).
    /// <see cref="GlobalStateManager"/> responds with a <see cref="GlobalStatusResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStatusRequest, MessageDirection.ServerInternal)]
    public class GlobalStatusRequest : MetaMessage
    {
        public static readonly GlobalStatusRequest Instance = new GlobalStatusRequest();

        public GlobalStatusRequest() { }
    }

    /// <summary>
    /// Represents the current status of <see cref="GlobalStateManager"/>: contains maintenance status, logic version config, active entity
    /// counts, etc. Requested by admin dashboard.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStatusResponse, MessageDirection.ServerInternal)]
    public class GlobalStatusResponse : MetaMessage
    {
        public MaintenanceStatus                    MaintenanceStatus           { get; private set; }
        public ClientCompatibilitySettings          ClientCompatibilitySettings { get; private set; }
        public MetaGuid                             ActiveStaticGameConfigId    { get; private set; }
        public MetaGuid                             ActiveLocalizationsId       { get; private set; }

        public GlobalStatusResponse() { }
        public GlobalStatusResponse(MaintenanceStatus maintenanceStatus, ClientCompatibilitySettings clientCompatibilitySettings,
            MetaGuid activeStaticGameConfigId, MetaGuid activeLocalizationsId)
        {
            MaintenanceStatus           = maintenanceStatus;
            ClientCompatibilitySettings = clientCompatibilitySettings;
            ActiveStaticGameConfigId    = activeStaticGameConfigId;
            ActiveLocalizationsId       = activeLocalizationsId;
        }
    }

    /// <summary>
    /// Represents the settings of which client versions are compatible with the server, and whether the server
    /// should redirect clients above the accepted version to the server specified by <see cref="RedirectServerEndpoint"/>.
    /// </summary>
    [MetaSerializable]
    public class ClientCompatibilitySettings : IEquatable<ClientCompatibilitySettings>
    {
        [MetaMember(4)] public int              ActiveLogicVersion              { get; private set; }   // Currently active LogicVersion
        [MetaMember(3)] public bool             RedirectEnabled                 { get; private set; }   // Enable redirecting for clients above AcceptedClientLogicVersions.MaxVersion
        [MetaMember(2)] public ServerEndpoint   RedirectServerEndpoint          { get; private set; }   // Server endpoint to redirect too new clients: client.SupportedLogicVersions.max > server.ActiveLogicVersion

        public ClientCompatibilitySettings() { }
        public ClientCompatibilitySettings(int activeLogicVersion, bool redirectEnabled, ServerEndpoint redirectServerEndpoint)
        {
            ActiveLogicVersion      = activeLogicVersion;
            RedirectEnabled         = redirectEnabled;
            RedirectServerEndpoint  = redirectServerEndpoint;
        }

        public bool Equals([AllowNull] ClientCompatibilitySettings other)
        {
            if (other is null)
                return false;

            return ActiveLogicVersion == other.ActiveLogicVersion
                && RedirectEnabled == other.RedirectEnabled
                && RedirectServerEndpoint == other.RedirectServerEndpoint;
        }

        public override bool Equals(object obj) => obj is ClientCompatibilitySettings other && Equals(other);
        public override int GetHashCode() => Util.CombineHashCode(ActiveLogicVersion.GetHashCode(), RedirectEnabled.GetHashCode(), RedirectServerEndpoint.GetHashCode());
    }

    [MetaMessage(MessageCodesCore.UpdateClientCompatibilitySettingsRequest, MessageDirection.ServerInternal)]
    public class UpdateClientCompatibilitySettingsRequest : MetaMessage
    {
        public ClientCompatibilitySettings Settings { get; private set; }

        public UpdateClientCompatibilitySettingsRequest() { }
        public UpdateClientCompatibilitySettingsRequest(ClientCompatibilitySettings config) { Settings = config; }
    }

    [MetaMessage(MessageCodesCore.UpdateClientCompatibilitySettingsResponse, MessageDirection.ServerInternal)]
    public class UpdateClientCompatibilitySettingsResponse : MetaMessage
    {
        public bool     IsSuccess       { get; private set; }
        public string   ErrorMessage    { get; private set; }

        public UpdateClientCompatibilitySettingsResponse() { }
        public UpdateClientCompatibilitySettingsResponse(bool isSuccess, string errorMessage) { IsSuccess = isSuccess; ErrorMessage = errorMessage; }
    }

    #region BroadcastConsumption

    /// <summary>
    /// A whole list of consumed broadcast counts. The GlobalStateProxies periodically
    /// send these to the GlobalStateManager
    /// </summary>
    [MetaMessage(MessageCodesCore.BroadcastConsumedCountInfo, MessageDirection.ServerInternal)]
    public class BroadcastConsumedCountInfo : MetaMessage
    {
        public Dictionary<int, int> BroadcastsConsumed { get; private set; }

        public BroadcastConsumedCountInfo() { }
        public BroadcastConsumedCountInfo(Dictionary<int, int> broadcastsConsumed) { BroadcastsConsumed = broadcastsConsumed; }
    }

    #endregion // BroadcastConsumption

    /// <summary>
    /// Tell <see cref="GlobalStateManager"/> to discard its <see cref="GlobalState.LegacyActivableStatistics"/>
    /// and <see cref="GlobalState.LegacyMetaOfferStatistics"/>. Sent by <see cref="StatsCollectorManager"/>
    /// after it has migrated the statistics into its own state.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateForgetActivableAndOfferStatistics, MessageDirection.ServerInternal)]
    public class GlobalStateForgetActivableAndOfferStatistics : MetaMessage
    {
        public static readonly GlobalStateForgetActivableAndOfferStatistics Instance = new GlobalStateForgetActivableAndOfferStatistics();
    }

    /// <summary>
    /// Request for toggling a player's developer status. GlobalStateManager responds with a <see cref="SetDeveloperPlayerResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.SetDeveloperPlayerRequest, MessageDirection.ServerInternal)]
    public class SetDeveloperPlayerRequest : MetaMessage
    {
        public EntityId PlayerId;
        public bool     IsDeveloper;

        public SetDeveloperPlayerRequest() { }

        public SetDeveloperPlayerRequest(EntityId playerId, bool isDeveloper)
        {
            PlayerId        = playerId;
            IsDeveloper = isDeveloper;
        }
    }

    /// <summary>
    /// Response to a <see cref="SetDeveloperPlayerRequest"/>. Contains whether the request was successful or not.
    /// </summary>
    [MetaMessage(MessageCodesCore.SetDeveloperPlayerResponse, MessageDirection.ServerInternal)]
    public class SetDeveloperPlayerResponse : MetaMessage
    {
        public static SetDeveloperPlayerResponse Success = new SetDeveloperPlayerResponse(true);
        public static SetDeveloperPlayerResponse AlreadySatisfied = new SetDeveloperPlayerResponse(false);

        public bool IsSuccess { get; private set; }

        private SetDeveloperPlayerResponse() { }

        private SetDeveloperPlayerResponse(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }
    }


    /// <summary>
    /// Represents the database-persisted portion <see cref="GlobalStateManager"/>.
    /// </summary>
    [Table("GlobalStates")]
    public class PersistedGlobalState : IPersistedEntity
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   EntityId        { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime PersistedAt     { get; set; }

        [Required]
        public byte[]   Payload         { get; set; }   // TaggedSerialized<GlobalState>

        [Required]
        public int      SchemaVersion   { get; set; }   // Schema version for object

        [Required]
        public bool     IsFinal         { get; set; }
    }

    [MetaMessage(MessageCodesCore.GlobalStateRequest, MessageDirection.ServerInternal)]
    public class GlobalStateRequest : MetaMessage
    {
        public static readonly GlobalStateRequest Instance = new GlobalStateRequest();
    }

    [MetaMessage(MessageCodesCore.GlobalStateSnapshot, MessageDirection.ServerInternal)]
    public class GlobalStateSnapshot : MetaMessage
    {
        public MetaSerialized<GlobalState> GlobalState { get; private set; }

        public GlobalStateSnapshot() { }
        public GlobalStateSnapshot(MetaSerialized<GlobalState> globalState)
        {
            GlobalState = globalState;
        }
    }

    [MetaMessage(MessageCodesCore.GlobalStateUpdateGameConfig, MessageDirection.ServerInternal)]
    public class GlobalStateUpdateGameConfig : MetaMessage
    {
        public MetaGuid                                                             StaticConfigId                  { get; private set; }
        public MetaGuid                                                             DynamicConfigId                 { get; private set; }
        public ConfigArchiveDeliverables                                            SharedConfigDeliverables        { get; private set; }
        public OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState>   PlayerExperiments               { get; private set; } // experiment state is tied to the config state, and both must update atomically.
        public ContentHash                                                          SharedPatchesForPlayersContentHash { get; private set; }
        public ContentHash                                                          SharedPatchesForTestersContentHash { get; private set; }
        public MetaTime                                                             Timestamp                       { get; private set; }

        GlobalStateUpdateGameConfig() { }
        public GlobalStateUpdateGameConfig(MetaGuid staticConfigId, MetaGuid dynamicConfigId, ConfigArchiveDeliverables sharedConfigDeliverables,
            OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState> playerExperiments, ContentHash sharedPatchesForPlayers, ContentHash sharedPatchesForTesters, MetaTime timestamp)
        {
            StaticConfigId = staticConfigId;
            DynamicConfigId = dynamicConfigId;
            SharedConfigDeliverables = sharedConfigDeliverables;
            PlayerExperiments = playerExperiments;
            SharedPatchesForPlayersContentHash = sharedPatchesForPlayers;
            SharedPatchesForTestersContentHash = sharedPatchesForTesters;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Notification to GSPs that the active localization has changed.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateUpdateLocalizationsConfigVersion, MessageDirection.ServerInternal)]
    [LocalizationsEnabledCondition]
    public class GlobalStateUpdateLocalizationsConfigVersion : MetaMessage
    {
        public MetaGuid                  LocalizationsId { get; private set; }
        public LocalizationsDeliverables Deliverables    { get; private set; }

        private GlobalStateUpdateLocalizationsConfigVersion() { }

        public GlobalStateUpdateLocalizationsConfigVersion(MetaGuid localizationsId, LocalizationsDeliverables deliverables)
        {
            LocalizationsId = localizationsId;
            Deliverables    = deliverables;
        }
    }

    /// <summary>
    /// Results of a write-config-archive-into-CDN operation.
    /// </summary>
    [MetaSerializable]
    public struct ConfigArchiveDeliverables
    {
        [MetaMember(1)] public ContentHash Version;
        // \todo: list of delta encoded versions here

        public ConfigArchiveDeliverables(ContentHash version)
        {
            Version = version;
        }
    }

    /// <summary>
    /// Results of a write-localizations-into-CDN operation.
    /// </summary>
    [MetaSerializable]
    public struct LocalizationsDeliverables
    {
        [MetaMember(1)] public OrderedDictionary<LanguageId, ContentHash> Languages;

        public LocalizationsDeliverables(IEnumerable<(LanguageId id, ContentHash version)> languages)
        {
            Languages = languages.ToOrderedDictionary(x => x.id, x => x.version);
        }
    }

    /// <summary>
    /// Version identifying a server build.
    /// </summary>
    [MetaSerializable]
    public class ServerBuildVersion
    {
        [MetaMember(1)] public string BuildNumber   { get; private set; }
        [MetaMember(2)] public string CommitId      { get; private set; }

        ServerBuildVersion() { }
        public static ServerBuildVersion GetCurrent()
        {
            return new ServerBuildVersion()
            {
                BuildNumber = CloudCoreVersion.BuildNumber,
                CommitId = CloudCoreVersion.CommitId,
            };
        }

        public static string ToDisplayString(ServerBuildVersion version)
        {
            if (version == null)
                return "(version:unknown)";
            string buildNumber = version.BuildNumber ?? "unknown";
            string commitId = version.CommitId ?? "unknown";
            return $"(version:{buildNumber};commit:{commitId})";
        }
    }

    // GlobalState

    [MetaSerializable]
    [MetaBlockedMembers(6, 20, 21)]
    [MetaReservedMembers(1, 100)]
    public abstract partial class GlobalState : ISchemaMigratable
    {
        [MetaMember(4)] public ClientCompatibilitySettings                      ClientCompatibilitySettings { get; set; } = null;
        [MetaMember(1)] public ScheduledMaintenanceMode                         ScheduledMaintenanceMode    { get; set; } = null;
        [MetaMember(2)] public int                                              RunningBroadcastMessageId   { get; set; } = 1;
        [MetaMember(3)] public OrderedDictionary<int, BroadcastMessageParams>   LegacyBroadcastMessages     { get; set; } = new OrderedDictionary<int, BroadcastMessageParams>();   // Legacy version of Broadcast Messages from schema version 2
        [MetaMember(8)] public OrderedDictionary<int, BroadcastMessage>         BroadcastMessages           { get; set; } = new OrderedDictionary<int, BroadcastMessage>();
        [MetaMember(9)] public FullMetaActivableStatistics                      LegacyActivableStatistics   { get; set; } = new FullMetaActivableStatistics();  // Legacy, migrated to StatsCollectorManager. Set to null in HandleGlobalStateForgetActivableAndOfferStatistics.
        [MetaMember(27)] public MetaOffersStatistics                            LegacyMetaOfferStatistics   { get; set; } = new MetaOffersStatistics();         // Legacy, migrated to StatsCollectorManager. Set to null in HandleGlobalStateForgetActivableAndOfferStatistics.
        [MetaMember(24)] public MetaGuid                                        StaticGameConfigId          { get; set; }

        // \todo [petri] made GameConfigVersions non-Transient, need to properly handle backward compatibility ..
        //[MetaMember(6)] public Dictionary<string, ContentHash>                  GameConfigVersions          { get; set; } = new Dictionary<string, ContentHash>();
        [MetaMember(7), Transient]  public byte[]                                                                  SharedClusterNonce                           { get; set; } // 32 bytes (256 bits) of random, shared between all nodes.
        [MetaMember(22)]            public OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState>      PlayerExperiments                            { get; set; } = new OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState>();
        [MetaMember(23)]            public OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalStatistics> PlayerExperimentsStats                       { get; set; } = new OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalStatistics>();
        [MetaMember(25)]            public ContentHash                                                             SharedGameConfigPatchesForPlayersContentHash { get; set; }
        [MetaMember(26)]            public ContentHash                                                             SharedGameConfigPatchesForTestersContentHash { get; set; }
        [MetaMember(28), Transient] public ConfigArchiveDeliverables                                               SharedGameConfigDeliverables                 { get; set; }
        [MetaMember(30)]            public OrderedSet<EntityId>                                                    DeveloperPlayerIds                           { get; set; } = new OrderedSet<EntityId>();
        [MetaMember(31)]            public MetaTime                                                                LatestGameConfigUpdate                       { get; set; } = MetaTime.Now; // \note: Default value only gets used when upgrading from older version and not updating game config
        [MetaMember(32)]            public ServerBuildVersion                                                      ServerBuildVersion                           { get; set; }
        [MetaMember(33)]            public MetaGuid                                                                LatestGameConfigAutoUpdateId                 { get; set; }
        [MetaMember(34)]            public MetaGuid                                                                ActiveLocalizationsId                        { get; set; }
        [MetaMember(35)]            public MetaTime                                                                LatestLocalizationsUpdate                    { get; set; }
        [MetaMember(36)]            public MetaGuid                                                                LatestLocalizationsAutoUpdateId              { get; set; }
        [MetaMember(37), Transient] public LocalizationsDeliverables                                               LocalizationsDeliverables                    { get; set; }

        #region Schema migrations

        protected void DummyMigration()
        {
            // Dummy migration to match customer migrations
        }

        protected void MigrateLegacyBroadcasts()
        {
            // Version 4 changed format of Broadcast Messages
            // Old messages load as LegacyBroadcastMessages and are converted to BroadcastMessages
            OrderedDictionary<int, BroadcastMessage> newBroadcasts = new OrderedDictionary<int, BroadcastMessage>();
            foreach ((int broadcastId, BroadcastMessageParams message) in LegacyBroadcastMessages)
            {
                newBroadcasts[broadcastId] = new BroadcastMessage(message, new BroadcastMessageStats());
            }
            BroadcastMessages = newBroadcasts;
            LegacyBroadcastMessages = new OrderedDictionary<int, BroadcastMessageParams>();
        }

        protected void MigrateBroadcastContents()
        {
            // Version 5 changed format of Broadcast Messages again
            foreach (BroadcastMessage broadcast in BroadcastMessages.Values)
            {
                broadcast.Params.MigrateContents();
            }
        }

        protected void MigrateExperiments()
        {
            // Version 6 changed format of Experiments
            foreach (PlayerExperimentGlobalState experiment in PlayerExperiments.Values)
            {
                experiment.MigrateDataFrom5To6();
            }
        }

        protected void MigrateExperiments6To7()
        {
            // Version 7 changed format of Experiments
            foreach (PlayerExperimentGlobalState experiment in PlayerExperiments.Values)
            {
                experiment.MigrateDataFrom6To7();
            }
        }

        protected void MigrateBroadcasts7To8()
        {
            // Version 8 changed broadcast targeting
            foreach (BroadcastMessage broadcast in BroadcastMessages.Values)
            {
                broadcast.Params.MigrateTargetSegments();
            }
        }

        protected void MigrateExperiments8To9()
        {
            // Version 9 changed experiments targeting
            foreach (PlayerExperimentGlobalState experiment in PlayerExperiments.Values)
            {
                experiment.MigrateDataFrom8To9();
            }
        }


        protected static Action<GlobalState> DefaultMigrationForVersion(int fromVersion)
        {
            return fromVersion switch
            {
                2 => x => x.DummyMigration(),
                3 => x => x.MigrateLegacyBroadcasts(),
                4 => x => x.MigrateBroadcastContents(),
                5 => x => x.MigrateExperiments(),
                6 => x => x.MigrateExperiments6To7(),
                7 => x => x.MigrateBroadcasts7To8(),
                8 => x => x.MigrateExperiments8To9(),
                _ => null,
            };
        }

        #endregion
    }

    [MetaSerializableDerived(100)]
    [SupportedSchemaVersions(2, 9)]
    public class DefaultGlobalState : GlobalState
    {
        [RegisterMigrationsFunction]
        public static Action<DefaultGlobalState> RegisterMigration(int fromVersion)
        {
            return DefaultMigrationForVersion(fromVersion);
        }
    }

    // GlobalStateManager

    [EntityConfig]
    internal sealed class GlobalStateManagerConfig : PersistedEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindCloudCore.GlobalStateManager;
        public override Type              EntityActorType      => IntegrationRegistry.GetSingleIntegrationType<IGlobalStateManager>();
        public override IShardingStrategy ShardingStrategy     => new ServiceShardingStrategy(numServiceShards: 1);
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(10);
        public override List<Type>        EntityComponentTypes => new List<Type>() { typeof(GameConfigManager), typeof(LocalizationsDataManager) };
    }

    public interface IGlobalStateManager : IMetaIntegrationConstructible<IGlobalStateManager>
    {
        internal IMetaLogger Logger { get; }
        internal GlobalState State  { get; }
        internal EntityActor Actor  { get; }
        internal Task OnActiveGameConfigChanged(FullGameConfig gameConfig, bool isInitial);
        internal Task OnActiveLocalizationsChanged();
    }

    public abstract partial class GlobalStateManagerBase<TGlobalState> : PersistedEntityActor<PersistedGlobalState, TGlobalState>, IGlobalStateManager where TGlobalState : GlobalState
    {
        protected override sealed AutoShutdownPolicy    ShutdownPolicy      => AutoShutdownPolicy.ShutdownNever();
        protected override sealed TimeSpan              SnapshotInterval    => TimeSpan.FromMinutes(3);     // Persist state every 3 minutes to minimize data loss on failures

        protected TGlobalState                          _state;

        readonly RandomPCG                                                      _analyticsEventRandom = RandomPCG.CreateNew();
        readonly AnalyticsEventBatcher<ServerEventBase, ServerAnalyticsContext> _analyticsEventBatcher;
        static readonly TimeSpan                                                AnalyticsEventFlushInterval = TimeSpan.FromSeconds(15);
        internal class AnalyticsEventFlush { public static readonly AnalyticsEventFlush Instance = new AnalyticsEventFlush(); }

        protected GameConfigManager _gameConfigManager;
        protected LocalizationsDataManager _localizationsManager;

        protected GlobalStateManagerBase(EntityId entityId) : base(entityId)
        {
            _analyticsEventBatcher = new AnalyticsEventBatcher<ServerEventBase, ServerAnalyticsContext>(entityId, maxBatchSize: 10);
        }

        protected override void PreStart()
        {
            base.PreStart();

            StartPeriodicTimer(TimeSpan.FromSeconds(5), AnalyticsEventFlushInterval, AnalyticsEventFlush.Instance);
        }

        protected override async Task OnShutdown()
        {
            _analyticsEventBatcher.Flush();

            await OnShutdownMaintenanceMode();

            await base.OnShutdown();
        }

        protected override EntityComponent CreateComponent(Type componentType)
        {
            if (componentType == typeof(GameConfigManager))
            {
                _gameConfigManager = new GameConfigManager(this);
                return _gameConfigManager;
            }
            else if (componentType == typeof(LocalizationsDataManager))
            {
                _localizationsManager = new LocalizationsDataManager(this);
                return _localizationsManager;
            }

            return base.CreateComponent(componentType);
        }

        protected override sealed async Task Initialize()
        {
            // Try to fetch from database & restore from it (if exists)
            PersistedGlobalState persisted = await MetaDatabase.Get().TryGetAsync<PersistedGlobalState>(_entityId.ToString());
            await InitializePersisted(persisted);
        }

        protected override sealed Task<TGlobalState> RestoreFromPersisted(PersistedGlobalState persisted)
        {
            // Deserialize actual state
            TGlobalState state = DeserializePersistedPayload<TGlobalState>(persisted.Payload, resolver: null, logicVersion: null);

            return Task.FromResult(state);
        }

        protected override sealed async Task PostLoad(TGlobalState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _state = payload;
            _state.SharedClusterNonce = NewClusterNonce();

            // Ensure that VersionConfig is up-to-date
            if (_state.ClientCompatibilitySettings == null)
            {
                _log.Info("Initializing VersionConfig with accepted client LogicVersions={AcceptedClientVersions}", MetaplayCore.Options.SupportedLogicVersions);
                _state.ClientCompatibilitySettings = new ClientCompatibilitySettings(
                    activeLogicVersion:                 MetaplayCore.Options.SupportedLogicVersions.MaxVersion,
                    redirectEnabled:                    false,
                    redirectServerEndpoint:             null);
            }

            // Update ActiveLogicVersion: must be at least supported min version & check for auto-upgrade to latest (useful in local builds)
            SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
            int activeLogicVersion = _state.ClientCompatibilitySettings.ActiveLogicVersion;
            if (systemOpts.AutoUpgradeLogicVersion && activeLogicVersion < MetaplayCore.Options.SupportedLogicVersions.MaxVersion)
            {
                _log.Info("Auto-upgrading ActiveLogicVersion from {OldActiveLogicVersion} to latest {NewActiveLogicVersion}", activeLogicVersion, MetaplayCore.Options.SupportedLogicVersions.MaxVersion);
                activeLogicVersion = MetaplayCore.Options.SupportedLogicVersions.MaxVersion;
            }
            else if (activeLogicVersion < MetaplayCore.Options.SupportedLogicVersions.MinVersion)
            {
                _log.Info("Force-upgrading ActiveLogicVersion from {OldActiveLogicVersion} to {NewActiveLogicVersion}", activeLogicVersion, MetaplayCore.Options.SupportedLogicVersions.MinVersion);
                activeLogicVersion = MetaplayCore.Options.SupportedLogicVersions.MinVersion;
            }
            else if (activeLogicVersion > MetaplayCore.Options.SupportedLogicVersions.MaxVersion)
            {
                _log.Error(
                    "ActiveLogicVersion {ActiveLogicVersion} is newer than the latest supported logic version {SupportedLogicVersionMax}. "
                    + "This is most likely an unintended version rollback which could result in data loss. "
                    + "Aborting. "
                    + "Previously successfully running server version was {PreviousVersion} and this server is {CurrentVersion}",
                    activeLogicVersion,
                    MetaplayCore.Options.SupportedLogicVersions.MaxVersion,
                    ServerBuildVersion.ToDisplayString(_state.ServerBuildVersion),
                    ServerBuildVersion.ToDisplayString(ServerBuildVersion.GetCurrent())
                    );
                throw new InvalidOperationException("Illegal SupportedLogicVersions. Max version too low.");
            }

            // Store updated activeLogicVersion (if changed)
            if (activeLogicVersion != _state.ClientCompatibilitySettings.ActiveLogicVersion)
            {
                var prevConfig = _state.ClientCompatibilitySettings;
                _state.ClientCompatibilitySettings = new ClientCompatibilitySettings(
                    activeLogicVersion,
                    prevConfig.RedirectEnabled,
                    prevConfig.RedirectServerEndpoint);
            }

            await _localizationsManager.Initialize();
            await _gameConfigManager.Initialize();
            InitializeMaintenanceMode();

            // Find & filter out any invalid broadcasts
            List<BroadcastMessage> invalidBroadcasts = _state.BroadcastMessages.Values.Where(broadcast => !broadcast.Params.IsContentsValid).ToList();
            if (invalidBroadcasts.Count > 0)
            {
                _log.Warning("Filtering out {NumInvalidBroadcasts} invalid broadcasts: {InvalidBroadcasts}", invalidBroadcasts.Count, PrettyPrint.Verbose(invalidBroadcasts));
                foreach (BroadcastMessage broadcast in invalidBroadcasts)
                    _state.BroadcastMessages.Remove(broadcast.Params.Id);
            }

            _state.ServerBuildVersion = ServerBuildVersion.GetCurrent();
        }

        async Task IGlobalStateManager.OnActiveGameConfigChanged(FullGameConfig config, bool isInitial)
        {
            // Sync experiments state with new config
            SyncExperimentsPlan syncPlan = PrepareSyncExperimentsWithConfig(config);
            await ActivatePlannedExperimentChangeAsync(syncPlan, uploadPatchesEvenIfNoChanges: isInitial);

            if (!isInitial)
            {
                // Persist changes to game config related global state
                await PersistStateIntermediate();

                // Broadcast to proxies
                PublishGameConfigOrExperimentUpdate();
            }
        }

        async Task IGlobalStateManager.OnActiveLocalizationsChanged()
        {
            // Ensure updated config is persisted
            await PersistStateIntermediate();

            // Publish update to proxies
            PublishMessage(EntityTopic.Member, new GlobalStateUpdateLocalizationsConfigVersion(_state.ActiveLocalizationsId, _state.LocalizationsDeliverables));
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            // Serialize and compress the state
            byte[] persistedPayload = SerializeToPersistedPayload(_state, resolver: null, logicVersion: null);

            // Persist in database
            PersistedGlobalState persisted = new PersistedGlobalState
            {
                EntityId        = _entityId.ToString(),
                PersistedAt     = DateTime.UtcNow,
                Payload         = persistedPayload,
                SchemaVersion   = _entityConfig.CurrentSchemaVersion,
                IsFinal         = isFinal,
            };

            if (isInitial)
                await MetaDatabase.Get().InsertAsync(persisted).ConfigureAwait(false);
            else
                await MetaDatabase.Get().UpdateAsync(persisted).ConfigureAwait(false);
        }

        [CommandHandler]
        void ReceiveAnalyticsEventFlush(AnalyticsEventFlush _)
        {
            _analyticsEventBatcher.Flush();
        }

        protected override sealed Task<MetaMessage> OnNewSubscriber(EntitySubscriber subscriber, MetaMessage message)
        {
            // Respond to proxies with the global state
            MetaSerialized<GlobalState> serialized = new MetaSerialized<GlobalState>(_state, MetaSerializationFlags.IncludeAll, logicVersion: null);
            return Task.FromResult<MetaMessage>(new GlobalStateSnapshot(serialized));
        }

        protected sealed override void OnSubscriberLost(EntitySubscriber subscriber)
        {
            // \note Also warns when GSP unsubscribes due to cluster shutdown
            _log.Warning("Subscriber unexpectedly lost: {SubscriberId} ({SubscriberActorRef})", subscriber.EntityId, subscriber.ActorRef);
        }

        [EntityAskHandler]
        public async Task<AddBroadcastMessageResponse> HandleAddBroadcastMessage(AddBroadcastMessage addBroadcast)
        {
            AddBroadcastMessageResponse reply;
            if (!addBroadcast.BroadcastParams.Validate(out string paramsValidationError))
                reply = AddBroadcastMessageResponse.Failure($"Invalid broadcast params: {paramsValidationError}");
            else
            {
                // Allocate id for broadcast
                BroadcastMessageParams broadcastParams = addBroadcast.BroadcastParams;
                _log.Info("Add broadcast: {Message}", PrettyPrint.Verbose(broadcastParams));
                int broadcastId = _state.RunningBroadcastMessageId++;
                broadcastParams.Id = broadcastId;
                _state.BroadcastMessages.Add(broadcastParams.Id, new BroadcastMessage(broadcastParams, new BroadcastMessageStats()));

                // Persist & forward to proxies (with broadcast.Id set)
                await PersistStateIntermediate();
                PublishMessage(EntityTopic.Member, addBroadcast);
                reply = AddBroadcastMessageResponse.Ok(broadcastId);
            }

            return reply;
        }

        [MessageHandler]
        public async Task HandleUpdateBroadcastMessage(UpdateBroadcastMessage updateBroadcast)
        {
            // Update broadcast in state
            BroadcastMessageParams broadcastParams = updateBroadcast.BroadcastParams;
            _log.Info("Update broadcast: {Message}", PrettyPrint.Verbose(broadcastParams));
            _state.BroadcastMessages[broadcastParams.Id].Params = broadcastParams;

            // Persist & forward to proxies
            await PersistStateIntermediate();
            PublishMessage(EntityTopic.Member, updateBroadcast);
        }

        [MessageHandler]
        public async Task HandleDeleteBroadcastMessage(DeleteBroadcastMessage deleteBroadcast)
        {
            // Delete broadcast
            _log.Info("Delete broadcast: #{BroadcastId}", deleteBroadcast.BroadcastId);
            _state.BroadcastMessages.Remove(deleteBroadcast.BroadcastId);

            // Persist & forward to proxies
            await PersistStateIntermediate();
            PublishMessage(EntityTopic.Member, deleteBroadcast);
        }

        [EntityAskHandler]
        public GlobalStateSnapshot HandleGlobalStateRequest(GlobalStateRequest _)
        {
            MetaSerialized<GlobalState> serialized = new MetaSerialized<GlobalState>(_state, MetaSerializationFlags.IncludeAll, logicVersion: null);
            return new GlobalStateSnapshot(serialized);
        }

        [EntityAskHandler]
        public GlobalStatusResponse HandleGlobalStatusRequest(GlobalStatusRequest _)
        {
            MaintenanceStatus maintenanceStatus = GetMaintenanceStatusAtNow();
            return new GlobalStatusResponse(maintenanceStatus, _state.ClientCompatibilitySettings, _state.StaticGameConfigId, _state.ActiveLocalizationsId);
        }

        [EntityAskHandler]
        public async Task<UpdateClientCompatibilitySettingsResponse> HandleUpdateClientCompatibilitySettingsRequest(UpdateClientCompatibilitySettingsRequest updateSettings)
        {
            // Store new config & save
            ClientCompatibilitySettings settings = updateSettings.Settings;
            _log.Info("Updating ClientCompatibilitySettings to {ClientCompatibilitySettings}", PrettyPrint.Verbose(settings));
            _state.ClientCompatibilitySettings = settings;

            // Persist & publish to proxies
            await PersistStateIntermediate();
            PublishMessage(EntityTopic.Member, updateSettings);

            // Respond
            return new UpdateClientCompatibilitySettingsResponse(isSuccess: true, errorMessage: null);
        }

        [EntityAskHandler]
        public async Task<SetDeveloperPlayerResponse> HandleSetDeveloperPlayerRequest(SetDeveloperPlayerRequest request)
        {
            _log.Info("Set developer player: {Message}", PrettyPrint.Verbose(request));

            if(request.IsDeveloper)
            {
                if(_state.DeveloperPlayerIds.Contains(request.PlayerId))
                    return SetDeveloperPlayerResponse.AlreadySatisfied;

                _state.DeveloperPlayerIds.Add(request.PlayerId);
            }
            else
            {
                if(!_state.DeveloperPlayerIds.Contains(request.PlayerId))
                    return SetDeveloperPlayerResponse.AlreadySatisfied;

                _state.DeveloperPlayerIds.Remove(request.PlayerId);
            }

            // Persist & forward to proxies
            await PersistStateIntermediate();
            PublishMessage(EntityTopic.Member, request);

            /* BEWARE:
             * Some race conditions are possible here:
             *
             * GSM is always the authority of developer-hood
             * GSP is always behind for GSM
             * Model.IsDeveloper is both ahead and behind of GSP.
             * Behind after PostLoad (GSP continues to evolve, model frozen)
             * Possibly ahead when controller uses api route and GSM informs the client directly.
             * (Theoretically the Model could shutdown and wake up before GSP is updated, getting a stale value)
             *
             * This behavior of Model.IsDeveloper is not a problem, as long as IsDeveloper is used to deliver developer-hood info to client.
             * Client cannot blindly trust the value anyway as it can change, so no problems there.
             *
             * However, if IsDeveloper is used for other purposes, Model.IsDeveloper should always be behind GSP.
             * This means the GSM cannot inform Model of flag change as it can only happen after GSP update.
             */
            CastMessage(request.PlayerId, new InternalPlayerDeveloperStatusChanged(request.IsDeveloper));

            return SetDeveloperPlayerResponse.Success;
        }

        /// <summary>
        /// Publishes game config changes to all GSPs, making it active.
        /// </summary>
        void PublishGameConfigOrExperimentUpdate()
        {
            PublishMessage(EntityTopic.Member, new GlobalStateUpdateGameConfig(
                staticConfigId:                 _state.StaticGameConfigId,
                dynamicConfigId:                MetaGuid.None, // _state.DynamicGameConfigVersion
                sharedConfigDeliverables:       _state.SharedGameConfigDeliverables,
                playerExperiments:              _state.PlayerExperiments,
                sharedPatchesForPlayers:        _state.SharedGameConfigPatchesForPlayersContentHash,
                sharedPatchesForTesters:        _state.SharedGameConfigPatchesForTestersContentHash,
                timestamp:                      _state.LatestGameConfigUpdate));
        }

        /// <summary>
        /// Handle a list of consumed broadcast messages that are sent sent periodically from a shard
        /// </summary>
        /// <param name="fromEntityId"></param>
        /// <param name="info"></param>
        [MessageHandler]
        public void HandleBroadcastConsumedCountInfo(EntityId fromEntityId, BroadcastConsumedCountInfo info)
        {
            foreach ((int broadcastId, int count) in info.BroadcastsConsumed)
            {
                // Add to a broadcast's received count if that broadcast still exists
                if (_state.BroadcastMessages.ContainsKey(broadcastId))
                    _state.BroadcastMessages[broadcastId].Stats.ReceivedCount += count;
            }
        }

        [MessageHandler]
        void HandleGlobalStateForgetActivableAndOfferStatistics(GlobalStateForgetActivableAndOfferStatistics _)
        {
            if (_state.LegacyActivableStatistics != null
             || _state.LegacyMetaOfferStatistics != null)
            {
                _log.Info("Forgetting activable and offer statistics");

                _state.LegacyActivableStatistics = null;
                _state.LegacyMetaOfferStatistics = null;
            }
        }

        protected override sealed void OnTerminated(Terminated terminated)
        {
            _log.Info("LOST SUBSCRIBER: {Actor}", terminated.ActorRef);
        }

        void EmitAnalyticsEvent(ServerEventBase payload)
        {
            AnalyticsEventSpec      eventSpec       = AnalyticsEventRegistry.GetEventSpec(payload.GetType());
            string                  eventType       = eventSpec.EventType;
            MetaTime                collectedAt     = MetaTime.Now;
            MetaUInt128             uniqueId        = new MetaUInt128((ulong)collectedAt.MillisecondsSinceEpoch, _analyticsEventRandom.NextULong());
            int                     schemaVersion   = eventSpec.SchemaVersion;
            ServerAnalyticsContext  context         = new ServerAnalyticsContext();

            _analyticsEventBatcher.Enqueue(_entityId, collectedAt, modelTime: collectedAt, uniqueId, eventType, schemaVersion, payload, context, labels: null, resolver: null, logicVersion: null);
        }

        static byte[] NewClusterNonce()
        {
            return RandomNumberGenerator.GetBytes(32);
        }

        // Expose internals to internal EntityComponents
        IMetaLogger IGlobalStateManager.Logger => _log;
        GlobalState IGlobalStateManager.State  => _state;
        EntityActor IGlobalStateManager.Actor  => this;
    }

    public class DefaultGlobalStateManager : GlobalStateManagerBase<DefaultGlobalState>
    {
        public DefaultGlobalStateManager(EntityId entityId) : base(entityId)
        {
        }

        protected override Task<DefaultGlobalState> InitializeNew()
        {
            // Create new state
            DefaultGlobalState state = new DefaultGlobalState();
            return Task.FromResult(state);
        }
    }

    public static class GlobalStateManager
    {
        public static readonly EntityId EntityId = EntityId.Create(EntityKindCloudCore.GlobalStateManager, 0);
    }
}
