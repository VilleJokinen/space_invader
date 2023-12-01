// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Cloud;
using Metaplay.Cloud.Analytics;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Entity.Synchronize;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Actions;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Json;
using Metaplay.Core.Math;
using Metaplay.Core.Memory;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Model.JournalCheckers;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using Metaplay.Server.EntityArchive;
using Metaplay.Server.EventLog;
using Metaplay.Server.Guild.InternalMessages;
using Metaplay.Server.GuildDiscovery;
using Metaplay.Server.MaintenanceJob;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace Metaplay.Server.Guild
{
    #region Event log boilerplate

    using GuildEventLogUtil = EntityEventLogUtil<GuildEventLogEntry, GuildEventLogSegmentPayload, PersistedGuildEventLogSegment, GuildEventLogScanResponse>;

    [MetaSerializable]
    public class GuildEventLogSegmentPayload : MetaEventLogSegmentPayload<GuildEventLogEntry>{ }
    [GuildsEnabledCondition]
    [Table("GuildEventLogSegments")]
    public class PersistedGuildEventLogSegment : PersistedEntityEventLogSegment{ }

    [MetaMessage(MessageCodesCore.InternalGuildEventLogScanResponse, MessageDirection.ServerInternal)]
    public class GuildEventLogScanResponse : EntityEventLogScanResponse<GuildEventLogEntry>{ }

    [EntityEventLogOptions("Guild")]
    public class GuildEventLogOptions : EntityEventLogOptionsBase { }

    #endregion

    /// <summary>
    /// The core persisted fields of a guild. Used when storing in a database.
    /// </summary>
    [GuildsEnabledCondition]
    [Table("Guilds")]
    [MetaPersistedVirtualItem]
    public class PersistedGuildBase : IPersistedEntity, IMetaIntegrationConstructible<PersistedGuildBase>
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   EntityId            { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime PersistedAt         { get; set; }

        public byte[]   Payload             { get; set; }   // Null for just-registered guild (created by CreateNewGuildAsync)

        [Required]
        public int      SchemaVersion       { get; set; }   // Schema version for object

        [Required]
        public bool     IsFinal             { get; set; }   // Is this a final persisted version (warn if resuming from non-final)

        [Obsolete]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   CachedDisplayName   { get; set; } = "";
    }

    /// <summary>
    /// Database entry for fast searching of guilds by name. Used to store unique searchable parts
    /// of the guild's name in an efficiently-searchable SQL index.
    /// </summary>
    [GuildsEnabledCondition]
    [Table("GuildNameSearches")]
    [NoPrimaryKey]
    [Keyless]
    [Index(nameof(EntityId))]
    [Index(nameof(NamePart), nameof(EntityId))]
    public class PersistedGuildSearch : IPersistedItem
    {
        // Parameters for searches
        public const int MaxNameSearchQueries       = 10;   // Maximum number of queries to perform when searching
        public const int MaxPersistNameParts        = 6;    // Maximum number of name parts to persist in database
        public const int MinPartLengthCodepoints    = 2;    // Minimum length of name part (in codepoints)
        public const int MaxPartLengthCodepoints    = 16;   // Maximum length of name part (in codepoints)

        [Required]
        [Column(TypeName = "varchar(32)")] // \note must be at least MaxWordLengthCodepoints (reserve some extra space to grow without schema migration)
        public string   NamePart        { get; set; }

        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   EntityId        { get; set; }
    }

    /// <summary>
    /// Information about an active guild.
    /// </summary>
    [MetaSerializableDerived(101)]
    public class GuildActiveEntityInfo : IActiveEntityInfo
    {
        [MetaMember(1)] public EntityId             EntityId        { get; private set; }
        [MetaMember(2)] public string               DisplayName     { get; private set; }
        [MetaMember(3)] public MetaTime             CreatedAt       { get; private set; }
        [MetaMember(4)] public MetaTime             ActivityAt      { get; private set; }
        [MetaMember(5)] public GuildLifecyclePhase  Phase           { get; private set; }
        [MetaMember(6)] public int                  NumMembers      { get; private set; }
        [MetaMember(7)] public int                  MaxNumMembers   { get; private set; }
        [MetaMember(8)] public MetaTime             LastLoginAt     { get; private set; }

        public GuildActiveEntityInfo() { }
        public GuildActiveEntityInfo(EntityId entityId, string displayName, MetaTime createdAt, MetaTime activityAt, GuildLifecyclePhase phase, int numMembers, int maxNumMembers, MetaTime lastLoginAt)
        {
            EntityId = entityId;
            DisplayName = displayName;
            CreatedAt = createdAt;
            ActivityAt = activityAt;
            Phase = phase;
            NumMembers = numMembers;
            MaxNumMembers = maxNumMembers;
            LastLoginAt = lastLoginAt;
        }
    }

    public sealed class ServerGuildModelJournal : ModelJournal<IGuildModelBase>.Leader
    {
        public ServerGuildModelJournal(LogChannel log, IGuildModelBase model, bool enableConsistencyChecks)
            : base(log, enableConsistencyChecks: enableConsistencyChecks, computeChecksums: true)
        {
            if (enableConsistencyChecks)
            {
                AddListener(new JournalModelOutsideModificationChecker<IGuildModelBase>(log));
                AddListener(new JournalModelCloningChecker<IGuildModelBase>(log));
                AddListener(new JournalModelChecksumChecker<IGuildModelBase>(log));
                AddListener(new JournalModelActionImmutabilityChecker<IGuildModelBase>(log));
                AddListener(new JournalModelRerunChecker<IGuildModelBase>(log));
                AddListener(new JournalModelModifyHistoryChecker<IGuildModelBase>(log));
            }

            AddListener(new JournalModelCommitChecker<IGuildModelBase>(log));
            AddListener(new FailingActionWarningListener<IGuildModelBase>(log));

            Setup(model, JournalPosition.AfterTick(model.CurrentTick));
        }
    }

    [EntityMaintenanceRefreshJob]
    [EntityMaintenanceSchemaMigratorJob]
    [EntityArchiveImporterExporter("guild", typeof(DefaultImportGuildHandler), typeof(DefaultExportGuildHandler))]
    public abstract class GuildEntityConfigBase : PersistedEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCore.Guild;
        public override IShardingStrategy   ShardingStrategy        => new HashedShardingStrategy(Application.Instance.ClusterConfig, EntityKindCore.Guild);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(30);
    }

    public abstract class GuildActorBase<TModel, TPersisted>
        : PersistedEntityActor<TPersisted, TModel>
        , IGuildModelServerListenerCore
        where TModel : class, IGuildModel<TModel>, new()
        where TPersisted : PersistedGuildBase, new()
    {
        protected override TimeSpan             SnapshotInterval    => TimeSpan.FromMinutes(3);
        protected override AutoShutdownPolicy   ShutdownPolicy      => AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: TimeSpan.FromSeconds(5));

        internal sealed class DoInfrequentPeriodicWork { public static DoInfrequentPeriodicWork Instance = new DoInfrequentPeriodicWork(); }
        internal sealed class TickUpdateCommand { public static TickUpdateCommand Instance = new TickUpdateCommand(); }
        internal sealed class FlushActionsCommand { public static FlushActionsCommand Instance = new FlushActionsCommand(); }
        internal sealed class RefreshGuildDiscoveryInfo { public static RefreshGuildDiscoveryInfo Instance = new RefreshGuildDiscoveryInfo(); }

        static Prometheus.Counter               c_actorsStarted                     = Prometheus.Metrics.CreateCounter("game_guild_started_total", "Number of guild actors started");
        static Prometheus.Histogram             c_persistedSize                     = Prometheus.Metrics.CreateHistogram("game_guild_persisted_size", "Persisted size of guild logic state", new Prometheus.HistogramConfiguration { Buckets = Metaplay.Cloud.Metrics.Defaults.EntitySizeBuckets });

        /// <summary> LogicVersion in use for this actor </summary>
        protected readonly int                          _logicVersion;
        /// <summary> Logging channel to route log events from PlayerModel into Akka logger </summary>
        protected readonly LogChannel                   _modelLogChannel;
        /// <summary> Currently active config. This is always set. </summary>
        protected ActiveGameConfig                      _activeGameConfig;

        /// <summary>
        /// Currently active baseline-version of the game config. This is always set.
        /// Note that Guilds do not participate in Player Experiments and hence will not have
        /// specialized configs.
        /// </summary>
        protected FullGameConfig                        _baselineGameConfig;
        /// <summary>
        /// Resolver for <see cref="_baselineGameConfig"/>. This is always set.
        /// Note that Guilds do not participate in Player Experiments and hence will not have
        /// specialized configs.
        /// </summary>
        protected IGameConfigDataResolver               _baselineGameConfigResolver;

        // \todo [petri] move further down to base class?
        // \todo Duplicated between PlayerActorBase and GuildActorBase
        private RandomPCG                                                       _analyticsEventRandom;
        protected AnalyticsEventBatcher<GuildEventBase, GuildAnalyticsContext>  _analyticsEventBatcher;
        protected AnalyticsEventHandler<IGuildModelBase, GuildEventBase>        _analyticsEventHandler;    // Analytics event handler: write to player event log & queue up in _analyticsEventBatcher for sending down to AnalyticsDispatcher

        string                                  _persistedSearchName;   // Name that has been stored in the search table (so tables can be updated when name changes)

        MetaTime                                _previousTickUpdateAt;
        bool                                    _startedTickTimer;
        bool                                    _pendingDiscoveryInfoUpdate;

        Dictionary<EntityId, int>               _persistedLastGuildOpEpochs;
        OrderedSet<EntityId>                    _shouldSendMemberGuildOpEpochUpdateAfterPersist;
        OrderedDictionary<int, MetaTime>        _nextInvitationCreationEarliestAt;

        protected ServerGuildModelJournal       _journal;               // State and a bounded history. (shared with client, persisted to database)
        protected TModel                        Model => (TModel)(_journal?.StagedModel ?? null);

        /// <summary>
        /// Time interval determining how often the model is updated to run any pending Ticks(). If TicksPerSecond is 10 and
        /// this is 1s, the model (if idle) will be woken once a second to run on average 10 ticks.
        /// </summary>
        protected abstract TimeSpan             TickUpdateInterval { get; }

        protected GuildActorBase(EntityId guildId) : base(guildId)
        {
            c_actorsStarted.Inc();

            // Create default log channel (used for active Model)
            _modelLogChannel = CreateModelLogChannel("guild");

            // Initialize analytics events collecting: append all events to EventLog immediately & batch events for sending to AnalyticsDispatcher
            // \todo Duplicated between PlayerActorBase and GuildActorBase, move to some base class
            _analyticsEventRandom  = RandomPCG.CreateNew();
            _analyticsEventBatcher = new AnalyticsEventBatcher<GuildEventBase, GuildAnalyticsContext>(guildId, maxBatchSize: 100);
            _analyticsEventHandler = new AnalyticsEventHandler<IGuildModelBase, GuildEventBase>((model, payload) =>
            {
                // \note The given `model` is not necessarily the currently-active `Model`,
                //       for example when this event handler is invoked from MigrateState
                //       (which operates on an explicitly-provided model instead of the
                //       currently-active `Model`).
                //
                //       This event handler should be careful to not use `Model`, except
                //       after explicitly checking it is appropriate.

                // Metadata
                AnalyticsEventSpec      eventSpec       = AnalyticsEventRegistry.GetEventSpec(payload.GetType());
                string                  eventType       = eventSpec.EventType; // \todo [petri] using class name as EventType -- is that good?
                MetaTime                collectedAt     = MetaTime.Now;
                MetaTime                modelTime       = model.CurrentTime;
                MetaUInt128             uniqueId        = new MetaUInt128((ulong)collectedAt.MillisecondsSinceEpoch, _analyticsEventRandom.NextULong());
                int                     schemaVersion   = eventSpec.SchemaVersion;
                IGameConfigDataResolver resolver        = model.GetDataResolver();

                // \todo [petri] add metrics
                //c_analyticsEvents.WithLabels(new string[] { eventType }).Inc();

                // Add to the model's event log (if enabled)
                if (eventSpec.IncludeInEventLog)
                {
                    // \note: defensive copy of the event. Caller might modify the object after this call but those changes should not be visible here.
                    GuildEventBase payloadCopy = MetaSerialization.CloneTagged<GuildEventBase>(payload, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver);
                    GuildEventLogUtil.AddEntry(model.EventLog, GetEventLogConfiguration(), collectedAt, uniqueId,
                        baseParams => new GuildEventLogEntry(baseParams, modelTime, schemaVersion, payloadCopy));

                    // Trigger event log flushing only if the model is the currently-active Model.
                    if (model == Model)
                        TryEnqueueTriggerEventLogFlushing();
                }

                // Enqueue event for sending analytics (if enabled)
                if (eventSpec.SendToAnalytics)
                {
                    GuildAnalyticsContext context = GetAnalyticsContext();
                    OrderedDictionary<AnalyticsLabel, string> labels = GetAnalyticsLabels((TModel)model);
                    _analyticsEventBatcher.Enqueue(_entityId, collectedAt, modelTime, uniqueId, eventType, schemaVersion, payload, context, labels, resolver, model.LogicVersion);
                }
            });

            // Fetch current LogicVersion & GameConfigs
            _logicVersion               = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;
            _activeGameConfig           = GlobalStateProxyActor.ActiveGameConfig.Get();
            _baselineGameConfig         = _activeGameConfig.BaselineGameConfig;
            _baselineGameConfigResolver = GetConfigResolver(_baselineGameConfig);
        }

        protected override void PreStart()
        {
            base.PreStart();

            _persistedLastGuildOpEpochs = new Dictionary<EntityId, int>();
            _shouldSendMemberGuildOpEpochUpdateAfterPersist = new OrderedSet<EntityId>();
            _nextInvitationCreationEarliestAt = new OrderedDictionary<int, MetaTime>();
        }

        protected override async Task Initialize()
        {
            // Fetch state from database (at least an empty state must exist)
            TPersisted persisted = await MetaDatabase.Get().TryGetAsync<TPersisted>(_entityId.ToString());
            if (persisted == null)
                throw new InvalidOperationException($"Trying to initialize GuildActor for whom no state exists in database. At least an empty PersistedGuild must exist in database!");

            // Initialize from persisted state
            await InitializePersisted(persisted);

            // Start periodic timer for miscellaneous infrequent things.
            StartRandomizedPeriodicTimer(TimeSpan.FromSeconds(30), DoInfrequentPeriodicWork.Instance);

            // Actor is active
            RegisterAsActive();
            UpdateGuildRecommender();

            await base.Initialize();
        }

        protected override sealed Task<TModel> InitializeNew()
        {
            TModel model = CreateInitialModel(CreateModelLogChannel("guild-initial"));
            return Task.FromResult<TModel>(model);
        }

        protected override sealed Task<TModel> RestoreFromPersisted(TPersisted persisted)
        {
            c_persistedSize.Observe(persisted.Payload.Length);
            TModel model = CreateModelFromSerialized(persisted.Payload, CreateModelLogChannel("guild-restored"));
            return Task.FromResult(model);
        }

        protected override async Task OnShutdown()
        {
            // Flush any pending events
            _analyticsEventBatcher.Flush();

            await base.OnShutdown();
        }

        protected override sealed async Task PostLoad(TModel model, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _log.Info("PostLoad() for {GuildId} logicVersion={LogicVersion}, away for {AwayDuration} (since {PersistedAt})", _entityId, _logicVersion, elapsedTime, persistedAt);

            // \note Defensive: AssignBasicRuntimePropertiesToModel should've ideally been done already
            //       right after `model` was created, but let's ensure it just in case.
            AssignBasicRuntimePropertiesToModel(model, model.Log);

            if (model.LifecyclePhase == GuildLifecyclePhase.Running)
            {
                // Fast forward sleepy time
                model.ResetTime(MetaTime.FromDateTime(persistedAt) + MetaDuration.FromTimeSpan(elapsedTime));
                model.OnFastForwardTime(MetaDuration.FromTimeSpan(elapsedTime));
            }

            // Clean expired cleanups
            await CleanExpiredInvitesOnModelAsync(_log, model, MetaTime.Now);

            // Reset clock and start from this point
            OnSwitchedToModelCore(model);
            ResetJournalToModel(model);

            // Start ticking
            if (model.LifecyclePhase == GuildLifecyclePhase.Running)
                StartTickTimer();

            // Check the epoch that was committed
            _persistedLastGuildOpEpochs.Clear();
            foreach (EntityId playerId in model.EnumerateMembers())
            {
                if (!model.TryGetMember(playerId, out GuildMemberBase guildMember))
                    continue;
                _persistedLastGuildOpEpochs[playerId] = guildMember.LastGuildOpEpoch;
            }

            // If we have pending kicks, inform players of them
            foreach ((EntityId kickedPlayerId, GuildPendingMemberKickState kickState) in model.PendingKicks)
            {
                // \todo: should clear PendingKick if it is too old?
                CastMessage(kickedPlayerId, new InternalPlayerKickedFromGuild(_entityId, kickState.MemberInstanceId));
            }

            // If the name has been stored in name search table, remember the name so we can detect changes (otherwise any valid name requires an update)
            _persistedSearchName = Model.IsNameSearchValid ? Model.DisplayName : null;
            _log.Debug("Initializing name search: '{GuildName}' (IsNameSearchValid={IsNameSearchValid})", Model.DisplayName, Model.IsNameSearchValid);
        }

        /// <summary>
        /// Switches immediately to the given model (by calling <see cref="OnSwitchedToModel"/>) and creates a new journal for it. The model
        /// clock will be reset to MetaTime.now, and CurrentTick to 0.
        /// </summary>
        protected void SwitchToNewModelImmediately(TModel model)
        {
            _persistedLastGuildOpEpochs.Clear();
            _shouldSendMemberGuildOpEpochUpdateAfterPersist.Clear();

            OnSwitchedToModelCore(model);
            ResetJournalToModel(model);

            // Start ticking in case we didn't do that already
            if (model.LifecyclePhase == GuildLifecyclePhase.Running)
                StartTickTimer();
        }

        /// <summary>
        /// Don't call directly. Use <see cref="SwitchToNewModelImmediately"/>. (<see cref="PostLoad"/> is a special case).
        /// </summary>
        void ResetJournalToModel(TModel model)
        {
            // Reset from the current position in time
            model.ResetTime(MetaTime.Now);

            _journal = CreateModelJournal(model, enableConsistencyChecks: false);

            // The model is now the currently-active `Model`.
            // Trigger event log flushing in case new events were logged in the model before it became the currently-active `Model`.
            TryEnqueueTriggerEventLogFlushing();
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            MetaTime now = MetaTime.Now;
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            // Last player leaving implicitly closes the guild
            if (Model.LifecyclePhase == GuildLifecyclePhase.Running && Model.MemberCount == 0)
            {
                _log.Debug("Guild has no longer any members, turning into a tombstone.");
                await SwitchToATombstoneAsync();
            }

            // On final persist, ensure Model is up-to-date
            if (isFinal)
            {
                MetaDuration elapsedTime = now - Model.CurrentTime;
                if (elapsedTime > MetaDuration.Zero)
                {
                    _log.Debug("Fast-forwarding Model {ElapsedTime} before final persist", elapsedTime);
                    Model.ResetTime(now);
                    Model.OnFastForwardTime(elapsedTime);
                }
            }

            // If guild name has changed, update the search tables (and mark in Model that name search has been populated for guild)
            // \note Null and "" names are considered equal for search.
            string updateGuildName = ((Model.DisplayName ?? "") != (_persistedSearchName ?? "")) ? Model.DisplayName : null;
            if (updateGuildName != null)
                _log.Info("Name has changed from '{OldGuildName}' to '{NewGuildName}', search table update required", _persistedSearchName, Model.DisplayName);

            // Mark the name search as inserted into the name search table (even if name is empty)
            Model.IsNameSearchValid = true;

            BeforePersistCore();
            await BeforePersistAsync();

            // Serialize and compress the model
            byte[] persistedPayload = SerializeToPersistedPayload(Model, _baselineGameConfigResolver, _logicVersion);

            // Store Model in database
            TPersisted persisted = CreatePersisted(
                entityId:           _entityId,
                persistedAt:        now.ToDateTime(),
                payload:            persistedPayload,
                schemaVersion:      _entityConfig.CurrentSchemaVersion,
                isFinal:            isFinal
            );

            // \note PersistedGuild has already been created in database during guild creation, so we always pass isInitial==false
            await MetaDatabase.Get().UpdateGuildAsync(_entityId, (PersistedGuildBase)persisted, updateNameSearch: updateGuildName);

            // Remember updated name (to avoid updating again)
            if (updateGuildName != null)
                _persistedSearchName = Model.DisplayName;

            // Actor is active
            RegisterAsActive();
            UpdateGuildRecommender();

            // Update member epochs and inform interested members of a succesful commit

            _persistedLastGuildOpEpochs.Clear();
            foreach (EntityId playerId in Model.EnumerateMembers())
            {
                if (!Model.TryGetMember(playerId, out GuildMemberBase guildMember))
                    continue;

                _persistedLastGuildOpEpochs[playerId] = guildMember.LastGuildOpEpoch;

                // sent only to the interested members that current members.
                if (_shouldSendMemberGuildOpEpochUpdateAfterPersist.Contains(playerId))
                    CastMessage(playerId, new InternalPlayerPendingGuildOpsCommitted(_entityId, guildMember.MemberInstanceId, _persistedLastGuildOpEpochs[playerId]));
            }
            _shouldSendMemberGuildOpEpochUpdateAfterPersist.Clear();
        }

        void BeforePersistCore()
        {
            // Flush any pending events
            _analyticsEventBatcher.Flush();
        }

        void RegisterAsActive(bool allowInClosedLifecyclePhase = false)
        {
            if (Model.LifecyclePhase != GuildLifecyclePhase.Running && !(allowInClosedLifecyclePhase && Model.LifecyclePhase == GuildLifecyclePhase.Closed))
                return;

            MetaTime currentTime = MetaTime.Now;

            IActiveEntityInfo info = new GuildActiveEntityInfo(
                entityId:       _entityId,
                displayName:    Model.DisplayName,
                createdAt:      Model.CreatedAt,
                activityAt:     currentTime,
                phase:          Model.LifecyclePhase,
                numMembers:     Model.MemberCount,
                maxNumMembers:  Model.MaxNumMembers,
                lastLoginAt:    Model.GetMemberOnlineLatestAt(timestampNow: currentTime));
            Context.System.EventStream.Publish(new ActiveEntityInfoEnvelope(info));
        }

        [CommandHandler]
        void HandleDoInfrequentPeriodicWork(DoInfrequentPeriodicWork _)
        {
            // Try flush events
            // \todo [petri] better timing controls
            _analyticsEventBatcher.Flush();
        }

        void StartTickTimer()
        {
            if (_startedTickTimer)
                return;
            _startedTickTimer = true;
            _previousTickUpdateAt = MetaTime.Now;
            StartRandomizedPeriodicTimer(TickUpdateInterval, TickUpdateCommand.Instance);
        }

        [EntityAskHandler]
        InternalEntityStateResponse HandleInternalGuildStateRequest(InternalEntityStateRequest _)
        {
            MetaSerialized<IModel> serialized = MetaSerialization.ToMetaSerialized<IModel>(Model, MetaSerializationFlags.IncludeAll, _logicVersion);
            return new InternalEntityStateResponse(serialized, _logicVersion, _activeGameConfig.BaselineStaticGameConfigId, _activeGameConfig.BaselineDynamicGameConfigId, specializationKey: null);
        }

        [EntityAskHandler]
        InternalGuildPlayerDashboardInfoResponse HandleInternalGuildPlayerDashboardInfoRequest(InternalGuildPlayerDashboardInfoRequest request)
        {
            if (Model.LifecyclePhase == GuildLifecyclePhase.Running && Model.TryGetMember(request.PlayerId, out GuildMemberBase member))
            {
                return InternalGuildPlayerDashboardInfoResponse.CreateForSuccess(Model.DisplayName, member.Role);
            }
            else
            {
                return InternalGuildPlayerDashboardInfoResponse.CreateForRefusal();
            }
        }

        [EntityAskHandler]
        InternalGuildDiscoveryGuildDataResponse HandleInternalGuildDiscoveryGuildDataRequest(InternalGuildDiscoveryGuildDataRequest request)
        {
            if (Model.LifecyclePhase == GuildLifecyclePhase.Running)
            {
                (GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo) = CreateGuildDiscoveryInfo();
                return InternalGuildDiscoveryGuildDataResponse.CreateForSuccess(publicDiscoveryInfo, serverOnlyDiscoveryInfo);
            }
            else if (Model.LifecyclePhase == GuildLifecyclePhase.Closed)
            {
                return InternalGuildDiscoveryGuildDataResponse.CreateForPermanentRefusal();
            }
            else
            {
                return InternalGuildDiscoveryGuildDataResponse.CreateForTemporaryRefusal();
            }
        }

        [EntitySynchronizeHandler]
        async Task HandleInternalGuildSetupSyncBegin(EntitySynchronize sync, InternalGuildSetupSync.Begin begin)
        {
            if (Model.LifecyclePhase != GuildLifecyclePhase.WaitingForSetup)
            {
                _log.Warning("Got setup request but guild was not waiting for setup. Phase was {Phase}.", Model.LifecyclePhase);
                sync.Send(new InternalGuildSetupSync.SetupResponse(false, 0));
                return;
            }

            // Setup empty guild waiting for the leader

            SetupGuildWithCreationParams(begin.CreationParams);

            Model.EventStream.Event(new GuildEventCreated(begin.CreationParams.DisplayName, begin.CreationParams.Description));

            int memberInstanceId = Model.RunningMemberInstanceId++;
            Model.LifecyclePhase = GuildLifecyclePhase.WaitingForLeader;

            await PersistStateIntermediate();
            sync.Send(new InternalGuildSetupSync.SetupResponse(true, memberInstanceId));

            // let leader join the guild
            // if we crash here, player will either try to recreate a new guild, or
            // if it managed to commit, subscribe to this guild on next login. The subscribe
            // code path will automatically init the guild if that is needed.

            InternalGuildSetupSync.PlayerCommitted playerCommit = await sync.ReceiveAsync<InternalGuildSetupSync.PlayerCommitted>();
            HandleInitialMember(playerCommit.PlayerId, memberInstanceId, playerCommit.PlayerData);

            await PersistStateIntermediate();
            sync.Send(new InternalGuildSetupSync.GuildCommitted());
        }

        protected override sealed Task<MetaMessage> OnNewSubscriber(EntitySubscriber subscriber, MetaMessage message)
        {
            if (subscriber.Topic == EntityTopic.Member && message is InternalEntitySubscribeRequestBase memberSubscribe)
                return HandleMemberSubscribeRequest(memberSubscribe);
            else if (subscriber.Topic == EntityTopic.Spectator && message is InternalGuildViewerSubscribeRequest viewerSubscribe)
                return HandleViewerSubscribeRequest(subscriber, viewerSubscribe);

            _log.Warning("Subscriber {EntityId} on unknown topic [{Topic}]", subscriber.EntityId, subscriber.Topic);
            throw new InvalidOperationException();
        }

        async Task<MetaMessage> HandleMemberSubscribeRequest(InternalEntitySubscribeRequestBase subscribeRequest)
        {
            InternalOwnedGuildAssociationRef associationRef = (InternalOwnedGuildAssociationRef)subscribeRequest.AssociationRef;
            GuildMemberBase member;

            // Leader join and becomes online
            if (Model.LifecyclePhase == GuildLifecyclePhase.WaitingForLeader)
            {
                member = null; // not used
            }
            else
            {
                // Kicked player attempts to come online.
                // \note: tombstones also emit kick results.
                if (Model.PendingKicks.TryGetValue(associationRef.PlayerId, out GuildPendingMemberKickState pendingKickState))
                {
                    if (pendingKickState.MemberInstanceId == associationRef.MemberInstanceId)
                    {
                        // Kicked player. No need to handle pending player ops yet. The kick handshake takes care of that.
                        // We can already forget about committed ones though.

                        AfterMemberPendingPlayerOpsCommitted(pendingKickState, associationRef.CommittedPlayerOpEpoch);
                        throw InternalGuildMemberSubscribeRefused.CreateKicked();
                    }
                    else
                    {
                        // Player is desynchronized to a point it cannot fetch kick state. Clear it as it has become useless.
                        Model.PendingKicks.Remove(associationRef.PlayerId);
                        throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();
                    }
                }

                // Member comes online
                if (Model.LifecyclePhase != GuildLifecyclePhase.Running)
                    throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();
                if (!Model.TryGetMember(associationRef.PlayerId, out member))
                    throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();

                if (member.MemberInstanceId != associationRef.MemberInstanceId)
                {
                    // Player is a member of this guild and it agrees, but their timelines disagree. Remove the player,
                    // the timeline is broken already.

                    _log.Warning("Player subscribed to a guild but it has incorrect member instance id. Removing. Expected={ExpectedMemberInstanceId} Got={ReceivedMemberInstanceId}", member.MemberInstanceId, associationRef.MemberInstanceId);

                    GuildEventMemberInfo eventMemberInfo = GuildEventMemberInfo.Create(associationRef.PlayerId, member);
                    ExecuteAction(new GuildMemberRemove(associationRef.PlayerId));
                    Model.EventStream.Event(new GuildEventMemberRemovedDueToInconsistency(eventMemberInfo, GuildEventMemberRemovedDueToInconsistency.InconsistencyType.SubscribeAttemptMemberInstanceIdDiffers));
                    await RemoveAllMemberInviteExternalResourcesAsync(member);
                    await PersistStateIntermediate();

                    throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();
                }

                // Guild has pending ops (run before returning state but no need to commit).
                // \note: player's guild ops are not run if the player is kicked. (Player was kicked before the ops were issued, so they will not be run).
                if (associationRef.CommittedPendingGuildOps != null)
                    ExecutePendingGuildMemberGuildOps(associationRef.PlayerId, member, associationRef.CommittedPendingGuildOps);

                // Player has any pending ops
                AfterMemberPendingPlayerOpsCommitted(member, associationRef.CommittedPlayerOpEpoch);

                OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingOps = TryGetMemberPendingPlayerOpsAfterEpoch(member, associationRef.LastPlayerOpEpoch);
                if (pendingOps != null)
                    throw InternalGuildMemberSubscribeRefused.CreatePendingPlayerOps(pendingOps);

                // Rollback detection.
                // If member has seen a higher player op than we have sent, we must have rolled back.
                // Move us forward to prevent further divergence in histories. Same for player.
                if (associationRef.LastPlayerOpEpoch > member.LastPendingPlayerOpEpoch)
                {
                    _log.Warning("Rollback detected: Player {PlayerId} has observed playerop epoch {SeenEpoch} which is not yet issued. Guild has lost {NumLost} ops.", associationRef.PlayerId, associationRef.LastPlayerOpEpoch, associationRef.LastPlayerOpEpoch - member.LastPendingPlayerOpEpoch);
                    member.LastPendingPlayerOpEpoch = associationRef.LastPlayerOpEpoch;
                }
                if (member.LastGuildOpEpoch > associationRef.CommittedGuildOpEpoch)
                {
                    _log.Warning("Rollback detected: Player {PlayerId} has not observed the guildop on epoch {SeenEpoch} we have executed. Player has lost {NumLost} ops.", associationRef.PlayerId, member.LastGuildOpEpoch, member.LastGuildOpEpoch - associationRef.CommittedGuildOpEpoch);
                    throw InternalGuildMemberSubscribeRefused.CreateGuildOpEpochSkip(member.LastGuildOpEpoch);
                }
            }

            // Get associated entities
            List<AssociatedEntityRefBase> associatedEntities = new List<AssociatedEntityRefBase>();

            // Check resource proposal, if such was given
            // Client gives resource proposals in session setup where client can fix and retry. But for mid-session join and creates, we
            // just tell the client the version it should use, and client delays operations locally until the version could be fetched.
            if (subscribeRequest.ResourceProposal.HasValue)
            {
                SessionProtocol.SessionResourceCorrection resourceCorrection = GetNewSessionResourceCorrection(subscribeRequest.ResourceProposal.Value, subscribeRequest.SupportedArchiveCompressions);
                if (resourceCorrection.HasAnyCorrection())
                    throw new InternalEntitySubscribeRefusedBase.Builtins.ResourceCorrection(resourceCorrection, associatedEntities);
            }
            if (subscribeRequest.IsDryRun)
                throw new InternalEntitySubscribeRefusedBase.Builtins.DryRunSuccess(associatedEntities);

            // Success, subscriber is accepted
            if (Model.LifecyclePhase == GuildLifecyclePhase.WaitingForLeader)
                HandleInitialMember(associationRef.PlayerId, associationRef.MemberInstanceId, associationRef.PlayerLoginData);
            else
                HandleMemberSubscriber(associationRef.PlayerId, member, associationRef.PlayerLoginData);

            return new InternalEntitySubscribeResponseBase.Default(
                state: new EntityInitialState(
                    state:          CreateGuildSerializedState(associationRef.PlayerId),
                    channelId:      subscribeRequest.ClientChannelId,
                    contextData:    new ChannelContextDataBase.Default(ClientSlotCore.Guild)),
                associatedEntities: associatedEntities);
        }

        void HandleInitialMember(EntityId playerId, int memberInstanceId, GuildMemberPlayerDataBase playerData)
        {
            // first joiner. This moves us into the running phase.
            Model.ResetTime(MetaTime.Now);
            Model.LifecyclePhase = GuildLifecyclePhase.Running;
            StartTickTimer();

            ExecuteAction(new GuildMemberAdd(playerId, memberInstanceId, playerData));
            Model.EventStream.Event(new GuildEventFounderJoined(GuildEventMemberInfo.Create(playerId, memberInstanceId, playerData)));
            ExecuteAction(new GuildMemberIsOnlineUpdate(playerId, isOnline: true, lastUpdateAt: MetaTime.Now));

            OnGuildBecameRunning();
            RegisterAsActive();
            UpdateGuildRecommender();
        }

        void HandleMemberSubscriber(EntityId playerId, GuildMemberBase member, GuildMemberPlayerDataBase playerData)
        {
            if (!playerData.IsUpToDate(member))
                ExecuteAction(new GuildMemberPlayerDataUpdate(playerId, playerData));
            ExecuteAction(new GuildMemberIsOnlineUpdate(playerId, isOnline: true, lastUpdateAt: MetaTime.Now));
            UpdateGuildRecommender();
        }

        EntitySerializedState CreateGuildSerializedState(EntityId? memberPlayerId)
        {
            // Flush any pending work. Unflushed actions are already baked to the model, so
            // we don't want them twice.
            UpdateTicks();
            FlushActions();

            // Snapshot of the state
            MetaSerialized<IMultiplayerModel>                   publicData  = MetaSerialization.ToMetaSerialized<IMultiplayerModel>(Model, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            MetaSerialized<MultiplayerMemberPrivateStateBase>   memberData  = default;
            if (memberPlayerId != null)
            {
                MultiplayerMemberPrivateStateBase memberDataState = Model.GetMemberPrivateState(memberPlayerId.Value);
                if (memberDataState != null)
                    memberData = MetaSerialization.ToMetaSerialized(memberDataState, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            }

            ContentHash sharedGameConfigVersion = _activeGameConfig.ClientSharedGameConfigContentHash;
            EntitySerializedState guildState = new EntitySerializedState(
                publicState:                publicData,
                memberPrivateState:         memberData,
                currentOperation:           _journal.StagedPosition.Operation,
                logicVersion:               _logicVersion,
                sharedGameConfigVersion:    sharedGameConfigVersion,
                sharedConfigPatchesVersion: ContentHash.None,
                activeExperiments:          Array.Empty<EntityActiveExperiment>());
            return guildState;
        }

        SessionProtocol.SessionResourceCorrection GetNewSessionResourceCorrection(SessionProtocol.SessionResourceProposal proposal, CompressionAlgorithmSet supportedArchiveCompressions)
        {
            SessionProtocol.SessionResourceCorrection correction = new SessionProtocol.SessionResourceCorrection();

            if (proposal.ConfigVersions.TryGetValue(ClientSlotCore.Guild, out ContentHash proposedVersion) && proposedVersion == _activeGameConfig.ClientSharedGameConfigContentHash)
            {
                // all ok
            }
            else
            {
                correction.ConfigUpdates[ClientSlotCore.Guild] = _activeGameConfig.BaselineGameConfigSharedConfigDeliverySources.GetCorrection(supportedArchiveCompressions);
            }

            return correction;
        }

        Task<MetaMessage> HandleViewerSubscribeRequest(EntitySubscriber subscriber, InternalGuildViewerSubscribeRequest subscribeRequest)
        {
            bool wasAccepted;
            if (Model.LifecyclePhase == GuildLifecyclePhase.Running)
            {
                wasAccepted = true;
            }
            else
            {
                wasAccepted = false;
            }

            if (wasAccepted)
            {
                EntitySerializedState guildState = CreateGuildSerializedState(null);
                return Task.FromResult<MetaMessage>(new InternalGuildViewerSubscribeResponse(guildState));
            }
            else
            {
                return Task.FromResult<MetaMessage>(InternalGuildViewerSubscribeResponse.CreateForRefusal());
            }
        }

        protected sealed override void OnSubscriberLost(EntitySubscriber subscriber)
        {
            if (subscriber.Topic == EntityTopic.Member)
            {
                EntityId playerId = SessionIdUtil.ToPlayerId(subscriber.EntityId);

                if (Model.TryGetMember(playerId, out GuildMemberBase _))
                {
                    ExecuteAction(new GuildMemberIsOnlineUpdate(playerId, isOnline: false, lastUpdateAt: MetaTime.Now));
                }
            }
        }

        [CommandHandler]
        void HandleTickUpdateCommand(TickUpdateCommand _)
        {
            UpdateTicks();
            FlushActions();
        }

        [CommandHandler]
        void HandleFlushActionsCommand(FlushActionsCommand _)
        {
            FlushActions();
        }

        void EnqueueFlush()
        {
            _self.Tell(FlushActionsCommand.Instance, null);
        }

        protected void ExecuteAction(GuildActionBase action)
        {
            UpdateTicks();
            ExecuteActionImmediately(action);
            EnqueueFlush();
        }

        void ExecuteActionImmediately(GuildActionBase action)
        {
            _log.Debug("Execute action (tick {Tick}): {Action}", Model.CurrentTick, PrettyPrint.Compact(action));
            _journal.StageAction(action);
        }

        void UpdateTicks()
        {
            if (Model.LifecyclePhase != GuildLifecyclePhase.Running)
                return;

            MetaTime lastTime = _previousTickUpdateAt;
            _previousTickUpdateAt = MetaTime.Now;

            long lastTotalTicks     = ModelUtil.TotalNumTicksElapsedAt(lastTime, Model.TimeAtFirstTick, Model.TicksPerSecond);
            long currentTotalTicks  = ModelUtil.TotalNumTicksElapsedAt(_previousTickUpdateAt, Model.TimeAtFirstTick, Model.TicksPerSecond);
            long newTicks           = currentTotalTicks - lastTotalTicks;

            //_log.Debug("Simulating {NumTicks} ticks (from {FromTick} to {ToTick})", newTicks, lastTotalTicks, currentTotalTicks);

            // Execute ticks
            for (int tick = 0; tick < newTicks; tick++)
                _journal.StageTick();
        }

        InternalSessionGuildTimelineUpdate TryGatherFlushActions()
        {
            var                                         walker          = _journal.WalkJournal(from: _journal.CheckpointPosition);
            JournalPosition                             gatherEnd       = JournalPosition.Epoch;
            List<GuildTimelineUpdateMessage.Operation>  operations      = new List<GuildTimelineUpdateMessage.Operation>();
            JournalPosition                             startPosition   = default;
            bool                                        isFirstOp       = true;

            while (walker.MoveNext())
            {
                if (isFirstOp && (walker.IsTickFirstStep || walker.IsActionFirstStep))
                {
                    isFirstOp = false;
                    startPosition = walker.PositionBefore;
                }

                if (walker.IsTickFirstStep)
                {
                    operations.Add(new GuildTimelineUpdateMessage.Operation(null, EntityId.None));

                    for (int i = 0; i < walker.NumStepsTotal - 1; ++i)
                        walker.MoveNext();
                }
                else if (walker.IsActionFirstStep)
                {
                    // \todo: don't read InvokingPlayerId back from timeline, but store it when it is added to the timeline
                    GuildActionBase guildAction = (GuildActionBase)walker.Action;
                    operations.Add(new GuildTimelineUpdateMessage.Operation(guildAction, guildAction.InvokingPlayerId));

                    for (int i = 0; i < walker.NumStepsTotal - 1; ++i)
                        walker.MoveNext();
                }
                gatherEnd = walker.PositionAfter;
            }

            if (operations.Count > 0)
            {
                uint finalChecksum = _journal.ForceComputeChecksum(gatherEnd);
                _journal.CaptureStageSnapshot();
                _journal.Commit(gatherEnd);

                return new InternalSessionGuildTimelineUpdate(MetaSerialization.ToMetaSerialized(operations, MetaSerializationFlags.SendOverNetwork, _logicVersion), startPosition.Tick, startPosition.Operation, finalChecksum);
            }
            return null;
        }

        void FlushActions()
        {
            InternalSessionGuildTimelineUpdate update = TryGatherFlushActions();
            if (update != null)
                PublishToMembers(update);
        }

        protected void PublishToMembers(MetaMessage msg)
        {
            PublishMessage(EntityTopic.Member, msg);
        }

        protected void PublishToMembers(MetaMessage msg, EntityId exceptForPlayer)
        {
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber.Topic != EntityTopic.Member)
                    continue;
                if (SessionIdUtil.ToPlayerId(subscriber.EntityId) == exceptForPlayer)
                    continue;

                SendMessage(subscriber, msg);
            }
        }

        protected void PublishToMember(EntityId memberPlayerId, MetaMessage msg)
        {
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber.Topic != EntityTopic.Member)
                    continue;
                if (SessionIdUtil.ToPlayerId(subscriber.EntityId) != memberPlayerId)
                    continue;

                SendMessage(subscriber, msg);
            }
        }

        [PubSubMessageHandler]
        void HandleInternalGuildEnqueueActionsRequest(EntitySubscriber session, InternalGuildEnqueueActionsRequest request)
        {
            if (Model.LifecyclePhase != GuildLifecyclePhase.Running)
            {
                _log.Warning("Got GuildEnqueueActionsRequest while phase was not Running. Ignored. Phase was {Phase}.", Model.LifecyclePhase);
                return;
            }
            if (!Model.TryGetMember(request.PlayerId, out GuildMemberBase member))
            {
                _log.Warning("Got GuildEnqueueActionsRequest from non-member player {PlayerId}. Ignoring.", request.PlayerId);
                return;
            }
            if (SessionIdUtil.ToPlayerId(session.EntityId) != request.PlayerId)
            {
                _log.Warning("Got GuildEnqueueActionsRequest from session {SessionId} claiming to be player {PlayerId}. Ignoring.", session.EntityId, request.PlayerId);
                return;
            }

            // We don't need to check MemberInstanceId here because it is checked when the subscription is opened and the id cannot change while a channel is open.

            UpdateTicks();

            List<GuildActionBase> actions = request.Actions.Deserialize(_baselineGameConfigResolver, _logicVersion);
            foreach(GuildActionBase action in actions)
            {
                // not allowed, but don't trust input
                if (action == null)
                {
                    _log.Warning("Got GuildEnqueueActionsRequest from {PlayerId} with null action. Ignoring.", request.PlayerId);
                    return;
                }

                action.InvokingPlayerId = request.PlayerId;

                // Validation
                if (!ValidateClientOriginatingAction(action))
                {
                    _log.Warning("Validation failed for action by {PlayerId}, ignored. Action: {Action}", request.PlayerId, PrettyPrint.Compact(action));

                    // skip
                    continue;
                }

                // Dry run
                MetaActionResult dryRunResult = ModelUtil.DryRunAction(Model, action);
                if (dryRunResult != MetaActionResult.Success)
                {
                    // only on debug level. This is expected due to races.
                    _log.Debug("Failed to execute action by {PlayerId}, ignored. Action: {Action}. Result: {Result}", request.PlayerId, PrettyPrint.Compact(action), dryRunResult);

                    // skip
                    continue;
                }

                ExecuteActionImmediately(action);
            }
            EnqueueFlush();
        }

        [MessageHandler]
        void HandleInternalGuildEnqueueMemberActionRequest(EntityId fromEntityId, InternalGuildEnqueueMemberActionRequest request)
        {
            GuildMemberBase member = null;
            bool rejected = false;
            if (Model.LifecyclePhase != GuildLifecyclePhase.Running)
            {
                _log.Warning("Got InternalGuildEnqueueMemberActionRequest while phase was not Running. Ignored. Phase was {Phase}.", Model.LifecyclePhase);
                rejected = true;
            }
            else if (!Model.TryGetMember(request.PlayerId, out member))
            {
                _log.Warning("Got InternalGuildEnqueueMemberActionRequest from non-member player {PlayerId}. Ignoring.", request.PlayerId);
                rejected = true;
            }
            else if (request.MemberInstanceId != member.MemberInstanceId)
            {
                _log.Warning("Got InternalGuildEnqueueMemberActionRequest from stale member instance {PlayerId}, Instance={InstanceId}, Expected={ExpectedInstanceId}. Ignoring.", request.PlayerId, request.MemberInstanceId, member.MemberInstanceId);
                rejected = true;
            }

            if (rejected)
            {
                // Most likely stale request, but in case the player is out of sync, tell player to check its status
                CastMessage(request.PlayerId, new InternalPlayerKickedFromGuild(request.PlayerId, request.MemberInstanceId));
                return;
            }

            UpdateTicks();

            GuildActionBase action = request.Action.Deserialize(resolver: _baselineGameConfigResolver, logicVersion: _logicVersion);
            action.InvokingPlayerId = request.PlayerId;
            ExecuteActionImmediately(action);

            EnqueueFlush();
        }

        [MessageHandler]
        void HandleInternalGuildRunPendingGuildOpsRequest(InternalGuildRunPendingGuildOpsRequest request)
        {
            GuildMemberBase member = null;
            bool rejected = false;
            if (Model.LifecyclePhase != GuildLifecyclePhase.Running)
            {
                _log.Warning("Got InternalGuildCommitPendingMemberActionsRequest while phase was not Running. Ignored. Phase was {Phase}.", Model.LifecyclePhase);
                rejected = true;
            }
            else if (!Model.TryGetMember(request.PlayerId, out member))
            {
                _log.Warning("Got InternalGuildCommitPendingMemberActionsRequest from non-member player {PlayerId}. Ignoring.", request.PlayerId);
                rejected = true;
            }
            else if (request.MemberInstanceId != member.MemberInstanceId)
            {
                _log.Warning("Got InternalGuildCommitPendingMemberActionsRequest from stale member instance {PlayerId}, Instance={InstanceId}, Expected={ExpectedInstanceId}. Ignoring.", request.PlayerId, request.MemberInstanceId, member.MemberInstanceId);
                rejected = true;
            }

            if (rejected)
            {
                // Most likely stale request, but in case the player is out of sync, tell player to check its status
                CastMessage(request.PlayerId, new InternalPlayerKickedFromGuild(request.PlayerId, request.MemberInstanceId));
                return;
            }

            // Have we already committed these? If so, we can reply immediately

            if (!ContainsAnyUncommittedGuildMemberGuildOp(request.PlayerId, request.Ops))
            {
                CastMessage(request.PlayerId, new InternalPlayerPendingGuildOpsCommitted(_entityId, member.MemberInstanceId, _persistedLastGuildOpEpochs[request.PlayerId]));
                return;
            }

            // \note: In the case of ConsistentWithUniqueExecution, we would write Actions to WAL, persist, and reply.
            //        Then run one by one from WAL, with persist after each step.

            // We haven't committed the actions yet (we might have run them though).
            // Run all ops (if they are new). After we are persisted, we'll let Player know the actions were
            // committed. We are not in a hurry to persist, so we'll wait until next natural moment. This
            // might mean we only reply at the shutdown of the server and message gets lost, but that is not a
            // problem. Player will resend the query until it is satisfied, and the above fast-path will
            // handle it.

            ExecutePendingGuildMemberGuildOps(request.PlayerId, member, request.Ops);

            // Mark player interested in epoch update on next persist
            // This happens even if we didn't need to run anything

            _shouldSendMemberGuildOpEpochUpdateAfterPersist.Add(request.PlayerId);
        }

        /// <summary>
        /// Closes the guild. This replaces the Model with a tombstone with lifecycle phase Closed.
        /// </summary>
        protected async Task CloseGuild()
        {
            // \todo: kick players. And viewers.
            // Persisting automatically converts to a tombstone
            await PersistStateIntermediate();
        }

        async Task SwitchToATombstoneAsync()
        {
            await OnGuildBecomingClosed();

            // replace guild with a closed tombstone.
            TModel closedModel = CreateClosedTombstoneModel(CreateModelLogChannel("guild-closed"));

            // keep pending kicks to deliver final kicks
            closedModel.PendingKicks = Model.PendingKicks;

            // clear all invites
            await RemoveAllInviteExternalResourcesAsync();

            SwitchToNewModelImmediately(closedModel);

            RegisterAsActive(allowInClosedLifecyclePhase: true);
        }

        [EntityAskHandler]
        async Task<InternalGuildLeaveResponse> HandleInternalGuildLeaveRequest(InternalGuildLeaveRequest request)
        {
            if (Model.TryGetMember(request.PlayerId, out GuildMemberBase member))
            {
                if (member.MemberInstanceId == request.MemberInstanceId)
                {
                    // Member is leaving

                    AfterMemberPendingPlayerOpsCommitted(member, request.CommittedPlayerOpEpoch);

                    // Make sure guild has completed all of player's guild ops
                    if (request.PendingGuildOps != null)
                    {
                        // Execute. We don't need to commit here because we commit
                        // just after the player is removed.
                        ExecutePendingGuildMemberGuildOps(request.PlayerId, member, request.PendingGuildOps);
                    }

                    // Make sure player has executed all of player ops.
                    // Except if this is ForceLeave (i.e. Admin operation), we forcibly remove the member even if actions are not complete.
                    if (member.PendingPlayerOps != null && !request.ForceLeave)
                        return InternalGuildLeaveResponse.CreatePendingPlayerOps(member.PendingPlayerOps);
                }
                else
                {
                    // Not-a-member is leaving but there is an earlier (or newer) replica of that player in our state.
                    // Fix state.
                }

                // last player leaving closes the guild implicitly.
                GuildEventMemberInfo eventMemberInfo = GuildEventMemberInfo.Create(request.PlayerId, member);
                ExecuteAction(new GuildMemberRemove(request.PlayerId));
                Model.EventStream.Event(new GuildEventMemberLeft(eventMemberInfo));
                await RemoveAllMemberInviteExternalResourcesAsync(member);
                await PersistStateIntermediate();

                UpdateGuildRecommender();

                return InternalGuildLeaveResponse.CreateOk();
            }
            else if (Model.PendingKicks.TryGetValue(request.PlayerId, out GuildPendingMemberKickState pendingKickState))
            {
                if (pendingKickState.MemberInstanceId == request.MemberInstanceId)
                {
                    // Kicked. No need to handle pending player ops, those get handled in kick handshake. We
                    // can already forget about committed ones though.
                    AfterMemberPendingPlayerOpsCommitted(pendingKickState, request.CommittedPlayerOpEpoch);

                    // Let player know it should do the handshake
                    // Except if this is ForceLeave (i.e. Admin operation), we forcibly remove the from kicked list as well
                    if (!request.ForceLeave)
                        return InternalGuildLeaveResponse.CreateKicked();
                    else
                        Model.PendingKicks.Remove(request.PlayerId);
                }
                else
                {
                    // Not-a-member is not in a guild but there is an earlier (or newer) replica that got kicked. Fix state.
                    Model.PendingKicks.Remove(request.PlayerId);
                }
            }

            // not-a-member is leaving. Nothing to do with us, let's allow it.
            return InternalGuildLeaveResponse.CreateOk();
        }

        [EntitySynchronizeHandler]
        async Task HandleInternalGuildJoinGuildSyncBegin(EntitySynchronize sync, InternalGuildJoinGuildSync.Begin request)
        {
            if (Model.LifecyclePhase != GuildLifecyclePhase.Running)
            {
                _log.Warning("Got join attempt but guild was not running. Phase was {Phase}.", Model.LifecyclePhase);
                sync.Send(InternalGuildJoinGuildSync.PreflightDone.CreateReject());
                return;
            }

            if (Model.TryGetMember(request.PlayerId, out GuildMemberBase alreadyExistingMember))
            {
                _log.Debug("Got join request but player {PlayerId} was already a member. Cleaning up.", request.PlayerId);

                // Already a member. Clearly not. Fix state.
                GuildEventMemberInfo eventMemberInfo = GuildEventMemberInfo.Create(request.PlayerId, alreadyExistingMember);
                ExecuteAction(new GuildMemberRemove(request.PlayerId));
                Model.EventStream.Event(new GuildEventMemberRemovedDueToInconsistency(eventMemberInfo, GuildEventMemberRemovedDueToInconsistency.InconsistencyType.JoinAttemptAlreadyMember));
                await RemoveAllMemberInviteExternalResourcesAsync(alreadyExistingMember);
                await PersistStateIntermediate();

                // we just might have been closed. Rather than handle that here, make caller retry.
                sync.Send(InternalGuildJoinGuildSync.PreflightDone.CreateTryAgain());
                return;
            }

            // Find invite
            bool isInvited;

            if (request.OriginalRequest.Mode == GuildJoinRequest.JoinMode.InviteCode)
            {
                if (TryGetGuildInviteByIdAndCode(request.OriginalRequest.InviteId, request.OriginalRequest.InviteCode, out GuildInviteState foundInvite, out EntityId foundInviterPlayerId))
                {
                    if (foundInvite.IsExpired(MetaTime.Now))
                    {
                        _log.Debug("Got join request with invite code but invite {InviteId} was expired. Rejecting.", request.OriginalRequest.InviteId);

                        // No such invite id, cannot continue
                        sync.Send(InternalGuildJoinGuildSync.PreflightDone.CreateReject());
                        await RemoveExpiredInviteAsync(foundInviterPlayerId, request.OriginalRequest.InviteId, foundInvite);
                        return;
                    }

                    isInvited = true;
                }
                else
                {
                    _log.Debug("Got join request with unknown (already used and cleaned?) invite {InviteId}. Rejecting.", request.OriginalRequest.InviteId);

                    // No such invite id, cannot continue
                    sync.Send(InternalGuildJoinGuildSync.PreflightDone.CreateReject());
                    return;
                }
            }
            else
            {
                isInvited = false;
            }

            if (!ShouldAcceptPlayerJoin(request.PlayerId, request.PlayerData, isInvited))
            {
                sync.Send(InternalGuildJoinGuildSync.PreflightDone.CreateReject());
                return;
            }

            // we would accept the player. Wait for player to commit first
            int memberInstanceId = Model.RunningMemberInstanceId++;
            sync.Send(InternalGuildJoinGuildSync.PreflightDone.CreateOk(memberInstanceId));
            try
            {
                _ = await sync.ReceiveAsync<InternalGuildJoinGuildSync.PlayerCommitted>();
            }
            catch (Exception ex)
            {
                // Protect GuildActor from PlayerActor issues. If player fails to inform us, we just forget it. `sync` will be closed on return.
                _log.Warning("Player {PlayerId} failed to commit during guild join request, ignoring. Error: {Error}", request.PlayerId, ex);
                return;
            }

            // Make sure player is never both Member and kicked. This should never do anything as player
            // CANNOT leave a guild without the leave handshake. But better safe than sorry.
            _ = Model.PendingKicks.Remove(request.PlayerId);

            // consume invite
            GuildInviteState inviteToBeRemoved = null;
            if (isInvited)
            {
                UseGuildInvite(request.OriginalRequest.InviteId, out inviteToBeRemoved);
            }

            // commit
            ExecuteAction(new GuildMemberAdd(request.PlayerId, memberInstanceId, request.PlayerData));
            Model.EventStream.Event(new GuildEventMemberJoined(GuildEventMemberInfo.Create(request.PlayerId, memberInstanceId, request.PlayerData)));
            await PersistStateIntermediate();

            sync.Send(new InternalGuildJoinGuildSync.GuildCommitted());
            UpdateGuildRecommender();

            // prune external resources if invite expired
            if (inviteToBeRemoved != null)
                await RemoveInviteExternalResourcesAsync(_log, inviteToBeRemoved);
        }

        [EntitySynchronizeHandler]
        async Task HandleInternalGuildTransactionGuildSyncBegin(EntitySynchronize sync, InternalGuildTransactionGuildSync.Begin begin)
        {
            UpdateTicks();
            FlushActions();

            EntityId            playerId        = begin.PlayerId;
            bool                isAMember;

            if (!Model.TryGetMember(playerId, out GuildMemberBase member))
            {
                _log.Warning("Got transaction request from non-member {PlayerId}. Cancel.", playerId);
                isAMember = false;
            }
            else if (member.MemberInstanceId != begin.MemberInstanceId)
            {
                _log.Warning("Got transaction request from stale member {PlayerId}. MemberInstanceId={MemberInstanceId}, request {RequestMemberInstanceId}. Cancel.", playerId, member.MemberInstanceId, begin.MemberInstanceId);
                isAMember = false;
            }
            else
                isAMember = true;

            if (!isAMember)
            {
                // Make caller update its state.
                CastMessage(playerId, new InternalPlayerKickedFromGuild(_entityId, begin.MemberInstanceId));
                sync.Send(InternalGuildTransactionGuildSync.PlannedAndCommitted.CreateCancel(preceedingPlayerOps: null));
                return;
            }

            IGuildTransaction               transaction     = begin.Transaction.Deserialize(resolver: null, logicVersion: _logicVersion);
            GuildTransactionConsistencyMode consistencyMode = transaction.ConsistencyMode;
            transaction.InvokingPlayerId = playerId;

            // Find all ops the player has not executed yet.
            // PendingPlayerOps contains all tha have not yet been committed, but now
            // we are only interested in unexecuted ops.

            OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps = TryGetMemberPendingPlayerOpsAfterEpoch(member, begin.LastPlayerOpEpoch);

            // Plan fo guild

            ITransactionPlan    playerPlan      = begin.PlayerPlan;
            ITransactionPlan    serverPlan      = begin.ServerPlan;
            ITransactionPlan    guildPlan;
            ITransactionPlan    finalizingPlan;

            try
            {
                guildPlan = transaction.PlanForGuild(Model, member);
                finalizingPlan = transaction.PlanForFinalizing(playerPlan, guildPlan, serverPlan);
            }
            catch (TransactionPlanningFailure)
            {
                sync.Send(InternalGuildTransactionGuildSync.PlannedAndCommitted.CreateCancel(preceedingPlayerOps));
                return;
            }

            // Handle success: apply finalizing action and distribute it members

            if (consistencyMode == GuildTransactionConsistencyMode.EventuallyConsistent)
            {
                // Consistent execution. Write player actions to member-message-queue so that if player crashes, it get the actions from here.
                // If we crash, the player does not advance.

                PlayerActionBase                                        playerInitiatingAction  = transaction.CreateInitiatingPlayerAction(playerPlan);
                PlayerTransactionFinalizingActionBase                   playerFinalizingAction  = transaction.CreateFinalizingPlayerAction(finalizingPlan);
                MetaSerialized<PlayerActionBase>                        serializedPlayerInitiatingAction;
                MetaSerialized<PlayerTransactionFinalizingActionBase>   serializedPlayerFinalizingAction;

                if (playerInitiatingAction != null)
                    serializedPlayerInitiatingAction = MetaSerialization.ToMetaSerialized(playerInitiatingAction, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
                else
                    serializedPlayerInitiatingAction = default;

                if (playerFinalizingAction != null)
                    serializedPlayerFinalizingAction = MetaSerialization.ToMetaSerialized(playerFinalizingAction, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
                else
                    serializedPlayerFinalizingAction = default;

                // \note: we write to log even if all actions are null. Keeps the logic simple (and both actions being null doesn't make sense anyway).
                // \note: player bumps PlayerOpEpoch implicitly.
                // \note: we append this before executing finalizing action because the action might append more to the opLog and we want this to be the first

                EnqueueMemberPendingPlayerOp(member, new PlayerGuildOpTransactionCommitted(serializedPlayerInitiatingAction, serializedPlayerFinalizingAction));
            }

            GuildActionBase                 finalizingAction            = transaction.CreateFinalizingGuildAction(finalizingPlan);
            MetaSerialized<GuildActionBase> serializedFinalizingAction;

            if (finalizingAction != null)
                serializedFinalizingAction = MetaSerialization.ToMetaSerialized(finalizingAction, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
            else
                serializedFinalizingAction = default;

            if (finalizingAction != null)
            {
                finalizingAction.InvokingPlayerId = playerId;
                ExecuteActionImmediately(finalizingAction);
            }

            if (consistencyMode == GuildTransactionConsistencyMode.EventuallyConsistent)
            {
                // Commit with completed Txn and log updated.
                // We don't need to persist now, but we must guarantee that we persist BEFORE player does.
                // This is the easiest way. Alternative would be to prevent player from persisting until
                // this guild has persisted. Then in Player.PersistState we need to either wait for the guild
                // or not persist at all.
                await PersistStateIntermediate();
            }

            sync.Send(InternalGuildTransactionGuildSync.PlannedAndCommitted.CreateOk(guildPlan, serializedFinalizingAction, preceedingPlayerOps, member.LastPendingPlayerOpEpoch));

            // send the finalizing action only to other members. The invoking member will get it via the response
            InternalSessionGuildTimelineUpdate actionUpdate = TryGatherFlushActions();
            if (actionUpdate != null)
                PublishToMembers(actionUpdate, exceptForPlayer: playerId);

            PublishToMember(playerId, new InternalPig(begin.PubsubPiggingId));
        }

        [MessageHandler]
        void HandleInternalGuildMemberPlayerDataUpdate(InternalGuildMemberPlayerDataUpdate update)
        {
            bool isAMember;
            if (!Model.TryGetMember(update.PlayerId, out GuildMemberBase member))
            {
                _log.Warning("Got player data update from non-member {PlayerId}.", update.PlayerId);
                isAMember = false;
            }
            else if (member.MemberInstanceId != update.MemberInstanceId)
            {
                _log.Warning("Got player data update from stale member {PlayerId}. MemberInstanceId={MemberInstanceId}, request {RequestMemberInstanceId}.", update.PlayerId, member.MemberInstanceId, update.MemberInstanceId);
                isAMember = false;
            }
            else
                isAMember = true;

            if (!isAMember)
            {
                // Make caller update its state.
                CastMessage(update.PlayerId, new InternalPlayerKickedFromGuild(_entityId, update.MemberInstanceId));
                return;
            }

            if (!update.PlayerData.IsUpToDate(member))
                ExecuteAction(new GuildMemberPlayerDataUpdate(update.PlayerId, update.PlayerData));
        }

        #region GuildRecommender

        /// <summary>
        /// Enqueues the update of the <see cref="CreateGuildDiscoveryInfo"/> to the guild recommender
        /// and search.
        /// </summary>
        protected void EnqueueGuildDiscoveryInfoUpdate()
        {
            // postpone to coalesce updates and avoid re-entrancy from action listener
            if (_pendingDiscoveryInfoUpdate)
                return;
            _pendingDiscoveryInfoUpdate = true;
            _self.Tell(RefreshGuildDiscoveryInfo.Instance, sender: _self);
        }

        [CommandHandler]
        void HandleRefreshGuildDiscoveryInfo(RefreshGuildDiscoveryInfo command)
        {
            if (!_pendingDiscoveryInfoUpdate)
                return;

            _pendingDiscoveryInfoUpdate = false;
            UpdateGuildRecommender();
        }

        void UpdateGuildRecommender()
        {
            switch (Model.LifecyclePhase)
            {
                case GuildLifecyclePhase.Running:
                    (GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo) = CreateGuildDiscoveryInfo();
                    CastMessage(GuildRecommenderActorBase.EntityId, new InternalGuildRecommenderGuildUpdate(publicDiscoveryInfo, serverOnlyDiscoveryInfo));
                    break;

                case GuildLifecyclePhase.Closed:
                    CastMessage(GuildRecommenderActorBase.EntityId, new InternalGuildRecommenderGuildRemove(_entityId));
                    break;
            }

            _pendingDiscoveryInfoUpdate = false;
        }

        #endregion

        #region GuildOps

        bool ContainsAnyUncommittedGuildMemberGuildOp(EntityId playerId, OrderedDictionary<int, GuildMemberGuildOpLogEntry> ops)
        {
            if (!_persistedLastGuildOpEpochs.TryGetValue(playerId, out int committedLastGuildOpEpoch))
                return true;

            foreach (int epoch in ops.Keys)
            {
                if (epoch > committedLastGuildOpEpoch)
                    return true;
            }
            return false;
        }

        void ExecutePendingGuildMemberGuildOps(EntityId playerId, GuildMemberBase member, OrderedDictionary<int, GuildMemberGuildOpLogEntry> ops)
        {
            bool didExecuteAnyAction = false;
            foreach ((int epoch, GuildMemberGuildOpLogEntry pendingOp) in ops)
            {
                if (epoch <= member.LastGuildOpEpoch)
                {
                    // we have already run this epoch
                    continue;
                }

                // we are going to execute this. Sync time

                if (!didExecuteAnyAction)
                {
                    didExecuteAnyAction = true;
                    UpdateTicks();
                }

                // Execute op.

                ExecuteGuildMemberGuildOp(playerId, member, epoch, pendingOp);
            }
        }

        void ExecuteGuildMemberGuildOp(EntityId playerId, GuildMemberBase member, int epoch, GuildMemberGuildOpLogEntry pendingOp)
        {
            member.LastGuildOpEpoch = epoch;

            // We need to be a bit careful if execution of an action crashes we don't end up with a
            // infinite loop (because member will send the actions again until they are executed).

            try
            {
                switch (pendingOp)
                {
                    case GuildOpRunGuildAction pendingAction:
                    {
                        GuildActionBase action = pendingAction.Action.Deserialize(resolver: null, logicVersion: _logicVersion);
                        action.InvokingPlayerId = playerId;
                        ExecuteActionImmediately(action);
                        break;
                    }

                    default:
                    {
                        _log.Warning("Unhandled guild op {Type}. Ignored.", pendingOp.GetType());
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Executing ExecuteGuildMemberGuildOp failed. Dropping op. Error={Cause}", ex);
            }
        }

        #endregion

        #region PlayerOps

        OrderedDictionary<int, GuildMemberPlayerOpLogEntry> TryGetMemberPendingPlayerOpsAfterEpoch(GuildMemberBase member, int afterEpoch) => TryGetMemberPendingPlayerOpsAfterEpoch(member.PendingPlayerOps, afterEpoch);
        OrderedDictionary<int, GuildMemberPlayerOpLogEntry> TryGetMemberPendingPlayerOpsAfterEpoch(GuildPendingMemberKickState kickState, int afterEpoch) => TryGetMemberPendingPlayerOpsAfterEpoch(kickState.PendingPlayerOps, afterEpoch);
        OrderedDictionary<int, GuildMemberPlayerOpLogEntry> TryGetMemberPendingPlayerOpsAfterEpoch(OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingOps, int afterEpoch)
        {
            OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps = null;
            if (pendingOps != null)
            {
                foreach ((int epoch, GuildMemberPlayerOpLogEntry op) in pendingOps)
                {
                    if (epoch <= afterEpoch)
                        continue;
                    if (preceedingPlayerOps == null)
                        preceedingPlayerOps = new OrderedDictionary<int, GuildMemberPlayerOpLogEntry>();
                    preceedingPlayerOps.Add(epoch, op);
                }
            }
            return preceedingPlayerOps;
        }

        void AfterMemberPendingPlayerOpsCommitted(GuildMemberBase member, int committedOpEpoch) =>  AfterMemberPendingPlayerOpsCommitted(ref member.PendingPlayerOps, committedOpEpoch);
        void AfterMemberPendingPlayerOpsCommitted(GuildPendingMemberKickState kickState, int committedOpEpoch) => AfterMemberPendingPlayerOpsCommitted(ref kickState.PendingPlayerOps, committedOpEpoch);
        void AfterMemberPendingPlayerOpsCommitted(ref OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingOps, int committedOpEpoch)
        {
            if (pendingOps == null)
                return;
            pendingOps.RemoveWhere(kv => kv.Key <= committedOpEpoch);
            if (pendingOps.Count == 0)
                pendingOps = null;
        }

        void EnqueueMemberPendingPlayerOp(GuildMemberBase member, GuildMemberPlayerOpLogEntry entry)
        {
            member.LastPendingPlayerOpEpoch++;
            int epoch = member.LastPendingPlayerOpEpoch;
            if (member.PendingPlayerOps == null)
                member.PendingPlayerOps = new OrderedDictionary<int, GuildMemberPlayerOpLogEntry>();
            member.PendingPlayerOps.Add(epoch, entry);
        }

        [MessageHandler]
        void HandleInternalGuildPlayerOpsCommitted(InternalGuildPlayerOpsCommitted opsCommitted)
        {
            if (Model.TryGetMember(opsCommitted.PlayerId, out GuildMemberBase member))
            {
                if (opsCommitted.MemberInstanceId == member.MemberInstanceId)
                {
                    AfterMemberPendingPlayerOpsCommitted(member, opsCommitted.CommittedOpEpoch);
                    return;
                }
            }
            else if (Model.PendingKicks.TryGetValue(opsCommitted.PlayerId, out GuildPendingMemberKickState pendingKickState))
            {
                // Handle player acking player ops issued before it was kicked.

                if (opsCommitted.MemberInstanceId == pendingKickState.MemberInstanceId)
                {
                    AfterMemberPendingPlayerOpsCommitted(pendingKickState, opsCommitted.CommittedOpEpoch);
                    return;
                }
            }

            // Make caller update its state.
            CastMessage(opsCommitted.PlayerId, new InternalPlayerKickedFromGuild(_entityId, opsCommitted.MemberInstanceId));
        }

        #endregion

        #region Guild Invites

        [PubSubMessageHandler]
        async Task HandleGuildCreateInvitationRequest(EntitySubscriber session, GuildCreateInvitationRequest request)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(session.EntityId);
            MetaTime now = MetaTime.Now;

            // validate and rate limit
            GuildCreateInvitationResponse.StatusCode status;
            if (!Model.TryGetMember(playerId, out GuildMemberBase member))
            {
                _log.Warning("Got invite code generation request from non-member {PlayerId}.", playerId);
                status = GuildCreateInvitationResponse.StatusCode.NotAMember;
            }
            else if (!Model.HasPermissionToInvite(playerId, request.Type))
            {
                _log.Warning("Got invite code generation request from {PlayerId}. No permission.", playerId);
                status = GuildCreateInvitationResponse.StatusCode.NotAllowed;
            }
            else if (_nextInvitationCreationEarliestAt.GetValueOrDefault(member.MemberInstanceId) > now)
            {
                status = GuildCreateInvitationResponse.StatusCode.RateLimited;
            }
            else
            {
                status = GuildCreateInvitationResponse.StatusCode.Success;
            }

            if (status == GuildCreateInvitationResponse.StatusCode.Success)
            {
                _nextInvitationCreationEarliestAt[member.MemberInstanceId] = now + MetaDuration.FromSeconds(1);

                UpdateTicks();
                await CleanExpiredInvitesOnTimelineAsync(playerId, member);
                FlushActions();

                if (member.Invites.Count >= member.MaxNumInvites)
                    status = GuildCreateInvitationResponse.StatusCode.TooManyInvites;
            }

            if (status != GuildCreateInvitationResponse.StatusCode.Success)
            {
                SendMessage(session, GuildCreateInvitationResponse.CreateRefusal(request.QueryId, status));
                return;
            }

            // Validation ok. Create invitation

            GuildInviteState invite;
            int inviteId;

            switch (request.Type)
            {
                case GuildInviteType.InviteCode:
                {
                    inviteId = Model.RunningInviteId++;

                    GuildInviteCode inviteCode = await CreateGuildInviteCodeAsync(inviteId);

                    invite = new GuildInviteState(
                        type:           GuildInviteType.InviteCode,
                        createdAt:      MetaTime.Now,
                        expiresAfter:   request.ExpiresAfter,
                        numMaxUsages:   request.NumMaxUsages,
                        numTimesUsed:   0,
                        inviteCode:     inviteCode);
                    break;
                }

                default:
                    throw new InvalidOperationException();
            }

            // Execute action on invoking player and the server. Other members are not informed
            GuildInviteUpdate action = new GuildInviteUpdate(playerId, inviteId, invite);
            ExecuteAndFlushSecretActionImmediately(action, visibleToMember: playerId);

            SendMessage(session, GuildCreateInvitationResponse.CreateSuccess(request.QueryId, inviteId));
        }

        [PubSubMessageHandler]
        async Task HandleGuildRevokeInvitationRequest(EntitySubscriber session, GuildRevokeInvitationRequest request)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(session.EntityId);

            if (!Model.TryGetMember(playerId, out GuildMemberBase member))
            {
                _log.Warning("Got invite code revocation request from non-member {PlayerId}.", playerId);
                return;
            }
            if (!member.Invites.TryGetValue(request.InviteId, out GuildInviteState invite))
            {
                _log.Debug("Got invite code revocation for unknown id {InviteId}.", request.InviteId);
                return;
            }

            await RemoveInviteExternalResourcesAsync(_log, invite);

            GuildInviteUpdate action = new GuildInviteUpdate(playerId, request.InviteId, newState: null);
            ExecuteAndFlushSecretActionImmediately(action, visibleToMember: playerId);
        }

        [EntityAskHandler]
        async Task<InternalGuildInspectInviteCodeResponse> HandleInternalGuildInspectInviteCodeRequest(InternalGuildInspectInviteCodeRequest request)
        {
            if (!TryGetGuildInviteByIdAndCode(request.InviteId, request.InviteCode, out GuildInviteState invite, out EntityId playerId))
            {
                return new InternalGuildInspectInviteCodeResponse(false, default, EntityId.None);
            }

            if (invite.IsExpired(MetaTime.Now))
            {
                // Remove invite from the player
                await RemoveExpiredInviteAsync(playerId, request.InviteId, invite);
                return new InternalGuildInspectInviteCodeResponse(false, default, EntityId.None);
            }

            (GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase _) = CreateGuildDiscoveryInfo();
            MetaSerialized<GuildDiscoveryInfoBase> discoveryInfo = MetaSerialization.ToMetaSerialized<GuildDiscoveryInfoBase>(publicDiscoveryInfo, MetaSerializationFlags.IncludeAll, _logicVersion);
            return new InternalGuildInspectInviteCodeResponse(true, discoveryInfo, playerId);
        }

        async Task RemoveExpiredInviteAsync(EntityId playerId, int inviteId, GuildInviteState invite)
        {
            GuildInviteUpdate action = new GuildInviteUpdate(playerId, inviteId, newState: null);
            ExecuteAndFlushSecretActionImmediately(action, visibleToMember: playerId);
            await RemoveInviteExternalResourcesAsync(_log, invite);
        }

        bool TryGetGuildInviteByIdAndCode(int inviteId, GuildInviteCode inviteCode, out GuildInviteState outInvite, out EntityId outPlayerId)
        {
            foreach (EntityId playerId in Model.EnumerateMembers())
            {
                if (!Model.TryGetMember(playerId, out GuildMemberBase member))
                    continue;
                if (!member.Invites.TryGetValue(inviteId, out GuildInviteState invite))
                    continue;
                if (invite.InviteCode != inviteCode)
                    continue;

                outInvite = invite;
                outPlayerId = playerId;
                return true;
            }

            outInvite = default;
            outPlayerId = default;
            return false;
        }

        void UseGuildInvite(int inviteId, out GuildInviteState inviteRemoved)
        {
            foreach (EntityId memberId in Model.EnumerateMembers())
            {
                if (!Model.TryGetMember(memberId, out GuildMemberBase member))
                    continue;
                if (!member.Invites.TryGetValue(inviteId, out GuildInviteState invite))
                    continue;

                bool countExpires = invite.NumMaxUsages > 0 && (invite.NumTimesUsed + 1) >= invite.NumMaxUsages;
                bool timeExpires = invite.IsExpired(MetaTime.Now);

                GuildInviteUpdate action;
                if (countExpires || timeExpires)
                {
                    // invite expires after this use
                    action = new GuildInviteUpdate(memberId, inviteId, newState: null);
                    inviteRemoved = invite;
                }
                else
                {
                    // invite did not expire, bump use count
                    GuildInviteState newState = MetaSerialization.CloneTagged<GuildInviteState>(invite, MetaSerializationFlags.IncludeAll, _logicVersion, null);
                    newState.NumTimesUsed++;
                    action = new GuildInviteUpdate(memberId, inviteId, newState);
                    inviteRemoved = null;
                }

                ExecuteAndFlushSecretActionImmediately(action, memberId);
                return;
            }

            inviteRemoved = null;
        }

        void ExecuteAndFlushSecretActionImmediately(GuildActionBase action, EntityId visibleToMember)
        {
            // Flush all previous. This is necessary for the hack
            UpdateTicks();
            FlushActions();

            // Execute real action
            ExecuteActionImmediately(action);
            InternalSessionGuildTimelineUpdate actionUpdate = TryGatherFlushActions();
            if (actionUpdate == null)
                throw new InvalidOperationException();

            // publish real update to the visible member
            PublishToMember(visibleToMember, actionUpdate);

            // publish hidden update to other members. This syncs the timeline
            // \todo: consider syncing timeline explicitly instead of tracking real timeline with hidden actions

            GuildHiddenAction hiddenAction = new GuildHiddenAction();
            List<GuildTimelineUpdateMessage.Operation> hiddenOperations = new List<GuildTimelineUpdateMessage.Operation>(capacity: 1);
            hiddenOperations.Add(new GuildTimelineUpdateMessage.Operation(hiddenAction, invokingPlayerId: EntityId.None));
            InternalSessionGuildTimelineUpdate hiddenUpdate = new InternalSessionGuildTimelineUpdate(MetaSerialization.ToMetaSerialized(hiddenOperations, MetaSerializationFlags.SendOverNetwork, _logicVersion), actionUpdate.StartTick, actionUpdate.StartOperation, actionUpdate.FinalChecksum);
            PublishToMembers(hiddenUpdate, exceptForPlayer: visibleToMember);
        }

        /// <summary>
        /// Cleans expired invites by directly mutating state
        /// </summary>
        static async Task CleanExpiredInvitesOnModelAsync(IMetaLogger log, TModel model, MetaTime currentTime)
        {
            foreach (EntityId playerId in model.EnumerateMembers())
            {
                if (!model.TryGetMember(playerId, out GuildMemberBase member))
                    continue;

                bool didAdvance;
                do
                {
                    didAdvance = false;
                    foreach ((int inviteId, GuildInviteState inviteState) in member.Invites)
                    {
                        if (!inviteState.IsExpired(currentTime))
                            continue;

                        await RemoveInviteExternalResourcesAsync(log, inviteState);
                        member.Invites.Remove(inviteId);
                        didAdvance = true;
                        break;
                    }
                } while (didAdvance);
            }
        }

        /// <summary>
        /// Cleans expired invites by using Actions.
        /// </summary>
        async Task CleanExpiredInvitesOnTimelineAsync(EntityId memberId, GuildMemberBase member)
        {
            bool didAdvance;
            do
            {
                didAdvance = false;
                foreach ((int inviteId, GuildInviteState inviteState) in member.Invites)
                {
                    if (!inviteState.IsExpired(Model.CurrentTime))
                        continue;

                    await RemoveInviteExternalResourcesAsync(_log, inviteState);
                    ExecuteActionImmediately(new GuildInviteUpdate(memberId, inviteId, newState: null));
                    didAdvance = true;
                    break;
                }
            } while (didAdvance);
        }

        static async Task RemoveInviteExternalResourcesAsync(IMetaLogger log, GuildInviteState invite)
        {
            switch (invite.Type)
            {
                case GuildInviteType.InviteCode:
                {
                    // invite code expired. It might be removed already
                    string inviteCodeString = invite.InviteCode.ToString();
                    log.Debug("Removing expired invitation {InviteCode} from database", inviteCodeString);
                    // \todo[jarkko]: could this be fire-and-forget?
                    _ = await MetaDatabase.Get().RemoveAsync<PersistedGuildInviteCode>(inviteCodeString);
                    break;
                }
            }
        }

        async Task<GuildInviteCode> CreateGuildInviteCodeAsync(int inviteId)
        {
            const int MaxNumAttempts = 3;
            int numAttempts = 0;

            for (;;)
            {
                GuildInviteCode inviteCode = GuildInviteCode.CreateNew();

                try
                {
                    PersistedGuildInviteCode persisted = new PersistedGuildInviteCode(
                        inviteCode: inviteCode.ToString(),
                        guildId:    _entityId.ToString(),
                        inviteId:   inviteId,
                        createdAt:  DateTime.UtcNow);
                    await MetaDatabase.Get().InsertAsync<PersistedGuildInviteCode>(persisted);
                    return inviteCode;
                }
                catch (Exception ex)
                {
                    _log.Warning("Failed to register invite, already exists?: {Exception}", ex);

                    numAttempts++;
                    if (numAttempts >= MaxNumAttempts)
                        throw;
                }
            }
        }

        async Task RemoveAllInviteExternalResourcesAsync()
        {
            foreach (EntityId playerId in Model.EnumerateMembers())
            {
                if (!Model.TryGetMember(playerId, out GuildMemberBase member))
                    continue;
                await RemoveAllMemberInviteExternalResourcesAsync(member);
            }
        }

        async Task RemoveAllMemberInviteExternalResourcesAsync(GuildMemberBase member)
        {
            foreach ((int inviteId, GuildInviteState inviteState) in member.Invites)
                await RemoveInviteExternalResourcesAsync(_log, inviteState);
        }

        async Task RefreshAllInviteExternalResourcesAsync()
        {
            foreach (EntityId playerId in Model.EnumerateMembers())
            {
                if (!Model.TryGetMember(playerId, out GuildMemberBase member))
                    continue;

                foreach ((int inviteId, GuildInviteState inviteState) in member.Invites)
                {
                    if (inviteState.Type == GuildInviteType.InviteCode)
                    {
                        PersistedGuildInviteCode persisted = new PersistedGuildInviteCode(
                            inviteCode: inviteState.InviteCode.ToString(),
                            guildId:    _entityId.ToString(),
                            inviteId:   inviteId,
                            createdAt:  inviteState.CreatedAt.ToDateTime());

                        try
                        {
                            await MetaDatabase.Get().InsertAsync<PersistedGuildInviteCode>(persisted);
                        }
                        catch (Exception ex)
                        {
                            _log.Warning("Failed refresh guild invite code: {Exception}", ex);
                        }
                    }
                }
            }
        }

        #endregion

        #region Analytics

        static GuildAnalyticsContext GetAnalyticsContext()
        {
            return new GuildAnalyticsContext();
        }

        /// <summary>
        /// Gets the analytics labels and their value which are added to emitted analytics events. Returning null means no labels. The default implementation returns null.
        /// </summary>
        protected virtual OrderedDictionary<AnalyticsLabel, string> GetAnalyticsLabels(TModel model)
        {
            return null;
        }

        #endregion

        /// <summary>
        /// Should be called after member has been removed from Members.
        /// </summary>
        protected void OnPlayerKicked(EntityId kickedPlayerId, GuildMemberBase kickedMember, GuildEventInvokerInfo kickInvoker, IGuildMemberKickReason kickReasonOrNull)
        {
            Model.PendingKicks.Add(kickedPlayerId, new GuildPendingMemberKickState(MetaTime.Now, kickReasonOrNull, kickedMember.PendingPlayerOps, kickedMember.MemberInstanceId));
            Model.EventStream.Event(new GuildEventMemberKicked(kickedMember: GuildEventMemberInfo.Create(kickedPlayerId, kickedMember), kickInvoker: kickInvoker));
            SchedulePersistState();

            // Let player and possible session know.
            CastMessage(kickedPlayerId, new InternalPlayerKickedFromGuild(_entityId, kickedMember.MemberInstanceId));
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber.Topic == EntityTopic.Member && SessionIdUtil.ToPlayerId(subscriber.EntityId) == kickedPlayerId)
                {
                    KickSubscriber(subscriber, InternalSessionGuildMemberKicked.Instance);
                    break;
                }
            }

            // Enqueue cleanup
            _ = ExecuteOnActorContextAsync(async () => await RemoveAllMemberInviteExternalResourcesAsync(kickedMember));
        }

        [EntityAskHandler]
        async Task<InternalGuildPeekKickedStateResponse> HandleInternalGuildPeekKickedStateRequest(InternalGuildPeekKickedStateRequest request)
        {
            // Is a member, i.e. not kicked?
            if (Model.TryGetMember(request.PlayerId, out GuildMemberBase member))
            {
                if (member.MemberInstanceId == request.MemberInstanceId)
                {
                    // Player is a member. Should not continue with the kick flow.
                    return InternalGuildPeekKickedStateResponse.CreateNotKicked();
                }
                else
                {
                    // Player's other instance is a member. Since the player (caller) does not
                    // think it is that member, our member player is not "real". Remove it.

                    _log.Warning("Got kick peek request from stale member {PlayerId}. MemberInstanceId={MemberInstanceId}, request {RequestMemberInstanceId}. Removing member.", request.PlayerId, member.MemberInstanceId, request.MemberInstanceId);

                    GuildEventMemberInfo eventMemberInfo = GuildEventMemberInfo.Create(request.PlayerId, member);
                    ExecuteAction(new GuildMemberRemove(request.PlayerId));
                    Model.EventStream.Event(new GuildEventMemberRemovedDueToInconsistency(eventMemberInfo, GuildEventMemberRemovedDueToInconsistency.InconsistencyType.PeekKickedStateAttemptMemberInstanceIdDiffers));
                    await RemoveAllMemberInviteExternalResourcesAsync(member);
                    await PersistStateIntermediate();

                    return InternalGuildPeekKickedStateResponse.CreateNotAMember();
                }
            }

            // Not a member or pending kick?
            if (!Model.PendingKicks.TryGetValue(request.PlayerId, out GuildPendingMemberKickState pendingKickState))
            {
                return InternalGuildPeekKickedStateResponse.CreateNotAMember();
            }
            if (pendingKickState.MemberInstanceId != request.MemberInstanceId)
            {
                // Player (caller) is desynchronized to a point it cannot fetch kick state. Clear it as it has become useless.
                Model.PendingKicks.Remove(request.PlayerId);
                return InternalGuildPeekKickedStateResponse.CreateNotAMember();
            }

            // Make sure player has executed all of player ops.
            AfterMemberPendingPlayerOpsCommitted(pendingKickState, request.CommittedPlayerOpEpoch);
            if (pendingKickState.PendingPlayerOps != null)
            {
                return InternalGuildPeekKickedStateResponse.CreatePendingPlayerOps(pendingKickState.PendingPlayerOps);
            }

            return InternalGuildPeekKickedStateResponse.CreateKicked(pendingKickState.ReasonOrNull);
        }

        [MessageHandler]
        void HandleInternalGuildPlayerClearKickedState(InternalGuildPlayerClearKickedState request)
        {
            if (!Model.PendingKicks.TryGetValue(request.PlayerId, out GuildPendingMemberKickState pendingKickState))
                return;
            if (pendingKickState.MemberInstanceId != request.MemberInstanceId)
                return;
            Model.PendingKicks.Remove(request.PlayerId);
        }

        [EntityAskHandler]
        async Task<PersistStateRequestResponse> HandlePersistStateRequestRequest(PersistStateRequestRequest _)
        {
            await PersistStateIntermediate();
            return PersistStateRequestResponse.Instance;
        }

        #region Event log

        static EntityEventLogOptionsBase GetEventLogConfiguration()
        {
            return RuntimeOptionsRegistry.Instance.GetCurrent<GuildEventLogOptions>();
        }

        void TryEnqueueTriggerEventLogFlushing()
        {
            if (GuildEventLogUtil.CanFlush(Model.EventLog, GetEventLogConfiguration()))
                CastMessage(_entityId, new TriggerEventLogFlushing());
        }

        [MessageHandler]
        async Task HandleTriggerEventLogFlushing(TriggerEventLogFlushing _)
        {
            await GuildEventLogUtil.TryFlushAndCullSegmentsAsync(Model.EventLog, GetEventLogConfiguration(), _entityId, _logicVersion, _log, PersistStateIntermediate);
        }

        [EntityAskHandler]
        async Task<GuildEventLogScanResponse> HandleEntityEventLogScanRequest(EntityEventLogScanRequest request)
        {
            try
            {
                return await GuildEventLogUtil.ScanEntriesAsync(Model.EventLog, _entityId, _baselineGameConfigResolver, _logicVersion, request);
            }
            catch (Exception ex)
            {
                throw new InvalidEntityAsk(ex.ToString());
            }
        }

        Task RemoveAllOwnedEventLogSegmentsAsync()
        {
            return GuildEventLogUtil.RemoveAllSegmentsAsync(Model.EventLog, _entityId, PersistStateIntermediate);
        }

        #endregion

        #region IGuildModelServerListenerCore

        void IGuildModelServerListenerCore.GuildDiscoveryInfoChanged()
        {
            EnqueueGuildDiscoveryInfoUpdate();
        }

        void IGuildModelServerListenerCore.PlayerKicked(EntityId kickedPlayerId, GuildMemberBase kickedMember, EntityId kickingPlayerId, IGuildMemberKickReason kickReasonOrNull)
        {
            if (Model.TryGetMember(kickedPlayerId, out var _))
                throw new InvalidOperationException("PlayerKicked must be called only after member has been removed from Members");

            if (!Model.TryGetMember(kickingPlayerId, out GuildMemberBase kickingMember))
                _log.Warning("PlayerKicked called with a kickingPlayerId that isn't a current member");

            GuildEventInvokerInfo kickInvoker = GuildEventInvokerInfo.ForMember(kickingMember != null ? GuildEventMemberInfo.Create(kickingPlayerId, kickingMember) : new GuildEventMemberInfo());
            OnPlayerKicked(kickedPlayerId, kickedMember, kickInvoker, kickReasonOrNull);
        }

        void IGuildModelServerListenerCore.MemberPlayerDataUpdated(EntityId memberId)
        {
        }

        void IGuildModelServerListenerCore.MemberRoleChanged(EntityId memberId)
        {
        }

        void IGuildModelServerListenerCore.MemberRemoved(EntityId memberId)
        {
        }

        #endregion

        #region Model creation and initialization

        TModel CreateInitialModel(LogChannel initialLogChannel)
        {
            TModel model = new TModel();
            model.GuildId = _entityId;
            model.CreatedAt = MetaTime.Now;
            model.LifecyclePhase = GuildLifecyclePhase.WaitingForSetup;
            model.DisplayName = "";
            AssignBasicRuntimePropertiesToModel(model, initialLogChannel);
            return model;
        }

        TModel CreateClosedTombstoneModel(LogChannel logChannel)
        {
            TModel closedModel = new TModel();
            closedModel.GuildId = _entityId;
            closedModel.CreatedAt = Model.CreatedAt;
            closedModel.LifecyclePhase = GuildLifecyclePhase.Closed;
            closedModel.DisplayName = "";
            AssignBasicRuntimePropertiesToModel(closedModel, logChannel);
            return closedModel;
        }

        protected TModel CreateModelFromSerialized(byte[] serialized, LogChannel logChannel)
        {
            TModel model = DeserializePersistedPayload<TModel>(serialized, _baselineGameConfigResolver, _logicVersion);
            AssignBasicRuntimePropertiesToModel(model, logChannel);
            return model;
        }

        protected LogChannel CreateModelLogChannel(string name)
        {
            return new LogChannel(name, _log, MetaLogger.MetaLogLevelSwitch);
        }

        /// <summary>
        /// Assign to the model runtime properties that should always be present, such as Game Configs and logic versions.
        /// </summary>
        protected void AssignBasicRuntimePropertiesToModel(TModel model, LogChannel logChannel)
        {
            // \note ServerListener is not assigned here. That's only done when the model becomes the actor's current execution model (i.e. in OnSwitchedToModel).
            //       Here should be assigned runtime properties that we want to be always available in the model, even during MigrateState.
            model.LogicVersion          = _logicVersion;
            model.GameConfig            = _activeGameConfig.BaselineGameConfig.SharedConfig;
            model.Log                   = logChannel; // \note Assigning the specified log channel, which is not necessarily _modelLogChannel.
            model.AnalyticsEventHandler = _analyticsEventHandler;
            model.ResetTime(MetaTime.Now);
        }

        protected override void OnSchemaMigrated(TModel model, int fromVersion, int toVersion)
        {
            // Analytics
            _analyticsEventHandler.Event(model, new GuildEventModelSchemaMigrated(fromVersion, toVersion));
        }

        #endregion

        #region Callbacks to userland

        /// <summary>
        /// Returns true if player is allowed to join this guild. False otherwise. This is called for joining players,
        /// but not for the guild creating player (the first member).
        /// </summary>
        protected abstract bool ShouldAcceptPlayerJoin(EntityId playerId, GuildMemberPlayerDataBase playerData, bool isInvited);

        /// <summary>
        /// Creates a <typeparamref name="TPersisted"/> representation of the current Model with the given arguments.
        /// </summary>
        protected abstract TPersisted CreatePersisted(EntityId entityId, DateTime persistedAt, byte[] payload, int schemaVersion, bool isFinal);

        /// <summary>
        /// Creates a discovery data representation of the current Model. This data is used for search
        /// and recommendation. See <see cref="GuildDiscoveryInfoBase"/> and <see cref="GuildDiscoveryServerOnlyInfoBase"/>
        /// for more information.
        /// </summary>
        protected abstract (GuildDiscoveryInfoBase, GuildDiscoveryServerOnlyInfoBase) CreateGuildDiscoveryInfo();

        /// <summary>
        /// Called when new model instance is becoming active. Callee should should set up listeners.
        /// After switching to the model is complete, the <see cref="_journal"/> will be
        /// set to track it and <see cref="Model"/> can be used to refer to it.
        ///
        /// This function is called during entity initialization, after state reset, and in
        /// <see cref="SwitchToNewModelImmediately(TModel)"/>.
        /// </summary>
        protected virtual void OnSwitchedToModel(TModel model) { }

        void OnSwitchedToModelCore(TModel model)
        {
            AssignBasicRuntimePropertiesToModel(model, _modelLogChannel);
            model.ServerListenerCore = this;

            // Forward to game.
            OnSwitchedToModel(model);
        }

        protected virtual void SetupGuildWithCreationParams(GuildCreationParamsBase args)
        {
            Model.DisplayName = args.DisplayName;
            Model.Description = args.Description;
        }

        /// <summary>
        /// When a player request GDPR data export, the relevant fields from Members[PlayerId] are automatically
        /// included in the report. However, the guild state might contain personal player data also elsewhere.
        /// If this is the case, package and return that personal data for the given member in a json serializeable
        /// object, or return null otherwise.
        /// </summary>
        protected abstract object GetMemberGdprExportExtraData(EntityId memberPlayerId);

        /// <summary>
        /// Called when the guild is created and has just transitioned to Running phase but just before the founding player becomes
        /// online in the guild. The founding member is the only member in the Members list. During this call, the Model fields may
        /// be modified directly without the use of actions. If actions are used, the founding memeber will only see the final result.
        /// Specifically, the action's client listeners are not called as the founding member is not yet online on this guild.
        /// </summary>
        protected virtual void OnGuildBecameRunning() { }

        /// <summary>
        /// Called when the guild has become empty and is transitioning into Closed phase.
        /// </summary>
        protected virtual Task OnGuildBecomingClosed() => Task.CompletedTask;

        /// <summary>
        /// Called just before Model is serialized and DB write is issued. This can be used to update auxiliary tables
        /// and or modify Model just before it is written.
        /// </summary>
        protected virtual ValueTask BeforePersistAsync() => ValueTask.CompletedTask;

        #endregion

        #region Validation

        bool ValidateClientOriginatingAction(GuildActionBase action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // Must be client-issuable and enqueable.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerSynchronized))
            {
                _log.Warning("Client tried to enqueue Action {Action} which does not have FollowerSynchronized mode. ExecuteFlags={ExecuteFlags}.", action.GetType().ToGenericTypeString(), actionSpec.ExecuteFlags);
                return false;
            }

            bool isDevelopmentOnlyAction = actionSpec.HasCustomAttribute<DevelopmentOnlyActionAttribute>();
            if (isDevelopmentOnlyAction)
            {
                _log.Info("Executing development-only action: {Action}", action.GetType().ToGenericTypeString());

                EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
                if (!envOpts.EnableDevelopmentFeatures && !GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(action.InvokingPlayerId))
                {
                    _log.Warning("Client tried to run development-only action {Action}, but Development-Only actions are not enabled.", action.GetType().ToGenericTypeString());
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Admin

        [MessageHandler]
        void HandleInternalGuildAdminKickMember(EntityId fromEntityId, InternalGuildAdminKickMember request)
        {
            if (!Model.TryGetMember(request.PlayerId, out GuildMemberBase member))
            {
                _log.Warning("Received admin action to kick member {PlayerId}, but there is no such member.", request.PlayerId);
                return;
            }

            _log.Debug("Received admin action to kick member {PlayerId}.", request.PlayerId);
            Model.RemoveMember(request.PlayerId);
            OnPlayerKicked(request.PlayerId, member, GuildEventInvokerInfo.ForAdmin(), kickReasonOrNull: null);
        }

        /// <summary>
        /// Attempt to change the guild's name and description. Can be used to validate the change only (ie: validate but don't set)
        /// by passing true in request.ValidateOnly
        /// </summary>
        [EntityAskHandler]
        async Task<InternalGuildChangeDisplayNameAndDescriptionResponse> HandleInternalGuildChangeDisplayNameAndDescriptionRequest(InternalGuildChangeDisplayNameAndDescriptionRequest request)
        {
            var result = await ValidateAndChangeNameAndDescription(request.Invoker, request.NewDisplayName, request.NewDescription, request.ValidateOnly);
            return new InternalGuildChangeDisplayNameAndDescriptionResponse(result.DisplayNameWasValid, result.DescriptionWasValid, result.ChangeWasCommitted);
        }

        /// <summary>
        /// Validate a new guild name and description and set it (using a ServerAction) if it is acceptable
        /// </summary>
        /// <param name="newName">The proposed new name</param>
        /// <param name="validateOnly">If set to true then the name change will not be executed, only validation of the name will occur</param>
        /// <returns>True is the name was valid</returns>
        public async Task<(bool DisplayNameWasValid, bool DescriptionWasValid, bool ChangeWasCommitted)> ValidateAndChangeNameAndDescription(GuildEventInvokerInfo invoker, string newDisplayName, string newDescription, bool validateOnly)
        {
            // Is the input valid?
            bool displayNameWasValid;
            bool descriptionWasValid;
            bool changeWasCommitted = false;

            if (Model.LifecyclePhase == GuildLifecyclePhase.Running)
            {
                // Is the input valid?
                displayNameWasValid = IntegrationRegistry.Get<GuildRequirementsValidator>().ValidateDisplayName(newDisplayName);
                descriptionWasValid = IntegrationRegistry.Get<GuildRequirementsValidator>().ValidateDescription(newDescription);
            }
            else
            {
                // Cannot change while dead or just being set up
                displayNameWasValid = false;
                descriptionWasValid = false;
            }

            // Execute the name change if we were requested to do so and if the name is valid and
            // does actually change, then immediately persist the guild's state
            if (displayNameWasValid && descriptionWasValid &&
                (Model.DisplayName != newDisplayName || Model.Description != newDescription) &&
                !validateOnly)
            {
                string oldDisplayName = Model.DisplayName;
                string oldDescription = Model.Description;

                ExecuteAction(new GuildNameAndDescriptionUpdate(newDisplayName, newDescription));
                Model.EventStream.Event(new GuildEventNameAndDescriptionChanged(invoker,
                    oldName:        oldDisplayName,
                    oldDescription: oldDescription,
                    newName:        newDisplayName,
                    newDescription: newDescription));
                await PersistStateIntermediate();

                changeWasCommitted = true;
            }

            return (
                DisplayNameWasValid: displayNameWasValid,
                DescriptionWasValid: descriptionWasValid,
                ChangeWasCommitted: changeWasCommitted
            );
        }

        [EntityAskHandler]
        InternalGuildMemberGdprExportResponse HandleInternalGuildMemberGdprExportRequest(InternalGuildMemberGdprExportRequest request)
        {
            if (!Model.TryGetMember(request.PlayerId, out GuildMemberBase member))
                return new InternalGuildMemberGdprExportResponse(false, null);


            object membershipData = new
            {
                MemberData = member,
                ExtraData = GetMemberGdprExportExtraData(request.PlayerId),
            };
            string jsonString = JsonSerialization.SerializeToString(membershipData, JsonSerialization.GdprSerializer);
            return new InternalGuildMemberGdprExportResponse(true, jsonString);
        }

        [EntityAskHandler]
        async Task<InternalGuildImportModelDataResponse> HandleInternalGuildImportModelDataRequest(InternalGuildImportModelDataRequest request)
        {
            // Try to deserialise the incoming model
            try
            {
                // Deserialize model
                TModel importModel = CreateModelFromSerialized(request.Payload, CreateModelLogChannel("guild-import"));

                // Imported model's bookkeeping must forget the persisted segments, otherwise we'd have to actually copy the source entity's persisted log segments as well!
                importModel.EventLog.ForgetAllPersistedSegments();

                // Run migrations
                if (request.SchemaVersion.HasValue)
                    MigrateState(importModel, request.SchemaVersion.Value);

                // clear all invites
                await RemoveAllInviteExternalResourcesAsync();

                // clear log segments
                await GuildEventLogUtil.RemoveAllSegmentsAsync(Model.EventLog, _entityId, PersistStateIntermediate);

                // Set new model
                SwitchToNewModelImmediately(importModel);

                // write all invites back
                await RefreshAllInviteExternalResourcesAsync();

                // Kick views and member
                foreach (EntitySubscriber subscriber in new List<EntitySubscriber>(_subscribers.Values))
                {
                    if (subscriber.Topic == EntityTopic.Member)
                        KickSubscriber(subscriber, null);
                    else if (subscriber.Topic == EntityTopic.Spectator)
                        KickSubscriber(subscriber, null);
                }

                // Persist before responding ok
                await PersistStateIntermediate();

                return new InternalGuildImportModelDataResponse(true, null);
            }
            catch (Exception ex)
            {
                return new InternalGuildImportModelDataResponse(false, ex.ToString());
            }
        }

        [EntityAskHandler]
        InternalGuildAdminEditRolesResponse HandleInternalGuildAdminEditRolesRequest(InternalGuildAdminEditRolesRequest request)
        {
            if (!Model.TryGetMember(request.TargetPlayerId, out GuildMemberBase invokingMember))
            {
                return new InternalGuildAdminEditRolesResponse(false);
            }

            OrderedDictionary<EntityId, GuildMemberRole> actualChanges = Model.ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberEdit, request.TargetPlayerId, request.TargetNewRole);
            if (!(new HashSet<KeyValuePair<EntityId, GuildMemberRole>>(actualChanges)).SetEquals(new HashSet<KeyValuePair<EntityId, GuildMemberRole>>(request.ExpectedChanges)))
            {
                return new InternalGuildAdminEditRolesResponse(false);
            }

            ExecuteAction(new GuildMemberRolesUpdate(actualChanges));
            return new InternalGuildAdminEditRolesResponse(true);
        }

        #endregion

        #region Misc

        IGameConfigDataResolver GetConfigResolver(FullGameConfig config)
        {
            return config.SharedConfig;
        }

        protected ServerGuildModelJournal CreateModelJournal(IGuildModelBase model, bool enableConsistencyChecks)
        {
            return new ServerGuildModelJournal(
                _modelLogChannel,
                model,
                enableConsistencyChecks:   enableConsistencyChecks);
        }

        #endregion
    }

    public abstract class GuildActorBase<TModel>
    : GuildActorBase<TModel, PersistedGuildBase>
    where TModel : class, IGuildModel<TModel>, new()
    {
        protected GuildActorBase(EntityId guildId) : base(guildId)
        {
        }

        protected sealed override PersistedGuildBase CreatePersisted(EntityId entityId, DateTime persistedAt, byte[] payload, int schemaVersion, bool isFinal)
        {
            return new PersistedGuildBase()
            {
                EntityId = entityId.ToString(),
                PersistedAt = persistedAt,
                Payload = payload,
                SchemaVersion = schemaVersion,
                IsFinal = isFinal,
            };
        }
    }
}

#endif
