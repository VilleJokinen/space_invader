// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    /// <summary>
    /// Reports a batch of MetaActivable statistics information.
    ///
    /// Also includes additional information for certain kinds of activables
    /// (namely, MetaOffers). They're sent together with general activables
    /// info to ensure they're updated at the same time.
    ///
    /// Sent by StatsCollectorProxys periodically to StatsCollectorManager.
    /// </summary>
    [MetaMessage(MessageCodesCore.MetaActivableStatisticsInfo, MessageDirection.ServerInternal)]
    public class MetaActivableStatisticsInfo : MetaMessage
    {
        public FullMetaActivableStatistics  ActivableStatistics;
        public MetaOffersStatistics         MetaOffersStatistics;

        MetaActivableStatisticsInfo(){ }
        public MetaActivableStatisticsInfo(FullMetaActivableStatistics activableStatistics, MetaOffersStatistics metaOffersStatistics)
        {
            ActivableStatistics = activableStatistics ?? throw new ArgumentNullException(nameof(activableStatistics));
            MetaOffersStatistics = metaOffersStatistics ?? throw new ArgumentNullException(nameof(metaOffersStatistics));
        }
    }

    [MetaMessage(MessageCodesCore.StatsCollectorStateRequest, MessageDirection.ServerInternal)]
    public class StatsCollectorStateRequest : MetaMessage
    {
        public static readonly StatsCollectorStateRequest Instance = new StatsCollectorStateRequest();
    }

    [MetaMessage(MessageCodesCore.StatsCollectorStateResponse, MessageDirection.ServerInternal)]
    public class StatsCollectorStateResponse : MetaMessage
    {
        public MetaSerialized<StatsCollectorState> State { get; private set; }

        StatsCollectorStateResponse() { }
        public StatsCollectorStateResponse(MetaSerialized<StatsCollectorState> state)
        {
            State = state;
        }
    }

    /// <summary>
    /// Query how many items of a specific <see cref="EntityKind"/> are persisted in the database.
    /// </summary>
    [MetaMessage(MessageCodesCore.StatsCollectorDatabaseEntityCountRequest, MessageDirection.ServerInternal)]
    public class StatsCollectorDatabaseEntityCountRequest : MetaMessage
    {
        public EntityKind EntityKind { get; private set; }

        StatsCollectorDatabaseEntityCountRequest() { }
        public StatsCollectorDatabaseEntityCountRequest(EntityKind entityKind) { EntityKind = entityKind; }
    }

    [MetaMessage(MessageCodesCore.StatsCollectorDatabaseEntityCountResponse, MessageDirection.ServerInternal)]
    public class StatsCollectorDatabaseEntityCountResponse : MetaMessage
    {
        public int      EntityCount { get; private set; }
        public MetaTime UpdatedAt   { get; private set; }

        StatsCollectorDatabaseEntityCountResponse() { }
        public StatsCollectorDatabaseEntityCountResponse(int entityCount, MetaTime updatedAt) { EntityCount = entityCount; UpdatedAt = updatedAt; }
    }

    /// <summary>
    /// Query how many Entities of each kind are currently live in the cluster.
    /// </summary>
    [MetaMessage(MessageCodesCore.StatsCollectorLiveEntityCountRequest, MessageDirection.ServerInternal)]
    public class StatsCollectorLiveEntityCountRequest : MetaMessage
    {
        public static readonly StatsCollectorLiveEntityCountRequest Instance = new StatsCollectorLiveEntityCountRequest();
    }

    [MetaMessage(MessageCodesCore.StatsCollectorLiveEntityCountResponse, MessageDirection.ServerInternal)]
    public class StatsCollectorLiveEntityCountResponse : MetaMessage
    {
        public OrderedDictionary<EntityKind, int> LiveEntityCounts { get; private set; }

        StatsCollectorLiveEntityCountResponse() { }
        public StatsCollectorLiveEntityCountResponse(OrderedDictionary<EntityKind, int> liveEntityCounts) { LiveEntityCounts = liveEntityCounts; }
    }

    #region ActiveEntityLists

    /// <summary>
    /// Information about an active Entity to be displayed in Dashboard in Recently Active entities lists. Use
    /// <see cref="EntityId.Kind"/> of <see cref="IActiveEntityInfo.EntityId"/> to determine which type of an
    /// actor published the info.
    /// </summary>
    [MetaSerializable]
    public interface IActiveEntityInfo
    {
        EntityId EntityId { get; }
        string DisplayName { get; }
        MetaTime CreatedAt { get; }
        MetaTime ActivityAt { get; }
    }

    /// <summary>
    /// Wrapper around an <see cref="IActiveEntityInfo"> so that we can send it
    /// over an EventStream (which can only handle concrete types)
    /// </summary>
    public sealed class ActiveEntityInfoEnvelope
    {
        public readonly IActiveEntityInfo Value;
        public ActiveEntityInfoEnvelope(IActiveEntityInfo value) { Value = value; }
    }

    /// <summary>
    /// Information about an active player
    /// </summary>
    [MetaSerializableDerived(100)]
    public class ActivePlayerInfo : IActiveEntityInfo
    {
        [MetaMember(1)] public EntityId             EntityId        { get; private set; }
        [MetaMember(2)] public string               DisplayName     { get; private set; }
        [MetaMember(3)] public MetaTime             CreatedAt       { get; private set; }
        [MetaMember(4)] public MetaTime             ActivityAt      { get; private set; }

        [MetaMember(5)] public int                  Level           { get; private set; }
        [MetaMember(6)] public PlayerDeletionStatus DeletionStatus  { get; private set; }
        [MetaMember(7)] public F64                  TotalIapSpend   { get; private set; }
        [MetaMember(8)] public bool                 IsDeveloper     { get; private set; }

        public ActivePlayerInfo() { }
        public ActivePlayerInfo(EntityId entityId, string displayName, MetaTime createdAt, MetaTime activityAt, int level, PlayerDeletionStatus deletionStatus, F64 totalIapSpend, bool isDeveloper)
        {
            EntityId = entityId;
            DisplayName = displayName;
            CreatedAt = createdAt;
            ActivityAt = activityAt;
            Level = level;
            DeletionStatus = deletionStatus;
            TotalIapSpend = totalIapSpend;
            IsDeveloper = isDeveloper;
        }
    }

    /// <summary>
    /// * -> SCM Request to fetch current NumConcurrents
    /// </summary>
    [MetaMessage(MessageCodesCore.StatsCollectorNumConcurrentsRequest, MessageDirection.ServerInternal)]
    public class StatsCollectorNumConcurrentsRequest : MetaMessage
    {
        public static readonly StatsCollectorNumConcurrentsRequest Instance = new StatsCollectorNumConcurrentsRequest();
    }
    [MetaMessage(MessageCodesCore.StatsCollectorNumConcurrentsResponse, MessageDirection.ServerInternal)]
    public class StatsCollectorNumConcurrentsResponse : MetaMessage
    {
        public int NumConcurrents { get; private set; }

        StatsCollectorNumConcurrentsResponse() { }
        public StatsCollectorNumConcurrentsResponse(int numConcurrents)
        {
            NumConcurrents = numConcurrents;
        }
    }

    /// <summary>
    /// Periodically inform the StatsCollectorManager of the recently active entities on each shard.
    /// </summary>
    [MetaMessage(MessageCodesCore.UpdateShardActiveEntityList, MessageDirection.ServerInternal)]
    public class UpdateShardActiveEntityList : MetaMessage
    {
        public string                                           ShardName       { get; private set; }
        public Dictionary<EntityKind, List<IActiveEntityInfo>>  ActiveEntities  { get; private set; }

        UpdateShardActiveEntityList() { }
        public UpdateShardActiveEntityList(string shardName, Dictionary<EntityKind, List<IActiveEntityInfo>> entities)
        {
            ShardName = shardName;
            ActiveEntities = entities;
        }
    }

    /// <summary>
    /// Request a list of recently active entities. StatsCollectorManager responds with a <see cref="ActiveEntitiesResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.ActiveEntitiesRequest, MessageDirection.ServerInternal)]
    public class ActiveEntitiesRequest : MetaMessage
    {
        public EntityKind EntityKind { get; private set; }

        ActiveEntitiesRequest() { }
        public ActiveEntitiesRequest(EntityKind entityKind) { EntityKind = entityKind; }
    }

    /// <summary>
    /// Response to a <see cref="ActiveEntitiesRequest"/>. Contains information about recently active entities.
    /// </summary>
    [MetaMessage(MessageCodesCore.ActiveEntitiesResponse, MessageDirection.ServerInternal)]
    public class ActiveEntitiesResponse : MetaMessage
    {
        public List<IActiveEntityInfo> ActiveEntities { get; private set; }

        ActiveEntitiesResponse() { }
        public ActiveEntitiesResponse(List<IActiveEntityInfo> activeEntities) { ActiveEntities = activeEntities; }
    }

    #endregion // ActiveEntityLists

    /// SCP -> SCM info to update stats on the its pod.
    /// </summary>
    [MetaMessage(MessageCodesCore.StatsCollectorNumConcurrentsUpdate, MessageDirection.ServerInternal)]
    public class StatsCollectorNumConcurrentsUpdate : MetaMessage
    {
        public string   ShardName       { get; private set; }
        public int      NumConcurrents  { get; private set; }
        public MetaTime SampledAt       { get; private set; }

        StatsCollectorNumConcurrentsUpdate() { }
        public StatsCollectorNumConcurrentsUpdate(string shardName, int numConcurrents, MetaTime sampledAt)
        {
            ShardName = shardName;
            NumConcurrents = numConcurrents;
            SampledAt = sampledAt;
        }
    }

    /// <summary>
    /// SCP -> SCM cast message to update experiment statistics
    /// </summary>
    [MetaMessage(MessageCodesCore.StatsCollectorPlayerExperimentAssignmentSampleUpdate, MessageDirection.ServerInternal)]
    public class StatsCollectorPlayerExperimentAssignmentSampleUpdate : MetaMessage
    {
        /// <summary>
        /// A single of the sampled operations.
        /// </summary>
        [MetaSerializable]
        public struct Sample
        {
            [MetaMember(1)] public ExperimentVariantId  IntoVariant;
            [MetaMember(2)] public ExperimentVariantId  FromVariant;
            [MetaMember(3)] public bool                 HasFromVariant;

            public Sample(ExperimentVariantId intoVariant, ExperimentVariantId fromVariant, bool hasFromVariant)
            {
                IntoVariant = intoVariant;
                FromVariant = fromVariant;
                HasFromVariant = hasFromVariant;
            }
        }

        public OrderedDictionary<PlayerExperimentId, OrderedDictionary<EntityId, Sample>> ExperimentSamples { get; private set; }

        StatsCollectorPlayerExperimentAssignmentSampleUpdate() { }
        public StatsCollectorPlayerExperimentAssignmentSampleUpdate(OrderedDictionary<PlayerExperimentId, OrderedDictionary<EntityId, Sample>> experimentSample)
        {
            ExperimentSamples = experimentSample;
        }
    }

    [Table("StatsCollectors")]
    public class PersistedStatsCollector : IPersistedEntity
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
        public byte[]   Payload         { get; set; }   // TaggedSerialized<StatsCollectorState>

        [Required]
        public int      SchemaVersion   { get; set; }   // Schema version for object

        [Required]
        public bool     IsFinal         { get; set; }
    }

    // StatsCollectorState

    [MetaSerializable]
    [SupportedSchemaVersions(1, 1)]
    public class StatsCollectorState : ISchemaMigratable
    {
        [MetaMember(1)] public FullMetaActivableStatistics  ActivableStatistics { get; set; } = new FullMetaActivableStatistics();
        [MetaMember(2)] public MetaOffersStatistics         MetaOfferStatistics { get; set; } = new MetaOffersStatistics();
        /// <summary>
        /// Used for doing a one-time migration of statistics from
        /// <see cref="GlobalStateManager"/> to <see cref="StatsCollectorManager"/>.
        /// </summary>
        /// <remarks>
        /// This migration was added in R17, so assuming it has been deployed
        /// in all relevant environments by R18, it can be removed in R18.
        /// At the same time, also the legacy statistics from <see cref="GlobalState"/>
        /// can be removed.
        /// #r17 #r18
        /// </remarks>
        [MetaMember(3)] public bool HasMigratedActivableAndOfferStatisticsFromGlobalState { get; set; } = false;

        [MetaMember(4)] public OrderedDictionary<PlayerExperimentId, PlayerExperimentStatistics> PlayerExperimentStatistics { get; set; } = new OrderedDictionary<PlayerExperimentId, PlayerExperimentStatistics>();
        [MetaMember(5)] public bool HasMigratedPlayerExperimentStatisticsFromGlobalState { get; set; } = false;

        // Accurate tracking of database item counts.
        [MetaMember(6)] public MetaTime                         DatabaseItemCountsUpdatedAt { get; set; }
        [MetaMember(7)] public OrderedDictionary<string, int[]> DatabaseShardItemCounts     { get; set; }
    }

    public class StatsCollectorInMemoryState
    {
        public struct ProxyConcurrentsStatistics
        {
            public MetaTime ExpireAt;
            public int      NumConcurrents;

            public ProxyConcurrentsStatistics(MetaTime expireAt, int numConcurrents)
            {
                ExpireAt = expireAt;
                NumConcurrents = numConcurrents;
            }
        }

        public OrderedDictionary<string, ProxyConcurrentsStatistics> ProxyConcurrents = new OrderedDictionary<string, ProxyConcurrentsStatistics>(); // shard name -> stats
    }

    // StatsCollectorManager

    [EntityConfig]
    internal sealed class StatsCollectorManagerConfig : PersistedEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.StatsCollectorManager;
        public override Type                EntityActorType         => typeof(StatsCollectorManager);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(numServiceShards: 1);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(5);
    }

    public class StatsCollectorManager : PersistedEntityActor<PersistedStatsCollector, StatsCollectorState>
    {
        public const int                                MaxActiveEntities = 50;

        public static readonly EntityId                 EntityId = EntityId.Create(EntityKindCloudCore.StatsCollectorManager, 0);

        protected override sealed AutoShutdownPolicy    ShutdownPolicy      => AutoShutdownPolicy.ShutdownNever();
        protected override sealed TimeSpan              SnapshotInterval    => TimeSpan.FromMinutes(3);

        StatsCollectorState                             _state;
        StatsCollectorInMemoryState                     _inMemory = new StatsCollectorInMemoryState();

        // Number of active entities on each shard by entity kind
        Dictionary<string, Dictionary<EntityKind, int>> _shardLiveEntityCounts  = new Dictionary<string, Dictionary<EntityKind, int>>();
        static readonly TimeSpan                        LogStatsInterval        = TimeSpan.FromSeconds(30);
        internal class LogLiveEntityCounts { public static readonly LogLiveEntityCounts Instance = new LogLiveEntityCounts(); }

        // In-memory list of entities that were recently active on all shards
        Dictionary<string, Dictionary<EntityKind, List<IActiveEntityInfo>>> _shardActiveEntities    = new Dictionary<string, Dictionary<EntityKind, List<IActiveEntityInfo>>>();
        Dictionary<EntityKind, List<IActiveEntityInfo>>                     _globalActiveEntities   = new Dictionary<EntityKind, List<IActiveEntityInfo>>();

        // Database item count tracking
        static readonly TimeSpan                        UpdateDatabaseItemCountInterval = TimeSpan.FromMinutes(15);
        internal class UpdateDatabaseItemCounts { public static readonly UpdateDatabaseItemCounts Instance = new UpdateDatabaseItemCounts(); }

        class StatsMigratedFromGlobalStateCommand { public static readonly StatsMigratedFromGlobalStateCommand Instance = new StatsMigratedFromGlobalStateCommand(); }

        class ActivableAndOfferStatsMigratedFromGlobalStateCommand { public static readonly ActivableAndOfferStatsMigratedFromGlobalStateCommand Instance = new ActivableAndOfferStatsMigratedFromGlobalStateCommand(); }
        class ExperimentSamplesMigratedFromGlobalStateCommand { public static readonly ExperimentSamplesMigratedFromGlobalStateCommand Instance = new ExperimentSamplesMigratedFromGlobalStateCommand(); }

        public StatsCollectorManager(EntityId entityId) : base(entityId)
        {
            // Initialize empty global active entity lists
            foreach (EntityKind kind in EntityKindRegistry.AllValues)
                _globalActiveEntities.Add(kind, new List<IActiveEntityInfo>());
        }

        protected override void PreStart()
        {
            base.PreStart();

            StartPeriodicTimer(TimeSpan.FromSeconds(5), LogStatsInterval, LogLiveEntityCounts.Instance);

            // Use manual timer to avoid risking unbounded growth of the inbox, in case the update takes longer than the interval
            Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(10), _self, UpdateDatabaseItemCounts.Instance, _self);
        }

        protected override sealed async Task Initialize()
        {
            PersistedStatsCollector persisted = await MetaDatabase.Get().TryGetAsync<PersistedStatsCollector>(_entityId.ToString());
            await InitializePersisted(persisted);
        }

        protected override sealed Task<StatsCollectorState> InitializeNew()
        {
            StatsCollectorState state = new StatsCollectorState();

            return Task.FromResult(state);
        }

        protected override sealed Task<StatsCollectorState> RestoreFromPersisted(PersistedStatsCollector persisted)
        {
            StatsCollectorState state = DeserializePersistedPayload<StatsCollectorState>(persisted.Payload, resolver: null, logicVersion: null);

            return Task.FromResult(state);
        }

        protected override sealed async Task PostLoad(StatsCollectorState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _state = payload;

            // Migrate some statistics away from GlobalStateManager, to StatsCollectorManager
            // (unless already migrated).
            // \note This migration was added in R17, so assuming it has been deployed
            //       in all relevant environments by R18, it can be removed in R18.
            //       #r17 #r18
            if (!_state.HasMigratedActivableAndOfferStatisticsFromGlobalState)
            {
                MetaDebug.Assert(_state.ActivableStatistics.KindStatistics.Count == 0, $"{nameof(_state.ActivableStatistics)} is nonempty even though initial statistics haven't yet been migrated from GlobalState");
                MetaDebug.Assert(!_state.MetaOfferStatistics.HasAny, $"{nameof(_state.MetaOfferStatistics)} is nonempty even though initial statistics haven't yet been migrated from GlobalState");

                GlobalStateSnapshot globalStateSnapshot = await EntityAskAsync<GlobalStateSnapshot>(GlobalStateManager.EntityId, GlobalStateRequest.Instance);
                GlobalState         globalState         = globalStateSnapshot.GlobalState.Deserialize(resolver: null, logicVersion: null);

                _state.ActivableStatistics = globalState.LegacyActivableStatistics;
                _state.MetaOfferStatistics = globalState.LegacyMetaOfferStatistics;
                _state.HasMigratedActivableAndOfferStatisticsFromGlobalState = true;

                // In ActivableAndOfferStatsMigratedFromGlobalStateCommand handler, persist our state,
                // and then send GlobalStateForgetActivableAndOfferStatistics to GlobalStateManager.
                // We need to delay it (via this command-to-self) because if this entity
                // was just created, the initial persist hasn't been done yet, and thus
                // PersistStateIntermediate won't work here.
                _self.Tell(ActivableAndOfferStatsMigratedFromGlobalStateCommand.Instance);
            }
            else
            {
                // Tell GlobalStateManager to forget the statistics that we've now migrated to StatsCollectorManager.
                // Note that we're in the branch where we had already migrated the state from GSM in the past,
                // so this message is likely redundant. But there's no guarantee that any past message was actually
                // received and processed by GSM, so we send it every time. We could have GSM respond to us and keep
                // track of that in our state, but this is simpler.
                CastMessage(GlobalStateManager.EntityId, GlobalStateForgetActivableAndOfferStatistics.Instance);
            }

            // One-time migration for offer statistics, added when revenue collection was implemented.
            // \note This was added in R18, so assuming it has been deployed in all relevant environments
            //       by R18, it can be removed in R18.
            //       #r17 #r18
            if (!_state.MetaOfferStatistics.HasInitializedLegacyRevenues)
            {
                ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
                if (activeGameConfig != null) // Tolerate missing config
                {
                    ISharedGameConfig sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;
                    _state.MetaOfferStatistics.InitializeLegacyRevenues(sharedGameConfig);
                }
            }

            // Migrate Experiment samples.
            // See HasMigratedActivableAndOfferStatisticsFromGlobalState for logic
            if (!_state.HasMigratedPlayerExperimentStatisticsFromGlobalState)
            {
                GlobalStateSnapshot globalStateSnapshot = await EntityAskAsync<GlobalStateSnapshot>(GlobalStateManager.EntityId, GlobalStateRequest.Instance);
                GlobalState         globalState         = globalStateSnapshot.GlobalState.Deserialize(resolver: null, logicVersion: null);

                foreach ((PlayerExperimentId experimentId, PlayerExperimentGlobalStatistics globalExperimentStats) in globalState.PlayerExperimentsStats)
                {
                    if (!_state.PlayerExperimentStatistics.TryGetValue(experimentId, out PlayerExperimentStatistics statistics))
                    {
                        statistics = new PlayerExperimentStatistics();
                        _state.PlayerExperimentStatistics.Add(experimentId, statistics);
                    }

                    statistics.PlayerSample = globalExperimentStats.LegacyPlayerSample;
                    foreach ((ExperimentVariantId variantId, PlayerExperimentGlobalStatistics.VariantStats variantGlobalStats) in globalExperimentStats.Variants)
                    {
                        if (!statistics.Variants.TryGetValue(variantId, out PlayerExperimentStatistics.VariantStats variantStatistics))
                        {
                            variantStatistics = new PlayerExperimentStatistics.VariantStats();
                            statistics.Variants.Add(variantId, variantStatistics);
                        }
                        variantStatistics.PlayerSample = variantGlobalStats.LegacyPlayerSample;
                    }
                }
                _state.HasMigratedPlayerExperimentStatisticsFromGlobalState = true;
                _self.Tell(ExperimentSamplesMigratedFromGlobalStateCommand.Instance);
            }
            else
            {
                CastMessage(GlobalStateManager.EntityId, GlobalStateForgetPlayerExperimentSamples.Instance);
            }

            // If we don't yet have database item counts, get an estimate so we always have valid values.
            if (_state.DatabaseShardItemCounts == null)
            {
                _state.DatabaseShardItemCounts = await MetaDatabase.Get(QueryPriority.Normal).EstimateTableItemCountsAsync();
                _state.DatabaseItemCountsUpdatedAt = MetaTime.Now;
            }
        }

        [CommandHandler]
        async Task HandleActivableAndOfferStatsMigratedFromGlobalStateCommand(ActivableAndOfferStatsMigratedFromGlobalStateCommand _)
        {
            // We've migrated the stats from GlobalState.
            // Persist right away to ensure we don't forget the migrated state.
            // After that, we can safely tell GlobalStateManager to forget its copy of the state.
            await PersistStateIntermediate();
            _log.Info("Migrated activable and offer statistics from GlobalState, and persisted");

            // Tell GlobalStateManager to forget the statistics that we've now migrated to StatsCollectorManager.
            CastMessage(GlobalStateManager.EntityId, GlobalStateForgetActivableAndOfferStatistics.Instance);
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator<StatsCollectorState>();
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, migrator.CurrentSchemaVersion);

            // Serialize and compress the state
            byte[] persistedPayload = SerializeToPersistedPayload(_state, resolver: null, logicVersion: null);

            // Persist in database
            PersistedStatsCollector persisted = new PersistedStatsCollector
            {
                EntityId        = _entityId.ToString(),
                PersistedAt     = DateTime.UtcNow,
                Payload         = persistedPayload,
                SchemaVersion   = migrator.CurrentSchemaVersion,
                IsFinal         = isFinal,
            };

            if (isInitial)
                await MetaDatabase.Get().InsertAsync(persisted).ConfigureAwait(false);
            else
                await MetaDatabase.Get().UpdateAsync(persisted).ConfigureAwait(false);
        }

        [EntityAskHandler]
        StatsCollectorStateResponse HandleStateRequest(StatsCollectorStateRequest _)
        {
            MetaSerialized<StatsCollectorState> serializedState = new MetaSerialized<StatsCollectorState>(_state, MetaSerializationFlags.IncludeAll, logicVersion: null);
            return new StatsCollectorStateResponse(serializedState);
        }

        /// <summary>
        /// Handle report from StatsCollectorProxy concerning statistics about activables.
        /// The reported statistics get combined to StatsCollectorManager's total statistics.
        /// </summary>
        [MessageHandler]
        public void HandleMetaActivableStatisticsInfo(MetaActivableStatisticsInfo info)
        {
            AggregateActivableStatisticsInPlace(
                src: info.ActivableStatistics,
                dst: _state.ActivableStatistics);

            AggregateMetaOffersStatisticsInPlace(
                src: info.MetaOffersStatistics,
                dst: _state.MetaOfferStatistics);
        }

        static void AggregateActivableStatisticsInPlace(FullMetaActivableStatistics src, FullMetaActivableStatistics dst)
        {
            foreach ((MetaActivableKindId kindId, MetaActivableKindStatistics srcKindStats) in src.KindStatistics)
            {
                MetaActivableKindStatistics dstKindStats = StatsUtil.GetOrAddDefaultConstructed(dst.KindStatistics, kindId);
                StatsUtil.AggregateValues(dstKindStats.ActivableStatistics, srcKindStats.ActivableStatistics, MetaActivableStatistics.Sum);
            }
        }

        static void AggregateMetaOffersStatisticsInPlace(MetaOffersStatistics src, MetaOffersStatistics dst)
        {
            foreach ((MetaOfferGroupId groupId, MetaOfferGroupStatistics srcGroupStats) in src.OfferGroups)
            {
                MetaOfferGroupStatistics dstGroupStats = StatsUtil.GetOrAddDefaultConstructed(dst.OfferGroups, groupId);
                StatsUtil.AggregateValues(dstGroupStats.OfferPerGroupStatistics, srcGroupStats.OfferPerGroupStatistics, MetaOfferStatistics.Sum);
            }

            StatsUtil.AggregateValues(dst.Offers, src.Offers, MetaOfferStatistics.Sum);
        }

        #region DatabaseItemCounts

        [CommandHandler]
        void HandleUpdateDatabaseItemCounts(UpdateDatabaseItemCounts _)
        {
            IScheduler scheduler = Context.System.Scheduler;

            // Count the items in background. MySQL is quite slow at counting and may take even minutes
            // with a large database. When results are ready, store them in the state.
            ContinueTaskOnActorContext(
                Task.Run(async () =>
                {
                    try
                    {
                        // \note These queries can take minutes with a large database, and thus block the Lowest pipeline for a good while.
                        return await MetaDatabase.Get(QueryPriority.Lowest).GetTableItemCountsAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        // Use manual timer to avoid risking unbounded growth of the inbox, in case the update takes longer than the interval.
                        // \note The duration it takes to run the operation is included interval of the operations, but that's okay.
                        scheduler.ScheduleTellOnce(UpdateDatabaseItemCountInterval, _self, UpdateDatabaseItemCounts.Instance, _self);
                    }
                }),
                (OrderedDictionary<string, int[]> shardItemCounts) =>
                {
                    _state.DatabaseShardItemCounts = shardItemCounts;
                    _state.DatabaseItemCountsUpdatedAt = MetaTime.Now;
                },
                ex =>
                {
                    _log.Error("Failed to update database item counts: {Exception}", ex);
                });
        }

        [EntityAskHandler]
        StatsCollectorDatabaseEntityCountResponse HandleStatsCollectorDatabaseEntityCountRequest(StatsCollectorDatabaseEntityCountRequest request)
        {
            PersistedEntityConfig   entityConfig    = EntityConfigRegistry.Instance.GetPersistedConfig(request.EntityKind);
            DatabaseItemSpec        itemSpec        = DatabaseTypeRegistry.GetItemSpec(entityConfig.PersistedType);
            int                     entityCount     = _state.DatabaseShardItemCounts[itemSpec.TableName].Sum();
            return new StatsCollectorDatabaseEntityCountResponse(entityCount, _state.DatabaseItemCountsUpdatedAt);
        }

        #endregion

        #region LiveEntityCounts

        [CommandHandler]
        void HandleLogLiveEntityCounts(LogLiveEntityCounts _)
        {
            if (_log.IsInformationEnabled)
            {
                IEnumerable<string> lines = _shardLiveEntityCounts.Select(kvp => $"  {kvp.Key}: {PrettyPrint.Compact(kvp.Value)}");
                _log.Info("Global entity counts:\n{0}", string.Join("\n", lines));
            }
        }

        [MessageHandler]
        public void HandleUpdateShardEntityCount(UpdateShardLiveEntityCount updateStats)
        {
            //_log.Info("Received entity {0} count: {1}", updateStats.EntityKind, updateStats.EntityCount);

            // Make sure we have LiveEntityCounts dictionary for given shard
            if (!_shardLiveEntityCounts.TryGetValue(updateStats.ShardName, out Dictionary<EntityKind, int> liveEntityCounts))
            {
                liveEntityCounts = new Dictionary<EntityKind, int>();
                _shardLiveEntityCounts[updateStats.ShardName] = liveEntityCounts;
            }

            // Update entity count
            liveEntityCounts[updateStats.EntityKind] = updateStats.LiveEntityCount;
        }

        [EntityAskHandler]
        StatsCollectorLiveEntityCountResponse HandleStatsCollectorLiveEntityCountRequest(StatsCollectorLiveEntityCountRequest _)
        {
            // Initialize with zero for each EntityKind (ensure there's a value for each known EntityKind)
            OrderedDictionary<EntityKind, int> liveEntityCounts = new OrderedDictionary<EntityKind, int>();
            foreach (EntityKind kind in EntityKindRegistry.AllValues)
                liveEntityCounts[kind] = 0;

            // Sum entity counts from all shards
            foreach (Dictionary<EntityKind, int> liveEntities in _shardLiveEntityCounts.Values)
            {
                foreach ((EntityKind entityKind, int numLive) in liveEntities)
                    liveEntityCounts[entityKind] += numLive;
            }

            return new StatsCollectorLiveEntityCountResponse(liveEntityCounts);
        }

        #endregion

        #region Active entities

        /// <summary>
        /// Handle a list of time-ordered active entities sent from a shard. These are sent
        /// periodically by the shards.
        /// </summary>
        [MessageHandler]
        public void HandleUpdateShardActiveEntityList(UpdateShardActiveEntityList update)
        {
            // Store per-shard list.
            _shardActiveEntities[update.ShardName] = update.ActiveEntities;

            // Combine the updated per-shard entity lists with the global lists
            foreach ((EntityKind kind, List<IActiveEntityInfo> activeEntities) in update.ActiveEntities)
            {
                _globalActiveEntities[kind] =
                    _globalActiveEntities[kind].Concat(activeEntities)
                    .OrderByDescending(item => item.ActivityAt)
                    .DistinctBy(item => item.EntityId)
                    .Take(MaxActiveEntities)
                    .ToList();
            }
        }

        /// <summary>
        /// Respond to a request for the active entities list
        /// </summary>
        /// <returns></returns>
        [EntityAskHandler]
        public ActiveEntitiesResponse HandleActiveEntitiesRequest(ActiveEntitiesRequest request)
        {
            return new ActiveEntitiesResponse(_globalActiveEntities.GetValueOrDefault(request.EntityKind, new List<IActiveEntityInfo>()));
        }

        #endregion

        #region Concurrents

        [MessageHandler]
        void HandleStatsCollectorNumConcurrentsUpdate(EntityId fromEntityId, StatsCollectorNumConcurrentsUpdate update)
        {
            PruneExpiredProxyConcurrents();

            _inMemory.ProxyConcurrents[update.ShardName] = new StatsCollectorInMemoryState.ProxyConcurrentsStatistics(
                expireAt: update.SampledAt + MetaDuration.FromMinutes(5),
                numConcurrents: update.NumConcurrents);
        }

        [EntityAskHandler]
        StatsCollectorNumConcurrentsResponse HandleStatsCollectorNumConcurrentsRequest(StatsCollectorNumConcurrentsRequest _)
        {
            PruneExpiredProxyConcurrents();

            // Sum recent data
            int numConcurrents = 0;
            foreach (StatsCollectorInMemoryState.ProxyConcurrentsStatistics proxyStats in _inMemory.ProxyConcurrents.Values)
                numConcurrents += proxyStats.NumConcurrents;

            return new StatsCollectorNumConcurrentsResponse(numConcurrents);
        }

        void PruneExpiredProxyConcurrents()
        {
            // Prune old data
            bool removedSomething = true;
            while (removedSomething)
            {
                removedSomething = false;
                foreach ((string shardName, StatsCollectorInMemoryState.ProxyConcurrentsStatistics stats) in _inMemory.ProxyConcurrents)
                {
                    if (MetaTime.Now >= stats.ExpireAt)
                    {
                        _log.Warning("StatsCollectorProxy on {ShardName} did not update concurrent count within time limit. Assuming previous values are stale and removing its contribution of {NumConcurrents}.", shardName, stats.NumConcurrents);
                        _inMemory.ProxyConcurrents.Remove(shardName);
                        removedSomething = true;
                        break;
                    }
                }
            }
        }

        #endregion // Concurrents

        #region Experiment player samples

        [CommandHandler]
        async Task HandleExperimentSamplesMigratedFromGlobalStateCommand(ExperimentSamplesMigratedFromGlobalStateCommand _)
        {
            // See ActivableAndOfferStatsMigratedFromGlobalStateCommand handler
            await PersistStateIntermediate();
            _log.Info("Migrated experiment player samples from GlobalState, and persisted");
            CastMessage(GlobalStateManager.EntityId, GlobalStateForgetPlayerExperimentSamples.Instance);
        }

        [MessageHandler]
        void HandleStatsCollectorPlayerExperimentAssignmentSampleUpdate(StatsCollectorPlayerExperimentAssignmentSampleUpdate message)
        {
            foreach ((PlayerExperimentId experimentId, OrderedDictionary<EntityId, StatsCollectorPlayerExperimentAssignmentSampleUpdate.Sample> playerUpdates) in message.ExperimentSamples)
            {
                if (!_state.PlayerExperimentStatistics.TryGetValue(experimentId, out PlayerExperimentStatistics statistics))
                {
                    statistics = new PlayerExperimentStatistics();
                    _state.PlayerExperimentStatistics.Add(experimentId, statistics);
                }

                foreach ((EntityId playerId, StatsCollectorPlayerExperimentAssignmentSampleUpdate.Sample update) in playerUpdates)
                {
                    if (!update.HasFromVariant)
                    {
                        // new assignment. Add to experiment samples.
                        PlayerExperimentPlayerSampleUtil.InsertPlayerSample(statistics.PlayerSample, playerId);
                    }
                    else
                    {
                        // assignment changed. Remove from old variant samples.
                        if (statistics.Variants.TryGetValue(update.FromVariant, out PlayerExperimentStatistics.VariantStats oldVariantStats))
                        {
                            PlayerExperimentPlayerSampleUtil.RemovePlayerSample(oldVariantStats.PlayerSample, playerId);
                        }
                    }

                    // Insert into new variant samples
                    PlayerExperimentStatistics.VariantStats variantStats;
                    if (!statistics.Variants.TryGetValue(update.IntoVariant, out variantStats))
                    {
                        variantStats = new PlayerExperimentStatistics.VariantStats();
                        statistics.Variants.Add(update.IntoVariant, variantStats);
                    }
                    PlayerExperimentPlayerSampleUtil.InsertPlayerSample(variantStats.PlayerSample, playerId);
                }
            }
        }

        #endregion
    }
}
