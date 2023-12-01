// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using Metaplay.Server.DatabaseScan.User;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Server.DatabaseScan
{
    [Table("DatabaseScanWorkers")]
    public class PersistedDatabaseScanWorker : IPersistedEntity
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
        public byte[]   Payload         { get; set; }   // TaggedSerialized<DatabaseScanWorkerState>

        [Required]
        public int      SchemaVersion   { get; set; }   // Schema version for object

        [Required]
        public bool     IsFinal         { get; set; }
    }

    /// <summary>
    /// Description of the status of a some database scan work.
    /// This is stuff that workers report to others.
    /// </summary>
    [MetaSerializable]
    public class DatabaseScanWorkStatus
    {
        [MetaMember(1)] public DatabaseScanJobId                Id                      { get; private set; }
        [MetaMember(2)] public DatabaseScanWorkPhase            Phase                   { get; private set; }
        [MetaMember(3)] public DatabaseScanStatistics           ScanStatistics          { get; private set; }
        [MetaMember(4)] public DatabaseScanProcessingStatistics ProcessingStatistics    { get; private set; }
        [MetaMember(5)] public long                             StatusObservationIndex  { get; private set; } // For comparing different statuses of the same job from the same worker. Used to resolve the ordering of otherwise unordered status messages from the worker to the coordinator.

        public DatabaseScanWorkStatus(){ }
        public DatabaseScanWorkStatus(DatabaseScanJobId id, DatabaseScanWorkPhase phase, DatabaseScanStatistics scanStatistics, DatabaseScanProcessingStatistics processingStatistics, long statusObservationIndex)
        {
            Id                      = id;
            Phase                   = phase;
            ScanStatistics          = scanStatistics;
            ProcessingStatistics    = processingStatistics;
            StatusObservationIndex  = statusObservationIndex;
        }
    }

    /// <summary>
    /// Statistics about database scanning.
    /// </summary>
    [MetaSerializable]
    public class DatabaseScanStatistics
    {
        [MetaMember(1)] public int      NumItemsScanned             { get; set; } = 0;
        [MetaMember(2)] public float    ScannedRatioEstimate        { get; set; } = 0f;
        [MetaMember(3)] public int      NumWorkProcessorRecreations { get; set; } = 0;
        [MetaMember(4)] public int      NumSurrendered              { get; set; } = 0;
        [MetaMember(5)] public long     WorkerPersistTotalBytes     { get; set; } = 0;
        [MetaMember(6)] public int      WorkerPersistCount          { get; set; } = 0;

        public static DatabaseScanStatistics ComputeAggregate(IEnumerable<DatabaseScanStatistics> parts)
        {
            DatabaseScanStatistics aggregate = new DatabaseScanStatistics();

            foreach (DatabaseScanStatistics part in parts)
            {
                aggregate.NumItemsScanned               += part.NumItemsScanned;
                aggregate.ScannedRatioEstimate          += part.ScannedRatioEstimate; // \note ScannedRatioEstimate is not additive but a proportion; will be divided by count after this loop
                aggregate.NumWorkProcessorRecreations   += part.NumWorkProcessorRecreations;
                aggregate.NumSurrendered                += part.NumSurrendered;
                aggregate.WorkerPersistTotalBytes       += part.WorkerPersistTotalBytes;
                aggregate.WorkerPersistCount            += part.WorkerPersistCount;
            }

            if (parts.Count() > 0)
                aggregate.ScannedRatioEstimate /= parts.Count();

            return aggregate;
        }
    }

    /// <summary>
    /// Worker's phase for a database scan job.
    /// When initializing a new job, a worker is initially in the Paused phase.
    /// Then it can be commanded to move into the Running phase.
    /// </summary>
    [MetaSerializable]
    public enum DatabaseScanWorkPhase
    {
        /// <summary> Worker is paused, work is not yet finished. This is the initial phase. </summary>
        Paused = 2,
        /// <summary> Worker is scanning database, items are being processed. </summary>
        Running = 0,
        /// <summary>
        /// Worker is no longer working, either due to normal completion of its work
        /// or due to surrendering, and is just waiting to be stopped (i.e. told to forget the job).
        /// </summary>
        Finished = 1,
    }

    /// <summary>
    /// Represents the I'th one piece of a N-piece job, where I is <see cref="WorkerIndex"/>
    /// and N is <see cref="NumWorkers"/>.
    /// </summary>
    [MetaSerializable]
    public struct DatabaseScanWorkShard
    {
        [MetaMember(1)] public int WorkerIndex  { get; private set; }
        [MetaMember(2)] public int NumWorkers   { get; private set; }

        public DatabaseScanWorkShard(int workerIndex, int numWorkers)
        {
            WorkerIndex = workerIndex;
            NumWorkers  = numWorkers;
        }
    }

    /// <summary>
    /// Just a serializable duplicate of <see cref="Metaplay.Cloud.Persistence.PagedIterator"/>.
    /// </summary>
    // \todo [nuutti] We could just make Metaplay.Cloud.Persistence.PagedIterator MetaSerializable, but that'd
    //                mean we'd sort of guarantee its representation. Do we want to do that?
    [MetaSerializable]
    public class DatabaseIterator
    {
        [MetaMember(1)] public int     ShardIndex           { get; private set; } = 0;
        [MetaMember(2)] public string  StartKeyExclusive    { get; private set; } = "";
        [MetaMember(3)] public bool    IsFinished           { get; private set; } = false;

        [IgnoreDataMember] public PagedIterator PagedIterator => new PagedIterator(ShardIndex, StartKeyExclusive, IsFinished);

        public DatabaseIterator() { }
        DatabaseIterator(int shardIndex, string startKeyExclusive, bool isFinished) { ShardIndex = shardIndex; StartKeyExclusive = startKeyExclusive; IsFinished = isFinished; }
        public static DatabaseIterator FromPagedIterator(PagedIterator it) => new DatabaseIterator(it.ShardIndex, it.StartKeyExclusive, it.IsFinished);
    }

    /// <summary> The manner in which a worker is stopped. </summary>
    [MetaSerializable]
    public enum DatabaseScanWorkerStopFlavor
    {
        /// <summary> Worker is cleanly stopped because its phase is Finished. </summary>
        Finished,
        /// <summary> Worker is stopped due to a cancellation. This can be done regardless of the work phase. </summary>
        Cancel,
    }

    /// <summary>
    /// Commands a worker to initialize (the given shard of) the given job, if it's not already initialized.
    /// The worker is initially put into Paused phase.
    /// Represents also an assertion that the worker isn't currently doing any other job, and
    /// doesn't have the job in any phase other than Paused.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerEnsureInitialized, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerEnsureInitialized : MetaMessage
    {
        public DatabaseScanJobId        JobId   { get; private set; }
        public DatabaseScanJobSpec      JobSpec { get; private set; }
        public DatabaseScanWorkShard    Shard   { get; private set; }

        public DatabaseScanWorkerEnsureInitialized(){ }
        public DatabaseScanWorkerEnsureInitialized(DatabaseScanJobId jobId, DatabaseScanJobSpec jobSpec, DatabaseScanWorkShard shard)
        {
            JobId   = jobId;
            JobSpec = jobSpec;
            Shard   = shard;
        }
    }

    /// <summary>
    /// Response to <see cref="DatabaseScanWorkerEnsureInitialized"/>, reporting also current status.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerInitializedOk, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerInitializedOk : MetaMessage
    {
        public DatabaseScanWorkStatus ActiveJobStatus { get; private set; }

        public DatabaseScanWorkerInitializedOk(){ }
        public DatabaseScanWorkerInitializedOk(DatabaseScanWorkStatus activeJobStatus) { ActiveJobStatus = activeJobStatus; }
    }

    /// <summary>
    /// Commands a worker to resume after it's been paused with <see cref="DatabaseScanWorkerEnsurePaused"/>,
    /// if it's not already resumed.
    /// Asserts that the worker currently has a job.
    /// Resuming only has an effect if the job's work phase is Paused;
    /// resuming a Finished or Running job doesn't change anything.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerEnsureResumed, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerEnsureResumed : MetaMessage
    {
        public static readonly DatabaseScanWorkerEnsureResumed Instance = new DatabaseScanWorkerEnsureResumed();
    }

    /// <summary>
    /// Response to <see cref="DatabaseScanWorkerEnsureResumed"/>, reporting also status just after reacting to the resume command.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerResumedOk, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerResumedOk : MetaMessage
    {
        public DatabaseScanWorkStatus ActiveJobStatus { get; private set; }

        public DatabaseScanWorkerResumedOk(){ }
        public DatabaseScanWorkerResumedOk(DatabaseScanWorkStatus activeJobStatus) { ActiveJobStatus = activeJobStatus; }
    }

    /// <summary>
    /// Commands a worker to pause the current job, if it's not already paused.
    /// Asserts that the worker currently has a job.
    /// Pausing only has an effect if the job's work phase is Running;
    /// pausing a Finished or Paused job doesn't change anything.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerEnsurePaused, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerEnsurePaused : MetaMessage
    {
        public static readonly DatabaseScanWorkerEnsurePaused Instance = new DatabaseScanWorkerEnsurePaused();
    }

    /// <summary>
    /// Response to <see cref="DatabaseScanWorkerEnsurePaused"/>, reporting also status just after reacting to the pause command.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerPausedOk, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerPausedOk : MetaMessage
    {
        public DatabaseScanWorkStatus ActiveJobStatus { get; private set; }

        public DatabaseScanWorkerPausedOk(){ }
        public DatabaseScanWorkerPausedOk(DatabaseScanWorkStatus activeJobStatus) { ActiveJobStatus = activeJobStatus; }
    }

    /// <summary>
    /// Commands a worker to stop its active job, if any.
    /// Also, the given stop flavor can place assertions on the worker's state.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerEnsureStopped, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerEnsureStopped : MetaMessage
    {
        public DatabaseScanWorkerStopFlavor StopFlavor { get; private set; }

        public DatabaseScanWorkerEnsureStopped(){ }
        public DatabaseScanWorkerEnsureStopped(DatabaseScanWorkerStopFlavor stopFlavor)
        {
            StopFlavor = stopFlavor;
        }
    }

    /// <summary>
    /// Response to <see cref="DatabaseScanWorkerEnsureStopped"/>, reporting also status, if any, just before reacting to the stop command.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerStoppedOk, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerStoppedOk : MetaMessage
    {
        /// <summary>
        /// Status of worker's active job just before reacting to the stop command, if any; null if none.
        /// </summary>
        public DatabaseScanWorkStatus ActiveJobJustBefore { get; private set; }

        public DatabaseScanWorkerStoppedOk(){ }
        public DatabaseScanWorkerStoppedOk(DatabaseScanWorkStatus activeJobJustBefore)
        {
            ActiveJobJustBefore = activeJobJustBefore;
        }
    }

    /// <summary>
    /// Dummy message occasionally sent by coordinator to ensure that workers are woken up.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerEnsureAwake, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerEnsureAwake : MetaMessage
    {
        public static readonly DatabaseScanWorkerEnsureAwake Instance = new DatabaseScanWorkerEnsureAwake();
    }

    /// <summary>
    /// Occasionally sent by worker to coordinator, while a job is active,
    /// to report its status.
    /// </summary>
    [MetaMessage(MessageCodesCore.DatabaseScanWorkerStatusReport, MessageDirection.ServerInternal)]
    public class DatabaseScanWorkerStatusReport : MetaMessage
    {
        /// <summary>
        /// Status of worker's currently-active job, if any; null if none.
        /// </summary>
        public DatabaseScanWorkStatus ActiveJob { get; private set; }

        public DatabaseScanWorkerStatusReport(){ }
        public DatabaseScanWorkerStatusReport(DatabaseScanWorkStatus activeJob) { ActiveJob = activeJob; }
    }

    /// <summary>
    /// State of database scan work for a specific job.
    /// </summary>
    [MetaSerializable]
    public class DatabaseScanJobWorkState
    {
        [MetaMember(1)] public DatabaseScanJobId        Id                              { get; private set; }
        [MetaMember(2)] public DatabaseScanJobSpec      Spec                            { get; private set; }
        [MetaMember(3)] public DatabaseScanWorkShard    WorkShard                       { get; private set; }
        [MetaMember(4)] public DatabaseScanWorkPhase    Phase                           { get; set; }
        [MetaMember(5)] public DatabaseIterator         Iterator                        { get; set; }
        [MetaMember(10)] public int                     ExplicitListIndex               { get; set; }
        [MetaMember(6)] public DatabaseScanStatistics   ScanStatistics                  { get; private set; }
        [MetaMember(7)] public DatabaseScanProcessor    Processor                       { get; set; }
        [MetaMember(8)] public int                      CrashStallCounter               { get; set; }
        [MetaMember(9)] public long                     RunningStatusObservationIndex   { get; private set; }

        public DatabaseScanWorkStatus ObserveStatus()
        {
            long statusObservationIndex = RunningStatusObservationIndex++;
            return new DatabaseScanWorkStatus(Id, Phase, ScanStatistics, Processor.Stats, statusObservationIndex);
        }

        public bool HasMoreItemsToScan
        {
            get
            {
                if (Spec.ExplicitEntityList != null)
                    return ExplicitListIndex < Spec.ExplicitEntityList.Count;
                else
                    return !Iterator.IsFinished;
            }
        }

        public DatabaseScanJobWorkState(){ }
        public DatabaseScanJobWorkState(DatabaseScanJobId id, DatabaseScanJobSpec spec, DatabaseScanWorkShard workShard)
        {
            Id                  = id;
            Spec                = spec;
            WorkShard           = workShard;
            Phase               = DatabaseScanWorkPhase.Paused;
            Iterator            = new DatabaseIterator();
            ExplicitListIndex   = 0;
            ScanStatistics      = new DatabaseScanStatistics();
            Processor           = spec.CreateProcessor(initialStatisticsMaybe: null);
            CrashStallCounter   = 0;
        }
    }

    /// <summary>
    /// State of a database scan worker.
    /// </summary>
    [MetaSerializable]
    [SupportedSchemaVersions(1, 1)]
    public class DatabaseScanWorkerState : ISchemaMigratable
    {
        [MetaMember(1)] public DatabaseScanJobWorkState ActiveJob { get; set; } = null;

        public DatabaseScanWorkerState(){ }

        #region Schema migrations

        // No migrations

        #endregion
    }

    [EntityConfig]
    internal sealed class DatabaseScanWorkerConfig : PersistedEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.DatabaseScanWorker;
        public override Type                EntityActorType         => typeof(DatabaseScanWorkerActor);
        public override IShardingStrategy   ShardingStrategy        => new HashedShardingStrategy(Application.Instance.ClusterConfig, EntityKindCloudCore.DatabaseScanWorker);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Actor that does database scan work, upon command from <see cref="DatabaseScanCoordinatorActor"/>.
    /// Each worker performs the work for a subset (shard) of the job,
    /// i.e. a subset of a database (in practice, a range of the entity id value space).
    ///
    /// <para>
    /// Workers are coordinated by the coordinator. To ensure a consistent state in the
    /// coordinator-workers-system, the worker shall persist its state when its state
    /// changes in a relevant way (e.g. when a job is initialized, or when the job phase changes).
    /// For example, when the worker is told to initialize a job, it shall make sure that
    /// the initialized job state is persisted before responding "OK".
    /// </para>
    /// </summary>
    /// <remarks>
    /// Database job work code speaks of two kinds of "shards":
    /// the work shards of these sharded database scan jobs,
    /// and the shards of shardy database.
    /// These are unrelated to each other, don't get confused.
    /// </remarks>
    public class DatabaseScanWorkerActor : PersistedEntityActor<PersistedDatabaseScanWorker, DatabaseScanWorkerState>, DatabaseScanProcessor.IContext
    {
        class ActiveJobUpdate { public static ActiveJobUpdate Instance = new ActiveJobUpdate(); }

        static readonly Prometheus.Counter c_itemsQueried = Prometheus.Metrics.CreateCounter("game_databasescan_items_queried_total", "Number of items queried by database scan workers (by job tag)", "job_tag");

        class ActiveJobRuntimeState
        {
            public DateTime                 NextScanAt;
            public DateTime                 NextProcessorTickAt;
            public DateTime                 NextPersistAt;
            public DateTime                 NextStatusReportAt;

            public ActiveJobRuntimeState(DateTime nextScanAt, DateTime nextProcessorTickAt, DateTime nextPersistAt, DateTime nextStatusReportAt)
            {
                NextScanAt          = nextScanAt;
                NextProcessorTickAt = nextProcessorTickAt;
                NextPersistAt       = nextPersistAt;
                NextStatusReportAt  = nextStatusReportAt;
            }
        }

        protected override sealed AutoShutdownPolicy    ShutdownPolicy                      => AutoShutdownPolicy.ShutdownNever();
        protected override sealed TimeSpan              SnapshotInterval                    => TimeSpan.FromMinutes(3);

        static TimeSpan                     StatusReportInterval                            = TimeSpan.FromSeconds(1);

        const int                           MaxCrashStallCounterBeforeProcessorRecreation   = 2;
        const int                           MaxCrashStallCounterBeforeSurrender             = 6;

        DatabaseScanWorkerState             _state;
        ActiveJobRuntimeState               _activeJobRuntimeState;

        public DatabaseScanWorkerActor(EntityId entityId) : base(entityId)
        {
        }

        protected override sealed async Task Initialize()
        {
            // Try to fetch from database & restore from it (if exists)
            PersistedDatabaseScanWorker persisted = await MetaDatabase.Get(QueryPriority.Normal).TryGetAsync<PersistedDatabaseScanWorker>(_entityId.ToString());
            await InitializePersisted(persisted);
        }

        protected override sealed Task<DatabaseScanWorkerState> InitializeNew()
        {
            // Create new state
            DatabaseScanWorkerState state = new DatabaseScanWorkerState();

            return Task.FromResult(state);
        }

        protected override sealed Task<DatabaseScanWorkerState> RestoreFromPersisted(PersistedDatabaseScanWorker persisted)
        {
            // Deserialize actual state
            DatabaseScanWorkerState state = DeserializePersistedPayload<DatabaseScanWorkerState>(persisted.Payload, resolver: null, logicVersion: null);

            return Task.FromResult(state);
        }

        protected override sealed async Task PostLoad(DatabaseScanWorkerState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _state = payload;

            DatabaseScanJobWorkState activeJob = _state.ActiveJob;

            if (activeJob != null)
            {
                _log.Info("PostLoad: active job: id={JobId}, phase={JobPhase}, specType={JobSpecType}", activeJob.Id, activeJob.Phase, activeJob.Spec.GetType().Name);
                InitActiveJobRuntimeState(activeJob, _self);

                if (activeJob.Phase == DatabaseScanWorkPhase.Running)
                {
                    activeJob.CrashStallCounter++;
                    await PersistStateIntermediate();

                    if (activeJob.CrashStallCounter > MaxCrashStallCounterBeforeSurrender)
                    {
                        _log.Warning("PostLoad: CrashStallCounter is {CrashStallCounter}, surrendering (limit is {MaxCrashStallCounterBeforeSurrender})", activeJob.CrashStallCounter, MaxCrashStallCounterBeforeSurrender);
                        activeJob.Phase = DatabaseScanWorkPhase.Finished;
                        activeJob.ScanStatistics.NumSurrendered = 1;
                        await PersistStateIntermediate();
                    }
                    else if (activeJob.CrashStallCounter > MaxCrashStallCounterBeforeProcessorRecreation)
                    {
                        _log.Warning("PostLoad: CrashStallCounter is {CrashStallCounter}, creating new processor (limit is {MaxCrashStallCounterBeforeProcessorRecreation})", activeJob.CrashStallCounter, MaxCrashStallCounterBeforeProcessorRecreation);
                        DatabaseScanProcessor newProcessor = activeJob.Spec.CreateProcessor(activeJob.Processor.Stats);
                        activeJob.Processor = newProcessor;
                        activeJob.ScanStatistics.NumWorkProcessorRecreations++;
                        await PersistStateIntermediate();
                    }
                    else
                        _log.Debug("PostLoad: CrashStallCounter is {CrashStallCounter}", activeJob.CrashStallCounter);
                }
            }
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            // Serialize and compress the state
            byte[] persistedPayload = SerializeToPersistedPayload(_state, resolver: null, logicVersion: null);

            if (_state.ActiveJob != null)
            {
                DatabaseScanJobWorkState activeJob = _state.ActiveJob;
                activeJob.ScanStatistics.WorkerPersistCount++;
                activeJob.ScanStatistics.WorkerPersistTotalBytes += persistedPayload.Length;
            }

            // Persist in database
            PersistedDatabaseScanWorker persisted = new PersistedDatabaseScanWorker
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

        [EntityAskHandler]
        private async Task HandleDatabaseScanWorkerEnsureInitialized(EntityAsk ask, DatabaseScanWorkerEnsureInitialized ensureInitialized)
        {
            if (_state.ActiveJob != null)
            {
                if (_state.ActiveJob.Id == ensureInitialized.JobId)
                {
                    if (_state.ActiveJob.Phase == DatabaseScanWorkPhase.Paused)
                        _log.Info("Got a request to initialize job {JobId}, we already have it", ensureInitialized.JobId);
                    else
                        throw new InvalidOperationException($"Got a request to initialize job {ensureInitialized.JobId}, but we already have it in {_state.ActiveJob.Phase} phase");
                }
                else
                    throw new InvalidOperationException($"Got a request to initialize job {ensureInitialized.JobId}, but we already have a different job {_state.ActiveJob.Id}");
            }
            else
            {
                _state.ActiveJob = new DatabaseScanJobWorkState(ensureInitialized.JobId, ensureInitialized.JobSpec, ensureInitialized.Shard);
                await PersistStateIntermediate();
                InitActiveJobRuntimeState(_state.ActiveJob, _self);
                _log.Info("Initialized new job {JobId}", ensureInitialized.JobId);
            }

            ReplyToAsk(ask, new DatabaseScanWorkerInitializedOk(_state.ActiveJob.ObserveStatus()));
        }

        [EntityAskHandler]
        private async Task HandleDatabaseScanWorkerEnsureResumed(EntityAsk ask, DatabaseScanWorkerEnsureResumed _)
        {
            if (_state.ActiveJob != null)
            {
                DatabaseScanJobWorkState job = _state.ActiveJob;

                if (job.Phase == DatabaseScanWorkPhase.Paused)
                {
                    job.Phase = DatabaseScanWorkPhase.Running;
                    await PersistStateIntermediate();
                    _log.Info("Resumed job {JobId}", job.Id);
                }
                else if (job.Phase == DatabaseScanWorkPhase.Finished)
                {
                    _log.Info("Got a request to resume job, ours ({JobId}) is already Finished", job.Id);
                }
                else
                {
                    MetaDebug.Assert(job.Phase == DatabaseScanWorkPhase.Running, "Unknown work phase {0}", job.Phase);
                    _log.Info("Got a request to resume job, ours ({JobId}) is already Running", job.Id);
                }
            }
            else
                throw new InvalidOperationException("Got a request to resume job, but we have no job");

            ReplyToAsk(ask, new DatabaseScanWorkerResumedOk(_state.ActiveJob.ObserveStatus()));
        }

        [EntityAskHandler]
        private async Task HandleDatabaseScanWorkerEnsurePaused(EntityAsk ask, DatabaseScanWorkerEnsurePaused _)
        {
            if (_state.ActiveJob != null)
            {
                DatabaseScanJobWorkState job = _state.ActiveJob;

                if (job.Phase == DatabaseScanWorkPhase.Running)
                {
                    job.Phase = DatabaseScanWorkPhase.Paused;
                    await PersistStateIntermediate();
                    _log.Info("Paused job {JobId}", job.Id);
                }
                else if (job.Phase == DatabaseScanWorkPhase.Finished)
                {
                    _log.Info("Got a request to pause job, ours ({JobId}) is already Finished", job.Id);
                }
                else
                {
                    MetaDebug.Assert(job.Phase == DatabaseScanWorkPhase.Paused, "Unknown work phase {0}", job.Phase);
                    _log.Info("Got a request to pause job, ours ({JobId}) is already Paused", job.Id);
                }
            }
            else
                throw new InvalidOperationException("Got a request to pause job, but we have no job");

            ReplyToAsk(ask, new DatabaseScanWorkerPausedOk(_state.ActiveJob.ObserveStatus()));
        }

        [CommandHandler]
        private async Task HandleActiveJobUpdate(ActiveJobUpdate _)
        {
            DatabaseScanJobWorkState job = _state.ActiveJob;
            if (job == null)
                return;

            if (job.Phase == DatabaseScanWorkPhase.Running)
                await UpdateRunningJob();

            DateTime currentTime = DateTime.UtcNow;
            if (currentTime >= _activeJobRuntimeState.NextStatusReportAt)
            {
                CastMessage(DatabaseScanCoordinatorActor.EntityId, new DatabaseScanWorkerStatusReport(job.ObserveStatus()));
                _activeJobRuntimeState.NextStatusReportAt = currentTime + StatusReportInterval;
            }

            ScheduleNextActiveJobUpdate(_self);
        }

        void ScheduleNextActiveJobUpdate(Akka.Actor.IActorRef self)
        {
            DateTime nextUpdateAt;

            // Certain timers only apply in the Running phase.
            if (_state.ActiveJob.Phase == DatabaseScanWorkPhase.Running)
            {
                nextUpdateAt = Util.Min(
                    _activeJobRuntimeState.NextProcessorTickAt,
                    _activeJobRuntimeState.NextScanAt,
                    _activeJobRuntimeState.NextPersistAt,
                    _activeJobRuntimeState.NextStatusReportAt);
            }
            else
            {
                nextUpdateAt = _activeJobRuntimeState.NextStatusReportAt;
            }

            TimeSpan timeUntilNextUpdate = Util.Max(TimeSpan.Zero, nextUpdateAt - DateTime.UtcNow);

            // Regardless of phase, make sure that updates happen every
            // now and then, to ensure that the update scheduling gets
            // back on track when the phase is changed to Running, such
            // as when resuming a paused job.
            // Note that the ActiveJobUpdate handler itself is the place
            // where ScheduleNextActiveJobUpdate is called from.
            timeUntilNextUpdate = Util.Min(timeUntilNextUpdate, TimeSpan.FromSeconds(1));

            Context.System.Scheduler.ScheduleTellOnce(
                delay:          timeUntilNextUpdate,
                receiver:       self,
                message:        ActiveJobUpdate.Instance,
                sender:         self);
        }

        async Task<IEnumerable<IPersistedEntity>> ScanNextBatch()
        {
            DatabaseScanJobWorkState job = _state.ActiveJob;
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Low);
            IEnumerable<IPersistedEntity> items;
            float? scannedRatioEstimate;

            if (job.Spec.ExplicitEntityList != null)
            {
                EntityId entityId = job.Spec.ExplicitEntityList[job.ExplicitListIndex++];
                PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(job.Spec.EntityKind);
                IPersistedEntity entity = await db.TryGetAsync<IPersistedEntity>(entityConfig.PersistedType, entityId.ToString());
                items = Enumerable.Repeat(entity, entity == null ? 0 : 1);
                scannedRatioEstimate = (float)job.ExplicitListIndex / job.Spec.ExplicitEntityList.Count;
            }
            else
            {
                EntityIdRange entityIdRange = EntityIdRangeFromWorkShard(job.Spec.EntityKind, job.Spec.EntityIdValueUpperBound, job.WorkShard);
                PagedQueryResult<IPersistedEntity> queryResult = await QueryPagedRangeAsync(
                    db,
                    job.Spec.EntityKind,
                    job.Spec.DatabaseQueryOpName,
                    job.Iterator.PagedIterator,
                    job.Processor.DesiredScanBatchSize,
                    entityIdRange.FirstInclusive.ToString(),
                    entityIdRange.LastInclusive.ToString());

                job.Iterator = DatabaseIterator.FromPagedIterator(queryResult.Iterator);
                items = queryResult.Items;
                scannedRatioEstimate = TryComputeScannedRatioEstimate(entityIdRange, queryResult, db.NumActiveShards);
            }

            int itemCount = items.Count();
            job.ScanStatistics.NumItemsScanned += itemCount;
            c_itemsQueried.WithLabels(job.Spec.MetricsTag).Inc(itemCount);
            if (scannedRatioEstimate.HasValue)
                job.ScanStatistics.ScannedRatioEstimate = scannedRatioEstimate.Value;

            return items;
        }

        async Task UpdateRunningJob()
        {
            DatabaseScanJobWorkState job = _state.ActiveJob;
            ActiveJobRuntimeState runtimeState = _activeJobRuntimeState;
            DateTime updateStartTime = DateTime.UtcNow;

            // Consider scanning more, if the time has come
            if (updateStartTime >= runtimeState.NextScanAt)
            {
                runtimeState.NextScanAt = updateStartTime + job.Processor.ScanInterval;

                // Scan if we've still got something to scan, and the processor can deal with more work.
                if (job.Processor.CanCurrentlyProcessMoreItems && job.HasMoreItemsToScan)
                {
                    IEnumerable<IPersistedEntity> batch = await ScanNextBatch();

                    if (batch.Any())
                        await job.Processor.StartProcessItemBatchAsync(this, batch);

                    if (job.CrashStallCounter > 0)
                    {
                        _log.Info("Resetting CrashStallCounter from {CrashStallCounter}", job.CrashStallCounter);
                        job.CrashStallCounter = 0;
                        await PersistStateIntermediate();
                    }
                }
            }

            // Tick the processor, if the time has come.
            if (updateStartTime >= runtimeState.NextProcessorTickAt)
            {
                runtimeState.NextProcessorTickAt = updateStartTime + job.Processor.TickInterval;
                await job.Processor.TickAsync(this);
            }

            // If we've scanned everything and processor is done, finish.
            // Otherwise, just persist periodically.
            if (!job.HasMoreItemsToScan && job.Processor.HasCompletedAllWorkSoFar)
            {
                job.Phase = DatabaseScanWorkPhase.Finished;
                await PersistStateIntermediate();
                _log.Info("Finished job {JobId}", job.Id);
            }
            else
            {
                if (updateStartTime >= runtimeState.NextPersistAt)
                {
                    await PersistStateIntermediate();
                    runtimeState.NextPersistAt = updateStartTime + job.Processor.PersistInterval;
                }
            }
        }

        /// <summary>
        /// Invoke <see cref="MetaDatabase.QueryPagedRangeAsync{T}(string, PagedIterator, int, string, string)"/>
        /// on <paramref name="db"/>, using the EntityKind's type, and up-convert the result.
        /// </summary>
        public async Task<PagedQueryResult<IPersistedEntity>> QueryPagedRangeAsync(MetaDatabase db, EntityKind entityKind, string opName, PagedIterator iterator, int pageSize, string firstKeyInclusive, string lastKeyInclusive)
        {
            PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(entityKind);
            return await DatabaseScanUtil.QueryPagedRangeAsync(db, entityConfig.PersistedType, opName, iterator, pageSize, firstKeyInclusive, lastKeyInclusive);
        }

        [EntityAskHandler]
        private async Task HandleDatabaseScanWorkerEnsureStopped(EntityAsk ask, DatabaseScanWorkerEnsureStopped ensureStopped)
        {
            DatabaseScanWorkStatus activeJobStatusJustBefore;

            if (_state.ActiveJob != null)
            {
                DatabaseScanJobWorkState job = _state.ActiveJob;

                activeJobStatusJustBefore = job.ObserveStatus();

                switch (ensureStopped.StopFlavor)
                {
                    case DatabaseScanWorkerStopFlavor.Finished:
                        if (job.Phase != DatabaseScanWorkPhase.Finished)
                            throw new InvalidOperationException($"Got a request to stop Finished job but ours ({job.Id}) has phase {job.Phase}");
                        break;

                    case DatabaseScanWorkerStopFlavor.Cancel:
                        if (job.Phase != DatabaseScanWorkPhase.Finished)
                        {
                            MetaDebug.Assert(job.Phase == DatabaseScanWorkPhase.Running
                                          || job.Phase == DatabaseScanWorkPhase.Paused,
                                "Unknown work phase {0}", job.Phase);

                            job.Processor.Cancel(this);
                        }
                        break;

                    default:
                        throw new MetaAssertException($"Invalid DatabaseScanWorkerStopFlavor: {ensureStopped.StopFlavor}");
                }

                _state.ActiveJob = null;
                await PersistStateIntermediate();
                DeinitActiveJobRuntimeState();

                _log.Info("Stopped job {JobId}, stop flavor {StopFlavor}", job.Id, ensureStopped.StopFlavor);
            }
            else
            {
                activeJobStatusJustBefore = null;
                _log.Info("Got request to stop job, we already have no job");
            }

            ReplyToAsk(ask, new DatabaseScanWorkerStoppedOk(activeJobStatusJustBefore));
        }

        [MessageHandler]
        private void HandleDatabaseScanWorkerEnsureAwake(DatabaseScanWorkerEnsureAwake _)
        {
            // thanks, i'm awake now
        }

        void InitActiveJobRuntimeState(DatabaseScanJobWorkState job, Akka.Actor.IActorRef self)
        {
            DateTime currentTime = DateTime.UtcNow;

            float offsetFactor = (float)job.WorkShard.WorkerIndex / (float)job.WorkShard.NumWorkers;

            _activeJobRuntimeState = new ActiveJobRuntimeState(
                nextScanAt:             currentTime + offsetFactor*job.Processor.ScanInterval,
                nextProcessorTickAt:    currentTime + offsetFactor*job.Processor.TickInterval,
                nextPersistAt:          currentTime + offsetFactor*job.Processor.PersistInterval,
                nextStatusReportAt:     currentTime + offsetFactor*StatusReportInterval);

            // Start the periodic ActiveJobUpdates. The ActiveJobUpdate handler itself
            // will schedule the next ActiveJobUpdate tell.
            self.Tell(ActiveJobUpdate.Instance, sender: self);
        }

        void DeinitActiveJobRuntimeState()
        {
            _activeJobRuntimeState = null;
        }

        IMetaLogger DatabaseScanProcessor.IContext.Log => _log;

        void DatabaseScanProcessor.IContext.ActorContinueTaskOnActorContext<TResult>(Task<TResult> asyncTask, Action<TResult> handleSuccess, Action<Exception> handleFailure)
        {
            ContinueTaskOnActorContext(asyncTask, handleSuccess, handleFailure);
        }

        Task<TResult> DatabaseScanProcessor.IContext.ActorEntityAskAsync<TResult>(EntityId targetEntityId, MetaMessage message)
        {
            return EntityAskAsync<TResult>(targetEntityId, message);
        }

        static float? TryComputeScannedRatioEstimate(EntityIdRange range, PagedQueryResult<IPersistedEntity> latestQueryResult, int numDatabaseShards)
        {
            if (latestQueryResult.Iterator.IsFinished)
                return 1f;
            else if (latestQueryResult.Items.Count == 0)
                return null;
            else
            {
                EntityId    latestEntityId      = EntityId.ParseFromString(latestQueryResult.Items.Last().EntityId);
                float       subShardRatio       = ComputeEntityIdDoneRatioInRange(range, latestEntityId);
                float       shardsDoneEstimate  = (float)latestQueryResult.Iterator.ShardIndex + subShardRatio;
                float       doneRatio           = shardsDoneEstimate / (float)numDatabaseShards;

                return MathF.Min(1f, doneRatio);
            }
        }

        static float ComputeEntityIdDoneRatioInRange(EntityIdRange range, EntityId doneEntityId)
        {
            ulong total = range.LastInclusive.Value - range.FirstInclusive.Value + 1;
            ulong done  = doneEntityId.Value - range.FirstInclusive.Value + 1;

            // For division, scale the numbers to a desired range to ensure they're not too big for float

            const int   DesiredBits = 20;
            int         numBits     = 32 - BitOperations.LeadingZeroCount(total);
            int         shift       = Math.Max(0, numBits - DesiredBits);

            return (float)(done >> shift) / (float)(total >> shift);
        }

        static EntityIdRange EntityIdRangeFromWorkShard(EntityKind kind, ulong entityIdValueUpperBound, DatabaseScanWorkShard workShard)
        {
            ulong firstValue    = ShardFirstEntityIdValueImpl(entityIdValueUpperBound, workShard.WorkerIndex, workShard.NumWorkers);
            ulong lastValue     = ShardFirstEntityIdValueImpl(entityIdValueUpperBound, workShard.WorkerIndex + 1, workShard.NumWorkers) - 1; // A shard's last is equal to the next shard's first minus one.

            return new EntityIdRange(kind, firstValue, lastValue);
        }

        static ulong ShardFirstEntityIdValueImpl(ulong entityIdValueUpperBound, int numeratorInt, int denominatorInt)
        {
            // \note ulong isn't enough for the calculation's intermediate values when numerator is large-ish.
            //       We could be smart about it somehow but this is ok, we're not gonna be doing this often anyway.
            BigInteger numerator    = new BigInteger(numeratorInt);
            BigInteger denominator  = new BigInteger(denominatorInt);
            BigInteger result       = numerator * entityIdValueUpperBound / denominator;

            return (ulong)result;
        }
    }
}
