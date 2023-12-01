// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Analytics;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Utility;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Debugging;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.InGameMail;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Localization;
using Metaplay.Core.Math;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Model.JournalCheckers;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Network;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Core.Web3;
using Metaplay.Server.Authentication;
using Metaplay.Server.Authentication.Authenticators;
using Metaplay.Server.Database;
using Metaplay.Server.EntityArchive;
using Metaplay.Server.EventLog;
using Metaplay.Server.GameConfig;
using Metaplay.Server.InAppPurchase;
using Metaplay.Server.League;
using Metaplay.Server.League.InternalMessages;
using Metaplay.Server.League.Player.InternalMessages;
using Metaplay.Server.MaintenanceJob;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Metaplay.Server.PushNotification;
using Metaplay.Server.ScheduledPlayerDeletion;
using Metaplay.Server.Web3;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;
using static Metaplay.Server.PlayerDeletion.PlayerDeletionRecords;

namespace Metaplay.Server
{
    #region Event log boilerplate

    using PlayerEventLogUtil = EntityEventLogUtil<PlayerEventLogEntry, PlayerEventLogSegmentPayload, PersistedPlayerEventLogSegment, PlayerEventLogScanResponse>;

    [MetaSerializable]
    public class PlayerEventLogSegmentPayload : MetaEventLogSegmentPayload<PlayerEventLogEntry>{ }
    [Table("PlayerEventLogSegments")]
    public class PersistedPlayerEventLogSegment : PersistedEntityEventLogSegment{ }

    [MetaMessage(MessageCodesCore.PlayerEventLogScanResponse, MessageDirection.ServerInternal)]
    public class PlayerEventLogScanResponse : EntityEventLogScanResponse<PlayerEventLogEntry>{ }

    [EntityEventLogOptions("Player")]
    public class PlayerEventLogOptions : EntityEventLogOptionsBase { }

    #endregion

    [RuntimeOptions("Player", isStatic: false, "Configuration options for a player.")]
    public class PlayerOptions : RuntimeOptionsBase
    {
        [MetaDescription("The maximum amount of time that the client-driven `PlayerModel.CurrentTime` is allowed to be behind the server. If a client falls further behind, then they are kicked from the server.")]
        public TimeSpan ClientTimeMaxBehind { get; private set; } = TimeSpan.FromSeconds(90);
        [MetaDescription("The maximum amount of time that the client-driven `PlayerModel.CurrentTime` is allowed to be ahead of the server. If a client moves further ahead, then they are kicked from the server.")]
        public TimeSpan ClientTimeMaxAhead  { get; private set; } = TimeSpan.FromSeconds(10);
        [MetaDescription("Maximum amount of time the client may be paused before session is closed.")]
        public TimeSpan MaxClientPauseDuration { get; private set; } = TimeSpan.FromMinutes(5);
        [MetaDescription("Maximum amount of time the client may take from to catch up from unpausing to unpaused state.")]
        public TimeSpan MaxClientUnpausingToUnpausedDuration { get; private set; } = TimeSpan.FromSeconds(5);

        [MetaDescription("`PlayerModel` size limit (in bytes) at which the server logs a warning. Such players should be investigated, as they might be indicative of erroneous `PlayerModel` bloat.")]
        public int ModelSizeWarningThreshold { get; private set; } = 1024*1024;
        [MetaDescription("The delay from latest GameConfig update until data in player models related to removed config items is purged. A value of 'null' disables purging.")]
        public TimeSpan? PurgeStateForRemovedConfigItemsDelay { get; private set; } = TimeSpan.FromDays(1);
    }

    public class PendingSynchronizedServerActionState
    {
        public readonly PlayerActionBase    Action;
        public readonly int                 TrackingId;
        public int                          DeadlineBeforeTick; // Action must be executed before this tick starts (i.e. tick number equals this value) (this is an approximation of client clock)
        public readonly MetaTime            DeadlineBeforeTime; // or Action must be executed before this point in time. (represents server clock.)

        public PendingSynchronizedServerActionState(PlayerActionBase action, int trackingId, int deadlineBeforeTick, MetaTime deadlineBeforeTime)
        {
            Action = action;
            TrackingId = trackingId;
            DeadlineBeforeTick = deadlineBeforeTick;
            DeadlineBeforeTime = deadlineBeforeTime;
        }
    }

    public struct PendingUnsynchronizedServerActionState
    {
        public readonly PlayerActionBase    Action;
        public readonly int                 TrackingId;

        public PendingUnsynchronizedServerActionState(PlayerActionBase action, int trackingId)
        {
            Action = action;
            TrackingId = trackingId;
        }
    }

    /// <summary>
    /// Base class for game-specific <c>PersistedPlayer</c> class. Implements all
    /// the core-required player members.
    /// </summary>
    [Table("Players")]
    [MetaPersistedVirtualItem]
    public class PersistedPlayerBase : IPersistedEntity, IMetaIntegrationConstructible<PersistedPlayerBase>
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

        public byte[]   Payload         { get; set; }   // Null for just-registered players (when written from Authenticator)

        [Required]
        public int      SchemaVersion   { get; set; }   // Schema version for object

        [Required]
        public bool     IsFinal         { get; set; }   // Is this a final persisted version (warn if resuming from non-final)
    }

    /// <summary>
    /// Database entry for fast searching of players by name. Used to store unique searchable parts
    /// of the player's name in an efficiently-searchable SQL index.
    /// </summary>
    [Table("PlayerNameSearches")]
    [NoPrimaryKey]
    [Keyless]
    [Index(nameof(EntityId))]
    [Index(nameof(NamePart), nameof(EntityId))]
    // \todo [petri] foreign key on PersistedPlayer.EntityId?
    public class PersistedPlayerSearch : IPersistedItem
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

    public sealed class ServerPlayerModelJournal : ModelJournal<IPlayerModelBase>.Follower
    {
        public bool EnableConsistencyChecks { get; private set; }

        public ServerPlayerModelJournal(LogChannel log, IPlayerModelBase model, bool enableConsistencyChecks)
            : base(log)
        {
            EnableConsistencyChecks = enableConsistencyChecks;

            if (enableConsistencyChecks)
            {
                AddListener(new JournalModelOutsideModificationChecker<IPlayerModelBase>(log));
                AddListener(new JournalModelCloningChecker<IPlayerModelBase>(log));
                AddListener(new JournalModelChecksumChecker<IPlayerModelBase>(log));
                AddListener(new JournalModelActionImmutabilityChecker<IPlayerModelBase>(log));
                AddListener(new JournalModelRerunChecker<IPlayerModelBase>(log));
                AddListener(new JournalModelModifyHistoryChecker<IPlayerModelBase>(log));
            }

            AddListener(new JournalModelCommitChecker<IPlayerModelBase>(log));
            AddListener(new FailingActionWarningListener<IPlayerModelBase>(log));

            Setup(model, JournalPosition.AfterTick(model.CurrentTick));
        }
    }

    public abstract class EntityGuildComponent : EntityComponent, IMetaIntegration<EntityGuildComponent> { }

    [EntityMaintenanceRefreshJob]
    [EntityMaintenanceSchemaMigratorJob]
    [EntityMaintenanceJob("Delete", typeof(ScheduledPlayerDeletionJobSpec))]
    [EntityArchiveImporterExporter("player", typeof(DefaultImportPlayerHandler), typeof(DefaultExportPlayerHandler))]
    public abstract class PlayerConfigBase : PersistedEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCore.Player;
        public override IShardingStrategy   ShardingStrategy        => new HashedShardingStrategy(Application.Instance.ClusterConfig, EntityKindCore.Player);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(60);
        public override List<Type>          EntityComponentTypes    => new List<Type>() { typeof(EntityGuildComponent) };
    }

    /// <summary>
    /// Base class for a game-specific PlayerActor class. Implements the game-agnostic features related to
    /// a player.
    /// </summary>
    public abstract partial class PlayerActorBase<TModel, TPersisted> : PersistedEntityActor<TPersisted, TModel>, IPlayerModelServerListenerCore
        where TModel : class, IPlayerModel<TModel>, new()
        where TPersisted : PersistedPlayerBase
    {
        protected override TimeSpan             SnapshotInterval    => TimeSpan.FromMinutes(3); // By default, persist state every 3 minutes to minimize data loss on failures
        protected override AutoShutdownPolicy   ShutdownPolicy      => AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: TimeSpan.FromSeconds(5)); // Allow shutting down when entity has no more subscribers

        internal class DoInfrequentPeriodicWork { public static DoInfrequentPeriodicWork Instance = new DoInfrequentPeriodicWork(); }
        internal class ActorFlushActionBatchCommand { public static ActorFlushActionBatchCommand Instance = new ActorFlushActionBatchCommand(); }
        internal class CheckpointDriftDetectedCommand { public static CheckpointDriftDetectedCommand Instance = new CheckpointDriftDetectedCommand(); }
        internal class ExecutePendingSynchronizedServerActionsCommand
        {
            public int UpToTrackingIdInclusive;
            public ExecutePendingSynchronizedServerActionsCommand(int upToTrackingIdInclusive)
            {
                UpToTrackingIdInclusive = upToTrackingIdInclusive;
            }
        }

        class TriggerIAPSubscriptionStateQuery
        {
            public readonly string OriginalTransactionId;
            public TriggerIAPSubscriptionStateQuery(string originalTransactionId)
            {
                OriginalTransactionId = originalTransactionId;
            }
        }

        class StartRefreshOwnedNftsCommand { public static readonly StartRefreshOwnedNftsCommand Instance = new StartRefreshOwnedNftsCommand(); }

        protected virtual MetaDuration IAPSubscriptionPeriodicCheckInterval => MetaDuration.FromDays(1);
        protected virtual MetaDuration IAPSubscriptionReuseCheckInterval    => MetaDuration.FromHours(1);

        protected static Prometheus.Counter     c_actorsStarted                     = Prometheus.Metrics.CreateCounter("game_player_started_total", "Number of player actors started");
        protected static Prometheus.Counter     c_actionsExecuted                   = Prometheus.Metrics.CreateCounter("game_player_actions_total", "Number of player actions executed", "action");
        protected static Prometheus.Counter     c_ticksExecuted                     = Prometheus.Metrics.CreateCounter("game_player_ticks_total", "Number of player ticks executed");
        protected static Prometheus.Counter     c_flushBatchesExecuted              = Prometheus.Metrics.CreateCounter("game_player_flush_batches_total", "Number of player flush batches validated");
        protected static Prometheus.Counter     c_checksumMismatches                = Prometheus.Metrics.CreateCounter("game_player_checksum_mismatches_total", "Number player checksum mismatches detected (by action type)", "action");
        protected static Prometheus.Histogram   c_persistedSize                     = Prometheus.Metrics.CreateHistogram("game_player_persisted_size", "Persisted size of player logic state", new Prometheus.HistogramConfiguration { Buckets = Metaplay.Cloud.Metrics.Defaults.EntitySizeBuckets });
        protected static Prometheus.Counter     c_inAppMissingContents              = Prometheus.Metrics.CreateCounter("game_player_validate_iap_missing_contents_total", "Number of IAP purchases with missing dynamic content");
        protected static Prometheus.Counter     c_inAppValidateDuplicates           = Prometheus.Metrics.CreateCounter("game_player_validate_iap_duplicates_total", "Number of IAP validation duplicates (by product type)", "product_type");
        protected static Prometheus.Counter     c_inAppValidateInitialRequests      = Prometheus.Metrics.CreateCounter("game_player_validate_iap_initial_requests_total", "Number of initial IAP validation requests");
        protected static Prometheus.Counter     c_inAppValidateRetryRequests        = Prometheus.Metrics.CreateCounter("game_player_validate_iap_retry_requests_total", "Number of IAP validation request retries");
        protected static Prometheus.Counter     c_inAppValidateResponses            = Prometheus.Metrics.CreateCounter("game_player_validate_iap_responses_total", "Number of IAP validation responses (by result)", "result");
        protected static Prometheus.Counter     c_inAppValidateUnexpectedResponses  = Prometheus.Metrics.CreateCounter("game_player_validate_iap_unexpected_responses_total", "Number of unexpected IAP validation responses (by kind)", "kind");

        /// <summary> LogicVersion in use for this actor </summary>
        protected readonly int                          _logicVersion;
        /// <summary> Logging channel to route log events from PlayerModel into Akka logger </summary>
        protected readonly LogChannel                   _modelLogChannel;
        /// <summary> Currently active config. This is always set. </summary>
        protected ActiveGameConfig                      _activeGameConfig;
        /// <summary> Currently active baseline-version of the game config. This is always set. </summary>
        protected FullGameConfig                        _baselineGameConfig;
        /// <summary> Resolver for <see cref="_baselineGameConfig"/>. This is always set. </summary>
        protected IGameConfigDataResolver               _baselineGameConfigResolver;

        /// <summary> Currently active specialized version of the baseline-version with <see cref="_specializedGameConfigKey"/>. Set in PostLoad </summary>
        protected FullGameConfig                        _specializedGameConfig;
        /// <summary> The key of <see cref="_specializedGameConfig"/>. Set in PostLoad. </summary>
        /// <remarks> This is the server-side specialization key, which is different than the specialization key visible to clients, as clients do not see all experiments. </remarks>
        protected GameConfigSpecializationKey           _specializedGameConfigKey;
        /// <summary> Resolver for <see cref="_specializedGameConfig"/>. Set in PostLoad. </summary>
        protected IGameConfigDataResolver               _specializedGameConfigResolver;

        // \todo [petri] move further down to base class?
        // \todo Duplicated between PlayerActorBase and GuildActorBase
        private RandomPCG                                                           _analyticsEventRandom;
        protected AnalyticsEventBatcher<PlayerEventBase, PlayerAnalyticsContext>    _analyticsEventBatcher;
        protected AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>          _analyticsEventHandler;    // Analytics event handler: write to player event log & queue up in _analyticsEventBatcher for sending down to AnalyticsDispatcher

        protected ServerPlayerModelJournal              _playerJournal;             // State and a bounded history. (shared with client, persisted to database)
        protected string                                _persistedSearchName;       // Name that has been stored in the name search table (so tables can be updated when name changes)

        protected TModel                                Model => (TModel)(_playerJournal?.StagedModel ?? null);

        protected DateTime                              _nextActionFlushEarliestAt                      = DateTime.UnixEpoch;
        protected int                                   _numActionFlushesScheduled                      = 0;
        protected int                                   _numActionFlushesSuppressed                     = 0;
        protected JournalPosition                       _actionFlushBatchUpperBound                     = JournalPosition.Epoch; // Upper bound position to which a pending action flush batch may commit to. This is StagedPosition, except during processing of FlushActions.
        protected TimeSpan                              ActionFlushBatchMinInterval                     = TimeSpan.FromSeconds(4); // Minimum time how often actions are validated. This should be shorter than client's flush interval or all messages might get throttled.
        protected bool                                  _isHandlingActionFlush                          = false;
        Action                                          _flushCompletionContinuations                   = null; // Multicast delegate containing all pending actions.
        List<PendingSynchronizedServerActionState>      _pendingSynchronizedServerActions               = new List<PendingSynchronizedServerActionState>();
        List<PendingUnsynchronizedServerActionState>    _pendingUnsynchronizedServerActions             = new List<PendingUnsynchronizedServerActionState>();
        int                                             _serverActionRunningTrackingId                  = 1; // \note: start from 1 so that we never use the index 0 (default value).
        ContentHash                                     _activeLocalizationVersion                      = ContentHash.None; // Currently active localization version on client. As this is client provided data, should be valided upon use.
        HashSet<Type>                                   _activeBroadcastTriggerConditionEventTypes      = new();

        Action                                          _afterPersistActions                            = null; // Multicast delegate of actions to execute after the next entity persist.

        protected AtomicValueSubscriber<ActiveBroadcastSet>             _activeBroadcastStateSubscriber         = GlobalStateProxyActor.ActiveBroadcastState.Subscribe();
        AtomicValueSubscriber<ActiveLocalizationVersions>               _activeLocalizationVersionSubscriber    = GlobalStateProxyActor.ActiveLocalizationVersions.Subscribe();

        /// <summary>
        /// The set of active experiments the current player is a member of. An active experiment is an experiment that is
        /// neither disabled or in an invalid state, i.e. the experiment's config changes have been applied. This field
        /// is set in PostLoad() and updated when specialized config is updated.
        /// </summary>
        protected OrderedDictionary<PlayerExperimentId, ExperimentMembershipStatus> ActiveExperiments { get; private set; }

        public PlayerActorBase(EntityId playerId) : base(playerId)
        {
            c_actorsStarted.Inc();

            // Create default log channel (used for active Model)
            _modelLogChannel = CreateModelLogChannel("player");

            // Initialize analytics events collecting: append all events to EventLog immediately & batch events for sending to AnalyticsDispatcher
            // \todo Duplicated between PlayerActorBase and GuildActorBase, move to some base class
            _analyticsEventRandom  = RandomPCG.CreateNew();
            _analyticsEventBatcher = new AnalyticsEventBatcher<PlayerEventBase, PlayerAnalyticsContext>(playerId, maxBatchSize: 100);
            _analyticsEventHandler = new AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>((model, payload) =>
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
                string                  eventType       = eventSpec.EventType;
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
                    PlayerEventBase payloadCopy = MetaSerialization.CloneTagged<PlayerEventBase>(payload, MetaSerializationFlags.IncludeAll, model.LogicVersion, resolver);
                    PlayerEventLogUtil.AddEntry(model.EventLog, GetEventLogConfiguration(), collectedAt, uniqueId,
                        baseParams => new PlayerEventLogEntry(baseParams, modelTime, schemaVersion, payloadCopy));

                    // Trigger event log flushing only if the model is the currently-active Model.
                    if (model == Model)
                        TryEnqueueTriggerEventLogFlushing();
                }

                // Enqueue event for sending analytics (if enabled)
                if (eventSpec.SendToAnalytics)
                {
                    PlayerAnalyticsContext context = GetAnalyticsContext((TModel)model);
                    OrderedDictionary<AnalyticsLabel, string> labels = GetAnalyticsLabels((TModel)model);
                    _analyticsEventBatcher.Enqueue(_entityId, collectedAt, modelTime, uniqueId, eventType, schemaVersion, payload, context, labels, resolver, model.LogicVersion);
                }

                // Only evaluate triggers when `model` is the currently active model, see note above.
                if (model == Model)
                    EvaluateTriggers(payload);
            });

            _logicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;
            _activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            _baselineGameConfig = _activeGameConfig.BaselineGameConfig;
            _baselineGameConfigResolver = GetConfigResolver(_baselineGameConfig);
        }

        protected override async Task Initialize()
        {
            // Fetch state from database (at least an empty state must exist)
            TPersisted persisted = await MetaDatabase.Get().TryGetAsync<TPersisted>(_entityId.ToString());
            if (persisted == null)
                throw new InvalidOperationException($"Trying to initialize PlayerActor for {_entityId} for whom no state exists in database. At least an empty PersistedPlayer must exist in database!");

            // Initialize from persisted state
            await InitializePersisted(persisted);

            // Start periodic timer for miscellaneous infrequent things.
            StartRandomizedPeriodicTimer(TimeSpan.FromSeconds(30), DoInfrequentPeriodicWork.Instance);

            await base.Initialize();
        }

        protected override async Task OnShutdown()
        {
            // Flush any pending events
            _analyticsEventBatcher.Flush();

            await base.OnShutdown();
        }

        protected override Task<TModel> InitializeNew()
        {
            // Create Model for player
            string name = RandomNewPlayerName();
            TModel model = CreateInitialModel(CreateModelLogChannel("player-initial"), name);

            _log.Debug("Initialized new player: {PlayerName} (logicVersion={LogicVersion}, now={Timestamp})", name, _logicVersion, DateTime.UtcNow);
            return Task.FromResult(model);
        }

        protected override Task<TModel> RestoreFromPersisted(TPersisted persisted)
        {
            PlayerOptions playerOpts = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerOptions>();

            // Model size diagnostics
            // \todo Generalize this into PersistedEntityActor
            c_persistedSize.Observe(persisted.Payload.Length);
            if (persisted.Payload.Length >= playerOpts.ModelSizeWarningThreshold)
            {
                _log.Warning(
                    $"The persisted model's size is {{PersistedSize}} bytes, which exceeds {{WarningThreshold}} (option Player:{nameof(PlayerOptions.ModelSizeWarningThreshold)})." +
                    $" You should investigate whether something is bloating the PlayerModel erroneously." +
                    $" Note that player login will fail when the size exceeds {{WireProtocolMaxPacketSize}} ({nameof(WireProtocol)}.{nameof(WireProtocol.MaxPacketUncompressedPayloadSize)}).",
                    persisted.Payload.Length, playerOpts.ModelSizeWarningThreshold,
                    WireProtocol.MaxPacketUncompressedPayloadSize);
            }

            // Deserialize model
            TModel model = CreateModelFromSerialized(persisted.Payload, CreateModelLogChannel("player-restored"));

            // If player has no language set, set it to the default
            if (model.Language == null)
            {
                model.Language = MetaplayCore.Options.DefaultLanguage;
                model.LanguageSelectionSource = LanguageSelectionSource.AccountCreationAutomatic;
            }

            // Run base (sdk-level) fixups.
            // These are run here, before RunMigrations which runs user migrations.
            // This way, user migrations can rely on base fixups being up to date.
            model.RunPlayerModelBaseFixups();

            return Task.FromResult(model);
        }

        protected override sealed Task PostLoad(TModel model, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _log.Info("PostLoad() for {PlayerId} logicVersion={LogicVersion}, away for {AwayDuration} (since {PersistedAt})", _entityId, _logicVersion, elapsedTime, persistedAt);

            PlayerOptions playerOpts = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerOptions>();

            // `model` is now completely loaded with Baseline Config. Choose and switch to the active Specialized Config
            GameConfigSpecializationKey specializationKey = CreateSpecializationKeyForServer(_activeGameConfig, model.Experiments);
            _specializedGameConfigKey = specializationKey;
            _specializedGameConfig = _activeGameConfig.GetSpecializedGameConfig(specializationKey);
            _specializedGameConfigResolver = GetConfigResolver(_specializedGameConfig);
            ActiveExperiments = GetInstanceActiveExperiment(_activeGameConfig, playerId: _entityId, model.Experiments);

            model.IsDeveloper = GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(model.PlayerId);

            // Switch from baseline config to the specialized config
            // \todo: try to organize code such that source config is clear (isolate persistent loading?)
            // \todo: the logchannel gets overwriten immediately anyway, get rid here and elsewhere
            model = CreateClonedModel(model, CreateModelLogChannel("player-post-load-clone"));
            SwitchToNewModelImmediately(model, elapsedTime);

            #if !METAPLAY_DISABLE_GUILDS
            Guilds?.PostLoad();
            #endif

            // If the name has been stored in name search table, remember the name so we can detect changes (otherwise any valid name requires an update)
            _persistedSearchName = Model.SearchVersion == PlayerModelConstants.PlayerSearchTableVersion ? Model.PlayerName : null;
            _log.Debug("Initializing name search: '{PlayerName}' (SearchVersion={SearchVersion})", Model.PlayerName, Model.SearchVersion);

            // Make sure _activeLocalizationVersionSubscriber.Current is always set by doing a dummy update
            // \todo[jarkko]: fix the _activeLocalizationVersionSubscriber API to avoid having to do this
            _activeLocalizationVersionSubscriber.Update((_, _) => { });

            // Purge stale data from model, if allowed
            if (playerOpts.PurgeStateForRemovedConfigItemsDelay.HasValue)
            {
                if (GlobalStateProxyActor.ActiveGameConfig.Get().ActiveSince + MetaDuration.FromTimeSpan(playerOpts.PurgeStateForRemovedConfigItemsDelay.Value) < MetaTime.Now)
                    model.PurgeStateForRemovedConfigItems();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Switches immediately to the given model (by calling <see cref="OnSwitchedToModel"/>) and creates a new journal for it. The model
        /// is treated as if it were just restored from the peristent storage, and in particular <see cref="IPlayerModelBase.OnRestoredFromPersistedState(MetaTime, MetaDuration)"/>
        /// is called for it with the supplied <paramref name="elapsedAsPersisted"/>.
        /// </summary>
        protected void SwitchToNewModelImmediately(TModel model, TimeSpan elapsedAsPersisted)
        {
            // \note Defensive: AssignBasicRuntimePropertiesToModel should've ideally been done already
            //       right after `model` was created, but let's ensure it just in case.
            AssignBasicRuntimePropertiesToModel(model, model.Log, _specializedGameConfig);

            // Simulate (or fast-forward) ticks until current time
            MetaTime curTime = MetaTime.Now;
            model.OnRestoredFromPersistedState(curTime, MetaDuration.FromMilliseconds((long)elapsedAsPersisted.TotalMilliseconds));

            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();

            // Check if there are any pending actions and consume them immediately.
            if (model.PendingSynchronizedServerActions != null)
            {
                if (model.PendingSynchronizedServerActions.PendingActions != null)
                {
                    _log.Warning("Player model has {NumPending} pending unexecuted synchronized server actions. Executing them now.", model.PendingSynchronizedServerActions.PendingActions.Length);

                    for (int ndx = 0; ndx < model.PendingSynchronizedServerActions.PendingActions.Length; ++ndx)
                    {
                        try
                        {
                            PlayerActionBase action = model.PendingSynchronizedServerActions.PendingActions[ndx].Deserialize(_specializedGameConfigResolver, _logicVersion);

                            _log.Debug("Executing pending synchronized server action: {Type}", action.GetType().Name);

                            MetaActionResult result = ModelUtil.RunAction(model, action, NullChecksumEvaluator.Context);
                            if (result != MetaActionResult.Success)
                                _log.Warning("Execute pending synchronized server action was not successful. ActionResult: {Result}", result);
                        }
                        catch (Exception exception)
                        {
                            _log.Warning("Failed to execute pending synchronized server action: {Cause}", exception);
                        }
                    }
                }
                model.PendingSynchronizedServerActions = null;
            }

            // Complete switch to the model. Set Log channel to the default, and reset journal.
            OnSwitchedToModelCore(model);
            AssignBasicRuntimePropertiesToModel(model, _modelLogChannel, _specializedGameConfig);
            _playerJournal = CreateModelJournal(model, enableDevelopmentFeatures: envOpts.EnableDevelopmentFeatures, enableConsistencyChecks: false);

            // The model is now the currently-active `Model`.
            // Trigger event log flushing in case new events were logged in the model before it became the currently-active `Model`.
            TryEnqueueTriggerEventLogFlushing();
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            MetaTime now = MetaTime.Now;
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            // On final persist, ensure PlayerModel is up-to-date. Otherwise, apply pending synchronized server actions.
            if (isFinal)
            {
                MetaDuration elapsedTime = now - Model.CurrentTime;
                if (elapsedTime > MetaDuration.Zero)
                {
                    _log.Debug("Fast-forwarding PlayerModel {ElapsedTime} before final persist", elapsedTime);
                    Model.ResetTime(now);
                    Model.OnFastForwardTime(elapsedTime);
                }

                // complete the pending actions
                foreach (PendingSynchronizedServerActionState pendingServerAction in _pendingSynchronizedServerActions)
                    ModelUtil.RunAction(Model, pendingServerAction.Action, NullChecksumEvaluator.Context);
                Model.PendingSynchronizedServerActions = null;
            }
            else
            {
                // store the pending actions so that they are not lost
                PlayerPendingSynchronizedServerActions pendingActionsState;

                if (_pendingSynchronizedServerActions.Count > 0)
                {
                    pendingActionsState = new PlayerPendingSynchronizedServerActions();
                    pendingActionsState.PendingActions = new MetaSerialized<PlayerActionBase>[_pendingSynchronizedServerActions.Count];
                    for (int ndx = 0; ndx < _pendingSynchronizedServerActions.Count; ++ndx)
                        pendingActionsState.PendingActions[ndx] = MetaSerialization.ToMetaSerialized<PlayerActionBase>(value: _pendingSynchronizedServerActions[ndx].Action, MetaSerializationFlags.Persisted, logicVersion: _logicVersion);
                }
                else
                {
                    pendingActionsState = null;
                }

                Model.PendingSynchronizedServerActions = pendingActionsState;
            }

            // If search inputs have changed, update the search tables (and mark in Model that search has been populated for player)
            IEnumerable<string> searchStrings = null;
            if (ShouldUpdatePlayerSearch())
            {
                searchStrings = GetPlayerSearchStrings();
                if (_log.IsDebugEnabled)
                {
                    string searchStringsStr = searchStrings != null ? string.Join(", ", searchStrings.Select(str => $"\"{str}\"")) : "null";
                    _log.Debug("Player search strings changed to: {SearchStrings}", searchStringsStr);
                }
            }

            // Mark the search as updated to current version
            Model.SearchVersion = PlayerModelConstants.PlayerSearchTableVersion;

            BeforePersistCore();
            await BeforePersistAsync();

            // Serialize and compress the model
            byte[] persistedPayload = SerializeToPersistedPayload(Model, _baselineGameConfigResolver, _logicVersion);

            // Prepare the persisted version of the player
            TPersisted persisted = CreatePersisted(
                entityId:       _entityId,
                persistedAt:    now.ToDateTime(),
                payload:        persistedPayload,
                schemaVersion:  _entityConfig.CurrentSchemaVersion,
                isFinal:        isFinal);

            // Update the player in the database (and optionally name search)
            await MetaDatabase.Get().UpdatePlayerAsync(_entityId, persisted, searchStrings);

            // Remember updated name (to avoid updating again)
            _persistedSearchName = Model.PlayerName;

            // Player is active if it has a session
            if (TryGetOwnerSession() != null)
                RegisterPlayerAsActive();

            #if !METAPLAY_DISABLE_GUILDS
            Guilds?.PersistState();
            #endif

            // Flush any pending after-persist actions.
            _afterPersistActions?.Invoke();
            _afterPersistActions = null;
        }

        void BeforePersistCore()
        {
            // Flush any pending events
            _analyticsEventBatcher.Flush();
        }

        protected override sealed async Task<MetaMessage> OnNewSubscriber(EntitySubscriber subscriber, MetaMessage message)
        {
            // If owner subscriber, check if there's already a session active
            if (subscriber.Topic == EntityTopic.Owner && message is InternalPlayerSessionSubscribeRequest request)
                return await OnSessionSubscriber(subscriber, request);
            else
                return await OnNewUnknownSubscriber(subscriber, message);
        }

        /// <summary>
        /// Creates specialization key for server game configs.
        /// </summary>
        protected GameConfigSpecializationKey CreateSpecializationKeyForServer(ActiveGameConfig activeConfig, PlayerExperimentsState experimentsState)
        {
            return CreateSpecializationKeyWithKeySchedule(activeConfig, experimentsState, activeConfig.BaselineGameConfig.ServerConfig.PlayerExperiments.Keys.ToList());
        }

        /// <summary>
        /// Creates specialization key for client game configs. (Client is only aware of a active subset of the experiments).
        /// </summary>
        protected GameConfigSpecializationKey CreateSpecializationKeyForClient(ActiveGameConfig activeConfig, PlayerExperimentsState experimentsState)
        {
            PlayerExperimentSubject experimentSubject = activeConfig.IsPlayerTesterInAnyExperiment(_entityId) ? PlayerExperimentSubject.Tester : PlayerExperimentSubject.Player;
            return CreateSpecializationKeyWithKeySchedule(activeConfig, experimentsState, activeConfig.GetVisibleExperimentsFor(experimentSubject).Keys.ToList());
        }

        /// <summary>
        /// Creates specialization key with the desired key schedule. Key schedule decides the game config the key is compatible with; The schedule must
        /// match the key schedule of the data.
        /// </summary>
        protected GameConfigSpecializationKey CreateSpecializationKeyWithKeySchedule(ActiveGameConfig activeConfig, PlayerExperimentsState experimentsState, List<PlayerExperimentId> keySchedule)
        {
            ExperimentVariantId[]   specializationKey   = new ExperimentVariantId[keySchedule.Count];
            int                     ndx                 = 0;

            foreach (PlayerExperimentId experimentId in keySchedule)
            {
                VariantValidationResult inspectResult = InspectPlayerExperiment(activeConfig, playerId: _entityId, experimentsState, experimentId);
                ExperimentVariantId variantId;

                switch (inspectResult.Code)
                {
                    case VariantValidationResultCode.NoSuchExperiment:
                    case VariantValidationResultCode.ExperimentNotActive:
                    case VariantValidationResultCode.PlayerNotEnrolled:
                    case VariantValidationResultCode.PlayerNotTester:
                    {
                        // not in experiment
                        variantId = null;
                        break;
                    }

                    case VariantValidationResultCode.UnknownVariant:
                    {
                        // Player has unknown experiment variant. Variants should not be removed but that happends, be defensive.
                        _log.Warning("Player has unknown experiment variant {VariantId} in experiment {ExperimentId}. No config modifications are applied for this experiment.", inspectResult.VariantId, experimentId);
                        variantId = null;
                        break;
                    }

                    case VariantValidationResultCode.DisabledVariant:
                    {
                        // disabled variant
                        variantId = null;
                        break;
                    }

                    case VariantValidationResultCode.ValidControlVariant:
                    {
                        // control variant
                        variantId = null;
                        break;
                    }

                    case VariantValidationResultCode.ValidNonControlVariant:
                    {
                        // non-control variant
                        variantId = inspectResult.VariantId;
                        break;
                    }

                    default:
                        throw new InvalidEnumArgumentException();
                }

                specializationKey[ndx] = variantId;
                ndx++;
            }

            return GameConfigSpecializationKey.FromRaw(specializationKey);
        }

        /// <summary>
        /// Returns the Game Config version of the current Entity.
        /// </summary>
        protected ServerEntityGameConfigVersion GetEntityGameConfigVersion()
        {
            PlayerExperimentSubject experimentSubject = _activeGameConfig.IsPlayerTesterInAnyExperiment(_entityId) ? PlayerExperimentSubject.Tester : PlayerExperimentSubject.Player;
            ContentHash patchesVersion = (experimentSubject == PlayerExperimentSubject.Tester)
                                            ? _activeGameConfig.SharedGameConfigPatchesForTestersContentHash
                                            : _activeGameConfig.SharedGameConfigPatchesForPlayersContentHash;
            return new ServerEntityGameConfigVersion(
                _activeGameConfig.BaselineStaticGameConfigId,
                _activeGameConfig.BaselineDynamicGameConfigId,
                CreateSpecializationKeyForServer(_activeGameConfig, Model.Experiments),
                CreateSpecializationKeyForClient(_activeGameConfig, Model.Experiments),
                patchesVersion);
        }

        static (string, string) TryGetExperimentAndVariantAnalyticsIds(ActiveGameConfig activeConfig, PlayerExperimentId experimentId, ExperimentVariantId variantId)
        {
            string experimentAid = null;
            string variantAid = null;

            if (activeConfig.BaselineGameConfig.ServerConfig.PlayerExperiments.TryGetValue(experimentId, out PlayerExperimentInfo experimentInfo))
            {
                experimentAid = experimentInfo.ExperimentAnalyticsId;

                if (experimentId == null)
                    variantAid = experimentInfo.ControlVariantAnalyticsId;
                else if (experimentInfo.Variants.TryGetValue(variantId, out PlayerExperimentInfo.Variant variantInfo))
                    variantAid = variantInfo.AnalyticsId;
            }

            return (experimentAid, variantAid);
        }

        void UpdatePlayerExperiments(ActiveGameConfig activeConfig, bool isFirstLogin, PlayerExperimentSubject experimentSubject)
        {
            // \note: Using isTester is kinda useless as the Experiments only in Tester set cannot have their IsRolloutEnabled. This is
            //        is because IsRolloutEnabled means that an experiment in Ongoing phase. Which adds it into the Player-visible experiment
            //        set. But let's do it for consistency anyway, and there is a chance that the behavior might change.
            OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> visibleExperiments = activeConfig.GetVisibleExperimentsFor(experimentSubject);
            foreach ((PlayerExperimentId experimentId, PlayerExperimentAssignmentPolicy experimentPolicy) in visibleExperiments)
            {
                // Skip if already assigned into this experiment
                if (Model.Experiments.ExperimentGroupAssignment.ContainsKey(experimentId))
                    continue;

                // Is the experminent enabled for assignment and is the player in the sample population
                if (!experimentPolicy.IsRolloutEnabled)
                    continue;
                if (experimentPolicy.IsCapacityReached)
                    continue;
                if (!experimentPolicy.IsPlayerInExperimentSamplePopulation(_entityId))
                    continue;

                // Is eligible for this experiment (segment and targeting)
                if (experimentPolicy.EnrollOnlyNewPlayers && !isFirstLogin)
                    continue;
                if (!Model.PassesFilter(experimentPolicy.EligibilityFilter, out bool filterEvalError))
                    continue;
                if (filterEvalError)
                {
                    _log.Warning("Filter evaluation error while trying to check experiment {ExperimentId}.", experimentId);
                    continue;
                }

                // Assign player to the experiment
                ExperimentVariantId variantId = experimentPolicy.GetRandomVariant();
                (string experimentAid, string variantAid) = TryGetExperimentAndVariantAnalyticsIds(activeConfig, experimentId, variantId);

                _log.Debug("Player assigned into Experiment {Experiment}, {VariantId} variant", experimentId, variantId?.ToString() ?? "control");
                Model.Experiments.ExperimentGroupAssignment[experimentId] = new PlayerExperimentsState.ExperimentAssignment(variantId);
                Model.EventStream.Event(new PlayerEventExperimentAssignment(PlayerEventExperimentAssignment.ChangeSource.AutomaticAssign, experimentId, variantId, experimentAid, variantAid));

                // Inform Proxy of group assignment
                GlobalStateProxyActor.PlayerAssignmentIntoExperimentChanged(_entityId, experimentId, addedIntoGroupId: variantId, wasRemovedFromGroup: false, removedFromGroupId: null);
                StatsCollectorProxy.PlayerAssignmentIntoExperimentChanged(_entityId, experimentId, addedIntoGroupId: variantId, wasRemovedFromGroup: false, removedFromGroupId: null);
            }
        }

        enum VariantValidationResultCode
        {
            NoSuchExperiment,
            ExperimentNotActive,
            PlayerNotEnrolled,
            PlayerNotTester,
            UnknownVariant,
            DisabledVariant,

            ValidControlVariant,
            ValidNonControlVariant,
        }
        readonly struct VariantValidationResult
        {
            public readonly VariantValidationResultCode                 Code;
            public readonly PlayerExperimentInfo                        ExperimentInfo;
            public readonly ExperimentVariantId                         VariantId;
            public readonly PlayerExperimentInfo.Variant                VariantInfo;

            public VariantValidationResult(VariantValidationResultCode code, PlayerExperimentInfo experimentInfo, ExperimentVariantId variantId, PlayerExperimentInfo.Variant variantInfo)
            {
                Code = code;
                ExperimentInfo = experimentInfo;
                VariantId = variantId;
                VariantInfo = variantInfo;
            }
        }
        static VariantValidationResult InspectPlayerExperiment(ActiveGameConfig activeConfig, EntityId playerId, PlayerExperimentsState playerExperimentsState, PlayerExperimentId experimentId)
        {
            if (!activeConfig.BaselineGameConfig.ServerConfig.PlayerExperiments.TryGetValue(experimentId, out PlayerExperimentInfo experimentInfo))
                return new VariantValidationResult(VariantValidationResultCode.NoSuchExperiment, null, null, null);
            if (!playerExperimentsState.ExperimentGroupAssignment.TryGetValue(experimentId, out PlayerExperimentsState.ExperimentAssignment variantAssignmentInModel))
                return new VariantValidationResult(VariantValidationResultCode.PlayerNotEnrolled, experimentInfo, null, null);

            // \note: we check the -ForTesters visibility set since it is a superset of -ForPlayers players. Any experiment not here
            //        must be a concluded or invalid experiment
            if (!activeConfig.VisibleExperimentsForTesters.TryGetValue(experimentId, out PlayerExperimentAssignmentPolicy experimentPolicy))
                return new VariantValidationResult(VariantValidationResultCode.ExperimentNotActive, experimentInfo, null, null);

            // Tester-only experiments are hidden for non-testers.
            if (experimentPolicy.IsOnlyForTester && !experimentPolicy.TesterPlayerIds.Contains(playerId))
                return new VariantValidationResult(VariantValidationResultCode.PlayerNotTester, experimentInfo, null, null);

            // Control?
            ExperimentVariantId variantIdInModel = variantAssignmentInModel.VariantId;
            if (variantIdInModel == null)
                return new VariantValidationResult(VariantValidationResultCode.ValidControlVariant, experimentInfo, variantId: null, variantInfo: null);

            if (!experimentPolicy.Variants.TryGetValue(variantIdInModel, out PlayerExperimentGlobalState.VariantState variantState))
                return new VariantValidationResult(VariantValidationResultCode.UnknownVariant, experimentInfo, variantIdInModel, null);
            if (!variantState.IsActive())
                return new VariantValidationResult(VariantValidationResultCode.DisabledVariant, experimentInfo, variantIdInModel, null);
            if (!experimentInfo.Variants.TryGetValue(variantIdInModel, out PlayerExperimentInfo.Variant variantInfo))
                return new VariantValidationResult(VariantValidationResultCode.UnknownVariant, experimentInfo, variantIdInModel, null);

            return new VariantValidationResult(VariantValidationResultCode.ValidNonControlVariant, experimentInfo, variantIdInModel, variantInfo);
        }

        static List<EntityActiveExperiment> GetSessionActiveExperiments(ActiveGameConfig activeConfig, EntityId playerId, PlayerExperimentsState playerExperimentsState, PlayerExperimentSubject experimentSubject)
        {
            List<EntityActiveExperiment> experimentInfos = new List<EntityActiveExperiment>();
            foreach ((PlayerExperimentId experimentId, PlayerExperimentAssignmentPolicy experimentPolicy) in activeConfig.GetVisibleExperimentsFor(experimentSubject))
            {
                VariantValidationResult inspectResult = InspectPlayerExperiment(activeConfig, playerId, playerExperimentsState, experimentId);
                ExperimentVariantId variantId;
                string variantAnalyticsId;

                switch (inspectResult.Code)
                {
                    case VariantValidationResultCode.NoSuchExperiment:
                    case VariantValidationResultCode.ExperimentNotActive:
                    case VariantValidationResultCode.PlayerNotEnrolled:
                    case VariantValidationResultCode.PlayerNotTester:
                    {
                        // not in active experiment
                        continue; // \note: this continues the parent foreach
                    }

                    case VariantValidationResultCode.UnknownVariant:
                    case VariantValidationResultCode.DisabledVariant:
                    {
                        // Player has unknown experiment variant. We could report the experiment Id, but what would we
                        // put into the variant Id? Null/empty could be confused with control group, so lets pretend we
                        // are not in the experiment to avoid giving wrong information.
                        continue; // \note: this continues the parent foreach
                    }

                    case VariantValidationResultCode.ValidControlVariant:
                    {
                        // control variant
                        variantId = null;
                        variantAnalyticsId = inspectResult.ExperimentInfo.ControlVariantAnalyticsId;
                        break;
                    }

                    case VariantValidationResultCode.ValidNonControlVariant:
                    {
                        // non-control variant
                        variantId = inspectResult.VariantId;
                        variantAnalyticsId = inspectResult.VariantInfo.AnalyticsId;
                        break;
                    }

                    default:
                        throw new InvalidEnumArgumentException();
                }

                experimentInfos.Add(new EntityActiveExperiment(
                    experimentId:           experimentId,
                    experimentAnalyticsId:  inspectResult.ExperimentInfo.ExperimentAnalyticsId,
                    variantId:              variantId,
                    variantAnalyticsId:     variantAnalyticsId));
            }
            return experimentInfos;
        }

        static OrderedDictionary<PlayerExperimentId, ExperimentMembershipStatus> GetInstanceActiveExperiment(ActiveGameConfig activeConfig, EntityId playerId, PlayerExperimentsState playerExperimentsState)
        {
            PlayerExperimentSubject experimentSubject = activeConfig.IsPlayerTesterInAnyExperiment(playerId) ? PlayerExperimentSubject.Tester : PlayerExperimentSubject.Player;
            List<EntityActiveExperiment> sessionExperiments = GetSessionActiveExperiments(activeConfig, playerId, playerExperimentsState, experimentSubject);
            OrderedDictionary<PlayerExperimentId, ExperimentMembershipStatus> result = new OrderedDictionary<PlayerExperimentId, ExperimentMembershipStatus>(sessionExperiments.Count);
            foreach (EntityActiveExperiment info in sessionExperiments)
                result.Add(info.ExperimentId, ExperimentMembershipStatus.FromSessionInfo(info));
            return result;
        }

        async Task<InternalPlayerSessionSubscribeResponse> OnSessionSubscriber(EntitySubscriber subscriber, InternalPlayerSessionSubscribeRequest request)
        {
            #if !METAPLAY_DISABLE_GUILDS
            // If we have pending operations, complete them first (it is our message queue) to make state handling easier.
            if (Guilds?.HasPendingOperations ?? false)
                throw InternalPlayerSessionSubscribeRefused.CreateTryAgain();
            #endif

            // Check if has existing owner
            // \note This shouldn't happen now that there's a unique SessionActor per PlayerActor
            EntitySubscriber existingOwner = TryGetOwnerSession();
            if (existingOwner != null)
            {
                _log.Warning("Got new owner subscriber when has an existing, forcing the old one out");
                KickSubscriber(existingOwner, new PlayerForceKickOwner(PlayerForceKickOwnerReason.ReceivedAnotherOwnerSubscriber));
            }

            // Check that session LogicVersion matches
            if (_logicVersion != request.SessionParams.LogicVersion)
            {
                _log.Warning("Mismatched LogicVersion between PlayerActor {RunningVersion} and ClientConnection {StartVersion}", _logicVersion, request.SessionParams.LogicVersion);

                // Restart actor. It should get us the up-to-date logic version on restart
                RequestShutdown();
                throw InternalPlayerSessionSubscribeRefused.CreateTryAgain();
            }

            bool isFirstLogin = Model.Stats.TotalLogins == 0;
            await OnClientSessionHandshakeCoreAsync(request.SessionParams, isFirstLogin);

            // Reject banned players
            if (Model.IsBanned)
            {
                _log.Debug("Refusing banned player login.");
                throw InternalPlayerSessionSubscribeRefused.CreateBanned();
            }

            // Update active configs before computing resource correction. This will apply changes if either:
            // * Active Config has changed. (i.e. Baseline config has been changed, or the set of active experiments have changed)
            // * Specialized Config has changed (i.e. player has changed specialization)
            ActiveGameConfig                            activeGameConfig;
            PlayerExperimentSubject  experimentSubject;
            GameConfigSpecializationKey                 specializationKey;
            FullGameConfig                              specializedGameConfig;
            List<EntityActiveExperiment>                activeExperiments;

            activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            experimentSubject = activeGameConfig.IsPlayerTesterInAnyExperiment(_entityId) ? PlayerExperimentSubject.Tester : PlayerExperimentSubject.Player;
            UpdatePlayerExperiments(activeGameConfig, isFirstLogin: isFirstLogin, experimentSubject);
            activeExperiments = GetSessionActiveExperiments(activeGameConfig, _entityId, Model.Experiments, experimentSubject);
            specializationKey = CreateSpecializationKeyForServer(activeGameConfig, Model.Experiments);

            if (activeGameConfig != _activeGameConfig || specializationKey != _specializedGameConfigKey)
            {
                specializedGameConfig = activeGameConfig.GetSpecializedGameConfig(specializationKey);
                if (!request.SessionParams.IsDryRun)
                {
                    _activeGameConfig = activeGameConfig;
                    _baselineGameConfig = _activeGameConfig.BaselineGameConfig;
                    _baselineGameConfigResolver = GetConfigResolver(_baselineGameConfig);

                    _specializedGameConfigKey = specializationKey;
                    _specializedGameConfig = specializedGameConfig;
                    _specializedGameConfigResolver = GetConfigResolver(_specializedGameConfig);
                    ActiveExperiments = GetInstanceActiveExperiment(_activeGameConfig, playerId: _entityId, Model.Experiments);

                    // Clone via serialization to ensure all references are up-to-date
                    // \note: elapsedAsPersisted is set to zero since we will FastForward manually just after this.
                    SwitchToNewModelImmediately(CreateClonedModel(Model, CreateModelLogChannel("player-config-change-clone")), elapsedAsPersisted: TimeSpan.Zero);
                }
            }
            else
            {
                specializedGameConfig = _specializedGameConfig;
            }

            // Check login resources are up-to-date and start sesion (or dry run).
            List<AssociatedEntityRefBase>                       associatedEntities = GetSessionStartAssociatedEntities();
            SessionStartResourceSideEffects                     sessionStartResourceSideEffects = default;
            SessionProtocol.SessionResourceCorrection           resourceCorrection;
            SessionProtocol.InitialPlayerState                  initialPlayerState;
            int                                                 guildIncarnation = 0;

            #if !METAPLAY_DISABLE_GUILDS
            guildIncarnation = Guilds?.GuildIncarnation ?? 0;
            #endif

            if (MetaplayCore.Options.FeatureFlags.EnablePlayerLeagues &&
                Model.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionClientStateBase) &&
                playerDivisionClientStateBase is IDivisionClientState divisionClientState)
            {
                await HandlePlayerLeaguesOnSessionSubscriber(divisionClientState, associatedEntities);
            }

            resourceCorrection = GetNewSessionResourceCorrection(
                request:                        request,
                sessionActiveGameConfig:        activeGameConfig,
                sessionSpecializedGameConfig:   specializedGameConfig,
                experimentSubject:              experimentSubject,
                sideEffects:                    ref sessionStartResourceSideEffects);

            if (resourceCorrection.HasAnyCorrection())
            {
                throw new InternalPlayerSessionSubscribeRefused(
                    result:                         InternalPlayerSessionSubscribeRefused.ResultCode.ResourceCorrectionRequired,
                    resourceCorrection:             resourceCorrection,
                    associatedEntities:             associatedEntities,
                    guildIncarnation:               guildIncarnation);
            }
            else if (request.SessionParams.IsDryRun)
            {
                throw new InternalPlayerSessionSubscribeRefused(
                    result:                         InternalPlayerSessionSubscribeRefused.ResultCode.DryRunSuccess,
                    resourceCorrection:             default,
                    associatedEntities:             associatedEntities,
                    guildIncarnation:               guildIncarnation);
            }

            // Refresh NFTs
            if (new Web3EnabledCondition().IsEnabled)
            {
                // Query owned NFTs from NftManager, and store in Model
                List<MetaNft> ownedNfts = (await EntityAskAsync<QueryNftsResponse>(NftManager.EntityId, new QueryNftsRequest(owner: _entityId)))
                                            .Items
                                            .Where(item => item.IsSuccess)
                                            .Select(item => item.Nft)
                                            .ToList();
                MetaSerialization.ResolveMetaRefs(ref ownedNfts, Model.GetDataResolver());
                Model.Nft.OwnedNfts = ownedNfts.ToOrderedDictionary(NftTypeRegistry.Instance.GetNftKey);

                // Tell NftManager to refresh this player's NFTs from Immutable X or other ledger
                await StartRefreshOwnedNftsAsync();
            }

            ExecuteServerActionImmediately(new PlayerSetIsOnline(true));

            // \note: we don't have session now, so we can modify directly
            if (sessionStartResourceSideEffects.Language != null)
                Model.Language = sessionStartResourceSideEffects.Language;
            if (sessionStartResourceSideEffects.LanguageSelectionSource != null)
                Model.LanguageSelectionSource = sessionStartResourceSideEffects.LanguageSelectionSource.Value;

            _activeLocalizationVersion = request.SessionParams.SessionResourceProposal.ClientLocalizationVersion;

            // If a subscription has ended since the last check, try to check now whether it has been renewed or not.
            // We wait at most a few seconds for the renewal queries to finish before we keep going.
            try
            {
                await TryCheckSubscriptionRenewalsAtSessionStartAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Error from TryCheckSubscriptionRenewalsAtSessionStartAsync - tolerating: {Exception}", ex);
            }

            TryReceiveGlobalBroadcasts();

            // Handle session start
            await OnSessionStartCoreAsync(request.SessionParams, isFirstLogin);

            // Execute pending synchronized actions to avoid needing to send them to client.
            ExecuteAllPendingSynchronizedServerActionsImmediately();

            // Bake changes, start a new journal for this session.
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            _playerJournal = CreateModelJournal(_playerJournal.StagedModel, enableDevelopmentFeatures: envOpts.EnableDevelopmentFeatures, enableConsistencyChecks: false);

            // Prepare initial state
            MetaSerialized<IPlayerModelBase> serializedModel = MetaSerialization.ToMetaSerialized((IPlayerModelBase)Model, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            initialPlayerState = new SessionProtocol.InitialPlayerState(serializedModel, currentOperation: _playerJournal.StagedPosition.Operation);

            await OnNewOwnerSession(subscriber);

            // Player is active
            RegisterPlayerAsActive();

            // Analytics
            PlayerSessionParamsBase parms = request.SessionParams;
            Model.EventStream.Event(new PlayerEventClientConnected(parms.SessionToken, Model.SessionDeviceGuid, parms.DeviceModel, Model.LogicVersion, parms.TimeZoneInfo, parms.Location, parms.ClientVersion, Model.Stats.TotalLogins, parms.AuthKey));

            // Announce the latest localization for player
            // \note: we do Update() to flush out pending changes. We later can use Update() to detect changes.
            _activeLocalizationVersionSubscriber.Update((_, _) => { });
            // \note: _activeLocalizationVersionSubscriber.Current is null if localizations are not enabled or available
            OrderedDictionary<LanguageId, ContentHash> localizationVersions = _activeLocalizationVersionSubscriber.Current?.Versions;

            // Session attached
            OnClientConnectivityStatusChanged();

            return new InternalPlayerSessionSubscribeResponse(
                playerState:                    initialPlayerState,
                localizationVersions:           localizationVersions,
                activeExperiments:              activeExperiments,
                associatedEntities:             associatedEntities,
                guildIncarnation:               guildIncarnation,
                correctedDeviceGuid:            request.SessionParams.DeviceGuid != Model.SessionDeviceGuid ? Model.SessionDeviceGuid : null);
        }

        protected override sealed void OnSubscriberLost(EntitySubscriber subscriber)
        {
            if (subscriber.Topic == EntityTopic.Owner)
            {
                _log.Debug("Owner connection {EntityId} lost", subscriber.EntityId);
                OnOwnerSessionEndedCore(subscriber, wasKicked: false);
            }
            else
            {
                OnUnknownSubscriberLost(subscriber);
            }
        }

        protected sealed override void OnSubscriberKicked(EntitySubscriber subscriber, MetaMessage message)
        {
            if (subscriber.Topic == EntityTopic.Owner)
            {
                _log.Debug("Owner connection {EntityId} kicked", subscriber.EntityId);
                OnOwnerSessionEndedCore(subscriber, wasKicked: true);
            }
        }

        void OnOwnerSessionEndedCore(EntitySubscriber subscriber, bool wasKicked)
        {
            if (_isHandlingActionFlush)
                _flushCompletionContinuations += () => HandleOwnerSessionEndedCore(subscriber, wasKicked);
            else
                HandleOwnerSessionEndedCore(subscriber, wasKicked);
        }

        void HandleOwnerSessionEndedCore(EntitySubscriber subscriber, bool wasKicked)
        {
            OnOwnerSessionEnded(subscriber, wasKicked);

            // Session is gone, so no need to wait for delayed flush anymore (i.e. throttling). Flush it instantly.
            ResetScheduledFlushActionBatches();
            FlushActionBatch(forceFlushImmediately: true);
            ExecuteAllPendingSynchronizedServerActionsImmediately();

            // Tracking unsynchronized server actions is no longer necessary
            _pendingUnsynchronizedServerActions.Clear();

            // Mark player as offline (the if here is a bit defensive)
            // \todo [petri] This gets called about 30sec after disconnecting, as SessionActor only unsubscribes after it has given on the session
            if (Model.IsOnline)
            {
                // \todo [petri] Not clean to rely on the IsOnline boolean for ClientDisconnected, clean this up
                // \todo [petri/nuutti] using TimeAtFirstTick is wrong -- it's actually (roughly) when Model was spawned on server!
                Model.EventStream.Event(new PlayerEventClientDisconnected(Model.SessionToken/*, Model.TimeAtFirstTick, MetaTime.Now - Model.TimeAtFirstTick*/));

                // \note [jarkko]: We must first emit the event, and only then we may clear IsOnline. Otherwise the event would not record the session context.
                ExecuteServerActionImmediately(new PlayerSetIsOnline(false));
            }

            Model.IsClientConnected = false;
            OnClientConnectivityStatusChanged();
        }

        /// <summary>
        /// Adds an association with an entity to the ongoing session. The associated entity will
        /// be subscribed-to by the session and the state and updates will be delivered to the client.
        /// If there is already a previous association on the <see cref="ClientSlot"/> set by this
        /// entity, the previous association is removed first. It is an error to replace an association
        /// on a slot set by some other Entity.
        /// </summary>
        protected void AddEntityAssociation(AssociatedEntityRefBase association)
        {
            // If there's no owner subscriber session, skip.
            EntitySubscriber owner = TryGetOwnerSession();

            if (owner == null)
                return;

            PublishMessage(EntityTopic.Owner, new InternalSessionEntityAssociationUpdate(association.GetClientSlot(), association));
        }

        /// <summary>
        /// Removes association on the given slot from the current session. The associated entity will no longer visible to the
        /// client. It is an error to remove an association set by some other entity.
        /// </summary>
        protected void RemoveEntityAssociation(ClientSlot slot)
        {
            // If there's no owner subscriber session, skip.
            EntitySubscriber owner = TryGetOwnerSession();

            if (owner == null)
                return;

            PublishMessage(EntityTopic.Owner, new InternalSessionEntityAssociationUpdate(slot, null));
        }

        #region Session Resources

        SessionProtocol.SessionResourceCorrection GetNewSessionResourceCorrection(InternalPlayerSessionSubscribeRequest request, ActiveGameConfig sessionActiveGameConfig, FullGameConfig sessionSpecializedGameConfig, PlayerExperimentSubject experimentSubject, ref SessionStartResourceSideEffects sideEffects)
        {
            SessionProtocol.SessionResourceCorrection correction = new SessionProtocol.SessionResourceCorrection();
            AddConfigurationResourceCorrection(ref correction, request, sessionActiveGameConfig);
            AddSpecializationPatchesResourceCorrection(ref correction, request, sessionActiveGameConfig, experimentSubject);
            AddLocalizationResourceCorrection(ref correction, request, sessionSpecializedGameConfig, ref sideEffects);
            return correction;
        }

        void AddConfigurationResourceCorrection(ref SessionProtocol.SessionResourceCorrection correction, InternalPlayerSessionSubscribeRequest request, ActiveGameConfig sessionActiveGameConfig)
        {
            ContentHash baselineSharedConfig = sessionActiveGameConfig.ClientSharedGameConfigContentHash;

            // Ensure the client is using the same version.
            // Note that we are using the Baseline config's version here since that is the version in the CDN.
            if (request.SessionParams.SessionResourceProposal.ConfigVersions.TryGetValue(ClientSlotCore.Player, out ContentHash proposed) && proposed == baselineSharedConfig)
            {
                // all is ok
                return;
            }

            // Client has wrong config version, correct it.
            correction.ConfigUpdates[ClientSlotCore.Player] = sessionActiveGameConfig.BaselineGameConfigSharedConfigDeliverySources.GetCorrection(request.SessionParams.SupportedArchiveCompressions);
        }

        void AddSpecializationPatchesResourceCorrection(ref SessionProtocol.SessionResourceCorrection correction, InternalPlayerSessionSubscribeRequest request, ActiveGameConfig sessionActiveGameConfig, PlayerExperimentSubject experimentSubject)
        {
            ContentHash patchesVersion = (experimentSubject == PlayerExperimentSubject.Tester)
                                            ? sessionActiveGameConfig.SharedGameConfigPatchesForTestersContentHash
                                            : sessionActiveGameConfig.SharedGameConfigPatchesForPlayersContentHash;

            // Ensure the client is using the same patches
            // \note: for patches, current version is Zero if there are no patches.
            ContentHash proposedVersion = request.SessionParams.SessionResourceProposal.PatchVersions.GetValueOrDefault(ClientSlotCore.Player);
            if (proposedVersion == patchesVersion)
            {
                // all is ok
                return;
            }
            else
            {
                // client has wrong config version, correct it
                correction.PatchUpdates[ClientSlotCore.Player] = new SessionProtocol.SessionResourceCorrection.ConfigPatchesUpdateInfo(
                    patchesVersion: patchesVersion
                    );
                return;
            }
        }

        struct SessionStartResourceSideEffects
        {
            public LanguageId Language;
            public LanguageSelectionSource? LanguageSelectionSource;
        }

        void AddLocalizationResourceCorrection(ref SessionProtocol.SessionResourceCorrection correction, InternalPlayerSessionSubscribeRequest request, FullGameConfig sessionSpecializedGameConfig, ref SessionStartResourceSideEffects sideEffects)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                return;

            OrderedDictionary<LanguageId, ContentHash> localizationVersions = GlobalStateProxyActor.ActiveLocalizationVersions.Get().Versions;
            ISharedGameConfig gameConfig = sessionSpecializedGameConfig.SharedConfig;

            // If language set in model, client will follow that language
            if (Model.LanguageSelectionSource == LanguageSelectionSource.UserSelected)
            {
                if (localizationVersions.TryGetValue(Model.Language, out ContentHash activeVersion))
                {
                    if (request.SessionParams.SessionResourceProposal.ClientActiveLanguage == Model.Language && request.SessionParams.SessionResourceProposal.ClientLocalizationVersion == activeVersion)
                    {
                        // all is ok
                        return;
                    }
                    else
                    {
                        // client has wrong language or language version, correct it
                        correction.LanguageUpdate = new SessionProtocol.SessionResourceCorrection.LanguageUpdateInfo(
                            activeLanguage:         Model.Language,
                            localizationVersion:    activeVersion);
                        return;
                    }
                }
                else
                {
                    // Model.Language is invalid. Fix it by falling thru here
                    _log.Warning("Player had invalid language {Language}. Not found in language repository.", Model.Language);
                }
            }

            // No language set in model, server will follow client
            if (request.SessionParams.SessionResourceProposal.ClientActiveLanguage != null)
            {
                if (localizationVersions.TryGetValue(request.SessionParams.SessionResourceProposal.ClientActiveLanguage, out ContentHash activeVersion))
                {
                    LanguageInfo languageInfo = gameConfig.Languages.GetValueOrDefault(request.SessionParams.SessionResourceProposal.ClientActiveLanguage);
                    if (languageInfo != null)
                    {
                        sideEffects.Language = languageInfo.LanguageId;
                        sideEffects.LanguageSelectionSource = LanguageSelectionSource.UserDeviceAutomatic;

                        if (request.SessionParams.SessionResourceProposal.ClientLocalizationVersion == activeVersion)
                        {
                            // all is ok
                            return;
                        }
                        else
                        {
                            // Client has a valid language, but wrong version. correct it
                            correction.LanguageUpdate = new SessionProtocol.SessionResourceCorrection.LanguageUpdateInfo(
                                activeLanguage:         languageInfo.LanguageId,
                                localizationVersion:    activeVersion);
                            return;
                        }
                    }
                    else
                    {
                        // Language was known to the content system, but unknown for configs. Cant accept it, pretend it had no language.
                    }
                }
                else
                {
                    // client has a unknown language. Pretend it had no language
                }
            }

            // Neither client or server has an opinion on language. Choose default language.
            LanguageId      defaultLanguage             = GetDefaultLanguageForNewSession(request) ?? MetaplayCore.Options.DefaultLanguage;
            ContentHash     defaultLocalizationVersion  = localizationVersions.GetValueOrDefault(defaultLanguage);
            LanguageInfo    defaultLanguageInfo         = gameConfig.Languages.GetValueOrDefault(defaultLanguage);

            if (defaultLanguage == null)
                throw new InvalidOperationException("Cannot set language. Default language was not set.");
            if (defaultLocalizationVersion == ContentHash.None)
                throw new InvalidOperationException($"Cannot set language {defaultLanguage}. Language version is not available.");
            if (defaultLanguageInfo == null)
                throw new InvalidOperationException($"Cannot set language {defaultLanguage}. Language config is not available.");

            sideEffects.Language = defaultLanguageInfo.LanguageId;
            sideEffects.LanguageSelectionSource = LanguageSelectionSource.ServerSideAutomatic;

            if (request.SessionParams.SessionResourceProposal.ClientActiveLanguage == defaultLanguage && request.SessionParams.SessionResourceProposal.ClientLocalizationVersion == defaultLocalizationVersion)
            {
                // all is ok
                return;
            }
            else
            {
                correction.LanguageUpdate = new SessionProtocol.SessionResourceCorrection.LanguageUpdateInfo(
                    activeLanguage:         defaultLanguage,
                    localizationVersion:    defaultLocalizationVersion);
                return;
            }
        }

        #endregion

        /// <summary>
        /// Check that the owner subscriber/client (if any) hasn't fallen too far behind the server's expected time using the given tick, nor is it too
        /// far ahead of the server. Checks the given <paramref name="tick"/> against server wall clock. Allows some leeway in both directions
        /// (<see cref="PlayerOptions.ClientTimeMaxBehind"/> and <see cref="PlayerOptions.ClientTimeMaxAhead"/>) to deal
        /// with bad network connections, and clock drift.
        /// </summary>
        void EnsureModelTickInBounds(int tick, string source)
        {
            // If there's no owner subscriber session, skip the checks
            EntitySubscriber owner = TryGetOwnerSession();
            if (owner == null)
                return;

            // If the client is paused or unpausing, skip the checks. During this time,
            // the time limits are checked by the Session.
            if (Model.ClientAppPauseStatus != ClientAppPauseStatus.Running)
                return;

            // Resolve wall clock time & time on tick
            MetaTime now        = MetaTime.Now;
            MetaTime checkTime  = ModelUtil.TimeAtTick(tick, Model.TimeAtFirstTick, Model.TicksPerSecond);

            // Check if too far behind wall clock
            PlayerOptions playerOpts = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerOptions>();
            MetaDuration playerModelBehind = now - checkTime;
            if (playerModelBehind > MetaDuration.FromTimeSpan(playerOpts.ClientTimeMaxBehind))
            {
                _log.Warning("PlayerModel is {PlayerModelBehind} behind wall clock time, kicking the subscriber out (source: {Source})", playerModelBehind, source);
                KickSubscriber(owner, new PlayerForceKickOwner(PlayerForceKickOwnerReason.ClientTimeTooFarBehind));
            }
            else if (playerModelBehind > MetaDuration.FromSeconds(10)) // log if more than 10sec behind (5sec is the default flush interval)
            {
                _log.Debug("PlayerModel is {PlayerModelBehind} behind server wall clock time {Now} (modelTime={ModelCurrentTime}) (source: {Source})", playerModelBehind, now, checkTime, source);
            }

            // Check if too far ahead of wall clock
            MetaDuration playerModelAhead = checkTime - now;
            if (playerModelAhead > MetaDuration.FromTimeSpan(playerOpts.ClientTimeMaxAhead))
            {
                _log.Warning("PlayerModel is {PlayerModelAhead} ahead server wall clock time, kicking the subscriber out (source: {Source})", playerModelAhead, source);
                KickSubscriber(owner, new PlayerForceKickOwner(PlayerForceKickOwnerReason.ClientTimeTooFarAhead));
            }
            else if (playerModelAhead > MetaDuration.FromSeconds(1)) // log if 1 second ahead
            {
                _log.Debug("PlayerModel is {PlayerModelAhead} ahead server wall clock time {Now} (modelTime={ModelCurrentTime}) (source: {Source})", playerModelAhead, now, checkTime, source);
            }
        }

        #region Analytics

        /// <summary>
        /// Gather analytics context data for player from the given model. Subclasses can override this to method and create game-specific PlayerAnalyticsContext
        /// implementations for capturing game-specific additions to context data in analytics events. Note that the data should be gathered from the passed in
        /// player model rathen than the currently active model of the player actor.
        /// </summary>
        protected virtual PlayerAnalyticsContext CreateAnalyticsContext(TModel model, int? sessionNumber, OrderedDictionary<string, string> experiments)
        {
            return new DefaultPlayerAnalyticsContext(sessionNumber, experiments);
        }

        PlayerAnalyticsContext GetAnalyticsContext(TModel model)
        {
            PlayerExperimentSubject experimentSubject = _activeGameConfig.IsPlayerTesterInAnyExperiment(model.PlayerId) ? PlayerExperimentSubject.Tester : PlayerExperimentSubject.Player;

            // Use TryAdd since multiple experiments could share the IDs.
            OrderedDictionary<string, string> experiments = new OrderedDictionary<string, string>();
            foreach (EntityActiveExperiment experimentInfo in GetSessionActiveExperiments(_activeGameConfig, model.PlayerId, model.Experiments, experimentSubject))
                experiments.TryAdd(experimentInfo.ExperimentAnalyticsId, experimentInfo.VariantAnalyticsId);

             return CreateAnalyticsContext(
                model,
                sessionNumber:  model.IsOnline ? model.Stats.TotalLogins : null,
                experiments:    experiments);
        }

        /// <summary>
        /// Gets the analytics labels and their value which are added to emitted analytics events. Returning null means no labels. The default implementation returns null.
        /// </summary>
        protected virtual OrderedDictionary<AnalyticsLabel, string> GetAnalyticsLabels(TModel model)
        {
            return null;
        }

        #endregion

        #region Handlers

        [CommandHandler]
        void HandleDoInfrequentPeriodicWork(DoInfrequentPeriodicWork _)
        {
            // Check that owner client is within acceptable time bounds
            EnsureModelTickInBounds(Model.CurrentTick, "periodic");

            // Poll for changed GameConfig & localizations.
            if (Model.IsOnline)
            {
                // Only receive broadcast messages when player is online
                TryReceiveGlobalBroadcasts();
                TryUpdateLocalizationVersions();
            }

            // Periodic IAP retries/checks.
            TryTriggerInAppPurchaseChecks();

            // Try flush events
            // \todo [petri] better timing controls
            _analyticsEventBatcher.Flush();
        }

        [MessageHandler]
        void HandlePlayerFlushActions(PlayerFlushActions flush)
        {
            _isHandlingActionFlush = true;
            try
            {
                // Flush the operations
                HandleFlush(flush);
            }
            finally
            {
                _isHandlingActionFlush = false;
            }
        }

        struct ListWalker<T>
        {
            readonly List<T> _list;
            int _ndx;

            public int NextIndex => _ndx;

            public ListWalker(List<T> list)
            {
                _list = list;
                _ndx = 0;
            }

            public bool TryPeekNext(out T element)
            {
                if (_list == null || _ndx >= _list.Count)
                {
                    element = default;
                    return false;
                }

                element = _list[_ndx];
                return true;
            }

            public void AdvanceNext()
            {
                _ndx++;
            }
        }

        void HandleFlush(PlayerFlushActions flush)
        {
            List<PlayerFlushActions.Operation>      operations      = flush.Operations.Deserialize(_specializedGameConfigResolver, _logicVersion);
            uint[]                                  checksums       = flush.Checksums;
            int                                     totalNumSteps   = 0;
            int                                     totalNumTicks   = 0;
            int                                     totalNumActions = 0;
            JournalPosition                         flushEndPosition;
            ListWalker<PendingSynchronizedServerActionState>    synchronizedActionsQueue = new(_pendingSynchronizedServerActions);
            ListWalker<PendingUnsynchronizedServerActionState>  unsynchronizedActionsQueue = new(_pendingUnsynchronizedServerActions);

            // Check that owner client is within acceptable time bounds
            // \note We check before flushing the actions, as checking after flush would allow client to be behind more than the allowed bound
            EnsureModelTickInBounds(Model.CurrentTick, "flush-pre");

            // Check that flush won't take PlayerModel too far into the future
            if (operations.Count > 0)
                EnsureModelTickInBounds(operations[operations.Count - 1].StartTick, "flush-post");

            // When flushing actions that happened during application pause, move also forward client-clock deadlines.
            // Deadline-ticks use PlayerModel Ticks which are an approximation of Client-side clock. When the deadline is set, the
            // server approximates the client-clock of the deadline and uses that. However, during resume from application pause*, the
            // client spools and flushes a large number of past Ticks. From server-perspective this appears as if the client-clock
            // jumped quickly forward and this temporarily breaks the assumption of the tick being an approximation of client clock. To
            // tolerate deadline failures during the jumpy period, we keep ignore this time jump in deadline computation by moving the
            // deadline along the jump.
            // *) ClientAppPauseStatus = ClientAppPauseStatus.Unpausing
            if (Model.ClientAppPauseStatus == ClientAppPauseStatus.Unpausing && _pendingSynchronizedServerActions.Count > 0)
            {
                int numTicksFlushed = operations.Count(op => op.Action == null);
                _log.Debug("Postponing {NumAction} server action deadline(s) by {NumTicks} ticks due to client app pause.", _pendingSynchronizedServerActions.Count, numTicksFlushed);
                foreach (PendingSynchronizedServerActionState pendingAction in _pendingSynchronizedServerActions)
                    pendingAction.DeadlineBeforeTick += numTicksFlushed;
            }

            // Sanitize
            try
            {
                JournalPosition previousPosition = _playerJournal.StagedPosition;

                foreach (var op in operations)
                {
                    if (op.Action != null)
                    {
                        totalNumActions++;

                        if (op.StartTick != previousPosition.Tick)
                        {
                            _log.Debug("Client supplied action for non-current tick {Position}.", op.StartTick);
                            throw new InvalidOperationException($"PlayerFlushActions, trying to run action on non-current tick {op.StartTick}.");
                        }
                        else if (op.OperationIndex == 0)
                        {
                            _log.Debug("Client supplied action overlapping Tick() {Position}.", op.OperationIndex);
                            throw new InvalidOperationException($"PlayerFlushActions, trying to run action overlapping Tick() {op.OperationIndex}");
                        }

                        if (op.Action is PlayerSynchronizedServerActionMarker synchronizedServerActionMarker)
                        {
                            // Action is a marker for synchronized server action. Find the concrete action by Id and check it is the next in order.

                            if (!synchronizedActionsQueue.TryPeekNext(out PendingSynchronizedServerActionState nextSynchronizedServerAction))
                            {
                                _log.Warning("Synchronized server action rejected. No such id in server buffer: {TrackingId}", synchronizedServerActionMarker.TrackingId);
                                throw new InvalidOperationException($"PlayerFlushActions, trying to run unknown Synchronized Server Action");
                            }
                            else if (nextSynchronizedServerAction.TrackingId != synchronizedServerActionMarker.TrackingId)
                            {
                                _log.Warning("Synchronized server action rejected. Out of order. Expected id {ExpectedId}, got: {ActionId}", nextSynchronizedServerAction.TrackingId, synchronizedServerActionMarker.TrackingId);
                                throw new InvalidOperationException($"PlayerFlushActions, trying to re-order Synchronized Server Action");
                            }
                            else
                            {
                                // Action is ok, move next.
                                synchronizedActionsQueue.AdvanceNext();
                            }
                        }
                        else if (op.Action is PlayerUnsynchronizedServerActionMarker unsynchronizedServerActionMarker)
                        {
                            // Action is a marker for unsynchronized server action. Find the concrete action by ID and fill it in.

                            if (!unsynchronizedActionsQueue.TryPeekNext(out PendingUnsynchronizedServerActionState nextUnsynchronizedServerAction))
                            {
                                _log.Warning("Unsynchronized server action rejected. No such id in server buffer: {ActionId}", unsynchronizedServerActionMarker.TrackingId);
                                throw new InvalidOperationException($"PlayerFlushActions, trying to run unknown Unsynchronized Server Action");
                            }
                            else if (nextUnsynchronizedServerAction.TrackingId != unsynchronizedServerActionMarker.TrackingId)
                            {
                                _log.Warning("Unsynchronized server action rejected. Out of order. Expected id {ExpectedId}, got: {ActionId}", nextUnsynchronizedServerAction.TrackingId, unsynchronizedServerActionMarker.TrackingId);
                                throw new InvalidOperationException($"PlayerFlushActions, trying to re-order Unsynchronized Server Action");
                            }
                            else
                            {
                                // Fill in the server action.
                                unsynchronizedServerActionMarker.ClientExecutedAction = nextUnsynchronizedServerAction.Action;

                                // Move next
                                unsynchronizedActionsQueue.AdvanceNext();
                            }
                        }
                        else
                        {
                            // Action created by client. Validate it.
                            ValidateClientOriginatingAction(op.Action);
                        }
                    }
                    else
                    {
                        totalNumTicks++;

                        if (op.StartTick != previousPosition.Tick + 1)
                        {
                            _log.Debug("Client supplied Tick {Position}, expected Tick {NextTick}.", op.StartTick, previousPosition.Tick + 1);
                            throw new InvalidOperationException($"PlayerFlushActions, trying to run tick on non-successive tick {op.StartTick}. Expected: {previousPosition.Tick + 1}");
                        }
                        if (op.OperationIndex != 0)
                        {
                            _log.Debug("Client supplied Tick overlapping Action() {Position}.", op.OperationIndex);
                            throw new InvalidOperationException($"PlayerFlushActions, trying to run Tick() overlapping Action() {op.OperationIndex}");
                        }
                    }

                    if (op.NumSteps < 1)
                    {
                        _log.Debug("Client supplied negative step count {NumSteps}", op.NumSteps);
                        throw new InvalidOperationException($"PlayerFlushActions, invalid step count {op.NumSteps}.");
                    }

                    JournalPosition startPosition = JournalPosition.FromTickOperationStep(op.StartTick, op.OperationIndex, 0);
                    if (startPosition < previousPosition)
                    {
                        _log.Debug("Client supplied action in past {Position}. Current = {Nowtime}", startPosition, previousPosition);
                        throw new InvalidOperationException($"PlayerFlushActions, trying to run op in past {startPosition}, now = {previousPosition}");
                    }
                    else if (synchronizedActionsQueue.TryPeekNext(out PendingSynchronizedServerActionState nextSynchronizedServerAction))
                    {
                        // Reject flush if exceeds BOTH the model time deadline and the wall clock deadline
                        if (startPosition >= JournalPosition.FromTickOperationStep(nextSynchronizedServerAction.DeadlineBeforeTick, 0, 0)
                            && MetaTime.Now >= nextSynchronizedServerAction.DeadlineBeforeTime)
                        {
                            _log.Warning("Missed deadline at Tick {DeadlineTick}, Time {DeadlineTime} for server action: {Action}", nextSynchronizedServerAction.DeadlineBeforeTick, nextSynchronizedServerAction.DeadlineBeforeTime, PrettyPrint.Compact(nextSynchronizedServerAction.Action));
                            throw new InvalidOperationException($"PlayerFlushActions, Server action deadline reached. Flush rejected.");
                        }
                    }

                    previousPosition = JournalPosition.FromTickOperationStep(startPosition.Tick, startPosition.Operation, op.NumSteps);
                    totalNumSteps += op.NumSteps;
                }

                if (totalNumTicks > PlayerFlushActions.MaxTicksPerFlush)
                {
                    _log.Debug("Trying to simulate too many ticks ({NumTicks})", totalNumTicks);
                    throw new InvalidOperationException($"PlayerFlushActions, max tick limit exeeded. Got {totalNumTicks}, max is {PlayerFlushActions.MaxTicksPerFlush}.");
                }
                if (checksums.Length != totalNumSteps)
                {
                    _log.Info("Client is advancing {NumSteps} steps, but provided {NumChecksums} step checksums", totalNumSteps, checksums.Length);
                    throw new InvalidOperationException($"PlayerFlushActions, ticks inconsistent. Got {totalNumSteps} steps, but {checksums.Length} checksums");
                }

                flushEndPosition = previousPosition;
            }
            catch (Exception ex)
            {
                // Kick out cleanly if any validation fails.
                _log.Error("Flush failed validation, kicking: {Error}", ex);
                KickPlayerIfConnected(PlayerForceKickOwnerReason.InternalError);

                // \hack: KickPlayerIfConnected is called during Flush, so it enqueues action on continuation.
                //        We force execute the continuations with this ugly hack to make sure the continuations
                //        get run and session cleanup is handled immediately.
                _flushCompletionContinuations?.Invoke();
                _flushCompletionContinuations = null;
                return;
            }

            // Remove all unsynchronized server actions.
            _pendingUnsynchronizedServerActions.RemoveRange(0, unsynchronizedActionsQueue.NextIndex);

            // Execute

            c_ticksExecuted.Inc(totalNumTicks);

            //_log.Debug("Simulating {NumTicks} ticks (from {FromTick} to {ToTick})", totalNumTicks, Model.CurrentTick, flushEndPosition.Tick);
            _ = flushEndPosition;

            int checksumOffset = 0;
            foreach (var op in operations)
            {
                if (op.Action != null)
                {
                    PlayerActionBase action;
                    if (op.Action is PlayerSynchronizedServerActionMarker executeSynchronizedAction)
                    {
                        var pendingActionState = _pendingSynchronizedServerActions[0];
                        _pendingSynchronizedServerActions.RemoveAt(0);

                        // Execute from our copy.
                        action = pendingActionState.Action;
                    }
                    else
                    {
                        action = op.Action;
                    }

                    _log.Debug("Execute action (tick {Tick}): {Action}", Model.CurrentTick, PrettyPrint.Compact(action));
                    _playerJournal.StageAction(action, new ArraySegment<uint>(checksums, checksumOffset, op.NumSteps));
                    c_actionsExecuted.WithLabels(action.GetType().Name).Inc();
                }
                else
                {
                    _playerJournal.StageTick(new ArraySegment<uint>(checksums, checksumOffset, op.NumSteps));
                }
                checksumOffset += op.NumSteps;
            }

            // After staging ticks and actions, we mark the flush committable. This needs to be done after to keep the order
            // in which direct side-effects and flush Acks are observed constant. The order is: Pending Acks, Flush
            // Side-effects, Flush Acks (which may get batched with next flush). This also guarantees that server only Acks
            // whole Flushes (in the absence of checksum mismatch) which is a desired property.
            _actionFlushBatchUpperBound = _playerJournal.StagedPosition;

            // Run any enqueued continuations. If there are any enqueued server actions, they will force flush the batch.
            _flushCompletionContinuations?.Invoke();
            _flushCompletionContinuations = null;

            // Hint we might want to flush now.
            FlushActionBatch(forceFlushImmediately: false);
        }

        [MessageHandler]
        void HandleInternalSessionNotifyClientAppStatusChanged(InternalSessionNotifyClientAppStatusChanged notify)
        {
            Model.IsClientConnected = notify.IsClientConnected;
            Model.ClientAppPauseStatus = notify.PauseStatus;
            OnClientConnectivityStatusChanged();
        }

        #endregion

        bool TryReceiveBroadcast(BroadcastMessage broadcast)
        {
            // Check that we are among target audience
            if (Model.PassesFilter(broadcast.Params.PlayerFilter, out bool segmentEvalError))
            {
                // Append as player mail with the language that the player is using
                _log.Debug("Receive global broadcast {Id}", broadcast.Params.Id);
                MetaInGameMail mail = broadcast.Params.ConvertToPlayerMail(Model);
                PlayerAddMail action = new PlayerAddMail(mail, broadcast.Params.StartAt);
                EnqueueServerAction(action, allowSynchronousExecution: true);
                Model.ReceivedBroadcastIds.Add(broadcast.Params.Id);

                // Remember that a broadcast was consumed.
                GlobalStateProxyActor.PlayerConsumedBroadcast(broadcast.Params);
                return true;
            }
            if (segmentEvalError)
            {
                _log.Warning("Player segment condition evaluation failed for broadcast {BroadcastId}:{BroadcastName}", broadcast.Params.Id, broadcast.Params.Name);
            }
            return false;
        }

        void TryReceiveGlobalBroadcasts()
        {
            _activeBroadcastStateSubscriber.Update((ActiveBroadcastSet newState, ActiveBroadcastSet prevState) =>
            {
                _log.Debug("List of active broadcasts has changed (from {OldNumBroadcasts} active broadcasts to {NewNumBroadcasts}), re-evaluate whether there are new ones for us!", prevState?.ActiveBroadcasts?.Count, newState.ActiveBroadcasts.Count);

                _activeBroadcastTriggerConditionEventTypes.Clear();

                // Append all new broadcast messages to player's state
                foreach (BroadcastMessage broadcast in newState.ActiveBroadcasts)
                {
                    // Check that broadcast not already received
                    if (Model.ReceivedBroadcastIds.Contains(broadcast.Params.Id))
                        continue;

                    if (broadcast.Params.TriggerCondition == null)
                    {
                        // Try receiving broadcasts without a trigger
                        TryReceiveBroadcast(broadcast);
                    }
                    else
                    {
                        _activeBroadcastTriggerConditionEventTypes.UnionWith(broadcast.Params.TriggerCondition.EventTypesToConsider);
                    }
                }

                // Remove any expired/archived messages from state
                // \todo [petri] optimize: linq generates garbage
                Model.ReceivedBroadcastIds.RemoveWhere(broadcastId => !newState.BroadcastIds.Contains(broadcastId));
            });
        }

        /// <summary>
        /// Polls current language localization versions and on detected change, informs the client.
        /// </summary>
        void TryUpdateLocalizationVersions()
        {
            _activeLocalizationVersionSubscriber.Update((ActiveLocalizationVersions newState, ActiveLocalizationVersions prevState) =>
            {
                // If we have a session, forward the update.
                // Otherwise, we just updated _activeLocalizationVersionSubscriber.Current
                PublishMessage(EntityTopic.Owner, new UpdateLocalizationVersions(newState.Versions));
            });
        }

        protected void TryTriggerInAppPurchaseChecks()
        {
            // Retry validation of pending in-app purchases.
            TryTriggerInAppPurchaseValidationRetries();

            // Check subscriptions: renewals of expired subscriptions,
            // and periodic state queries of non-expired subscriptions.
            TryTriggerIAPSubscriptionStateQueries();

            // Check subscription reuse: same subscription instance
            // being used on multiple players, in which case only the
            // player with the latest purchase gets to keep the
            // subscription active.
            TryTriggerIAPSubscriptionReuseChecks();
        }

        #region IAP subscriptions

        protected void TryTriggerInAppPurchaseValidationRetries()
        {
            // Retry validation of each pending purchase that has a non-terminal status and is old enough.
            // \note Young purchases are skipped here because they're likely still just getting their first validation.

            MetaTime        currentTime     = Model.CurrentTime;
            MetaDuration    minRetryDelay   = MetaDuration.FromSeconds(10); // Don't retry purchases younger than this

            foreach (InAppPurchaseEvent pendingEv in Model.PendingInAppPurchases.Values)
            {
                if (pendingEv.Status == InAppPurchaseStatus.PendingValidation && currentTime >= pendingEv.PurchaseTime + minRetryDelay)
                {
                    if (Model.GameConfig.InAppProducts.TryGetValue(pendingEv.ProductId, out InAppProductInfoBase productInfo))
                    {
                        _log.Debug("Triggering retry of IAP validation for product {ProductId}, pending transaction {TransactionId}...", pendingEv.ProductId, pendingEv.TransactionId);
                        InAppPurchaseTransactionInfo txnInfo = InAppPurchaseTransactionInfo.FromPurchaseEvent(pendingEv, productInfo.Type, Model.IsDeveloper);
                        CastMessage(_entityId, new TriggerInAppPurchaseValidation(txnInfo, isRetry: true));
                    }
                    else
                        _log.Error("Config is missing for product {ProductId}, cannot retry IAP validation of pending transaction {TransactionId}", pendingEv.ProductId, pendingEv.TransactionId);
                }
            }
        }

        protected void TryTriggerIAPSubscriptionStateQueries()
        {
            MetaTime currentTime = MetaTime.Now;

            foreach ((InAppProductId productId, SubscriptionModel subscriptionModel) in Model.IAPSubscriptions.Subscriptions)
            {
                foreach ((string originalTransactionId, SubscriptionInstanceModel subscriptionInstance) in subscriptionModel.SubscriptionInstances)
                {
                    if (subscriptionInstance.DisabledDueToReuse)
                        continue;
                    if (!subscriptionInstance.LastKnownState.HasValue)
                        continue;

                    SubscriptionInstanceState subscriptionState = subscriptionInstance.LastKnownState.Value;

                    bool shouldQueryState;
                    if (currentTime >= subscriptionState.ExpirationTime)
                    {
                        // Subscription has expired.
                        // If the last state query was done before it expired, then we should
                        // trigger a state query to check for renewal.
                        // \note In case this check happens to occur very soon after ExpirationTime,
                        //       we'll do another check 1 past the expiration time, in case our
                        //       clock is slightly ahead the IAP store's clock. We don't want to end
                        //       up thinking the subscription expired and wasn't renewed while the
                        //       store was just about to reach the expiration time.
                        //       \todo This is pretty speculative as it's quite possible the store
                        //             doesn't actually apply the renewal at exactly the expiration
                        //             time anyway. We could probably be still more robust in case
                        //             the store does the renewal a bit too late.

                        MetaTime expirationTimePlusMargin = subscriptionState.ExpirationTime + MetaDuration.FromMinutes(1);

                        if (currentTime >= expirationTimePlusMargin)
                            shouldQueryState = subscriptionInstance.StateQueriedAt < expirationTimePlusMargin;
                        else
                            shouldQueryState = subscriptionInstance.StateQueriedAt < subscriptionState.ExpirationTime;
                    }
                    else
                    {
                        // Subscription hasn't expired.
                        // If enough time has elapsed since the last state query, then we should
                        // trigger a periodic state query. The purpose of this is to refresh our
                        // knowledge of state that may update at any time, such as auto-renewal
                        // status.
                        shouldQueryState = currentTime >= subscriptionInstance.StateQueriedAt + IAPSubscriptionPeriodicCheckInterval;
                    }

                    if (shouldQueryState)
                    {
                        _log.Info("Triggering subscription state query: product {ProductId}, state queried at {SubscriptionStateQueriedAt}, state {SubscriptionState}, original transaction id {OriginalTransactionId}", productId, subscriptionInstance.StateQueriedAt, subscriptionState, originalTransactionId);
                        _self.Tell(new TriggerIAPSubscriptionStateQuery(originalTransactionId), sender: _self);
                    }
                }
            }
        }

        protected void TryTriggerIAPSubscriptionReuseChecks()
        {
            MetaTime currentTime = MetaTime.Now;

            foreach ((InAppProductId productId, SubscriptionModel subscriptionModel) in Model.IAPSubscriptions.Subscriptions)
            {
                if (currentTime >= subscriptionModel.GetExpirationTime()) // Don't bother checking if the subscription is already expired.
                    continue;

                foreach ((string originalTransactionId, SubscriptionInstanceModel subscriptionInstance) in subscriptionModel.SubscriptionInstances)
                {
                    if (subscriptionInstance.DisabledDueToReuse) // Already disabled - no need to check again.
                        continue;

                    if (currentTime >= subscriptionInstance.ReuseCheckedAt + IAPSubscriptionReuseCheckInterval)
                    {
                        _log.Debug("Triggering subscription purchase reuse check: product {ProductId}, reuse checked at {ReuseCheckedAt}, original transaction id {OriginalTransactionId}", productId, subscriptionInstance.ReuseCheckedAt, originalTransactionId);
                        CastMessage(_entityId, new TriggerIAPSubscriptionReuseCheck(originalTransactionId));
                    }
                }
            }
        }

        async Task<InAppPurchaseSubscriptionPersistedInfo> TryGetSubscriptionPersistedInfoAsync(string originalTransactionId)
        {
            string                              persistedSubscriptionKey    = PersistedInAppPurchaseSubscription.CreatePrimaryKey(_entityId, originalTransactionId);
            PersistedInAppPurchaseSubscription  persistedSubscription       = await MetaDatabase.Get().TryGetAsync<PersistedInAppPurchaseSubscription>(primaryKey: persistedSubscriptionKey, partitionKey: _entityId.ToString());

            if (persistedSubscription == null)
                return null;

            return MetaSerialization.DeserializeTagged<InAppPurchaseSubscriptionPersistedInfo>(persistedSubscription.SubscriptionInfo, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
        }

        [CommandHandler]
        async Task HandleTriggerIAPSubscriptionStateQueryAsync(TriggerIAPSubscriptionStateQuery trigger)
        {
            InAppPurchaseSubscriptionPersistedInfo subscriptionInfo = await TryGetSubscriptionPersistedInfoAsync(trigger.OriginalTransactionId);
            if (subscriptionInfo == null)
            {
                _log.Error("Couldn't find PersistedInAppPurchaseSubscription for {OriginalTransactionId}", trigger.OriginalTransactionId);
                return;
            }

            CastMessage(GetAssociatedServiceEntityId(EntityKindCloudCore.InAppValidator), new InAppPurchaseSubscriptionStateRequest(subscriptionInfo));
        }

        [MessageHandler]
        void HandleInAppPurchaseSubscriptionStateResponse(InAppPurchaseSubscriptionStateResponse response)
        {
            if (response.Result == InAppPurchaseSubscriptionStateResponse.ResultCode.Ok)
                EnqueueServerAction(new PlayerUpdateSubscriptionInstanceState(response.OriginalTransactionId, response.Subscription));
            else if (response.Result == InAppPurchaseSubscriptionStateResponse.ResultCode.Error)
                _log.Warning("Got non-success result for subscription query for {OriginalTransactionId}: {Result}, reason: {Reason}", response.OriginalTransactionId, response.Result, response.ErrorReason);
            else
                _log.Warning("Unknown IAP subscription query result {Result}", response.Result);
        }

        /// <summary>
        /// Query the states of all IAP subscriptions that have expired since the last query,
        /// wait for the query results, and update <see cref="Model"/> accordingly. This is
        /// meant to be used just before session start, and modifies Model directly instead
        /// of via the timeline.
        ///
        /// This waits at most 5 seconds for the async subscription queries to complete.
        /// Queries that do not complete within that time are ignored (besides logging).
        /// </summary>
        async Task TryCheckSubscriptionRenewalsAtSessionStartAsync()
        {
            MetaTime currentTime = MetaTime.Now;

            List<(string OriginalTransactionId, Task<InAppPurchaseSubscriptionStateResponse> Task)> queries = new ();

            // Start queries for all subscriptions that have expired since the last query.

            foreach ((InAppProductId productId, SubscriptionModel subscriptionModel) in Model.IAPSubscriptions.Subscriptions)
            {
                foreach ((string originalTransactionId, SubscriptionInstanceModel subscriptionInstance) in subscriptionModel.SubscriptionInstances)
                {
                    if (subscriptionInstance.DisabledDueToReuse)
                        continue;
                    if (!subscriptionInstance.LastKnownState.HasValue)
                        continue;

                    SubscriptionInstanceState subscriptionState = subscriptionInstance.LastKnownState.Value;

                    // Check if expired since the last query.
                    if (currentTime >= subscriptionState.ExpirationTime
                     && subscriptionInstance.StateQueriedAt < subscriptionState.ExpirationTime)
                    {
                        InAppPurchaseSubscriptionPersistedInfo subscriptionInfo = await TryGetSubscriptionPersistedInfoAsync(originalTransactionId);
                        if (subscriptionInfo == null)
                            _log.Error("Couldn't find PersistedInAppPurchaseSubscription for {OriginalTransactionId}", originalTransactionId);
                        else
                        {
                            // \note Here, we only check subscription state updates; we don't check purchase reuse disablement
                            //       like we do in TryTriggerInAppPurchaseChecks. Reuse disablement gets polled eventually anyway,
                            //       and it's not a thing that benefits the player, so it's not worth complicating the code here.

                            Task<InAppPurchaseSubscriptionStateResponse> queryTask = EntityAskAsync<InAppPurchaseSubscriptionStateResponse>(GetAssociatedServiceEntityId(EntityKindCloudCore.InAppValidator), new InAppPurchaseSubscriptionStateRequest(subscriptionInfo));
                            queries.Add((originalTransactionId, queryTask));
                        }
                    }
                }
            }

            if (queries.Count == 0)
                return;

            _log.Info("Doing {NumQueries} session-start subscription renewal checks", queries.Count);

            // Wait for all queries to finish, except don't wait longer than a few seconds.
            // \note Be sure to keep this timeout short enough to not trigger the timeout of
            //       the EntityAsk regarding the session start entity-subscription.
            await Task.WhenAny(
                Task.WhenAll(queries.Select(q => q.Task)),
                Task.Delay(5_000));

            // Handle all successfully completed queries.
            // \note Here, successful query results cause the Model to be modified
            //       directly and immediately, instead of via timelined actions.
            //       This method is only meant to be used at session start, before
            //       session actually exists, so we can modify Model directly.
            foreach ((string originalTransactionId, Task<InAppPurchaseSubscriptionStateResponse> queryTask) in queries)
            {
                if (!queryTask.IsCompleted)
                    _log.Warning("Session-start query for subscription {OriginalTransactionId} timed out", originalTransactionId);
                else if (queryTask.IsFaulted)
                    _log.Warning("Session-start query for subscription {OriginalTransactionId} faulted: {Exception}", queryTask.Exception);
                else if (queryTask.IsCompletedSuccessfully)
                {
                    InAppPurchaseSubscriptionStateResponse response = queryTask.GetCompletedResult();

                    // \todo [nuutti] This is modified copypaste from HandleInAppPurchaseSubscriptionStateResponse, unify them somehow

                    if (response.Result == InAppPurchaseSubscriptionStateResponse.ResultCode.Ok)
                    {
                        // \note In this method, we modify the model immediately and don't care about the timeline.
                        PlayerActionBase action = new PlayerUpdateSubscriptionInstanceState(response.OriginalTransactionId, response.Subscription);
                        MetaActionResult result = ModelUtil.RunAction(Model, action, NullChecksumEvaluator.Context);
                        if (result != MetaActionResult.Success)
                            _log.Warning($"Got non-Success result from {nameof(PlayerUpdateSubscriptionInstanceState)}: {{Result}}", result);
                    }
                    else if (response.Result == InAppPurchaseSubscriptionStateResponse.ResultCode.Error)
                        _log.Warning("Got non-success result for subscription query: {Result}, reason: {Reason}", response.Result, response.ErrorReason);
                    else
                        _log.Warning("Unknown IAP subscription query result {Result}", response.Result);
                }
            }
        }

        [MessageHandler]
        async Task HandleTriggerIAPSubscriptionReuseCheckAsync(TriggerIAPSubscriptionReuseCheck trigger)
        {
            MetaTime currentTime = MetaTime.Now;

            List<PersistedInAppPurchaseSubscription> subscriptionMetadatas = await MetaDatabase.Get().GetIAPSubscriptionMetadatas(trigger.OriginalTransactionId);
            if (subscriptionMetadatas.Count == 0)
            {
                _log.Error("GetIAPSubscriptionMetadatas returned no items for {OriginalTransactionId}", trigger.OriginalTransactionId);
                return;
            }

            PersistedInAppPurchaseSubscription latestSubscriptionMetadata = subscriptionMetadatas.MaxBy(m => m.CreatedAt);

            bool shouldDisable = latestSubscriptionMetadata.PlayerEntityId != _entityId;

            if (shouldDisable)
                _log.Info("Latest purchase of subscription {OriginalTransactionId} was made by another player, {OtherPlayerId}, at {SubscriptionItemCreatedAt}. Disabling own subscription.", trigger.OriginalTransactionId, latestSubscriptionMetadata.PlayerEntityId, latestSubscriptionMetadata.CreatedAt);

            EnqueueServerAction(new PlayerSetSubscriptionInstanceDisablementDueToReuse(trigger.OriginalTransactionId, disable: shouldDisable, checkedAt: currentTime));
        }

        #endregion

        /// <summary>
        /// Flushes or schedules the flush of the action batch. If the action batch processing is currently on a throttling-period, the flush is scheduled at
        /// the end of the period. If the throttling-period is over or <paramref name="forceFlushImmediately"/> is set, the flush is completed immediately
        /// synchonously and a new throttling-period is started.
        /// </summary>
        /// <param name="forceFlush"></param>
        protected void FlushActionBatch(bool forceFlushImmediately)
        {
            // nothing to flush?
            // \note: CheckpointPosition may be greater than UBound if _actionFlushBatchUpperBound is not yet set
            if (_playerJournal.CheckpointPosition >= _actionFlushBatchUpperBound)
                return;

            DateTime now = DateTime.UtcNow;
            TimeSpan durationToFlush;

            if (forceFlushImmediately)
            {
                durationToFlush = TimeSpan.Zero;
            }
            else
            {
                durationToFlush = _nextActionFlushEarliestAt - now;
            }

            if (durationToFlush <= TimeSpan.Zero)
            {
                // flush now, suppress all in-flight scheduled flushses
                CancelScheduledFlushActionBatches();
                _nextActionFlushEarliestAt = now + ActionFlushBatchMinInterval;
                InternalDoFlushActionBatch();
            }
            else if (_numActionFlushesSuppressed == _numActionFlushesScheduled)
            {
                //_log.Debug("Throttling action flush processing. Batch delayed for {NumMilliseconds}ms.", (int)durationToFlush.TotalMilliseconds);

                // no enqueued flushes, need to enqueue new
                _numActionFlushesScheduled++;
                Context.System.Scheduler.ScheduleTellOnce(durationToFlush, _self, ActorFlushActionBatchCommand.Instance, null);
            }
            else
            {
                // an un-suppressed flush is already enqueued, we can rely on it to flush us.
            }
        }

        void CancelScheduledFlushActionBatches()
        {
            _numActionFlushesSuppressed = _numActionFlushesScheduled;
        }

        protected void ResetScheduledFlushActionBatches()
        {
            CancelScheduledFlushActionBatches();
            _actionFlushBatchUpperBound = JournalPosition.Epoch;
        }

        [CommandHandler]
        void HandleActorFlushActionBatchCommand(ActorFlushActionBatchCommand _)
        {
            _numActionFlushesScheduled--;
            if (_numActionFlushesSuppressed > 0)
            {
                _numActionFlushesSuppressed--;
                return;
            }

            //_log.Debug("Action flush throttling over, processing delayed batch.");
            _nextActionFlushEarliestAt = DateTime.UtcNow + ActionFlushBatchMinInterval;
            InternalDoFlushActionBatch();
        }

        void InternalDoFlushActionBatch()
        {
            c_flushBatchesExecuted.Inc();

            // _actionFlushBatchUpperBound is always valid for the given journal. But if journal is replaced, the constraint might not hold. Just ignore.
            if (_actionFlushBatchUpperBound <= _playerJournal.CheckpointPosition || _actionFlushBatchUpperBound > _playerJournal.StagedPosition)
                return;

            using (var commitResult = _playerJournal.Commit(_actionFlushBatchUpperBound, ModelJournal<IPlayerModelBase>.Follower.ConflictResolutionMode.ContinueEvenIfConflicts))
            {
                if (!commitResult.HasConflict)
                {
                    // All is fine, send ack to client
                    // \note it's okay to send CurrentTick, no new actions are allowed on processed ticks
                    PublishMessage(EntityTopic.Owner, new PlayerAckActions(_actionFlushBatchUpperBound));
                    return;
                }

                Model.Stats.TotalDesyncs++;

                // In case of a checkpoint drift, save state and (enqueue) kill actor.
                // Checkpoint drift means that the journal has an internally invalid state and continuing from here is risky. The
                // only way to remedy the situation is to start from scratch. In this case, the resolved conflict journal position
                // in not reliable, so don't even rerpot the mismatch.
                if (commitResult.HasCheckpointDrift)
                {
                    _self.Tell(CheckpointDriftDetectedCommand.Instance, sender: null);
                    KickPlayerIfConnected(PlayerForceKickOwnerReason.InternalError);
                    return;
                }

                // Locate conflict and inform the client
                int conflictTick = (int)commitResult.ConflictAfterPosition.Tick;
                int actionNdx = (int)commitResult.ConflictAfterPosition.Operation - 1;

                if (actionNdx == -1)
                {
                    _log.Warning("Checksum mismatch on tick {CurrentTick}: client=0x{ClientChecksum:X8}, server=0x{ServerChecksum:X8}", conflictTick, commitResult.ExpectedChecksum, commitResult.ActualChecksum);
                    c_checksumMismatches.WithLabels("Tick").Inc();
                }
                else
                {
                    PlayerActionBase action = (PlayerActionBase)commitResult.ConflictAction;
                    _log.Warning("Checksum mismatch after executing tick {CurrentTick} action #{ActionIndex}: {Action}", conflictTick, actionNdx, PrettyPrint.Verbose(action));

                    // action might be null if it cannot be resolved.
                    if (action != null)
                        c_checksumMismatches.WithLabels(action.GetType().Name).Inc();
                }

                PublishMessage(EntityTopic.Owner, new PlayerChecksumMismatch(conflictTick, actionNdx, commitResult.ChecksumSerializedAfter, commitResult.ChecksumSerializedBefore));
            }
        }

        [CommandHandler]
        async Task HandleCheckpointDriftDetectedCommand(CheckpointDriftDetectedCommand _)
        {
            // Checkpoint drift in some earlier flush. Kill actor but save the state.
            await PersistStateIntermediate();
            throw new InvalidOperationException("checkpoint drift in action flush. Crashing actor for safety.");
        }

        [MessageHandler]
        void HandlePlayerChecksumMismatchDetails(PlayerChecksumMismatchDetails details)
        {
            PlayerActionBase action = null;
            if (!details.Action.IsEmpty)
                action = details.Action.Deserialize(_specializedGameConfigResolver, _logicVersion);

            _log.Warning("Checksum mismatch details: tick={Tick}, action={Action}\nPlayerModel diff (client vs. server): {StateDiff}", details.TickNumber, PrettyPrint.Compact(action), details.PlayerModelDiff);
        }

        [MessageHandler]
        async Task HandleValidateInAppPurchaseResponse(ValidateInAppPurchaseResponse validate)
        {
            InAppPurchaseTransactionInfo txnInfo = validate.TransactionInfo;

            _log.Debug("Received ValidateInAppPurchaseResponse: transactionId={TransactionId}, productId={ProductId}, result={Result}", txnInfo.TransactionId, txnInfo.ProductId, validate.Result);
            if (validate.Result != InAppPurchaseValidationResult.Valid)
                _log.Warning("Got non-valid purchase validation result {Result}, reason: {Reason}", validate.Result, validate.FailReason);

            c_inAppValidateResponses.WithLabels(validate.Result.ToString()).Inc();

            if (!Model.PendingInAppPurchases.TryGetValue(txnInfo.TransactionId, out InAppPurchaseEvent pendingEv))
            {
                // We're not expecting a validation response: no such purchase is pending.
                // Ignore.
                // \note Unexpected validation responses can happen in uncommon but valid circumstances due to validation retries.
                c_inAppValidateUnexpectedResponses.WithLabels("NotPending").Inc();
                _log.Debug("ValidateInAppPurchaseResponse received for non-pending transaction, ignoring: transactionId={TransactionId}", txnInfo.TransactionId);
            }
            else if (pendingEv.Status != InAppPurchaseStatus.PendingValidation)
            {
                // We're not expecting a validation response: the purchase is already in a terminal status.
                // Ignore.
                // \note Unexpected validation responses can happen in uncommon but valid circumstances due to validation retries.

                if ((validate.Result == InAppPurchaseValidationResult.Valid && pendingEv.Status != InAppPurchaseStatus.ValidReceipt)
                 || (validate.Result == InAppPurchaseValidationResult.Invalid && pendingEv.Status != InAppPurchaseStatus.InvalidReceipt))
                {
                    // Getting a terminal result that doesn't correspond to the existing terminal status isn't normal.
                    c_inAppValidateUnexpectedResponses.WithLabels("DifferentTerminal").Inc();
                    _log.Warning("ValidateInAppPurchaseResponse received for already-terminal pending transaction and with a differing terminal status, ignoring: transactionId={TransactionId}, pending status={PendingStatus}, new result={NewResult}", txnInfo.TransactionId, pendingEv.Status, validate.Result);
                }
                else
                {
                    // Otherwise, the new result either matches the existing terminal status, or is TransientError. Either is OK.
                    c_inAppValidateUnexpectedResponses.WithLabels("AlreadyTerminal").Inc();
                    _log.Debug("ValidateInAppPurchaseResponse received for already-terminal pending transaction, ignoring: transactionId={TransactionId}, pending status={PendingStatus}, new result={NewResult}", txnInfo.TransactionId, pendingEv.Status, validate.Result);
                }
            }
            else if (validate.Result == InAppPurchaseValidationResult.Valid)
            {
                Model.EventStream.Event(new PlayerEventInAppValidationComplete(PlayerEventInAppValidationComplete.ValidationResult.Valid, pendingEv.ProductId, pendingEv.Platform, pendingEv.TransactionId, validate.GoogleOrderId, pendingEv.PlatformProductId, pendingEv.ReferencePrice, pendingEv.GameProductAnalyticsId, validate.PaymentType, pendingEv.TryGetGamePurchaseAnalyticsContext()));

                string transactionDeduplicationId = InAppPurchaseTransactionIdUtil.ResolveTransactionDeduplicationId(txnInfo);

                // If this is a subscription product, we persist information about the subscription
                // which we can look up by OriginalTransactionId, and also disable the subscription
                // for other players who used the same purchase. Due to purchase restoration, the
                // same purchase can legitimately be used by multiple players.
                // This persisted information is used for:
                // - Checking renewals and other state updates of the subscription in the store.
                // - Finding other players who used the same OriginalTransactionId.
                // \note This is persisted before we update the PlayerModel with the purchase validation
                //       result, because we want to guarantee that this persisted entry is always present
                //       for subscription purchases that appear in the PlayerModel's IAPSubscription.
                if (validate.SubscriptionMaybe != null)
                {
                    string originalTransactionId = validate.OriginalTransactionId;

                    // First, resolve the already-existing purchases of this subscription.
                    // See below for usage.

                    List<PersistedInAppPurchaseSubscription> previousSubscriptionMetadatas = await MetaDatabase.Get().GetIAPSubscriptionMetadatas(originalTransactionId);

                    // Persist new information.

                    InAppPurchaseSubscriptionPersistedInfo subscriptionInfo = new InAppPurchaseSubscriptionPersistedInfo(
                        platform:                           txnInfo.Platform,
                        persistedValidatedTransactionId:    transactionDeduplicationId,
                        validatedTransactionPurchaseTime:   txnInfo.PurchaseTime,
                        productId:                          txnInfo.ProductId,
                        platformProductId:                  txnInfo.PlatformProductId,
                        originalTransactionId:              originalTransactionId,
                        receipt:                            txnInfo.Receipt);

                    MetaSerialized<InAppPurchaseSubscriptionPersistedInfo>  serializedSubscriptionInfo  = MetaSerialization.ToMetaSerialized(subscriptionInfo, MetaSerializationFlags.IncludeAll, logicVersion: null);
                    PersistedInAppPurchaseSubscription                      persistedSubscription       = new PersistedInAppPurchaseSubscription(_entityId, originalTransactionId, serializedSubscriptionInfo, DateTime.UtcNow);
                    await MetaDatabase.Get().InsertOrUpdateAsync(persistedSubscription);

                    // Figure out the player (if any) that previously had the latest purchase/restore
                    // of this same subscription instance. We'll want to tell that player to disable its
                    // instance (by using TriggerIAPSubscriptionReuseCheck). Note that this is done
                    // *after* persisting the new item to ensure that the other player sees that entry
                    // and TriggerIAPSubscriptionReuseCheck handling does the correct thing.
                    // \note This is not robust in rare scenarios like race conditions or delivery failure;
                    //       might fail to disable the subscription from the other player. However,
                    //       subscription reuse is also checked periodically, so the situation should
                    //       eventually heal. This fire-and-forget is done here just to update the state
                    //       more quickly.

                    if (previousSubscriptionMetadatas.Count > 0)
                    {
                        PersistedInAppPurchaseSubscription previousLatest = previousSubscriptionMetadatas.MaxBy(m => m.CreatedAt);
                        if (previousLatest.PlayerEntityId != _entityId)
                        {
                            _log.Info("Previous latest purchase of subscription {OriginalTransactionId} was made by another player, {PreviousPlayerId}, at {PreviousSubscriptionItemCreatedAt}. Telling that player to check subscription reuse.", originalTransactionId, previousLatest.PlayerEntityId, previousLatest.CreatedAt);
                            CastMessage(previousLatest.PlayerEntityId, new TriggerIAPSubscriptionReuseCheck(originalTransactionId));
                        }
                    }
                }

                // Update state according to response.
                ExecuteServerActionAfterPendingActions(new PlayerInAppPurchaseValidated(
                    transactionId: txnInfo.TransactionId,
                    status: InAppPurchaseStatus.ValidReceipt,
                    isDuplicateTransaction: false,
                    orderId: validate.GoogleOrderId,
                    originalTransactionId: validate.OriginalTransactionId,
                    subscription: validate.SubscriptionMaybe,
                    paymentType: validate.PaymentType));

                // Mark transaction id as used.

                // Persist (before marking the transaction as used), to make sure the player doesn't lose their IAP in case of crash.
                await PersistStateIntermediate();

                // Store IAP in database, with deduplication id as the key, thus marking the transaction as used.
                // \note Subscriptions have special behavior (see above): duplicate purchases _are_ allowed, so we upsert those.
                MetaSerialized<InAppPurchaseEvent>  serializedEvent             = MetaSerialization.ToMetaSerialized<InAppPurchaseEvent>(pendingEv, MetaSerializationFlags.IncludeAll, logicVersion: null);
                PersistedInAppPurchase              persistedInApp              = new PersistedInAppPurchase(transactionDeduplicationId, serializedEvent, isValidReceipt: true, _entityId, DateTime.UtcNow);
                // \todo [petri] catch exceptions from insert to catch insert races (Q: what's the already-exists exception?)
                _log.Debug("Storing IAP transaction {TransactionDeduplicationId} as used", transactionDeduplicationId);
                if (txnInfo.ProductType == InAppProductType.Subscription)
                    await MetaDatabase.Get().InsertOrUpdateAsync(persistedInApp);
                else
                    await MetaDatabase.Get().InsertAsync(persistedInApp);
            }
            else if (validate.Result == InAppPurchaseValidationResult.Invalid)
            {
                Model.EventStream.Event(new PlayerEventInAppValidationComplete(PlayerEventInAppValidationComplete.ValidationResult.Invalid, pendingEv.ProductId, pendingEv.Platform, pendingEv.TransactionId, pendingEv.OrderId, pendingEv.PlatformProductId, pendingEv.ReferencePrice, pendingEv.GameProductAnalyticsId, paymentType: null, pendingEv.TryGetGamePurchaseAnalyticsContext()));

                // Update state according to response.
                ExecuteServerActionAfterPendingActions(PlayerInAppPurchaseValidated.ForFailure(txnInfo.TransactionId, InAppPurchaseStatus.InvalidReceipt, isDuplicateTransaction: false));

                // \note We don't mark the transaction as used, since the purchase was invalid.
                //       We don't want to block possible future valid purchases,
                //       even if they have the same transaction id as an invalid purchase.
            }
            else if (validate.Result == InAppPurchaseValidationResult.TransientError)
            {
                // Just keep a (server-only) count of transient errors for debugging etc.
                // Don't send anything to the client; as far as it knows, the purchase is simply still pending.
                pendingEv.NumValidationTransientErrors++;
            }
            else
                _log.Warning("Unknown IAP validation result {Result}", validate.Result);
        }

        #region Player Scheduled Deletion

        /// <summary>
        // Client requests to schedule deletion of their player
        /// </summary>
        [MessageHandler]
        async Task HandlePlayerScheduleDeletionRequest(PlayerScheduleDeletionRequest _)
        {
            // Client may request new deletion when previous is in progress and that resets the timer
            if (Model.DeletionStatus == PlayerDeletionStatus.Deleted)
                return;

            SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
            MetaTime deletionTime = MetaTime.Now + MetaDuration.FromTimeSpan(systemOpts.PlayerDeletionDefaultDelay);
            await ChangeDeletionStatusAsync(deletionTime, targetStatus: PlayerDeletionStatus.ScheduledByUser, "Player");
        }

        /// <summary>
        // Client requests to unschedule deletion of their player
        /// </summary>
        [MessageHandler]
        async Task HandlePlayerCancelScheduledDeletionRequest(PlayerCancelScheduledDeletionRequest _)
        {
            // Client can only cancel if deletion is scheduled
            if (!Model.DeletionStatus.IsScheduled())
                return;

            await ChangeDeletionStatusAsync(null, targetStatus: PlayerDeletionStatus.None, "Player");
        }

        /// <summary>
        // Client requests to edit deletion schedule
        /// </summary>
        [EntityAskHandler]
        async Task<InternalPlayerScheduleDeletionResponse> HandleInternalPlayerScheduleDeletionRequest(InternalPlayerScheduleDeletionRequest request)
        {
            // State change not allowed if player is already deleted
            if (Model.DeletionStatus == PlayerDeletionStatus.Deleted)
                return InternalPlayerScheduleDeletionResponse.Instance;

            await ChangeDeletionStatusAsync(request.ScheduledAt, targetStatus: PlayerDeletionStatus.ScheduledByAdmin, request.Source);
            return InternalPlayerScheduleDeletionResponse.Instance;
        }

        /// <summary>
        /// Update deletion status of the player and auxiliary tables. When the scheduled deletion state of a player changes, we need to write it to
        /// the scheduled deletion log.
        /// </summary>
        async Task ChangeDeletionStatusAsync(MetaTime? deletionTime, PlayerDeletionStatus targetStatus, string source)
        {
            // Update state
            if (deletionTime != null)
                ExecuteServerActionImmediately(new PlayerSetIsScheduledForDeletionAt(deletionTime.Value, scheduledBy: targetStatus));
            else
                ExecuteServerActionImmediately(new PlayerSetUnscheduledForDeletion());

            // Update auxiliary tables
            MetaDatabase db = MetaDatabase.Get();
            bool recordExists = (await db.TryGetAsync<PersistedPlayerDeletionRecord>(_entityId.ToString()) != null);
            if (deletionTime != null)
            {
                // Deletion scheduled. Upsert record to the redeletion database
                PersistedPlayerDeletionRecord record = new PersistedPlayerDeletionRecord(_entityId, deletionTime.Value, source);
                if (recordExists)
                    await db.UpdateAsync<PersistedPlayerDeletionRecord>(record);
                else
                    await db.InsertAsync<PersistedPlayerDeletionRecord>(record);

                // Call the clean-up hook
                await OnPlayerScheduledForDeletion();
            }
            else
            {
                // Deletion unscheduled. Remove record from redeletion database
                if (recordExists)
                    await db.RemoveAsync<PersistedPlayerDeletionRecord>(_entityId.ToString());
            }

            await PersistStateIntermediate();
        }

        /// <summary>
        /// Handle a player deletion. The player must be in a state where deletion
        /// is scheduled to happen or else this request will fail. Called by the background
        /// job.
        /// </summary>
        [EntityAskHandler]
        async Task<PlayerCompleteScheduledDeletionResponse> HandlePlayerCompleteScheduledDeletionRequest(PlayerCompleteScheduledDeletionRequest _)
        {
            _log.Info("About to delete player..");
            if (!Model.DeletionStatus.IsScheduled())
            {
                _log.Warning("Failed to delete, player in wrong state: {Status}", Model.DeletionStatus);
                return new PlayerCompleteScheduledDeletionResponse(false);
            }
            else if (Model.ScheduledForDeletionAt > MetaTime.Now)
            {
                _log.Warning("Failed to delete, scheduled time not reached: {ScheduledTime}", Model.ScheduledForDeletionAt);
                return new PlayerCompleteScheduledDeletionResponse(false);
            }
            else
            {
                // Remove all auth and kick player so that they are not connected and cannot reconnect
                await RemoveAllAuthenticationMethodsAsync();
                KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);

                // Let the game can do clean up
                _log.Info("Calling player-being-deleted clean up hook");
                await OnPlayerBeingDeleted();

                // Emit event to mark player deleted also in the analytics data.
                Model.EventStream.Event(new PlayerEventDeleted());

                // Reset by creating a new PlayerModel
                TModel newPlayerModel = CreateInitialModel(CreateModelLogChannel("player-deletion"), RandomNewPlayerName());

                // Flag player as deleted (and let's remember the original scheduled deletion time too)
                newPlayerModel.DeletionStatus = PlayerDeletionStatus.Deleted;
                newPlayerModel.ScheduledForDeletionAt = Model.ScheduledForDeletionAt;

                // Clean up data dependencies and switch to model
                await CleanupInternalAndExternalStateOnAdminIssuedStateResetAndSwitchTo(newPlayerModel);

                _log.Info("Player deleted sucessfully");
                return new PlayerCompleteScheduledDeletionResponse(true);
            }
        }

        #endregion

        [MessageHandler]
        async Task HandleTriggerConfirmDynamicPurchaseContent(TriggerConfirmDynamicPurchaseContent confirmPurchase)
        {
            await PersistStateIntermediate();
            // If we crash after this point, the content is confirmed when actor wakes up again. The status is fixed
            // upon deserialization, as the field is Transient and defaults to ConfirmedByServer.
            ExecuteServerActionImmediately(new PlayerConfirmPendingDynamicPurchaseContent(confirmPurchase.ProductId));
        }

        [MessageHandler]
        async Task HandleTriggerConfirmStaticPurchaseContext(TriggerConfirmStaticPurchaseContext confirmPurchaseContext)
        {
            await PersistStateIntermediate();
            // If we crash after this point, the content is confirmed when actor wakes up again. The status is fixed
            // upon deserialization, as the field is Transient and defaults to ConfirmedByServer.
            ExecuteServerActionImmediately(new PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext(confirmPurchaseContext.ProductId));
        }

        [MessageHandler]
        async Task HandleTriggerInAppPurchaseValidation(TriggerInAppPurchaseValidation trigger)
        {
            string validationKind = trigger.IsRetry ? "validation retry" : "initial validation";

            InAppPurchaseTransactionInfo txnInfo = trigger.TransactionInfo;
            _log.Debug("TriggerInAppPurchaseValidation for transaction {TransactionId}, productId={ProductId} ({ValidationKind})", txnInfo.TransactionId, txnInfo.ProductId, validationKind);

            if (!Model.PendingInAppPurchases.TryGetValue(txnInfo.TransactionId, out InAppPurchaseEvent pendingEv))
                _log.Warning("Got TriggerInAppPurchaseValidation but no such purchase ({TransactionId}) is pending ({ValidationKind})", txnInfo.TransactionId, validationKind);
            else if (pendingEv.Status != InAppPurchaseStatus.PendingValidation)
                _log.Warning("Got TriggerInAppPurchaseValidation but the purchase ({TransactionId}) has status {PurchaseStatus} instead of PendingValidation ({ValidationKind})", txnInfo.TransactionId, pendingEv.Status, validationKind);
            else
            {
                // Resolve id that should be used for the key of PersistedInAppPurchase
                // (i.e. the "deduplication id").
                string deduplicationId = InAppPurchaseTransactionIdUtil.ResolveTransactionDeduplicationId(txnInfo);

                // Check whether the receipt has already been used earlier.
                // Duplicate purchases are not validated, except for subscriptions.
                // Subscriptions have special behavior due to purchase restoration (see also HandleValidateInAppPurchaseResponse).
                PersistedInAppPurchase existingInApp = await MetaDatabase.Get().TryGetAsync<PersistedInAppPurchase>(deduplicationId);
                if (existingInApp == null || trigger.TransactionInfo.ProductType == InAppProductType.Subscription)
                {
                    // Previously unseen receipt, or subscription. Now it needs to be validated.

                    if (txnInfo.HasMissingContent)
                    {
                        // This is a purchase with missing dynamic, and we only wanted to check if it's a duplicate purchase
                        // We've now checked, and it's not a duplicate; the missing content is not explained by it being
                        // a duplicate purchase. Fail the purchase.
                        // See the comment on InAppPurchaseEvent.HasMissingContent for more information.
                        ExecuteServerActionAfterPendingActions(PlayerInAppPurchaseValidated.ForFailure(txnInfo.TransactionId, InAppPurchaseStatus.MissingContent, isDuplicateTransaction: false));
                        Model.EventStream.Event(new PlayerEventInAppValidationComplete(PlayerEventInAppValidationComplete.ValidationResult.MissingContent, pendingEv.ProductId, pendingEv.Platform, pendingEv.TransactionId, pendingEv.OrderId, pendingEv.PlatformProductId, pendingEv.ReferencePrice, pendingEv.GameProductAnalyticsId, paymentType: null, pendingEv.TryGetGamePurchaseAnalyticsContext()));
                        c_inAppMissingContents.Inc();
                    }
                    else
                    {
                        // Let validator check that the receipt is actually legit
                        _log.Debug("Validating transaction {TransactionDeduplicationId}, isDuplicate={IsDuplicate} ({ValidationKind})..", deduplicationId, existingInApp != null, validationKind);
                        CastMessage(GetAssociatedServiceEntityId(EntityKindCloudCore.InAppValidator), new ValidateInAppPurchaseRequest(txnInfo));

                        if (trigger.IsRetry)
                            c_inAppValidateRetryRequests.Inc();
                        else
                            c_inAppValidateInitialRequests.Inc();

                        pendingEv.NumValidationsStarted++;
                    }
                }
                else
                {
                    // Receipt already used before!
                    if (Model.GameConfig.InAppProducts.TryGetValue(txnInfo.ProductId, out InAppProductInfoBase productInfo))
                    {
                        _log.Info("Duplicate IAP transaction {TransactionDeduplicationId}: type={ProductType}, isValidReceipt={IsValidReceipt} ({ValidationKind})", deduplicationId, productInfo.Type, existingInApp.IsValidReceipt, validationKind);
                        c_inAppValidateDuplicates.WithLabels(productInfo.Type.ToString()).Inc();

                        Model.EventStream.Event(new PlayerEventInAppValidationComplete(PlayerEventInAppValidationComplete.ValidationResult.Duplicate, pendingEv.ProductId, pendingEv.Platform, pendingEv.TransactionId, pendingEv.OrderId, pendingEv.PlatformProductId, pendingEv.ReferencePrice, pendingEv.GameProductAnalyticsId, paymentType: null, pendingEv.TryGetGamePurchaseAnalyticsContext()));

                        switch (productInfo.Type)
                        {
                            case InAppProductType.Consumable:
                                // Duplicate purchase of consumable IAP results in failure
                                ExecuteServerActionAfterPendingActions(PlayerInAppPurchaseValidated.ForFailure(txnInfo.TransactionId, InAppPurchaseStatus.ReceiptAlreadyUsed, isDuplicateTransaction: true));
                                break;

                            case InAppProductType.NonConsumable:
                                // Non-consumable success depends on whether the receipt itself was valid & belongs to this player
                                bool                isSuccess   = existingInApp.IsValidReceipt && (existingInApp.PlayerEntityId == _entityId);
                                InAppPurchaseStatus status      = isSuccess ? InAppPurchaseStatus.ValidReceipt : InAppPurchaseStatus.InvalidReceipt;
                                ExecuteServerActionAfterPendingActions(new PlayerInAppPurchaseValidated(
                                    transactionId: txnInfo.TransactionId,
                                    status: status,
                                    isDuplicateTransaction: true,
                                    orderId: null,
                                    originalTransactionId: null,
                                    subscription: null,
                                    paymentType: null));
                                break;

                            case InAppProductType.Subscription:
                                _log.Error("Didn't expect Subscription validation flow to reach this code path - even duplicate subscriptions are meant to be validated.");
                                break;

                            default:
                                _log.Warning("Invalid InAppProductType {IAPProductType} for product {IAPProductId}", productInfo.Type, txnInfo.ProductId);
                                break;
                        }
                    }
                    else
                        _log.Error("Unable to find IAP ProductInfo for {ProductId}", txnInfo.ProductId);
                }
            }
        }

        #region Incident Reports

        /// <summary>
        /// Client has some player incident reports available. Figure out which ones are of interest and
        /// should be uploaded to server.
        /// </summary>
        /// <param name="fromEntityId"></param>
        /// <param name="msg"></param>
        [MessageHandler]
        void HandlePlayerAvailableIncidentReports(EntityId fromEntityId, PlayerAvailableIncidentReports msg)
        {
            // Resolve all incidentIds that we are interested in
            // \todo [petri] collect statistics about all incidents (even the ones we don't want uploaded)
            List<string> uploadIncidentIds = new List<string>();
            foreach (ClientAvailableIncidentReport header in msg.IncidentHeaders)
            {
                // Compute fingerprint (not used yet)
                // \todo [petri] if already have enough of this incident type, just accumulate count (further reports likely not useful)
                //string reason = PlayerIncidentUtil.TruncateReason(header.Reason);
                //string fingerprint = PlayerIncidentUtil.ComputeFingerprint(header.Type, header.SubType, reason);

                // Check if the report is of interest, and we should ask client to upload it
                int uploadPercentage = PlayerIncidentStorage.GetUploadPercentage(_log, header);
                int roll = (int)((ulong)MiniMD5.ComputeMiniMD5(header.IncidentId) * 100 >> 32);
                bool shouldUpload = roll < uploadPercentage;

                if (shouldUpload)
                    uploadIncidentIds.Add(header.IncidentId);
            }

            // Request client to upload incidents of interest, even if there were none.
            CastMessage(fromEntityId, new PlayerRequestIncidentReportUploads(uploadIncidentIds));
        }

        [MessageHandler]
        async Task HandlePlayerUploadIncidentReport(EntityId fromEntityId, PlayerUploadIncidentReport upload)
        {
            try
            {
                // Uncompress & decode the payload
                PlayerIncidentReport report = PlayerIncidentUtil.DecompressNetworkDeliveredIncident(upload.Payload, out int uncompressedPayloadSize);
                _log.Debug("Received incident report: {IncidentReport} ({CompressedSize} bytes compressed, {UncompressedSize} bytes uncompressed)", report.GetType().ToGenericTypeString(), upload.Payload.Length, uncompressedPayloadSize);

                // Persist
                await PlayerIncidentStorage.PersistIncidentAsync(_entityId, report, upload.Payload);

                string reason = PlayerIncidentUtil.TruncateReason(report.GetReason());
                string fingerprint = PlayerIncidentUtil.ComputeFingerprint(report.Type, report.SubType, reason);

                // Analytics event
                Model.EventStream.Event(new PlayerEventIncidentRecorded(report.IncidentId, report.OccurredAt, report.Type, report.SubType, reason, fingerprint));

                // Inform integration
                OnPlayerIncidentRecorded(report.IncidentId, report.Type, report.SubType, reason);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to persist PlayerIncidentReport: {Exception}", ex);
            }

            // Ack report, so client can delete it
            // \note Report is acked even if it wasn't accepted, so client won't keep sending it again
            CastMessage(fromEntityId, new PlayerAckIncidentReportUpload(upload.IncidentId));
        }

        [EntityAskHandler]
        EntityAskOk HandleNewPlayerIncidentRecorded(InternalNewPlayerIncidentRecorded incident)
        {
            // Player incident report has been persisted outside of PlayerActor

            string fingerprint = PlayerIncidentUtil.ComputeFingerprint(incident.Type, incident.SubType, incident.Reason);

            // Analytics event
            Model.EventStream.Event(new PlayerEventIncidentRecorded(incident.IncidentId, incident.OccurredAt, incident.Type, incident.SubType, incident.Reason, fingerprint));

            // Inform integration
            OnPlayerIncidentRecorded(incident.IncidentId, incident.Type, incident.SubType, incident.Reason);

            return EntityAskOk.Instance;
        }

        /// <summary>
        /// PlayerActor hook for when a new player incident report has been recorded. Will also be called when the incident
        /// report is received as part of a rejected session start.
        /// </summary>
        protected virtual void OnPlayerIncidentRecorded(string incidentId, string type, string subType, string reason) { }

        #endregion

        async Task CleanupInternalAndExternalStateOnAdminIssuedStateResetAndSwitchTo(TModel playerModel)
        {
            // Kick player is if is still online
            // \note: this completes and clears _pendingSynchronizedServerActions and _pendingUnsynchronizedServerActions
            KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);

            // Delete event log segments
            await RemoveAllOwnedEventLogSegmentsAsync();

            #if !METAPLAY_DISABLE_GUILDS
            // Leave the old guild if we are removed from it:
            // * If the guild is the same, no action. This allows state rollbacks for development
            // * If we were in a certain guild and no longer are, leave it to keep the guild's state consistent.
            // * If we become a member of a new guild, don't do anything. This will be sorted out in session start.
            // This is admin action and we are losing state, so we can force leave.
            if (Guilds != null && Model.GuildState.GuildId != EntityId.None && Model.GuildState.GuildId != playerModel.GuildState.GuildId)
            {
                // forced leave, no need to care of the response
                _ = Guilds.LeaveGuildAsync(forceLeave: true);
            }
            #endif

            if (MetaplayCore.Options.FeatureFlags.EnablePlayerLeagues)
            {
                try
                {
                    if (Model.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionClientStateBase) &&
                        playerDivisionClientStateBase is IDivisionClientState divisionClientState && divisionClientState.CurrentDivision != EntityId.None)
                    {
                        EntityId leagueId = EntityId.Create(EntityKindCloudCore.LeagueManager, 0);
                        await EntityAskAsync<EntityAskOk>(leagueId, new InternalLeagueLeaveRequest(_entityId, false));
                    }
                }
                catch (EntityAskRefusal e)
                {
                    _log.Warning("Failed to leave league on player state reset. Reason: {Reason}", e.Message);
                }
            }

            // Update the configs, and switch the PlayerModel from Baseline to the Specialized config
            GameConfigSpecializationKey specializationKey = CreateSpecializationKeyForServer(_activeGameConfig, playerModel.Experiments);
            _specializedGameConfigKey = specializationKey;
            _specializedGameConfig = _activeGameConfig.GetSpecializedGameConfig(specializationKey);
            _specializedGameConfigResolver = GetConfigResolver(_specializedGameConfig);
            ActiveExperiments = GetInstanceActiveExperiment(_activeGameConfig, playerId: _entityId, playerModel.Experiments);

            // \todo: the logchannel gets overwriten immediately anyway, get rid here and elsewhere
            playerModel = CreateClonedModel(playerModel, CreateModelLogChannel("player-admin-state-reset-clone"));

            // Set the new player model and persist it
            SwitchToNewModelImmediately(playerModel, elapsedAsPersisted: TimeSpan.Zero);
            await PersistStateIntermediate();
        }

        [MessageHandler]
        async Task HandlePlayerResetState(PlayerResetState _)
        {
            _log.Warning("State reset requested!");

            // Create a new PlayerModel (only keep name)
            TModel playerModel = CreateInitialModel(CreateModelLogChannel("player-reset"), Model.PlayerName);
            playerModel.ImportAfterReset(Model);

            // Clean up data dependencies and switch to model
            await CleanupInternalAndExternalStateOnAdminIssuedStateResetAndSwitchTo(playerModel);
        }

        [EntityAskHandler]
        PlayerRemovePushNotificationTokenResponse HandlePlayerRemovePushNotificationTokenRequest(PlayerRemovePushNotificationTokenRequest request)
        {
            EnqueueServerAction(new PlayerServerCleanupRemoveFirebaseMessagingToken(request.FirebaseMessagingToken));
            return new PlayerRemovePushNotificationTokenResponse();
        }

        /// <summary>
        /// Sent when player is set as a developer via admin API. Player will be kicked.
        /// </summary>
        [MessageHandler]
        void HandleInternalPlayerDeveloperStatusChangedMessage(InternalPlayerDeveloperStatusChanged msg)
        {
            // Change model status Immediately
            Model.IsDeveloper = msg.IsDeveloper;

            KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);
        }

        /// <summary>
        /// Kicks the player client if they are connected
        /// </summary>
        protected void KickPlayerIfConnected(PlayerForceKickOwnerReason reason)
        {
            EntitySubscriber existingOwner = TryGetOwnerSession();
            if (existingOwner != null)
            {
                KickSubscriber(existingOwner, new PlayerForceKickOwner(reason));
            }
        }

        // \todo [petri] refactor: how to make generic?
        [EntityAskHandler]
        InternalEntityStateResponse HandlePlayerStateRequest(InternalEntityStateRequest _)
        {
            //throw new NotSupportedException("test crash in state request");

            return new InternalEntityStateResponse(
                model:                  MetaSerialization.ToMetaSerialized<IModel>(Model, MetaSerializationFlags.IncludeAll, Model.LogicVersion),
                logicVersion:           Model.LogicVersion,
                staticGameConfigId:     _activeGameConfig.BaselineStaticGameConfigId,
                dynamicGameConfigId:    _activeGameConfig.BaselineDynamicGameConfigId,
                specializationKey:      _specializedGameConfigKey);
        }

        /// <summary>
        /// Handle request from client to change the player's name
        /// </summary>
        /// <param name="fromEntityId"></param>
        /// <param name="request"></param>
        [MessageHandler]
        async Task HandlePlayerChangeOwnNameRequest(PlayerChangeOwnNameRequest request)
        {
            await ValidateAndChangePlayerName(PlayerEventNameChanged.ChangeSource.Player, request.NewName, validateOnly: false);
        }

        /// <summary>
        /// Attempt to change the player's name. Can be used to validate the change only (ie: validate but don't set)
        /// by passing true in request.ValidateOnly
        /// </summary>
        /// <param name="ask"></param>
        /// <param name="request"></param>
        [EntityAskHandler]
        async Task<PlayerChangeNameResponse> HandlePlayerChangeNameRequest(PlayerChangeNameRequest request)
        {
            bool nameValid = await ValidateAndChangePlayerName(PlayerEventNameChanged.ChangeSource.Admin, request.NewName, request.ValidateOnly);
            return new PlayerChangeNameResponse(nameWasValid: nameValid);
        }

        /// <summary>
        /// Validate a new player name and set it (using a ServerAction) if it is acceptable
        /// </summary>
        /// <param name="newName">The proposed new name</param>
        /// <param name="validateOnly">If set to true then the name change will not be executed, only validation of the name will occur</param>
        /// <returns>True is the name was valid</returns>
        public async Task<bool> ValidateAndChangePlayerName(PlayerEventNameChanged.ChangeSource source, string newName, bool validateOnly)
        {
            // Is the name valid?
            bool nameValid = IntegrationRegistry.Get<PlayerRequirementsValidator>().ValidatePlayerName(newName);

            // Execute the name change if we were requested to do so and if the name is valid and
            // does actually change, then immediately persist the player's state
            if (nameValid && !validateOnly && Model.PlayerName != newName)
            {
                string oldName = Model.PlayerName;
                ExecuteServerActionImmediately(new PlayerChangeName(newName));
                Model.EventStream.Event(new PlayerEventNameChanged(source, oldName, newName));
                await PersistStateIntermediate();
            }

            return nameValid;
        }

        /// <summary>
        /// Attempt to import model data to this player
        /// </summary>
        /// <param name="ask"></param>
        /// <param name="request"></param>
        [EntityAskHandler]
        async Task<PlayerImportModelDataResponse> HandlePlayerImportModelDataRequest(PlayerImportModelDataRequest request)
        {
            // Try to deserialise the incoming player model
            try
            {
                // Deserialize model
                TModel importModel = CreateModelFromSerialized(request.Payload, CreateModelLogChannel("player-import"));

                // Run base fixups
                importModel.RunPlayerModelBaseFixups();

                // Run migrations
                if (request.SchemaVersion.HasValue)
                    MigrateState(importModel, request.SchemaVersion.Value);

                // Imported model's bookkeeping must forget the persisted segments, otherwise we'd have to actually copy the source entity's persisted log segments as well!
                importModel.EventLog.ForgetAllPersistedSegments();

                // Tag import with timestamp
                importModel.Stats.LastImportAt = MetaTime.Now;

                // If this is an overwrite, copy data from the old model that should be preserved.
                // If this is a import-into-new-entity, we don't copy anything but may still need to reset some state.
                if (!request.IsNewEntity)
                    importModel.ImportAfterOverwrite(Model);
                else
                    importModel.ImportAfterCreateNew(Model);

                // Clean up data dependencies and switch to model
                // \note: this call persists the model
                await CleanupInternalAndExternalStateOnAdminIssuedStateResetAndSwitchTo(importModel);

                return new PlayerImportModelDataResponse(true, null);
            }
            catch (Exception ex)
            {
                return new PlayerImportModelDataResponse(false, ex.ToString());
            }
        }

        /// <summary>
        /// Handle request that the player immediately persists their state to the database
        /// </summary>
        /// <param name="ask"></param>
        /// <param name="_"></param>
        /// <returns></returns>
        [EntityAskHandler]
        async Task<PersistStateRequestResponse> HandlePersistStateRequestRequest(PersistStateRequestRequest _)
        {
            await PersistStateIntermediate();
            return PersistStateRequestResponse.Instance;
        }

        /// <summary>
        /// A purchase has become refunded. Mark the purcahse as refunded, and optionally revoke
        /// the resources obtained in the purchase.
        /// </summary>
        [EntityAskHandler]
        async Task<PlayerRefundPurchaseResponse> HandlePlayerRefundPurchaseRequest(PlayerRefundPurchaseRequest request)
        {
            InAppPurchaseEvent iap = Model.InAppPurchaseHistory.Find(ev => ev.TransactionId == request.TransactionId);
            if (iap != null && iap.Status == InAppPurchaseStatus.ValidReceipt)
            {
                // mark IAP as refunded
                iap.Status = InAppPurchaseStatus.Refunded;

                // TODO: remove obtained resources

                // persist immediately
                await PersistStateIntermediate();
            }

            return PlayerRefundPurchaseResponse.Instance;
        }

        /// <summary>
        /// Manually forcibly assigns player into certain experiment and experiment group in it.
        /// </summary>
        [EntityAskHandler]
        async Task<InternalPlayerSetExperimentGroupResponse> HandleInternalPlayerSetExperimentGroupRequest(InternalPlayerSetExperimentGroupRequest request)
        {
            // Inform Proxy of group assignment
            bool wasRemovedFromGroup;
            PlayerExperimentsState.ExperimentAssignment removedFromGroupAssingnment;
            wasRemovedFromGroup = Model.Experiments.ExperimentGroupAssignment.TryGetValue(request.PlayerExperimentId, out removedFromGroupAssingnment);
            GlobalStateProxyActor.PlayerAssignmentIntoExperimentChanged(_entityId, request.PlayerExperimentId, addedIntoGroupId: request.VariantId, wasRemovedFromGroup, wasRemovedFromGroup ? removedFromGroupAssingnment.VariantId : null);
            StatsCollectorProxy.PlayerAssignmentIntoExperimentChanged(_entityId, request.PlayerExperimentId, addedIntoGroupId: request.VariantId, wasRemovedFromGroup, wasRemovedFromGroup ? removedFromGroupAssingnment.VariantId : null);

            // Set player into the experiment group
            Model.Experiments.ExperimentGroupAssignment[request.PlayerExperimentId] = new PlayerExperimentsState.ExperimentAssignment(request.VariantId);

            // Kick player if it were online.
            KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);

            // Load latests config from GSP and reload config and model with the config
            _activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            _baselineGameConfig = _activeGameConfig.BaselineGameConfig;
            _baselineGameConfigResolver = GetConfigResolver(_baselineGameConfig);

            GameConfigSpecializationKey specializationKey = CreateSpecializationKeyForServer(_activeGameConfig, Model.Experiments);
            _specializedGameConfigKey = specializationKey;
            _specializedGameConfig = _activeGameConfig.GetSpecializedGameConfig(specializationKey);
            _specializedGameConfigResolver = GetConfigResolver(_specializedGameConfig);
            ActiveExperiments = GetInstanceActiveExperiment(_activeGameConfig, playerId: _entityId, Model.Experiments);

            // Clone via serialization to ensure all references are up-to-date
            // \note: elapsedAsPersisted is set to zero since we will FastForward manually just after this.
            SwitchToNewModelImmediately(CreateClonedModel(Model, CreateModelLogChannel("player-force-set-assignment-change-clone")), elapsedAsPersisted: TimeSpan.Zero);

            (string experimentAid, string variantAid) = TryGetExperimentAndVariantAnalyticsIds(_activeGameConfig, request.PlayerExperimentId, request.VariantId);
            Model.EventStream.Event(new PlayerEventExperimentAssignment(PlayerEventExperimentAssignment.ChangeSource.Admin, request.PlayerExperimentId, request.VariantId, experimentAid, variantAid));
            await PersistStateIntermediate();

            // Let caller know if we have reached the desired GSM version or do we need to wait.
            bool isWaitingForTesterEpochUpdate;
            if (_activeGameConfig.ExperimentTesterEpochs.TryGetValue(request.PlayerExperimentId, out uint experimentTesterEpoch))
                isWaitingForTesterEpochUpdate = request.TesterEpoch > experimentTesterEpoch;
            else
                isWaitingForTesterEpochUpdate = true;

            return new InternalPlayerSetExperimentGroupResponse(isWaitingForTesterEpochUpdate);
        }

        /// <summary>
        /// Checks if GSM is up-to-date enough, and if so, switches to it. Used after InternalPlayerSetExperimentGroupRequest to wait for changes to apply.
        /// </summary>
        [EntityAskHandler]
        InternalPlayerSetExperimentGroupWaitResponse HandleInternalPlayerSetExperimentGroupWaitRequest(InternalPlayerSetExperimentGroupWaitRequest request)
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();

            // Check if we have reached the desired GSM version or do we need to wait.
            bool isWaitingForTesterEpochUpdate;
            if (activeGameConfig.ExperimentTesterEpochs.TryGetValue(request.PlayerExperimentId, out uint experimentTesterEpoch))
                isWaitingForTesterEpochUpdate = request.TesterEpoch > experimentTesterEpoch;
            else
                isWaitingForTesterEpochUpdate = true;

            if (!isWaitingForTesterEpochUpdate)
            {
                // Wait is over, switch to the new config.

                // Kick player if it were online
                KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);

                // Load latests config from GSP and reload config and model with the config
                _activeGameConfig = activeGameConfig;
                _baselineGameConfig = _activeGameConfig.BaselineGameConfig;
                _baselineGameConfigResolver = GetConfigResolver(_baselineGameConfig);

                GameConfigSpecializationKey specializationKey = CreateSpecializationKeyForServer(_activeGameConfig, Model.Experiments);
                _specializedGameConfigKey = specializationKey;
                _specializedGameConfig = _activeGameConfig.GetSpecializedGameConfig(specializationKey);
                _specializedGameConfigResolver = GetConfigResolver(_specializedGameConfig);
                ActiveExperiments = GetInstanceActiveExperiment(_activeGameConfig, playerId: _entityId, Model.Experiments);

                // Clone via serialization to ensure all references are up-to-date
                // \note: elapsedAsPersisted is set to zero since we will FastForward manually just after this.
                SwitchToNewModelImmediately(CreateClonedModel(Model, CreateModelLogChannel("player-force-set-assignment-change-wait-clone")), elapsedAsPersisted: TimeSpan.Zero);
            }

            return new InternalPlayerSetExperimentGroupWaitResponse(isWaitingForTesterEpochUpdate);
        }

        [EntityAskHandler]
        InternalPlayerGetPlayerExperimentDetailsResponse HandleInternalPlayerGetPlayerExperimentDetailsRequest(InternalPlayerGetPlayerExperimentDetailsRequest request)
        {
            OrderedDictionary<PlayerExperimentId, PlayerExperimentDetails> details = new OrderedDictionary<PlayerExperimentId, PlayerExperimentDetails>();

            // First all in GameConfig, in game config order. This keeps the results relevant and in stable order.
            foreach ((PlayerExperimentId experimentId, PlayerExperimentGlobalState experimentState) in _activeGameConfig.AllExperimentsInConfig)
            {
                bool                                        isPlayerTester      = experimentState.TesterPlayerIds.Contains(_entityId);

                bool                                        isPlayerEnrolled;
                ExperimentVariantId                         enrolledVariant;
                PlayerExperimentDetails.NotActiveReason?    whyNotActive;
                PlayerExperimentDetails.NotEligibleReason?  whyNotEligible;

                if (Model.Experiments.ExperimentGroupAssignment.TryGetValue(experimentId, out PlayerExperimentsState.ExperimentAssignment assignment))
                {
                    isPlayerEnrolled    = true;
                    enrolledVariant     = assignment.VariantId;
                    whyNotEligible      = null;

                    if (experimentState.LifecyclePhase == PlayerExperimentPhase.Concluded)
                        whyNotActive = PlayerExperimentDetails.NotActiveReason.ExperimentIsConcluded;
                    else if (experimentState.IsConfigMissing || !experimentState.HasAnyNonControlVariantActive())
                        whyNotActive = PlayerExperimentDetails.NotActiveReason.ExperimentIsInvalid;
                    else if (experimentState.LifecyclePhase == PlayerExperimentPhase.Paused && !isPlayerTester)
                        whyNotActive = PlayerExperimentDetails.NotActiveReason.ExperimentIsPausedForNonTester;
                    else if (experimentState.LifecyclePhase == PlayerExperimentPhase.Testing && !isPlayerTester)
                        whyNotActive = PlayerExperimentDetails.NotActiveReason.ExperimentIsInTestingPhaseForNonTester;
                    else if (assignment.VariantId != null)
                    {
                        if (!experimentState.Variants.TryGetValue(assignment.VariantId, out var variantState))
                            whyNotActive = PlayerExperimentDetails.NotActiveReason.VariantIsUnknown;
                        else if (!variantState.IsActive())
                            whyNotActive = PlayerExperimentDetails.NotActiveReason.VariantIsDisabled;
                        else
                            whyNotActive = null;
                    }
                    else
                        whyNotActive = null;
                }
                else
                {
                    isPlayerEnrolled = false;
                    enrolledVariant = null;
                    whyNotActive = PlayerExperimentDetails.NotActiveReason.PlayerIsNotEnrolled;

                    if (experimentState.LifecyclePhase == PlayerExperimentPhase.Concluded)
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.ExperimentIsConcluded;
                    else if (experimentState.IsConfigMissing || !experimentState.HasAnyNonControlVariantActive())
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.ExperimentIsInvalid;
                    else if (!_activeGameConfig.VisibleExperimentsForTesters.TryGetValue(experimentId, out PlayerExperimentAssignmentPolicy policyForTesters))
                    {
                        // \note: we used -ForTesters as it is a superset of -ForPlayers
                        // \note: this branch should never be taken, but let's handle it anyway
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.ExperimentIsMissing;
                    }
                    else if (experimentState.IsRolloutDisabled)
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.RolloutDisabled;
                    else if (policyForTesters.IsCapacityReached)
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.CapacityReached;
                    else if (!policyForTesters.IsPlayerInExperimentSamplePopulation(_entityId))
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.NotInRolloutRatio;
                    else if (!Model.PassesFilter(policyForTesters.EligibilityFilter, out bool _))
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.NotInTargetSegments;
                    else if (policyForTesters.EnrollOnlyNewPlayers)
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.NewPlayersOnly;
                    else if (policyForTesters.IsOnlyForTester && !isPlayerTester)
                        whyNotEligible = PlayerExperimentDetails.NotEligibleReason.ForTestersOnly;
                    else
                        whyNotEligible = null;
                }

                details.Add(experimentId, new PlayerExperimentDetails(
                    experimentExists:       true,
                    experimentPhase:        experimentState.LifecyclePhase,
                    isPlayerTester:         isPlayerTester,
                    isPlayerEnrolled:       isPlayerEnrolled,
                    enrolledVariant:        enrolledVariant,
                    whyNotActive:           whyNotActive,
                    whyNotEligible:         whyNotEligible));
            }

            // Then any extra from player state. This can happen if assigned experiments are deleted.
            foreach ((PlayerExperimentId experimentId, PlayerExperimentsState.ExperimentAssignment assignment) in Model.Experiments.ExperimentGroupAssignment)
            {
                if (details.ContainsKey(experimentId))
                    continue;

                details.Add(experimentId, new PlayerExperimentDetails(
                    experimentExists:   false,
                    experimentPhase:    PlayerExperimentPhase.Concluded,
                    isPlayerTester:     false,
                    isPlayerEnrolled:   true,
                    enrolledVariant:    assignment.VariantId,
                    whyNotActive:       PlayerExperimentDetails.NotActiveReason.ExperimentIsMissing,
                    whyNotEligible:     PlayerExperimentDetails.NotEligibleReason.ExperimentIsMissing));
            }

            return new InternalPlayerGetPlayerExperimentDetailsResponse(details);
        }

        [MessageHandler]
        async Task HandleTriggerPlayerBanFlagChanged(TriggerPlayerBanFlagChanged _)
        {
            try
            {
                await OnPlayerBanStateChanged(Model.IsBanned);
            }
            catch (Exception ex)
            {
                _log.Error("OnPlayerBanStateChanged failed with {Error}", ex);
            }

            // Save state now that handlers are run.
            await PersistStateIntermediate();
        }

        [MessageHandler]
        void HandleInternalPlayerForceActivablePhaseRequest(InternalPlayerForceActivablePhaseMessage message)
        {
            KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);
            ExecuteServerActionAfterPendingActions(new PlayerServerDebugForceSetActivablePhase(message.KindId, message.ActivableIdStr, message.Phase));
        }


        // \note: copy-paste from PersistedMultiplayerEntityActor
        [EntityAskHandler]
        async Task<EntityAskOk> HandleInternalEntityAssociatedEntityRefusedRequest(EntityId sessionId, InternalEntityAssociatedEntityRefusedRequest request)
        {
            _log.Debug("Handling InternalEntityAssociatedEntityRefusedRequest from {Entity} with {ReasonType}.", request.AssociationRef.AssociatedEntity, request.Refusal.GetType().ToGenericTypeString());

            EntityId playerId = SessionIdUtil.ToPlayerId(sessionId);

            // Filter out stale requests already
            foreach (AssociatedEntityRefBase currentAssociation in GetSessionStartAssociatedEntities())
            {
                if (currentAssociation.AssociatedEntity != request.AssociationRef.AssociatedEntity)
                    continue;

                // \todo: check MemberInstanceId. If request is less, request was stale. If more, desynced and need to remove. For equal need to remove.

                bool handled = await OnAssociatedEntityRefusalAsync(playerId, request.AssociationRef, request.Refusal);
                if (!handled)
                    _log.Warning("Unhandled association refusal from {Entity}. Actor.OnAssociatedEntityRefusalAsync", request.AssociationRef.AssociatedEntity);

                await PersistStateIntermediate();
                return EntityAskOk.Instance;
            }

            _log.Warning("Ignoring stale InternalEntityAssociatedEntityRefusedRequest from {Entity}. There was no such entity associated.", request.AssociationRef.AssociatedEntity);
            return EntityAskOk.Instance;
        }

        /// <summary>
        /// Sends push notification to all registered devices.
        /// </summary>
        /// <param name="data">Optional Firebase key value pairs delivered the the client app when push notification is tapped.</param>
        protected void SendPushNotification(string title, string body, Dictionary<string, string> data = null)
        {
            IEnumerable<string> tokens = Model.GetAllFirebaseMessagingTokens();

            if (tokens.Any())
                CastMessage(GetAssociatedServiceEntityId(EntityKindCloudCore.PushNotifier), new SendPushNotification(_entityId, tokens, title, body, data));
            else
                _log.Debug("Trying to send push notification, but no registered device tokens found");
        }

        /// <summary>
        /// Sends a message to the GlobalStateProxy to register this player as "active" in the active player lists
        /// </summary>
        void RegisterPlayerAsActive()
        {
            IActiveEntityInfo info = new ActivePlayerInfo(
                _entityId,
                Model.PlayerName,
                Model.Stats.CreatedAt,
                MetaTime.Now,
                Model.PlayerLevel,
                Model.DeletionStatus,
                Model.TotalIapSpend,
                Model.IsDeveloper
            );
            Context.System.EventStream.Publish(new ActiveEntityInfoEnvelope(info));
        }

        /// <summary>
        /// Returns the currently active version of the current Player's language.
        /// </summary>
        protected async Task<LocalizationLanguage> GetLocalizationLanguageAsync()
        {
            LanguageId activeLanguage = Model.Language;

            // try to use the client's localization version
            if (_activeLocalizationVersion != ContentHash.None)
            {
                LocalizationLanguage clientLocalization = await ServerConfigDataProvider.Instance.LocalizationLanguageProvider.GetAsync(activeLanguage, _activeLocalizationVersion);
                if (clientLocalization != null)
                    return clientLocalization;
            }

            // use latest language version
            ContentHash latestVersion = _activeLocalizationVersionSubscriber.Current.Versions.GetValueOrDefault(activeLanguage);
            if (latestVersion == ContentHash.None)
                throw new InvalidOperationException($"Cannot get current localization for language {activeLanguage}. No such language.");

            LocalizationLanguage latestLocalization = await ServerConfigDataProvider.Instance.LocalizationLanguageProvider.GetAsync(activeLanguage, latestVersion);
            if (latestLocalization != null)
                return latestLocalization;

            throw new InvalidOperationException($"Cannot get current localization for language {activeLanguage}. No localization for language.");
        }

        #region Server Actions

        [EntityAskHandler]
        InternalPlayerExecuteServerActionResponse HandleInternalPlayerExecuteServerActionRequest(InternalPlayerExecuteServerActionRequest executeAction)
        {
            PlayerActionBase action = executeAction.Action.Deserialize(_specializedGameConfigResolver, _logicVersion);
            _log.Info("Server action execute request: {Action}", action);
            ExecuteServerActionImmediately(action);
            return InternalPlayerExecuteServerActionResponse.Instance;
        }

        [EntityAskHandler]
        InternalPlayerEnqueueServerActionResponse HandleInternalPlayerEnqueueServerActionRequest(InternalPlayerEnqueueServerActionRequest enqueueAction)
        {
            PlayerActionBase action = enqueueAction.Action.Deserialize(_specializedGameConfigResolver, _logicVersion);
            _log.Info("Server action enqueue request: {Action}", action);
            EnqueueServerAction(action);
            return InternalPlayerEnqueueServerActionResponse.Instance;
        }

        [MessageHandler]
        void HandleInternalPlayerExecuteServerActionMessage(InternalPlayerExecuteServerActionMessage executeAction)
        {
            PlayerActionBase action = executeAction.Action.Deserialize(_specializedGameConfigResolver, _logicVersion);
            _log.Info("Server action execute request: {Action}", action);
            ExecuteServerActionImmediately(action);
        }

        [MessageHandler]
        void HandleInternalPlayerEnqueueServerActionMessage(InternalPlayerEnqueueServerActionMessage enqueueAction)
        {
            PlayerActionBase action = enqueueAction.Action.Deserialize(_specializedGameConfigResolver, _logicVersion);
            _log.Info("Server action enqueue request: {Action}", action);
            EnqueueServerAction(action);
        }

        /// <summary>
        /// Execute unsyncronized server action only after any and all pending actions and ticks have
        /// been committed. If there are pending actions, this will defer the execution of the action
        /// until the end of the flush. This guarantees that any ServerActions that are emitted as a
        /// side-effect of an Action cannot be executed by the client before the emitting Action is
        /// executed.
        /// <para>
        /// <b>Rule of thumb: </b> Use when this action is issued as a response to another Action, for
        /// example in a requestAction-responseAction pair, or if this action is otherwise dependent on
        /// previous state changes.
        /// </para>
        /// <para>
        /// <b>Flags:</b> The action should have <see cref="Metaplay.Core.Model.ModelActionExecuteFlags.FollowerUnsynchronized"/> flag set.
        /// </para>
        /// </summary>
        protected void ExecuteServerActionAfterPendingActions(PlayerActionBase action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // On server-side programming violation, print an error but continue. This is only detectable at runtime, so let's be nice.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerUnsynchronized))
            {
                _log.Error("PlayerActor.ExecuteServerActionImmediately called with Action {ActionType} which does not have FollowerUnsynchronized ExecuteFlags. ExecuteFlags={Flags}. Tolerating.", action.GetType().ToGenericTypeString(), actionSpec.ExecuteFlags);
                // continue
            }

            // \todo: ideally caller could specify the point in timeline after which Action becomes valid
            // \note: we might have pending operations (StagedPosition != CheckpointPosition) from earlier, throttled flush. We
            //        ignore that here, DoExecuteServerActionImmediately will flush it immediately anyway.
            if (_isHandlingActionFlush)
                _flushCompletionContinuations += () => ExecuteServerActionImmediately(action);
            else
                ExecuteServerActionImmediately(action);
        }

        /// <summary>
        /// Execute server action at any point in time. Notably client may execute
        /// the action arbitrary point in the past, even before the "current" action if such
        /// exists. Server will execute the action synchronously.
        /// <para>
        /// <b>Rule of thumb: </b> Use when this action is caused by external service or
        /// operation, or does not depend on a previous state set by the triggering action.
        /// For example, if the action only sets a new value to the state.
        /// </para>
        /// <para>
        /// <b>Flags:</b> The action should have <see cref="Metaplay.Core.Model.ModelActionExecuteFlags.FollowerUnsynchronized"/> flag set.
        /// </para>
        /// </summary>
        protected MetaActionResult ExecuteServerActionImmediately(PlayerActionBase action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // On server-side programming violation, print an error but continue. This is only detectable at runtime, so let's be nice.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerUnsynchronized))
            {
                _log.Error("PlayerActor.ExecuteServerActionImmediately called with Action {ActionType} which does not have FollowerUnsynchronized ExecuteFlags. ExecuteFlags={Flags}. Tolerating.", action.GetType().ToGenericTypeString(), actionSpec.ExecuteFlags);
                // continue
            }

            // Any pending flushes to keep Client-side event order always consistent with the server
            FlushActionBatch(forceFlushImmediately: true);

            int trackingId = _serverActionRunningTrackingId++;

            // Execute action immediately
            _log.Debug("Execute server action (on tick {Tick}): {Action}", Model.CurrentTick, PrettyPrint.Compact(action));

            MetaActionResult result = default;
            _playerJournal.ModifyHistory((JournalPosition position, IPlayerModelBase model) =>
            {
                result = ModelUtil.RunAction(model, action, NullChecksumEvaluator.Context);
            });

            if (result != MetaActionResult.Success)
            {
                // On failure, abort.
                _log.Warning("Failed to execute server action (on tick {CurrentTick}): result={Result}, {Action}", Model.CurrentTick, result.ToString(), PrettyPrint.Verbose(action));
            }
            else
            {
                // On success, send to client for execution
                EntitySubscriber session = TryGetOwnerSession();
                if (session != null)
                {
                    MetaSerialized<PlayerActionBase> serialized = MetaSerialization.ToMetaSerialized(action, MetaSerializationFlags.SendOverNetwork, _logicVersion);
                    SendMessage(session, new PlayerExecuteUnsynchronizedServerAction(serialized, trackingId));
                    _pendingUnsynchronizedServerActions.Add(new PendingUnsynchronizedServerActionState(action, trackingId));
                }
            }

            return result;
        }

        /// <summary>
        /// The duration within an action must be executed.
        /// </summary>
        public struct ActionDeadline
        {
            bool _constructed;
            public bool IsDefault => _constructed == false;

            internal int NumRelaxedSeconds;

            ActionDeadline(int numRelaxedSeconds)
            {
                _constructed = true;
                NumRelaxedSeconds = numRelaxedSeconds;
            }

            /// <summary>
            /// Creates a deadline of the given seconds using both the Time of the PlayerModel
            /// and the Wall-clock time.
            /// <para>
            /// The Model Time is essentially the clock managed by the Client. Due to network
            /// latency, this can be more relaxed than the corresponding time in the real-world
            /// wall-clock seconds. However, the very same network latency can cause the clock
            /// to be already delayed when this deadline is set. If the delay is now corrected,
            /// the deadline is effecively moved closer than intended.
            /// </para>
            /// <para>
            /// To handle these time variations, the relaxed time seconds sets two deadlines, one
            /// using model time and one using wall-clock time. To exceed the combined deadline, the
            /// client needs to exceed the both of the deadlines.
            /// </para>
            /// </summary>
            /// <remarks>
            /// Note that a malicious client cannot forge arbitrary timestamps and prevent or delay
            /// an action execution indefinitely. The client clock is validated to be within a tolerable
            /// range from server-observed clock. Violations will result in client being kicked.
            /// </remarks>
            public static ActionDeadline FromRelaxedTimeSeconds(int numSeconds) => new ActionDeadline(numSeconds);
        }

        /// <summary>
        /// Enqueues an action to be executed on the client. If the action is not executed by the client
        /// by the given deadline, the client is kicked and the action is executed by the server. If no client
        /// is present, the action is executed almost instantly but notably NOT synchronously, unless
        /// <paramref name="allowSynchronousExecution"/> is set.
        /// <para>
        /// The durability of the enqueued actions is the same as with the Model state itself. In the case of an
        /// unclean shutdown, entity state is rolled back up to the last successful <see cref="PersistState(bool, bool)"/>
        /// call. Identically, the set of enqueued but not yet executed server actions is rolled back to this same moment
        /// in time. This identical durability allows state of the pending actions to be tracked in the Model itself.
        /// </para>
        /// <para>
        /// In this case of a unclean shutdown, the enqueued actions are executed upon actor wakeup. Since no automatic wakeup is
        /// guaranteed, the execution of these actions may be arbitrarily delayed.
        /// </para>
        /// <para>
        /// <b>Flags:</b> The action should have <see cref="Metaplay.Core.Model.ModelActionExecuteFlags.FollowerSynchronized"/> flag set.
        /// </para>
        /// </summary>
        /// <param name="deadline">60 seconds (as observed by PlayerModel) by default</param>
        protected void EnqueueServerAction(PlayerActionBase action, ActionDeadline deadline = default, bool allowSynchronousExecution = false)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            if (allowSynchronousExecution && _isHandlingActionFlush)
                throw new InvalidOperationException("Action or Tick listener may not execute other actions synchronously. This would result in multiple overlapping actions. You should enqueue the new action instead.");

            // Default deadline to 60 seconds
            if (deadline.IsDefault)
                deadline = ActionDeadline.FromRelaxedTimeSeconds(60);

            // Server is allowed to execute any action regardless of the flags, but log a message if the declared usage differs from actual use.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerSynchronized))
            {
                if (actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerUnsynchronized))
                {
                    // Wrong flag. Warning-level
                    _log.Warning(
                        "PlayerActor.EnqueueServerAction called with Action {ActionType} which does not have FollowerSynchronized ExecuteFlags but has FollowerUnsynchronized. "
                        + "ExecuteFlags={Flags}. "
                        + "Use ExecuteServerActionImmediately to execute unsynchronized actions.",
                        action.GetType().ToGenericTypeString(), actionSpec.ExecuteFlags);
                }
                else
                {
                    // Missing flag. Debug-level
                    _log.Debug("PlayerActor.EnqueueServerAction called with Action {ActionType} which does not have FollowerSynchronized ExecuteFlags. ExecuteFlags={Flags}.", action.GetType().ToGenericTypeString(), actionSpec.ExecuteFlags);
                }
            }

            EntitySubscriber session = TryGetOwnerSession();
            if (session != null)
            {
                // There is a client. Register action and let client execute it.
                int trackingId = RegisterSynchronizedServerAction(action, deadline);
                MetaSerialized<PlayerActionBase> serializedAction = MetaSerialization.ToMetaSerialized(action, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
                SendMessage(session, new PlayerEnqueueSynchronizedServerAction(serializedAction, trackingId));
            }
            else if (allowSynchronousExecution && _pendingSynchronizedServerActions.Count == 0)
            {
                // There is no client present, we are allowed to execute the the action immediately and there are no other
                // enqeueued actions that are should to be executed before this action. Execute immediately.
                ExecutePendingSynchronizedServerActionImmediately(action);
            }
            else
            {
                // There is no client present, but cannot execute the action immediately. Register action and enqueue
                // immediate flush. Note that we can only flush up to this action as a client might connect before the
                // command is handled.
                int trackingId = RegisterSynchronizedServerAction(action, deadline);
                _self.Tell(new ExecutePendingSynchronizedServerActionsCommand(upToTrackingIdInclusive: trackingId), null);
            }
        }

        /// <summary>
        /// Registers a synchronized action into the set of tracked actions the server expects the client to execute. This functions
        /// similarly to <see cref="EnqueueServerAction"/>, except it is the caller's responsibility to deliver this message to
        /// client either IMMEDIATELY, or otherwise guarantee that order in which server actions are registered is exactly the
        /// same they are received on the client.
        /// <para>
        /// <b>Important: </b> Registering an action allocates a tracking ID, which is returned by this method. It is the caller's responsibility to
        /// deliver it to the client along with the <paramref name="action"/>. The <paramref name="action"/> MUST NOT BE MODIFIED after this call.
        /// </para>
        /// </summary>
        /// <returns>The Tracking ID for the registered action.</returns>
        protected int RegisterSynchronizedServerAction(PlayerActionBase action, ActionDeadline deadline)
        {
            int trackingId = _serverActionRunningTrackingId++;
            int deadlineTick = Model.CurrentTick + Model.TicksPerSecond * deadline.NumRelaxedSeconds;
            MetaTime deadlineTime = MetaTime.Now + MetaDuration.FromSeconds(deadline.NumRelaxedSeconds);
            PendingSynchronizedServerActionState pendingState = new PendingSynchronizedServerActionState(action, trackingId, deadlineTick, deadlineTime);
            _pendingSynchronizedServerActions.Add(pendingState);
            return trackingId;
        }

        /// <inheritdoc cref="RegisterSynchronizedServerAction(PlayerActionBase, ActionDeadline)"/>
        protected int RegisterSynchronizedServerAction(PlayerActionBase action)
        {
            return RegisterSynchronizedServerAction(action, deadline: ActionDeadline.FromRelaxedTimeSeconds(60));
        }

        void ExecuteAllPendingSynchronizedServerActionsImmediately()
        {
            foreach (PendingSynchronizedServerActionState pendingServerAction in _pendingSynchronizedServerActions)
                ExecutePendingSynchronizedServerActionImmediately(pendingServerAction.Action);
            _pendingSynchronizedServerActions.Clear();
        }

        void ExecutePendingSynchronizedServerActionImmediately(PlayerActionBase action)
        {
            // Any pending flushes to keep Client-side event order always consistent with the server
            FlushActionBatch(forceFlushImmediately: true);

            // Execute action immediately
            _log.Debug("Execute synchronized server action (on tick {Tick}): {Action}", Model.CurrentTick, PrettyPrint.Compact(action));

            MetaActionResult result = ModelUtil.RunAction(Model, action, NullChecksumEvaluator.Context);
            if (result != MetaActionResult.Success)
                _log.Warning("Failed to execute synchronized action (on tick {CurrentTick}): result={Result}, {Action}", Model.CurrentTick, result.ToString(), PrettyPrint.Verbose(action));
        }

        [CommandHandler]
        void HandleExecutePendingSynchronizedServerActionsCommand(ExecutePendingSynchronizedServerActionsCommand cmd)
        {
            int ndx;
            for (ndx = 0; ndx < _pendingSynchronizedServerActions.Count; ++ndx)
            {
                PendingSynchronizedServerActionState pendingServerAction = _pendingSynchronizedServerActions[ndx];
                if (pendingServerAction.TrackingId > cmd.UpToTrackingIdInclusive)
                    break;

                ExecutePendingSynchronizedServerActionImmediately(pendingServerAction.Action);
            }
            _pendingSynchronizedServerActions.RemoveRange(0, ndx);
        }

        #endregion

        protected EntitySubscriber TryGetOwnerSession()
        {
            return _subscribers.Values.FirstOrDefault(sub => sub.Topic == EntityTopic.Owner);
        }

        #region NFTs

        [EntityAskHandler]
        async Task HandleRefreshOwnedNftsRequestAsync(EntityAsk ask, RefreshOwnedNftsRequest _)
        {
            await StartRefreshOwnedNftsAsync(
                onCompleted: response => ReplyToAsk(ask, response),
                onError: message => RefuseAsk(ask, new InvalidEntityAsk(message)));
        }

        [CommandHandler]
        async Task HandleStartRefreshOwnedNftsCommand(StartRefreshOwnedNftsCommand _)
        {
            await StartRefreshOwnedNftsAsync();
        }

        async Task StartRefreshOwnedNftsAsync(Action<RefreshOwnedNftsResponse> onCompleted = null, Action<string> onError = null)
        {
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            // First, gather this player's ethereum addresses based on auth records
            List<EthereumAddress> ownedEthereumAddresses = new List<EthereumAddress>();
            foreach ((AuthenticationKey authKey, PlayerAuthEntryBase authEntry) in Model.AttachedAuthMethods)
            {
                if (authKey.Platform == AuthenticationPlatform.Ethereum)
                {
                    // Ethereum addresses are wallets themselves
                    PersistedAuthenticationEntry persistedAuthEntry = await MetaDatabase.Get().TryGetAsync<PersistedAuthenticationEntry>(authKey.ToString());
                    if (persistedAuthEntry?.PlayerEntityId != _entityId)
                    {
                        // Stale info in Model, ignore
                        continue;
                    }

                    EthereumAddress? ethAddress = ImmutableXAuthenticator.ParseEthereumAuthenticationUserIdAndCheckNetwork(web3Options, authKey.Id);
                    if (!ethAddress.HasValue)
                    {
                        // Authentication was done using a different network/chain, ignore
                        continue;
                    }

                    ownedEthereumAddresses.Add(ethAddress.Value);
                }
            }

            List<NftOwnerAddress> ownedAddresses = ownedEthereumAddresses.Select(address => NftOwnerAddress.CreateEthereum(address)).ToList();

            // Tell NftManager to refresh
            ContinueTaskOnActorContext(
                EntityAskAsync<RefreshNftsOwnedByEntityResponse>(NftManager.EntityId, new RefreshNftsOwnedByEntityRequest(owner: _entityId, ownedAddresses: ownedAddresses)),
                handleSuccess: (RefreshNftsOwnedByEntityResponse response) =>
                {
                    onCompleted?.Invoke(new RefreshOwnedNftsResponse());
                },
                handleFailure: exception =>
                {
                    _log.Warning("Failed to refresh owned NFTs, for owner addresses [ {Addresses} ]: {Exception}", string.Join(", ", ownedAddresses), exception);
                    onError?.Invoke(exception.ToString());
                });
        }

        [MessageHandler]
        void HandleNftOwnershipGained(NftOwnershipGained ownershipGained)
        {
            MetaNft nft = ownershipGained.Nft;
            MetaSerialization.ResolveMetaRefs(ref nft, Model.GetDataResolver()); // \todo #nft Use baseline resolver instead? But be consistent with client!
            // \todo #nft The action will fail and warn if the nft is already in Model.
            //       This can happen if this message was sent when the player actor was not awake,
            //       because the player actor queries the owned NFTs at wakeup, before handling this message!
            EnqueueServerAction(new PlayerAddNft(nft));
        }

        [MessageHandler]
        void HandleNftOwnershipRemoved(NftOwnershipRemoved ownershipRemoved)
        {
            EnqueueServerAction(new PlayerRemoveNft(ownershipRemoved.NftKey));
        }

        [MessageHandler]
        void HandleNftStateUpdated(NftStateUpdated nftStateUpdated)
        {
            MetaNft nft = nftStateUpdated.Nft;
            MetaSerialization.ResolveMetaRefs(ref nft, Model.GetDataResolver()); // \todo #nft Use baseline resolver instead? But be consistent with client!
            EnqueueServerAction(new PlayerUpdateNft(nft));
        }

        [MessageHandler]
        async Task HandlePlayerNftTransactionRequestAsync(PlayerNftTransactionRequest transactionRequest)
        {
            // \todo #nft This is a poor implementation that does not actually achieve consistency;
            //       in case of a crash, finalization/cancellation may not get run.
            //       Need write-ahead log or similar.
            //
            //       Note: when this is addressed, and thus pending transactions are persisted,
            //       the assignment in PlayerFinalizeNftTransaction of the updated NFTs will probably
            //       cause consistency problems in the case that NFT ownership was lost during
            //       player actor downtime. Need to think carefully and fix.

            PlayerNftTransaction transaction = transactionRequest.Transaction;
            MetaSerialization.ResolveMetaRefs(ref transaction, Model.GetDataResolver()); // \todo #nft Use baseline resolver instead? But be consistent with client!

            // Take the NFTs from the OwnedNfts replica in the player model, but clone them
            // to avoid mutability issues between the model and the transaction initiation action.
            // (OwnedNfts will get updated in PlayerFinalizeNftTransaction, if the transaction
            // ends up being successful.)
            OrderedDictionary<NftKey, MetaNft> nfts = transaction.TargetNfts.ToOrderedDictionary(key => key, key => Model.Nft.OwnedNfts[key]);
            nfts = MetaSerialization.CloneTagged(nfts, MetaSerializationFlags.IncludeAll, Model.LogicVersion, Model.GetDataResolver());

            // Initiate

            PlayerExecuteNftTransaction executeTransaction = new PlayerExecuteNftTransaction(transaction, nfts);

            MetaActionResult executeResult = ModelUtil.DryRunAction(Model, executeTransaction);
            if (executeResult != MetaActionResult.Success)
            {
                _log.Warning("Dry-run of transaction {TransactionType} failed: {ExecuteResult}. Not running transaction.", transaction.GetType().Name, executeResult);
                return;
            }

            // \hack: #journalhack
            _playerJournal.StageAction(executeTransaction, ArraySegment<uint>.Empty);
            using (var _ = _playerJournal.Commit(_playerJournal.StagedPosition, ModelJournal<IPlayerModelBase>.Follower.ConflictResolutionMode.ContinueEvenIfConflicts))
            {
            }

            // Update NFTs to NftManager

            bool nftUpdateSuccess;

            if (Model.Nft.TransactionState.Nfts.Count > 0)
            {
                try
                {
                    _ = await EntityAskAsync<OwnerUpdateNftStatesResponse>(NftManager.EntityId, new OwnerUpdateNftStatesRequest(assertedOwner: _entityId, Model.Nft.TransactionState.Nfts.Values.ToList()));
                    nftUpdateSuccess = true;
                }
                catch (EntityAskRefusal refusal)
                {
                    // \todo #nft Should player actor use this as a hint that its nft replica state might be out of sync
                    //            and should therefore re-query owned nfts from manager? Currently ownership change messages
                    //            from manager to owner are fire-and-forget so might get lost.
                    _log.Warning($"{nameof(NftManager)} refused {nameof(OwnerUpdateNftStatesRequest)}: {refusal.Message}");
                    nftUpdateSuccess = false;
                }
            }
            else
            {
                // \note Not necessarily a mistake but kinda suspicious
                _log.Warning($"Transaction {{TransactionType}} targeted zero NFTs. Permitting transaction but omitting message to {nameof(NftManager)}.", transaction.GetType().Name, executeResult);
                nftUpdateSuccess = true;
            }

            // Finalize or cancel according to NftManager's response

            if (nftUpdateSuccess)
                EnqueueServerAction(new PlayerFinalizeNftTransaction(transaction));
            else
                EnqueueServerAction(new PlayerCancelNftTransaction(transaction));
        }

        #endregion

        #region Event log

        static EntityEventLogOptionsBase GetEventLogConfiguration()
        {
            return RuntimeOptionsRegistry.Instance.GetCurrent<PlayerEventLogOptions>();
        }

        void TryEnqueueTriggerEventLogFlushing()
        {
            if (PlayerEventLogUtil.CanFlush(Model.EventLog, GetEventLogConfiguration()))
                CastMessage(_entityId, new TriggerEventLogFlushing());
        }

        [MessageHandler]
        async Task HandleTriggerEventLogFlushing(TriggerEventLogFlushing _)
        {
            await PlayerEventLogUtil.TryFlushAndCullSegmentsAsync(Model.EventLog, GetEventLogConfiguration(), _entityId, _logicVersion, _log, PersistStateIntermediate);
        }

        [EntityAskHandler]
        protected async Task<PlayerEventLogScanResponse> HandleEntityEventLogScanRequest(EntityEventLogScanRequest request)
        {
            try
            {
                return await PlayerEventLogUtil.ScanEntriesAsync(Model.EventLog, _entityId, _baselineGameConfigResolver, _logicVersion, request);
            }
            catch (Exception ex)
            {
                throw new InvalidEntityAsk(ex.ToString());
            }
        }

        protected Task RemoveAllOwnedEventLogSegmentsAsync()
        {
            return PlayerEventLogUtil.RemoveAllSegmentsAsync(Model.EventLog, _entityId, PersistStateIntermediate);
        }

        #endregion

        #region IPlayerModelServerListenerCore

        public virtual void OnPlayerNameChanged(string newName){ }

        void IPlayerModelServerListenerCore.LanguageChanged(LanguageInfo newLanguage, ContentHash languageVersion)
        {
            // The version in use can be a private built-in version. Unfortunate, can't really do anything about it.
            _activeLocalizationVersion = languageVersion;
        }

        void IPlayerModelServerListenerCore.DynamicInAppPurchaseRequested(InAppProductId productId)
        {
            _log.Info("DynamicInAppPurchaseRequested(productId={ProductId})", productId);
            CastMessage(_entityId, new TriggerConfirmDynamicPurchaseContent(productId));
        }

        void IPlayerModelServerListenerCore.StaticInAppPurchaseContextRequested(InAppProductId productId)
        {
            _log.Info("StaticInAppPurchaseContextRequested(productId={ProductId})", productId);
            CastMessage(_entityId, new TriggerConfirmStaticPurchaseContext(productId));
        }

        void IPlayerModelServerListenerCore.InAppPurchased(InAppPurchaseEvent ev, InAppProductInfoBase productInfo)
        {
            _log.Info("InAppPurchased(platform={Platform}, transactionId={TransactionId}, productId={ProductId}, platformProductId={PlatformProductId}), validate it!", ev.Platform, ev.TransactionId, ev.ProductId, ev.PlatformProductId);

            Model.EventStream.Event(new PlayerEventInAppValidationStarted(ev.ProductId, ev.Platform, ev.TransactionId, ev.OrderId, ev.PlatformProductId, ev.ReferencePrice, ev.GameProductAnalyticsId, ev.TryGetGamePurchaseAnalyticsContext()));

            // Send message to self to start in-app validation sequence (some parts of it are async, so cannot perform immediately)
            InAppPurchaseTransactionInfo txnInfo = InAppPurchaseTransactionInfo.FromPurchaseEvent(ev, productInfo.Type, Model.IsDeveloper);
            CastMessage(_entityId, new TriggerInAppPurchaseValidation(txnInfo, isRetry: false));
        }

        void IPlayerModelServerListenerCore.FirebaseMessagingTokenAdded(string token)
        {
            // Persist soon, so that new tokens soon become visible to notification campaigns.
            SchedulePersistState();
        }

        void EvaluateTriggers(PlayerEventBase ev)
        {
            if (_activeBroadcastTriggerConditionEventTypes.Contains(ev.GetType()))
                CastMessage(_entityId, new InternalPlayerEvaluateTriggers(ev));
        }

        [MessageHandler]
        void HandlePlayerEvaluateTriggers(InternalPlayerEvaluateTriggers message)
        {
            // Trigger broadcasts
            foreach (BroadcastMessage broadcast in _activeBroadcastStateSubscriber.Current.ActiveBroadcasts)
            {
                if (Model.ReceivedBroadcastIds.Contains(broadcast.Params.Id))
                    continue;

                if (broadcast.Params.TriggerCondition != null &&
                    broadcast.Params.TriggerCondition.EventTypesToConsider.Contains(message.Event.GetType()) &&
                    broadcast.Params.TriggerCondition.SatisfiesCondition(message.Event))
                {
                    TryReceiveBroadcast(broadcast);
                }
            }
        }

        void IPlayerModelServerListenerCore.OnPlayerBannedStatusChanged(bool isBanned)
        {
            if (isBanned)
            {
                EntitySubscriber session = TryGetOwnerSession();
                if (session != null)
                {
                    _log.Info("Player was banned while online, kicking");
                    KickSubscriber(session, new PlayerForceKickOwner(PlayerForceKickOwnerReason.PlayerBanned));
                }
            }
            CastMessage(_entityId, new TriggerPlayerBanFlagChanged());
        }

        #if !METAPLAY_DISABLE_GUILDS
        void IPlayerModelServerListenerCore.GuildMemberPlayerDataChanged()
        {
            Guilds?.EnqueueGuildMemberPlayerDataUpdate();
        }
        #endif

        void IPlayerModelServerListenerCore.ActivableActivationStarted(MetaActivableKey activableKey)
        {
            IncreaseActivableStatistics(
                activableKey,
                "activation",
                activableState => MetaActivableStatistics.ForSingleActivation(isFirstActivation: activableState.NumActivated == 1));
        }

        void IPlayerModelServerListenerCore.ActivableConsumed(MetaActivableKey activableKey)
        {
            IncreaseActivableStatistics(
                activableKey,
                "consumption",
                activableState => MetaActivableStatistics.ForSingleConsumption(isFirstConsumption: activableState.TotalNumConsumed == 1));
        }

        void IPlayerModelServerListenerCore.ActivableFinalized(MetaActivableKey activableKey)
        {
            IncreaseActivableStatistics(
                activableKey,
                "finalization",
                activableState => MetaActivableStatistics.ForSingleFinalization(isFirstFinalization: activableState.NumFinalized == 1));
        }

        void IPlayerModelServerListenerCore.MetaOfferActivationStarted(MetaOfferGroupId groupId, MetaOfferId offerId)
        {
            IncreaseMetaOfferStatistics(
                groupId, offerId,
                "activation",
                offerPerPlayerState => MetaOfferStatistics.ForSingleActivation(isFirstActivation: offerPerPlayerState.NumActivatedByPlayer == 1),
                offerPerGroupState => MetaOfferStatistics.ForSingleActivation(isFirstActivation: offerPerGroupState.NumActivatedInGroup == 1));
        }

        void IPlayerModelServerListenerCore.MetaOfferPurchased(MetaOfferGroupId groupId, MetaOfferId offerId)
        {
            double offerPrice;

            if (Model.GameConfig.MetaOffers.TryGetValue(offerId, out MetaOfferInfoBase offerInfo))
                offerPrice = offerInfo.InAppProduct?.Ref.Price.Double ?? 0.0;
            else
            {
                // \note Should never happen, but is tolerable.
                _log.Error("MetaOfferPurchased invoked for {OfferId} but there's no config for it", offerId);
                offerPrice = 0.0;
            }

            IncreaseMetaOfferStatistics(
                groupId, offerId,
                "purchase",
                offerPerPlayerState => MetaOfferStatistics.ForSinglePurchase(isFirstPurchase: offerPerPlayerState.NumPurchasedByPlayer == 1, revenue: offerPrice),
                offerPerGroupState => MetaOfferStatistics.ForSinglePurchase(isFirstPurchase: offerPerGroupState.NumPurchasedInGroup == 1, revenue: offerPrice));
        }

        void IncreaseActivableStatistics(MetaActivableKey activableKey, string eventNameForLog, Func<MetaActivableState, MetaActivableStatistics> getStatisticsDelta)
        {
            MetaActivableState activableState = TryGetActivableState(activableKey);
            if (activableState == null)
            {
                // \note Should never happen, but is tolerable.
                _log.Error("Activable {Event} listener invoked for {ActivableKey}, but player has no state for it", eventNameForLog, activableKey);
                return;
            }

            // Delay the reporting of statistics until after the next persist, to avoid double-reporting in case of actor crash.
            // \note Evaluate getter before the delayed closure, so it doesn't refer to the live state.
            MetaActivableStatistics statisticsDelta = getStatisticsDelta(activableState);
            _afterPersistActions += () => StatsCollectorProxy.IncreaseActivableStatistics(activableKey, statisticsDelta: statisticsDelta);
        }

        void IncreaseMetaOfferStatistics(
            MetaOfferGroupId groupId, MetaOfferId offerId,
            string eventNameForLog,
            Func<MetaOfferPerPlayerStateBase, MetaOfferStatistics> getOfferStatisticsDelta,
            Func<MetaOfferPerGroupStateBase, MetaOfferStatistics> getOfferPerGroupStatisticsDelta)
        {
            MetaOfferGroupModelBase     offerGroup          = Model.MetaOfferGroups.TryGetState(groupId);
            MetaOfferPerGroupStateBase  offerPerGroupState  = offerGroup?.OfferStates.GetValueOrDefault(offerId);
            MetaOfferPerPlayerStateBase offerPerPlayerState = Model.MetaOfferGroups.TryGetOfferPerPlayerState(offerId);

            if (offerPerGroupState == null || offerPerPlayerState == null)
            {
                // \note Should never happen, but is tolerable.
                _log.Error("Offer {Event} listener invoked for offer {OfferId} in group {OfferGroupId}, but player has no state for it", eventNameForLog, offerId, groupId);
                return;
            }

            // Delay the reporting of statistics until after the next persist, to avoid double-reporting in case of actor crash.
            // \note Evaluate getters before the delayed closure, so it doesn't refer to the live state.
            MetaOfferStatistics offerStatisticsDelta            = getOfferStatisticsDelta(offerPerPlayerState);
            MetaOfferStatistics offerPerGroupStatisticsDelta    = getOfferPerGroupStatisticsDelta(offerPerGroupState);
            _afterPersistActions += () =>
                StatsCollectorProxy.IncreaseMetaOffersStatistics(groupId, offerId,
                    offerStatisticsDelta:           offerStatisticsDelta,
                    offerPerGroupStatisticsDelta:   offerPerGroupStatisticsDelta);
        }

        MetaActivableState TryGetActivableState(MetaActivableKey activableKey)
        {
            IMetaActivableSet   activableSet    = MetaActivableUtil.GetPlayerActivableSetForKind(activableKey.KindId, Model);
            IMetaActivableInfo  activableInfo   = MetaActivableUtil.GetActivableGameConfigData(activableKey, Model.GameConfig);

            return activableSet.TryGetState(activableInfo);
        }

        public virtual void AuthMethodAttached(AuthenticationKey authKey)
        {
            OnAuthMethodAttachedOrDetached(authKey);
        }

        public virtual void AuthMethodDetached(AuthenticationKey authKey)
        {
            OnAuthMethodAttachedOrDetached(authKey);
        }

        void OnAuthMethodAttachedOrDetached(AuthenticationKey authKey)
        {
            // \todo #nft More general condition for auth platform, in case using some other chain.
            if (authKey.Platform == AuthenticationPlatform.Ethereum)
                _self.Tell(new StartRefreshOwnedNftsCommand(), _self);
        }

        #endregion

        #region Model creation and initialization

        protected TModel CreateInitialModel(LogChannel initialLogChannel, string playerName)
        {
            TModel model = PlayerModelUtil.CreateNewPlayerModel<TModel>(MetaTime.Now, _baselineGameConfig.SharedConfig, _entityId, playerName);
            AssignBasicRuntimePropertiesToModel(model, initialLogChannel, _baselineGameConfig);
            return model;
        }

        protected TModel CreateModelFromSerialized(byte[] serialized, LogChannel logChannel)
        {
            TModel model = DeserializePersistedPayload<TModel>(serialized, _baselineGameConfigResolver, _logicVersion);
            AssignBasicRuntimePropertiesToModel(model, logChannel, _baselineGameConfig);
            return model;
        }

        protected TModel CreateClonedModel(TModel source, LogChannel logChannel)
        {
            TModel model = MetaSerializationUtil.CloneModel(source, _specializedGameConfigResolver);
            AssignBasicRuntimePropertiesToModel(model, logChannel, _specializedGameConfig);
            return model;
        }

        protected LogChannel CreateModelLogChannel(string name)
        {
            return new LogChannel(name, _log, MetaLogger.MetaLogLevelSwitch);
        }

        /// <summary>
        /// Assign to the model runtime properties that should always be present, such as Game Configs and logic versions.
        /// </summary>
        protected void AssignBasicRuntimePropertiesToModel(TModel model, LogChannel logChannel, FullGameConfig gameConfig)
        {
            // \note ServerListener is not assigned here. That's only done when the model becomes the actor's current execution model (i.e. in OnSwitchedToModel).
            //       Here should be assigned runtime properties that we want to be always available in the model, even during MigrateState.
            model.LogicVersion          = _logicVersion;
            model.GameConfig            = gameConfig.SharedConfig;
            model.Log                   = logChannel; // \note Assigning the specified log channel, which is not necessarily _modelLogChannel.
            model.AnalyticsEventHandler = _analyticsEventHandler;
            model.ResetTime(MetaTime.Now);
        }

        protected override void OnSchemaMigrated(TModel model, int fromVersion, int toVersion)
        {
            // Analytics
            _analyticsEventHandler.Event(model, new PlayerEventModelSchemaMigrated(fromVersion, toVersion));
        }

        #endregion

        #region Callbacks to userland

        /// <summary>
        /// Creates a <typeparamref name="TPersisted"/> representation of the current Model with the given arguments.
        /// </summary>
        protected abstract TPersisted CreatePersisted(EntityId entityId, DateTime persistedAt, byte[] payload, int schemaVersion, bool isFinal);

        /// <summary>
        /// Generate a random name for a new player
        /// </summary>
        /// <returns>New name</returns>
        protected abstract string RandomNewPlayerName();

        /// <summary>
        /// Called when new model instance is becoming active. Callee should should set up listeners.
        /// After switching to the model is complete, the <see cref="_playerJournal"/> will be
        /// set to track it and <see cref="Model"/> can be used to refer to it.
        ///
        /// This function is called during entity initialization, after state reset, and in
        /// <see cref="SwitchToNewModelImmediately(TModel, TimeSpan)"/>.
        /// </summary>
        protected virtual void OnSwitchedToModel(TModel model) { }

        void OnSwitchedToModelCore(TModel model)
        {
            model.ServerListenerCore = this;

            // Forward to game.
            OnSwitchedToModel(model);
        }

        /// <summary>
        /// Called when session handshake request is received from client. Session params have not yet been verified and this may not lead
        /// into a successful session creation (via <see cref="OnSessionStart"/>. In particular, the proposed configs are may not be correct.
        /// Additionally, this method is called before player is (potentially) assigned into a player experiment. The current game config
        /// of the Model may not match the config the session will be started with.
        /// <para>
        /// This method is useful for setting client information in Model, so that the player experiment targeting may then depend on them
        /// being set.
        /// </para>
        /// <para>
        /// Both synchronous and asynchronous versions of this callback are invoked.
        /// </para>
        /// </summary>
        [Obsolete("Replaced by OnClientSessionHandshakeAsync")]
        protected virtual void OnClientSessionHandshake(PlayerSessionParamsBase start) { }

        /// <inheritdoc cref="OnClientSessionHandshake(PlayerSessionParamsBase)"/>
        protected virtual Task OnClientSessionHandshakeAsync(PlayerSessionParamsBase start) => Task.CompletedTask;

        Task OnClientSessionHandshakeCoreAsync(PlayerSessionParamsBase start, bool isFirstLogin)
        {
            // Ensure client-reported time zone info is valid
            PlayerTimeZoneInfo newTimeZone = start.TimeZoneInfo.GetCorrected();
            // Update Model's time zone (implementation can be overridden)
            Model.UpdateTimeZone(newTimeZone, isFirstLogin);

            // Update location, but only if available
            if (start.Location != null)
                Model.LastKnownLocation = start.Location.Value;

            // Ensure that the used authKey is listed as attached.
            if (!Model.AttachedAuthMethods.ContainsKey(start.AuthKey))
            {
                PlayerAuthEntryBase entry;
                if (start.AuthKey.Platform == AuthenticationPlatform.DeviceId)
                    entry = new PlayerDeviceIdAuthEntry(attachedAt: MetaTime.Now, deviceModel: start.DeviceModel);
                else
                    entry = CreateSocialAuthenticationEntry(start.AuthKey);
                Model.AttachedAuthMethods.Add(start.AuthKey, entry);
            }

            // Forward to game.
            #pragma warning disable CS0618 // Type or member is obsolete
            #pragma warning disable VSTHRD103 // Call async methods when in an async method
            OnClientSessionHandshake(start);
            #pragma warning restore VSTHRD103 // Call async methods when in an async method
            #pragma warning restore CS0618 // Type or member is obsolete
            return OnClientSessionHandshakeAsync(start);
        }

        /// <summary>
        /// Called when session handshake has been validated and a new session is about to begin. This method may directly modify fields in the
        /// Model.
        /// <para>
        /// This method is useful for tracking login statistics and updating custom session-related state.
        /// </para>
        /// <para>
        /// Both synchronous and asynchronous versions of this callback are invoked.
        /// </para>
        /// </summary>
        /// <param name="start">Session start params as defined in SessionActor.</param>
        /// <param name="isFirstLogin">true, if this is the first time the session starts for this player.</param>
        [Obsolete("Replaced by OnSessionStartAsync")]
        protected virtual void OnSessionStart(PlayerSessionParamsBase start, bool isFirstLogin) { }

        /// <inheritdoc cref="OnSessionStart(PlayerSessionParamsBase, bool)"/>
        protected virtual Task OnSessionStartAsync(PlayerSessionParamsBase start, bool isFirstLogin) => Task.CompletedTask;

        bool ValidateDeviceGuid(string deviceGuid)
        {
            // Don't even try to parse for known-invalid values. This way we avoid
            // exceptions on happy path.
            if (string.IsNullOrEmpty(deviceGuid))
                return false;

            try
            {
                MetaGuid.Parse(deviceGuid);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        (string updatedDeviceGuid, PlayerDeviceEntry device) GetDeviceEntryOrCreateNew(PlayerSessionParamsBase start, MetaTime now)
        {
            // Migration path: if client doesn't have a DeviceGuid try to lookup based on DeviceId and migrate the history entry
            if (start.DeviceGuid == null)
            {
                if (start.AuthKey.Platform == AuthenticationPlatform.DeviceId &&
                    Model.DeviceHistory.TryGetValue(start.AuthKey.Id, out PlayerDeviceEntry deviceToMigrate) &&
                    deviceToMigrate.IncompleteHistory &&
                    deviceToMigrate.DeviceModel == start.DeviceModel)
                {
                    // ClientPlatform info is missing from legacy entries, add it here
                    deviceToMigrate.ClientPlatform = start.ClientPlatform;
                    // Generate new deviceId and update key
                    string newDeviceGuid = MetaGuid.New().ToString();
                    Model.DeviceHistory.Remove(start.AuthKey.Id);
                    Model.DeviceHistory.Add(newDeviceGuid, deviceToMigrate);
                    // Clear push notification token, client will create new one with appropriate device id
                    Model.PushNotifications.RemoveDevice(start.AuthKey.Id);
                    return (newDeviceGuid, deviceToMigrate);
                }
            }

            // Validate proposed deviceGuid
            string deviceGuid;
            if (ValidateDeviceGuid(start.DeviceGuid))
            {
                deviceGuid = start.DeviceGuid;

                // Valid GUID. Lookup from device history to see if it already exists.
                if (Model.DeviceHistory.TryGetValue(deviceGuid, out PlayerDeviceEntry existingDevice))
                {
                    // Only accept entry if device info matches
                    if (existingDevice.ClientPlatform == start.ClientPlatform && existingDevice.DeviceModel == start.DeviceModel)
                        return (deviceGuid, existingDevice);
                    // Must generate new device GUID: Client proposed one that is already in use on different device.
                    deviceGuid = MetaGuid.New().ToString();
                }

                // Client proposed a valid device guid but it's not used yet (or pruned from history)
            }
            else
            {
                // Create new device entry. Client proposed invalid id.
                deviceGuid = MetaGuid.New().ToString();
            }

            // Purge entries if necessary
            if (Model.DeviceHistory.Count >= PlayerModelConstants.DeviceHistoryMaxSize)
            {
                int numToRemove = Model.DeviceHistory.Count - PlayerModelConstants.DeviceHistoryMaxSize + 1;
                foreach (string idToRemove in Model.DeviceHistory.OrderBy(kv => kv.Value.LastLoginAt).Take(numToRemove).Select(x => x.Key).ToList())
                    Model.DeviceHistory.Remove(idToRemove);
            }

            // Create and store new device record
            PlayerDeviceEntry device = PlayerDeviceEntry.Create(start.DeviceModel, start.ClientPlatform, firstSeenAt: now);
            Model.DeviceHistory.Add(deviceGuid, device);
            return (deviceGuid, device);
        }

        Task OnSessionStartCoreAsync(PlayerSessionParamsBase start, bool isFirstLogin)
        {
            // Ensure that PlayerModel is up-to-date
            MetaTime now = MetaTime.Now;
            MetaDuration elapsedTime = now - Model.CurrentTime;
            if (elapsedTime > MetaDuration.Zero)
            {
                _log.Debug("Fast-forwarding PlayerModel {ElapsedTime} before starting session", elapsedTime);
                Model.ResetTime(now);
                Model.OnFastForwardTime(elapsedTime);
            }

            // Update stats
            if (isFirstLogin)
                Model.Stats.InitialClientVersion = start.ClientVersion;
            Model.Stats.LastLoginAt = now;
            Model.Stats.TotalLogins++;

            // Resolve device id and store device history
            (string deviceGuid, PlayerDeviceEntry device) = GetDeviceEntryOrCreateNew(start, now);
            device.RecordNewLogin(start.AuthKey, now);

            // Store per-session state in model
            Model.SessionDeviceGuid = deviceGuid;
            Model.SessionToken = start.SessionToken;

            // Store login event in history (and purge excess entries if necessary)
            Model.LoginHistory.Add(new PlayerLoginEvent(now, deviceGuid, start.DeviceModel, start.ClientVersion, start.Location, start.AuthKey));
            while (Model.LoginHistory.Count > PlayerModelConstants.LoginHistoryMaxSize)
                Model.LoginHistory.RemoveAt(0);

            if (isFirstLogin)
                Model.OnInitialLogin();
            Model.OnSessionStarted();
            #pragma warning disable CS0618 // Type or member is obsolete
            #pragma warning disable VSTHRD103 // Call async methods when in an async method
            OnSessionStart(start, isFirstLogin);
            #pragma warning restore VSTHRD103 // Call async methods when in an async method
            #pragma warning restore CS0618 // Type or member is obsolete
            return OnSessionStartAsync(start, isFirstLogin);
        }

        /// <summary>
        /// Called when a SessionActor is subscribing into this player as the Owner, i.e. a this player
        /// session is starting.
        /// </summary>
        protected virtual Task OnNewOwnerSession(EntitySubscriber subscriber) => Task.CompletedTask;

        /// <summary>
        /// Called when a owner session (i.e. current player session) ends for any reason, such as
        /// session timing out or being kicked.
        /// </summary>
        protected virtual void OnOwnerSessionEnded(EntitySubscriber subscriber, bool wasKicked) { }

        protected virtual Task<MetaMessage>                         OnNewUnknownSubscriber  (EntitySubscriber subscriber, MetaMessage message)  { _log.Warning("Subscriber {EntityId} on unknown topic [{Topic}]", subscriber.EntityId, subscriber.Topic); return Task.FromResult<MetaMessage>(null); }
        protected virtual void                                      OnUnknownSubscriberLost (EntitySubscriber subscriber)                       {}

        /// <summary>
        /// Called when a player is flagged for deletion. At this point the player still exists, but it
        /// is safe to assume that they are not coming back. We can use this opportunity to do things
        /// like cancel any active multi-player games that the player might be involved in
        /// </summary>
        /// <returns></returns>
        protected virtual Task OnPlayerScheduledForDeletion() => Task.CompletedTask;

        /// <summary>
        /// Called when the player is being deleted. At this point, the player's model
        /// is still valid but the player is disconnected and blocked from reconnecting.
        /// This is where we can remove the player from things like guilds, leagues, etc.
        /// so that other players don't see this deleted player.
        /// If using builtin Metaplay guilds, removal from the guild will be completed
        /// automatically if the callback does not handle it.
        /// </summary>
        protected virtual Task OnPlayerBeingDeleted() => Task.CompletedTask;

        /// <summary>
        /// Selects the default language for a new session. This method is only used if player has not set a language,
        /// and the client did not automatically choose a language. This method may return <c>null</c> to use the default
        /// language defined in gameconfig.
        /// </summary>
        protected virtual LanguageId GetDefaultLanguageForNewSession(InternalPlayerSessionSubscribeRequest request) => null;

        /// <summary>
        /// When persisting player, should the associated player search entries be updated as well?
        /// </summary>
        protected virtual bool ShouldUpdatePlayerSearch()
        {
            // \note Null and "" names are considered equal for search.
            return (Model.SearchVersion != PlayerModelConstants.PlayerSearchTableVersion) || ((Model.PlayerName ?? "") != (_persistedSearchName ?? ""));
        }
        protected virtual IEnumerable<string> GetPlayerSearchStrings()
        {
            return Enumerable.Repeat(EntityId.ValueToString(_entityId.Value), 1).Concat(
                SearchUtil.ComputeSearchablePartsFromName(Model.PlayerName ?? "",
                minLengthCodepoints: PersistedPlayerSearch.MinPartLengthCodepoints,
                maxLengthCodepoints: PersistedPlayerSearch.MaxPartLengthCodepoints,
                maxParts: PersistedPlayerSearch.MaxPersistNameParts));
        }

        /// <summary>
        /// Called after player ban state has changed (player is banned or unbanned). This method can be
        /// used to add custom banning behavior (like removing banned players from guilds) or to clean up external
        /// state, for example marking the player as unusable in a matchmaking system.
        /// </summary>
        protected virtual Task OnPlayerBanStateChanged(bool isBanned) => Task.CompletedTask;

        /// <summary>
        /// Called just before Model is serialized and DB write is issued. This can be used to update auxiliary tables
        /// and or modify Model just before it is written.
        /// </summary>
        protected virtual ValueTask BeforePersistAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Validates client-originating action. If action is not valid, the method throws <see cref="System.InvalidOperationException"/>.
        /// If validation fails, no actions in the Flush-batch get executed.
        /// </summary>
        protected virtual void ValidateClientOriginatingAction(PlayerActionBase action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // Must be client-issuable.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.LeaderSynchronized))
                throw new InvalidOperationException($"Client tried to run Action {action.GetType().ToGenericTypeString()} which does not have LeaderSynchronized ExecuteFlags. ExecuteFlags=${actionSpec.ExecuteFlags}.");

            // Development actions are only allowed if so configured
            bool isDevelopmentOnlyAction = actionSpec.HasCustomAttribute<DevelopmentOnlyActionAttribute>();
            if (isDevelopmentOnlyAction)
            {
                _log.Info("Executing development-only action: {Action}", action.GetType().ToGenericTypeString());

                EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
                if (!envOpts.EnableDevelopmentFeatures && !Model.IsDeveloper)
                    throw new InvalidOperationException($"Client tried to run development-only action {action.GetType().ToGenericTypeString()}, but Development-Only actions are not enabled.");
            }
        }

        /// <summary>
        /// Called after client network connectivity state has changed, or if application pause status has changed. The current connectivity related
        /// state is available in <see cref="Model"/> via <see cref="IPlayerModelBase.IsOnline"/>, <see cref="IPlayerModelBase.IsClientConnected"/> and <see cref="IPlayerModelBase.ClientAppPauseStatus"/>
        /// properties.
        /// </summary>
        protected virtual void OnClientConnectivityStatusChanged() { }

        /// <inheritdoc cref="Metaplay.Server.MultiplayerEntity.PersistedMultiplayerEntityActorBase{TModel, TAction, TPersisted}.GetSessionStartAssociatedEntities()"/>
        protected virtual List<AssociatedEntityRefBase> GetSessionStartAssociatedEntities()
        {
            List<AssociatedEntityRefBase> associatedEntities = new List<AssociatedEntityRefBase>();

            #if !METAPLAY_DISABLE_GUILDS
            AssociatedEntityRefBase guildEntity = Guilds?.GetSessionStartAssociatedGuildEntity();
            if (guildEntity != null)
                associatedEntities.Add(guildEntity);
            #endif

            return associatedEntities;
        }

        /// <inheritdoc cref="Metaplay.Server.MultiplayerEntity.PersistedMultiplayerEntityActorBase{TModel, TAction, TPersisted}.OnAssociatedEntityRefusalAsync(EntityId, AssociatedEntityRefBase, InternalEntitySubscribeRefusedBase)"/>
        protected virtual async Task<bool> OnAssociatedEntityRefusalAsync(EntityId playerId, AssociatedEntityRefBase association, InternalEntitySubscribeRefusedBase refusal)
        {
            #if !METAPLAY_DISABLE_GUILDS
            if (association.GetClientSlot() == ClientSlotCore.Guild)
                return await Guilds.OnAssociatedEntityRefusalAsync(association, refusal);
            #else
            // Silence compiler warning for async method without await
            await Task.CompletedTask;
            #endif

            return false;
        }

        /// <summary>
        /// Creates <see cref="PlayerAuthEntryBase"/> for a new social authentication that is being added to the player.
        /// This will be placed in <see cref="IPlayerModelBase.AttachedAuthMethods"/>.
        /// </summary>
        protected virtual PlayerAuthEntryBase CreateSocialAuthenticationEntry(AuthenticationKey key)
        {
            return new PlayerAuthEntryBase.Default(attachedAt: MetaTime.Now);
        }

        /// <summary>
        /// Create a division avatar from the player model.
        /// Avatars are public information shown to other players, that represent a player in the division leaderboards.
        /// Will return a <see cref="PlayerDivisionAvatarBase.Default"/> by default.
        /// </summary>
        protected virtual PlayerDivisionAvatarBase GetPlayerLeaguesDivisionAvatar() =>
            new PlayerDivisionAvatarBase.Default(Model.PlayerName);

        #endregion

        #region Misc

        IGameConfigDataResolver GetConfigResolver(FullGameConfig fullConfig)
        {
            return fullConfig.SharedConfig;
        }

        ServerPlayerModelJournal CreateModelJournal(IPlayerModelBase model, bool enableDevelopmentFeatures, bool enableConsistencyChecks)
        {
            return new ServerPlayerModelJournal(
                _modelLogChannel,
                model,
                enableConsistencyChecks:   enableConsistencyChecks);
        }

        #endregion

        #region Leagues

        /// <summary>
        /// Emits the score event to the current Player Division, if any.
        /// </summary>
        protected void EmitPlayerDivisionScoreEvent(IDivisionScoreEvent scoreEvent)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnablePlayerLeagues)
                return;
            if (!(Model.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionClientStateBase) &&
                    playerDivisionClientStateBase is IDivisionClientState divisionClientState))
                return;

            EntityId currentDivision = divisionClientState.CurrentDivision;

            if (!currentDivision.IsValid)
                return;

            InternalDivisionScoreEventMessage scoreMessage = new InternalDivisionScoreEventMessage(
                participantId: _entityId,
                playerId: _entityId,
                scoreEvent: scoreEvent);

            CastMessage(currentDivision, scoreMessage);
        }

        /// <summary>
        /// Request to join a player league. Joining a league is not possible if:
        /// <list type="bullet">
        /// <item>The player is already a participant in the league</item>
        /// <item>The league is not currently running</item>
        /// <item>The between-seasons migration has not finished for the new season</item>
        /// <item>The player does not meet the requirements set by the league manager</item>
        /// </list>
        /// Extending and passing in the <see cref="LeagueJoinRequestPayloadBase"/> class can be used to deliver any information about the player
        /// required by the league manager to decide the player's eligibility and their starting rank.
        /// </summary>
        /// <returns>A tuple of possible joinedDivision if successful, or a refusal reason if not.</returns>
        protected async Task<(DivisionIndex? joinedDivision, LeagueJoinRefuseReason? reason)> TryJoinPlayerLeague(EntityId leagueId, LeagueJoinRequestPayloadBase requestPayload = null)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnablePlayerLeagues)
                throw new InvalidOperationException("Cannot join leagues if leagues is not enabled.");

            if (!(Model.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionClientStateBase) &&
                    playerDivisionClientStateBase is IDivisionClientState divisionClientState))
                throw new InvalidOperationException($"PlayerModel does not have a valid {nameof(IDivisionClientState)}.");

            DivisionIndex?          joinedDivision = null;
            LeagueJoinRefuseReason? refuseReason   = null;

            PersistedParticipantDivisionAssociation oldAssociation = await MetaDatabase.Get().TryGetAsync<PersistedParticipantDivisionAssociation>(_entityId.ToString());

            // If already in league, early exit.
            if (oldAssociation != null && oldAssociation.LeagueId == leagueId.ToString())
                return (null, LeagueJoinRefuseReason.AlreadyInLeague);

            // Not already a participant. Try to join league.
            InternalLeagueJoinResponse response = await EntityAskAsync<InternalLeagueJoinResponse>(
                leagueId,
                new InternalLeagueJoinRequest(_entityId, requestPayload ?? EmptyLeagueJoinRequestPayload.Instance));

            EntityId newDivision;

            if (response.Success)
            {
                joinedDivision = response.DivisionToJoin;
                newDivision    = joinedDivision.Value.ToEntityId();
            }
            else
            {
                refuseReason = response.RefuseReason;

                _log.Debug($"League join refused. Reason: {refuseReason}");
                return (null, refuseReason);
            }

            PlayerDivisionAvatarBase avatar = GetPlayerLeaguesDivisionAvatar();
            if (avatar == null)
                throw new NullReferenceException($"{nameof(GetPlayerLeaguesDivisionAvatar)} returned a null avatar.");

            // Send avatar to new division.
            if (newDivision.IsValid)
            {
                bool success = false;
                try
                {
                    InternalPlayerDivisionJoinOrUpdateAvatarResponse joinResponse =
                        await EntityAskAsync<InternalPlayerDivisionJoinOrUpdateAvatarResponse>(newDivision, new InternalPlayerDivisionJoinOrUpdateAvatarRequest(_entityId, avatarDataEpoch: 0, avatar, allowJoiningConcluded: false));

                    // Update model state.
                    ExecuteServerActionImmediately(new PlayerSetCurrentDivision(newDivision, joinResponse.DivisionParticipantIndex));
                    success = true;

                }
                catch(Exception ex)
                {
                    _log.Error(ex, $"Joining division {newDivision} failed.");
                }

                if (success)
                {
                    // Set player associated with the division.
                    AddEntityAssociation(
                        new AssociatedDivisionRef(
                            ClientSlotCore.PlayerDivision,
                            _entityId,
                            newDivision,
                            divisionAvatarEpoch: 0));

                    // Success
                    return (joinedDivision, null);
                }
            }

            // DivisionId was not valid or joining division failed.
            return (null, LeagueJoinRefuseReason.UnknownReason);
        }

        /// <summary>
        /// Try finalize the currently active concluded division. This will fetch the division's history entry for the player, and add it to the division history.
        /// If the division is not concluded or the current divison is not valid, this will return false.
        /// If <see cref="leaveDivision"/> is set, this will remove the division from the active session, and set currentdivision to null.
        /// Returns true if everything went well.
        /// </summary>
        /// <param name="leaveDivision">If this is set to true, the division will be removed from the active session, and currentdivision is set to null.</param>
        /// <exception cref="InvalidOperationException">If leagues feature is not enabled or player's division client state is not valid.</exception>
        protected async Task<bool> TryFinalizeCurrentPlayerLeagueDivision(bool leaveDivision)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnablePlayerLeagues)
                throw new InvalidOperationException("Cannot use leagues if leagues is not enabled.");

            if (!(Model.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionClientStateBase) &&
                    playerDivisionClientStateBase is IDivisionClientState divisionClientState))
                throw new InvalidOperationException($"PlayerModel does not have a valid {nameof(IDivisionClientState)}.");

            EntityId currentDivision = divisionClientState.CurrentDivision;
            if (!currentDivision.IsValid)
                return false; // No valid division.

            // If no history entry exists, add one.
            if (divisionClientState.HistoricalDivisions.All(division => division.DivisionId != currentDivision))
            {
                try
                {
                    InternalDivisionParticipantHistoryResponse historyResponse = await EntityAskAsync<InternalDivisionParticipantHistoryResponse>(currentDivision, new InternalDivisionParticipantHistoryRequest(_entityId));

                    if (historyResponse.IsParticipant && historyResponse.IsConcluded && historyResponse.HistoryEntry != null)
                    {
                        EnqueueServerAction(new PlayerAddHistoricalDivisionEntry(historyResponse.HistoryEntry, leaveDivision));
                        _log.Debug("Current division was concluded. Adding a history entry.");

                        if (leaveDivision) // Leave division
                        {
                            _log.Debug("Leaving division.");
                            RemoveEntityAssociation(ClientSlotCore.PlayerDivision);
                        }

                        return true;
                    }
                }
                catch(Exception ex)
                {
                    _log.Error(ex, "Unable to receive history state for current division {DivisionId}.", currentDivision);
                }
            }
            return false;
        }

        /// <summary>
        /// Handles division state updates on session start.
        /// If the player's current divison has concluded, the division is added to the division history.
        /// If the player has been reassigned to a new division,
        /// the old division is added to the division history and the player joins the new division.
        /// If the old division is still running, the player updates the the division with an updated avatar.
        /// </summary>
        async Task HandlePlayerLeaguesOnSessionSubscriber(IDivisionClientState divisionClientState, List<AssociatedEntityRefBase> associatedEntities)
        {
            EntityId currentDivision = divisionClientState.CurrentDivision;

            PersistedParticipantDivisionAssociation divisionAssociation = await MetaDatabase.Get().TryGetAsync<PersistedParticipantDivisionAssociation>(_entityId.ToString());

            EntityId expectedDivision = EntityId.None;

            if (divisionAssociation != null)
                expectedDivision = EntityId.ParseFromString(divisionAssociation.DivisionId);

            if (expectedDivision.IsValid && divisionClientState.HistoricalDivisions.Any(division => division.DivisionId == expectedDivision))
            {
                // History state has already been received for expected division. No need to join.

                if(currentDivision == expectedDivision) // Current division is expected division. Clear state.
                {
                    _log.Debug("Current division was not cleared on adding historical entry. Clearing now.");
                    divisionClientState.CurrentDivision = EntityId.None;
                }

                return;
            }

            // Check if current division has concluded without being reassigned.
            if (expectedDivision == currentDivision && currentDivision.IsValid)
            {
                if (await TryFinalizeCurrentPlayerLeagueDivision(leaveDivision: false))
                {
                    divisionClientState.CurrentDivision = EntityId.None;
                    return;
                }
            }

            // Has been reassigned by the league manager.
            if (expectedDivision != currentDivision)
            {
                // Leave current division if valid.
                if (currentDivision.IsValid)
                    await TryFinalizeCurrentPlayerLeagueDivision(leaveDivision: false);

                // Set current to new division
                divisionClientState.CurrentDivision = expectedDivision;
                currentDivision = expectedDivision;
            }

            // Send updated avatar to current division. If valid.
            if (currentDivision.IsValid)
            {
                PlayerDivisionAvatarBase avatar = GetPlayerLeaguesDivisionAvatar();
                if (avatar == null)
                    throw new NullReferenceException($"{nameof(GetPlayerLeaguesDivisionAvatar)} returned a null avatar.");

                bool success = false;
                try
                {
                    InternalPlayerDivisionJoinOrUpdateAvatarResponse joinResponse =
                        await EntityAskAsync<InternalPlayerDivisionJoinOrUpdateAvatarResponse>(currentDivision, new InternalPlayerDivisionJoinOrUpdateAvatarRequest(_entityId, avatarDataEpoch: 0, avatar, allowJoiningConcluded: false));
                    divisionClientState.CurrentDivisionParticipantIdx = joinResponse.DivisionParticipantIndex;

                    success = true;
                }
                catch
                {
                    _log.Warning("Unable to join or update avatar for division {DivisionId}. Division might be concluded already.", currentDivision);
                }

                if (success)
                {
                    // Set player associated with the division.
                    associatedEntities.Add(new AssociatedDivisionRef(ClientSlotCore.PlayerDivision, _entityId, expectedDivision, divisionAvatarEpoch: 0));
                }
            }
        }

        [MessageHandler]
        async Task HandleInternalLeagueParticipantDivisionForceUpdated(InternalLeagueParticipantDivisionForceUpdated message)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnablePlayerLeagues)
                return;

            if (!(Model.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionClientStateBase) &&
                    playerDivisionClientStateBase is IDivisionClientState divisionClientState))
                return;

            KickPlayerIfConnected(PlayerForceKickOwnerReason.AdminAction);

            divisionClientState.CurrentDivision = message.NewDivision;

            // Send avatar to new division.
            if (message.NewDivision.IsValid)
            {
                PlayerDivisionAvatarBase avatar = GetPlayerLeaguesDivisionAvatar();
                if (avatar == null)
                    throw new NullReferenceException($"{nameof(GetPlayerLeaguesDivisionAvatar)} returned a null avatar.");

                try
                {
                    InternalPlayerDivisionJoinOrUpdateAvatarResponse joinResponse =
                        await EntityAskAsync<InternalPlayerDivisionJoinOrUpdateAvatarResponse>(message.NewDivision, new InternalPlayerDivisionJoinOrUpdateAvatarRequest(_entityId, avatarDataEpoch: 0, avatar, allowJoiningConcluded: false));
                    divisionClientState.CurrentDivisionParticipantIdx = joinResponse.DivisionParticipantIndex;
                }
                catch (Exception ex)
                {
                    _log.Error(ex, $"Joining division {message.NewDivision} failed.");
                }
            }

            await OnDivisionForceUpdated(message.NewDivision);
        }

        /// <summary>
        /// Called when the player's division is forcibly updated via an admin action.
        /// Either added, removed, or updated.
        /// </summary>
        protected virtual Task OnDivisionForceUpdated(EntityId newDivision) => Task.CompletedTask;

        #endregion // Leagues
    }

    /// <summary>
    /// PlayerActorBase class to be used when PersistedPlayer requires no customization
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    public abstract class PlayerActorBase<TModel> : PlayerActorBase<TModel, PersistedPlayerBase>
        where TModel : class, IPlayerModel<TModel>, new()
    {
        protected PlayerActorBase(EntityId playerId) : base(playerId)
        {
        }

        protected sealed override PersistedPlayerBase CreatePersisted(EntityId entityId, DateTime persistedAt, byte[] payload, int schemaVersion, bool isFinal)
        {
            return new PersistedPlayerBase()
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
