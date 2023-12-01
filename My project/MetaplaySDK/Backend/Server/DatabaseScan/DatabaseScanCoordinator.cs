// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.DatabaseScan.User;
using Metaplay.Server.Database;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;
using static System.FormattableString;
using Metaplay.Cloud.Sharding;

namespace Metaplay.Server.DatabaseScan
{
    [Table("DatabaseScanCoordinators")]
    public class PersistedDatabaseScanCoordinator : IPersistedEntity
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
        public byte[]   Payload         { get; set; }   // TaggedSerialized<DatabaseScanCoordinatorState>

        [Required]
        public int      SchemaVersion   { get; set; }   // Schema version for object

        [Required]
        public bool     IsFinal         { get; set; }
    }

    /// <summary>
    /// Unique identifier for a job.
    /// Just a running id should be sufficient, but this contains also
    /// a random component for debuggability in case of distributedness
    /// bugs between coordinator and workers.
    ///
    /// To be clear, it is a bug in the coordinator+workers system
    /// if any two different jobs only differ in their <see cref="RandomTag"/>s
    /// and not their <see cref="SequentialId"/>s.
    /// </summary>
    [MetaSerializable]
    public struct DatabaseScanJobId
    {
        [MetaMember(1)] public int  SequentialId    { get; private set; }
        [MetaMember(2)] public uint RandomTag       { get; private set; }

        public DatabaseScanJobId(int sequentialId, uint randomTag)
        {
            SequentialId    = sequentialId;
            RandomTag       = randomTag;
        }

        public static DatabaseScanJobId CreateNew(int sequentialId)
        {
            return new DatabaseScanJobId(
                sequentialId:   sequentialId,
                randomTag:      (uint)Guid.NewGuid().GetHashCode());
        }

        public static bool operator ==(DatabaseScanJobId a, DatabaseScanJobId b)
        {
            return a.SequentialId   == b.SequentialId
                && a.RandomTag      == b.RandomTag;
        }
        public static bool operator !=(DatabaseScanJobId a, DatabaseScanJobId b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            return (obj is DatabaseScanJobId other) && other == this;
        }

        public override int GetHashCode()
        {
            ulong idAsULong = ((ulong)SequentialId << 32) | (ulong)RandomTag;
            return idAsULong.GetHashCode();
        }

        public override string ToString()
        {
            return Invariant($"DatabaseScanJobId(sequentialId: {SequentialId}, randomTag: {RandomTag})");
        }
    }

    /// <summary>
    /// Represents a type of <see cref="DatabaseScanJobManager"/>.
    /// In the future we could make things more automagical and agnostic by using reflection.
    /// But for now we're using this lo-tech manner of identifying which manager a certain job came from.
    ///
    /// Note that you'll need to update <see cref="DatabaseScanCoordinatorActor.GetJobManagerOfKind"/> when you add values here.
    /// </summary>
    [MetaSerializable]
    public enum DatabaseScanJobManagerKind
    {
        NotificationCampaign = 0,
        ScheduledPlayerDeletion = 1,
        MaintenanceJobManager = 2,

        TestManager = 99,

        // Please add any game-specific manager kinds here with their numbers starting from 1000.
        // This will help mitigate future conflicts.
        // In the future there will hopefully be a cleaner way of separating game-specific managers to their own place.
        //
        // Note that you'll need to update DatabaseScanCoordinatorActor.GetJobManagerOfKind when you add values here.
        //SomeGameSpecificManager = 1000,
        //...
    }

    /// <summary>
    /// Phase of coordination of a specific database scan job.
    /// <para>
    /// A job starts in the Initializing phase, which initializes each worker for the job,
    /// putting them in Paused work phase initially. Then the job moves to the Paused phase.
    /// </para>
    /// <para>
    /// Unless global pause is enabled, the following automatic pausing/resuming behavior applies:
    /// If a job has precedence over all other active jobs and is Paused, it'll go Paused -> Resuming -> Running.
    /// If a job doesn't have precedence over all other active jobs and is Running, it'll go Running -> Pausing -> Paused.
    /// </para>
    /// <para>
    /// If global pause is enabled, then all Running jobs will go Running -> Pausing -> Paused,
    /// including the highest-precedence job.
    /// </para>
    /// <para>
    /// When all of the workers of a job are Finished, the job will go Running -> Stopping and
    /// will then be removed.
    /// Additionally, an active job can be put to the Cancelling phase, except if
    /// it's already Stopping or Cancelling.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum DatabaseScanCoordinationPhase
    {
        /// <summary> Workers are being initialized (initially in the Paused work phase) for the job by coordinator. </summary>
        Initializing = 0,
        /// <summary> Workers are all paused. </summary>
        Paused = 5,
        /// <summary> Workers are being resumed from pause by coordinator. </summary>
        Resuming = 6,
        /// <summary> Workers are all active with the job. </summary>
        Running = 1,
        /// <summary> Workers are being paused by coordinator. </summary>
        Pausing = 4,
        /// <summary> Workers are finished and being stopped from the job by coordinator. </summary>
        Stopping = 2,
        /// <summary> Workers are being stopped from the job by coordinator due to cancellation. </summary>
        Cancelling = 3,

        // Below is a dot graph describing the possible phase transitions.
        // The "removal" node is not a phase, but means that the job has ended
        // and is no longer active.
        // Note that the Initializing phase can end in removal as well,
        // if the job could not be started due to conditions not matching
        // (e.g. notifications are scheduled but Push Notifications were disabled during a restart)
        /*
        digraph
        {
            Initializing -> {Paused Cancelling}
            Paused -> {Resuming Cancelling}
            Resuming -> {Running Cancelling}
            Running -> {Pausing Stopping Cancelling}
            Pausing -> {Paused Cancelling}
            Stopping -> removal
            Cancelling -> removal
            Initializing -> removal
        }
        */
    }

    /// <summary>
    /// Coordinator's state concerning a specific worker of a database scan job.
    /// \todo [nuutti] Class name is a bit long
    /// </summary>
    [MetaSerializable]
    public class DatabaseScanJobWorkerCoordinationState
    {
        /// <summary>
        /// The index of this worker among this job's workers, used to determine its work shard.
        /// </summary>
        [MetaMember(1)] public int                      WorkerIndex         { get; set; }

        /// <summary>
        /// Last status that we know this worker had.
        /// May be null, indicating that this worker doesn't have an active job.
        /// </summary>
        [MetaMember(2)] public DatabaseScanWorkStatus   LastKnownStatus     { get; set; }

        /// <summary>
        /// The status that the worker had at the very end of the job, before
        /// being stopped by the coordinator.
        /// May be null, indicating that the worker hasn't been stopped yet.
        /// </summary>
        [MetaMember(3)] public DatabaseScanWorkStatus   FinalActiveStatus   { get; set; }

        public DatabaseScanJobWorkerCoordinationState(){ }
        public DatabaseScanJobWorkerCoordinationState(int workerIndex, DatabaseScanWorkStatus lastKnownStatus, DatabaseScanWorkStatus finalActiveStatus)
        {
            WorkerIndex         = workerIndex;
            LastKnownStatus     = lastKnownStatus;
            FinalActiveStatus   = finalActiveStatus;
        }
    }

    /// <summary>
    /// Coordination state of a database scan job.
    /// </summary>
    [MetaSerializable]
    public class DatabaseScanJobCoordinationState
    {
        [MetaMember(1)] public DatabaseScanJobId                                                    Id          { get; private set; }
        [MetaMember(2)] public DatabaseScanJobManagerKind                                           ManagerKind { get; private set; }
        [MetaMember(3)] public DatabaseScanJobSpec                                                  Spec        { get; private set; }
        [MetaMember(4)] public MetaTime                                                             StartTime   { get; private set; }
        [MetaMember(5)] public DatabaseScanCoordinationPhase                                        Phase       { get; set; }
        [MetaMember(6)] public OrderedDictionary<EntityId, DatabaseScanJobWorkerCoordinationState>  Workers     { get; private set; } // \note Orderedness might not be entirely necessary but let's be safe

        /// <summary>
        /// Whether this job has precedence over the other job.
        /// A higher-priority job has precedence over a lower-priority job,
        /// and if priorities are equal, then the one with *lower* <see cref="Id"/>.<see cref="DatabaseScanJobId.SequentialId"/>
        /// has precedence over the other.
        /// </summary>
        public bool HasPrecedenceOver(DatabaseScanJobCoordinationState other)
        {
            return GetUrgency().CompareTo(other.GetUrgency()) > 0;
        }

        (int, int) GetUrgency() => (Spec.Priority, -Id.SequentialId); // \note Negated SequentialId: lower SequentialId is more urgent.

        public DatabaseScanJobCoordinationState(){ }
        public DatabaseScanJobCoordinationState(DatabaseScanJobId id, DatabaseScanJobManagerKind managerKind, DatabaseScanJobSpec spec, MetaTime startTime, List<EntityId> workerIds)
        {
            MetaDebug.Assert(workerIds.Count > 0, "Must have nonzero amount of workers");

            Id          = id;
            ManagerKind = managerKind;
            Spec        = spec;
            StartTime   = startTime;
            Phase       = DatabaseScanCoordinationPhase.Initializing;

            Workers = new OrderedDictionary<EntityId, DatabaseScanJobWorkerCoordinationState>();
            for (int workerIndex = 0; workerIndex < workerIds.Count; workerIndex++)
            {
                Workers.Add(workerIds[workerIndex], new DatabaseScanJobWorkerCoordinationState(
                    workerIndex:        workerIndex,
                    lastKnownStatus:    null,
                    finalActiveStatus:  null));
            }
        }

        public DatabaseScanJobCoordinationState(DatabaseScanJobId id, DatabaseScanJobManagerKind managerKind, DatabaseScanJobSpec spec, MetaTime startTime, DatabaseScanCoordinationPhase phase, OrderedDictionary<EntityId, DatabaseScanJobWorkerCoordinationState> workers)
        {
            Id = id;
            ManagerKind = managerKind;
            Spec = spec;
            StartTime = startTime;
            Phase = phase;
            Workers = workers;
        }
    }

    /// <summary>
    /// Info about a database scan job that was active, but isn't anymore.
    /// </summary>
    [MetaSerializable]
    public class DatabaseScanJobHistoryEntry
    {
        [MetaMember(1)] public DatabaseScanJobCoordinationState FinalState  { get; private set; }
        [MetaMember(2)] public MetaTime                         EndTime     { get; private set; }

        DatabaseScanJobHistoryEntry(){ }
        public DatabaseScanJobHistoryEntry(DatabaseScanJobCoordinationState finalState, MetaTime endTime)
        {
            FinalState  = finalState;
            EndTime     = endTime;
        }
    }

    /// <summary>
    /// Info about a database scan job that is expected to become active
    /// at some time in the future.
    /// </summary>
    /// <remarks>
    /// Upcoming jobs aren't "jobs" in the same sense as active and past
    /// jobs are, and in particular an upcoming job doesn't have a
    /// <see cref="DatabaseScanJobId"/>. Instead, upcoming jobs are identifed
    /// by <see cref="JobManagerKind"/> and <see cref="UpcomingDatabaseScanJob.Id"/>
    /// together. The format of the latter is up to the job manager.
    /// </remarks>
    [MetaSerializable]
    public class DatabaseScanJobUpcomingEntry
    {
        [MetaMember(1)] public DatabaseScanJobManagerKind JobManagerKind;
        [MetaMember(2)] public UpcomingDatabaseScanJob Job;

        DatabaseScanJobUpcomingEntry() { }
        public DatabaseScanJobUpcomingEntry(DatabaseScanJobManagerKind jobManagerKind, UpcomingDatabaseScanJob job)
        {
            JobManagerKind = jobManagerKind;
            Job = job;
        }
    }

    [MetaMessage(MessageCodesCore.ListDatabaseScanJobsRequest, MessageDirection.ServerInternal)]
    public class ListDatabaseScanJobsRequest : MetaMessage
    {
        public int HistoryLimit;

        public ListDatabaseScanJobsRequest() { }
        public ListDatabaseScanJobsRequest(int historyLimit)
        {
            HistoryLimit = historyLimit;
        }
    }

    [MetaMessage(MessageCodesCore.ListDatabaseScanJobsResponse, MessageDirection.ServerInternal)]
    public class ListDatabaseScanJobsResponse : MetaMessage
    {
        public List<DatabaseScanJobCoordinationState>   ActiveJobs          { get; private set; }
        public List<DatabaseScanJobHistoryEntry>        JobHistory          { get; private set; }
        public bool                                     HasMoreJobHistory   { get; private set; }
        public List<DatabaseScanJobHistoryEntry>        LatestFinishedJobsOnePerKind { get; private set; }
        public List<DatabaseScanJobUpcomingEntry>       UpcomingJobs        { get; private set; }
        public bool                                     GlobalPauseIsEnabled{ get; private set; }

        ListDatabaseScanJobsResponse(){ }
        public ListDatabaseScanJobsResponse(List<DatabaseScanJobCoordinationState> activeJobs, List<DatabaseScanJobHistoryEntry> jobHistory, bool hasMoreJobHistory, List<DatabaseScanJobHistoryEntry> latestFinishedJobsOnePerKind, List<DatabaseScanJobUpcomingEntry> upcomingJobs, bool globalPauseIsEnabled)
        {
            ActiveJobs = activeJobs ?? throw new ArgumentNullException(nameof(activeJobs));
            JobHistory = jobHistory ?? throw new ArgumentNullException(nameof(jobHistory));
            HasMoreJobHistory = hasMoreJobHistory;
            LatestFinishedJobsOnePerKind = latestFinishedJobsOnePerKind ?? throw new ArgumentNullException(nameof(latestFinishedJobsOnePerKind));
            UpcomingJobs = upcomingJobs ?? throw new ArgumentNullException(nameof(upcomingJobs));
            GlobalPauseIsEnabled = globalPauseIsEnabled;
        }
    }

    [MetaMessage(MessageCodesCore.BeginCancelDatabaseScanJobRequest, MessageDirection.ServerInternal)]
    public class BeginCancelDatabaseScanJobRequest : MetaMessage
    {
        public DatabaseScanJobId JobId { get; private set; }

        public BeginCancelDatabaseScanJobRequest() { }
        public BeginCancelDatabaseScanJobRequest(DatabaseScanJobId jobId)
        {
            JobId = jobId;
        }
    }

    [MetaMessage(MessageCodesCore.BeginCancelDatabaseScanJobResponse, MessageDirection.ServerInternal)]
    public class BeginCancelDatabaseScanJobResponse : MetaMessage
    {
        public bool                             IsSuccess   { get; private set; }
        public DatabaseScanJobCoordinationState JobState    { get; private set; }
        public string                           Error       { get; private set; }

        public BeginCancelDatabaseScanJobResponse() { }
        BeginCancelDatabaseScanJobResponse(bool isSuccess, DatabaseScanJobCoordinationState jobState, string error)
        {
            IsSuccess = isSuccess;
            JobState = jobState;
            Error = error;
        }

        public static BeginCancelDatabaseScanJobResponse CreateSuccess(DatabaseScanJobCoordinationState jobState)
        {
            return new BeginCancelDatabaseScanJobResponse(
                isSuccess: true,
                jobState ?? throw new ArgumentNullException(nameof(jobState)),
                error: null);
        }

        public static BeginCancelDatabaseScanJobResponse CreateFailure(string error)
        {
            return new BeginCancelDatabaseScanJobResponse(
                isSuccess: false,
                jobState: null,
                error ?? throw new ArgumentNullException(nameof(error)));
        }
    }

    [MetaMessage(MessageCodesCore.SetGlobalDatabaseScanPauseRequest, MessageDirection.ServerInternal)]
    public class SetGlobalDatabaseScanPauseRequest : MetaMessage
    {
        public bool IsPaused;

        SetGlobalDatabaseScanPauseRequest() { }
        public SetGlobalDatabaseScanPauseRequest(bool isPaused)
        {
            IsPaused = isPaused;
        }
    }
    [MetaMessage(MessageCodesCore.SetGlobalDatabaseScanPauseResponse, MessageDirection.ServerInternal)]
    public class SetGlobalDatabaseScanPauseResponse : MetaMessage
    {
    }

    /// <summary>
    /// State of a database scan coordinator.
    /// </summary>
    [MetaSerializable]
    [SupportedSchemaVersions(1, 4)]
    public class DatabaseScanCoordinatorState : ISchemaMigratable
    {
        [MetaMember(1)] public int                                      RunningJobId    { get; set; } = 1;
        [MetaMember(3)] public List<DatabaseScanJobCoordinationState>   ActiveJobs      { get; set; } = new List<DatabaseScanJobCoordinationState>();
        [MetaMember(4)] public List<DatabaseScanJobHistoryEntry>        JobHistory      { get; set; } = new List<DatabaseScanJobHistoryEntry>();
        [MetaMember(6)] public List<DatabaseScanJobHistoryEntry>        LatestFinishedJobsOnePerKind { get; set; } = new List<DatabaseScanJobHistoryEntry>();
        [MetaMember(5)] public bool                                     GlobalPauseIsEnabled { get; set; } = false;

        [MetaMember(2)] public DatabaseScanJobCoordinationState         ActiveJobLegacy { get; set; } = null; // Legacy, ActiveJobs is the new one. This is kept for migration.

        // Job-type-specific managers. They're not actors by themselves.
        [MetaMember(100)] public NotificationCampaign.NotificationCampaignManager NotificationCampaignManager { get; private set; } = new NotificationCampaign.NotificationCampaignManager();
        [MetaMember(101)] public ScheduledPlayerDeletion.ScheduledPlayerDeletionManager ScheduledPlayerDeletionManager { get; private set; } = new ScheduledPlayerDeletion.ScheduledPlayerDeletionManager();
        [MetaMember(102)] public MaintenanceJob.MaintenanceJobManager MaintenanceJobManager { get; private set; } = new MaintenanceJob.MaintenanceJobManager();

        [MetaMember(199)] public TestJob.TestJobManager TestManager { get; private set; } = new TestJob.TestJobManager();

        // Please add any game-specific managers here with a MetaMember tag id starting from 1000.
        // This will help mitigate future conflicts.
        // In the future there will hopefully be a cleaner way of separating game-specific managers to their own place.
        //[MetaMember(1000)] public SomeGameSpecificManager SomeGameSpecificManager { get; private set; } = new SomeGameSpecificManager();
        //...

        #region Schema migrations

        [MigrationFromVersion(1)]
        void MigrateSimultaneousJobs()
        {
            // Added support for multiple simultaneous jobs.
            // - Notification campaigns now store scan job id in campaign state for active campaigns,
            //   because it's now needed to communicate back to coordinator in certain calls.
            //   So if a notification campaign is currently active, store the scan job id there.
            // - Move ActiveJobLegacy (if any) to ActiveJobs.
            MetaDebug.Assert(ActiveJobs.Count == 0, "Expected ActiveJobs to be an empty list before migration");

            if (ActiveJobLegacy != null)
            {
                //_log.Info("Migration to multi-job support: job {JobId} is active, spec={JobSpec}", payload.ActiveJobLegacy.Id, PrettyPrint.Compact(payload.ActiveJobLegacy.Spec));

                if (ActiveJobLegacy.ManagerKind == DatabaseScanJobManagerKind.NotificationCampaign)
                {
                    //_log.Info("Migration to multi-job support: active job is a notification campaign job, storing job id to campaign");
                    NotificationCampaignManager.MigrationSetActiveJobId(ActiveJobLegacy.Spec, ActiveJobLegacy.Id);
                }

                ActiveJobs.Add(ActiveJobLegacy);
                ActiveJobLegacy = null;
            }
            //else
            //    _log.Info("Migration to multi-job support: no current active job");
        }

        [MigrationFromVersion(2)]
        void MigrateNotificationCampaignTargets()
        {
            // Changed notification campaign targeting
            NotificationCampaignManager.MigrateTargetSegments();
        }

        [MigrationFromVersion(3)]
        void MigrateNotificationLocalization()
        {
            // Changed notification contents to use LocalizedString
            NotificationCampaignManager.MigrateLocalizations();
        }

        #endregion
    }

    [EntityConfig]
    internal sealed class DatabaseScanCoordinatorConfig : PersistedEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.DatabaseScanCoordinator;
        public override Type                EntityActorType         => typeof(DatabaseScanCoordinatorActor);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(numServiceShards: 1);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Database scan coordinator coordinates database scan jobs.
    /// It owns a number of job managers, which define the jobs to execute.
    /// It coordinates a number of workers that do the actual work
    /// in a sharded manner.
    ///
    /// <para>
    /// The purpose of the coordinator is to control and track the
    /// status of the job execution, in particular the phases of the workers.
    /// To ensure a consistent state, there's a "coordination phase" (<see cref="DatabaseScanCoordinationPhase"/>)
    /// which is related to workers' phases (<see cref="DatabaseScanWorkPhase"/>) via
    /// some invariants. The coordinator's state as well as the workers' states
    /// are persisted at crucial points when phases are changed.
    /// </para>
    ///
    /// <para>
    /// The coordinator holds the job managers as members; that is, the managers
    /// are not actors. This ensures some convenient atomicity. It does cause some
    /// awkwardness in job-manager-specific messaging; in particular, unknown messages
    /// and asks are routed to the managers. There exist possible alternative designs.
    /// </para>
    /// </summary>
    public class DatabaseScanCoordinatorActor : PersistedEntityActor<PersistedDatabaseScanCoordinator, DatabaseScanCoordinatorState>, DatabaseScanJobManager.IContext
    {
        class SendWorkerEnsureAwakes { public static readonly SendWorkerEnsureAwakes Instance = new SendWorkerEnsureAwakes(); }

        static readonly Prometheus.Gauge c_activeJobs               = Prometheus.Metrics.CreateGauge("game_databasescan_active_jobs_total", "Number of active database scan jobs");
        static readonly Prometheus.Gauge c_activeWorkers            = Prometheus.Metrics.CreateGauge("game_databasescan_active_workers_total", "Number of active database scan workers");
        static readonly Prometheus.Gauge c_runningJobs              = Prometheus.Metrics.CreateGauge("game_databasescan_running_jobs_total", "Number of active database scan jobs in the Running phase");
        static readonly Prometheus.Gauge c_runningWorkers           = Prometheus.Metrics.CreateGauge("game_databasescan_running_workers_total", "Number of active database scan workers in the Running phase");

        public static readonly EntityId EntityId = EntityId.Create(EntityKindCloudCore.DatabaseScanCoordinator, 0);

        protected override sealed AutoShutdownPolicy    ShutdownPolicy      => AutoShutdownPolicy.ShutdownNever();
        protected override sealed TimeSpan              SnapshotInterval    => TimeSpan.FromMinutes(3);

        /// <summary>
        /// Desired number of workers, per job.
        /// This is called "desired" because once a job has been started, its worker
        /// count cannot be changed; that is, the "desired" number of workers can differ
        /// from the actual number of workers for an ongoing job that was started on a
        /// previous server deployment that had a different desired number of workers.
        /// </summary>
        const int                           DesiredNumWorkersPerJob                 = 4; // \todo [nuutti] What's a good value? Put in some config? Make dynamic, dependent on job type? #notificationcampaign

        /// <summary>
        /// How many active jobs are allowed to exist simultaneously.
        /// This is intended mainly as a reasonable fail-safe bound
        /// in the case some other limits are accidentally missing.
        /// </summary>
        const int                           MaxNumSimultaneousActiveJobs            = 10;

        /// <summary>
        /// Max entries kept in DatabaseScanCoordinatorState.JobHistory
        /// </summary>
        const int                           MaxNumJobHistoryEntries                 = 20;

        DatabaseScanCoordinatorState        _state;

        public DatabaseScanCoordinatorActor(EntityId entityId) : base(entityId)
        {
        }

        protected override sealed async Task Initialize()
        {
            // Try to fetch from database & restore from it (if exists)
            PersistedDatabaseScanCoordinator persisted = await MetaDatabase.Get().TryGetAsync<PersistedDatabaseScanCoordinator>(_entityId.ToString());
            await InitializePersisted(persisted);

            foreach (JobManagerInfo jobManagerInfo in GetJobManagerInfos())
                await jobManagerInfo.Manager.InitializeAsync(this);

            StartPeriodicTimer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), SendWorkerEnsureAwakes.Instance);
            StartPeriodicTimer(TimeSpan.FromSeconds(1), ActorTick.Instance);
        }

        protected override sealed Task<DatabaseScanCoordinatorState> InitializeNew()
        {
            // Create new state
            DatabaseScanCoordinatorState state = new DatabaseScanCoordinatorState();

            return Task.FromResult(state);
        }

        protected override sealed Task<DatabaseScanCoordinatorState> RestoreFromPersisted(PersistedDatabaseScanCoordinator persisted)
        {
            // Deserialize actual state
            DatabaseScanCoordinatorState state = DeserializePersistedPayload<DatabaseScanCoordinatorState>(persisted.Payload, resolver: null, logicVersion: null);

            return Task.FromResult(state);
        }

        protected override sealed Task PostLoad(DatabaseScanCoordinatorState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _state = payload;

            _log.Info("PostLoad: {NumActiveJobs} active job(s)", _state.ActiveJobs.Count);
            foreach (DatabaseScanJobCoordinationState activeJob in _state.ActiveJobs)
                _log.Info("  Active job: id={JobId}, phase={JobPhase}, startTime={JobStartTime}, priority={JobPriority}, spec={JobSpec}", activeJob.Id, activeJob.Phase, activeJob.StartTime, activeJob.Spec.Priority, PrettyPrint.Compact(activeJob.Spec));

            CheckInvariants();

            return Task.CompletedTask;
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            // Serialize and compress the state
            byte[] persistedPayload = SerializeToPersistedPayload(_state, resolver: null, logicVersion: null);

            // Persist in database
            PersistedDatabaseScanCoordinator persisted = new PersistedDatabaseScanCoordinator
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

        /// <summary>
        /// Send dummy messages to workers of active jobs to ensure they're awake.
        /// The workers are not service entities and thus there's nothing formal
        /// that would restart them if they crash, so we deal with it like this for now.
        /// </summary>
        [CommandHandler]
        private void HandleSendWorkerEnsureAwakes(SendWorkerEnsureAwakes _)
        {
            foreach (DatabaseScanJobCoordinationState activeJob in _state.ActiveJobs)
            {
                foreach (EntityId workerId in activeJob.Workers.Keys)
                    CastMessage(workerId, DatabaseScanWorkerEnsureAwake.Instance);
            }
        }

        [CommandHandler]
        private async Task HandleActorTick(ActorTick _)
        {
            CheckInvariants();

            ReportMetrics();

            await TryStartNewJobsAsync();
            await UpdateActiveJobsAsync();
        }

        protected override Task HandleUnknownMessage(EntityId fromEntityId, MetaMessage message)
        {
            // Try routing the message to a job manager
            // \todo [nuutti] Is there a nicer way to handle job-type-specific messages?
            foreach (JobManagerInfo managerInfo in GetJobManagerInfos())
            {
                if (managerInfo.Manager.TryGetMessageHandler(this, message, out Task handle))
                    return handle;
            }

            _log.Warning("Received unknown message from {EntityId}, and it wasn't handled by any job manager: {Message}", fromEntityId, PrettyPrint.Compact(message));
            return Task.CompletedTask;
        }

        protected override Task HandleUnknownEntityAsk(EntityAsk ask, MetaMessage message)
        {
            // Try routing the ask to a job manager
            // \todo [nuutti] Is there a nicer way to handle job-type-specific asks?
            foreach (JobManagerInfo managerInfo in GetJobManagerInfos())
            {
                if (managerInfo.Manager.TryGetEntityAskHandler(this, ask, message, out Task handle))
                    return handle;
            }

            _log.Warning("Received unknown ask from {EntityId}, and it wasn't handled by any job manager: {Message}", ask.FromEntityId, PrettyPrint.Compact(message));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Try to get a new job from a job manager, and start it as a new active job.
        /// </summary>
        async Task TryStartNewJobsAsync()
        {
            // During global pause, don't start new jobs (they would remain in the initial Paused state)
            if (_state.GlobalPauseIsEnabled)
                return;

            // Overall limit on number of active job
            if (_state.ActiveJobs.Count >= MaxNumSimultaneousActiveJobs)
                return;

            MetaTime currentTime = MetaTime.Now;

            foreach (JobManagerInfo managerInfo in GetJobManagerInfos())
            {
                // Skip manager if it doesn't allow multiple simultaneous jobs, and we've already got an active job from it
                if (!managerInfo.Manager.AllowMultipleSimultaneousJobs && _state.ActiveJobs.Any(job => job.ManagerKind == managerInfo.ManagerKind))
                    continue;

                // Otherwise, try getting a new job from the manager, and start it if appropriate.
                (DatabaseScanJobSpec candidateJobSpec, bool canStart) = managerInfo.Manager.TryGetNextDueJob(this, currentTime);
                if (candidateJobSpec != null)
                {
                    DatabaseScanJobCoordinationState jobState = new DatabaseScanJobCoordinationState(
                        id:             CreateNextJobId(),
                        managerKind:    managerInfo.ManagerKind,
                        startTime:      currentTime,
                        spec:           candidateJobSpec,
                        workerIds:      ResolveIdleWorkerIds(candidateJobSpec.ExplicitEntityList == null ? DesiredNumWorkersPerJob : 1));

                    if (!canStart)
                    {
                        DatabaseScanJobManager jobManager = GetJobManagerOfKind(managerInfo.ManagerKind);
                        await jobManager.OnJobDidNotStartAsync(this, candidateJobSpec, currentTime);
                        AddJobToHistory(jobState, currentTime);
                        await PersistStateIntermediate();
                        continue;
                    }

                    // Only start the job if it's higher priority than any existing active job
                    bool shouldStart = _state.ActiveJobs.All(existingJob => candidateJobSpec.Priority > existingJob.Spec.Priority);
                    if (shouldStart)
                        await StartNewJob(jobState, currentTime);
                }
            }
        }

        /// <summary>
        /// Add the given job as a new active job, initially in the Initializing phase.
        /// Persist the coordinator's state.
        /// </summary>
        async Task StartNewJob(DatabaseScanJobCoordinationState jobState, MetaTime currentTime)
        {
            DatabaseScanJobManager jobManager = GetJobManagerOfKind(jobState.ManagerKind);

            _log.Info("Starting new job: id={JobId}, priority={Priority}, spec={JobSpec}, workerIds=[ {JobWorkerIds} ]", jobState.Id, jobState.Spec.Priority, PrettyPrint.Compact(jobState.Spec), string.Join(", ", jobState.Workers.Keys));

            _state.ActiveJobs.Add(jobState);

            CheckInvariants();
            await jobManager.OnJobStartedAsync(this, jobState.Spec, jobState.Id, currentTime);

            await PersistStateIntermediate();
        }

        async Task UpdateActiveJobsAsync()
        {
            // \note We take a copy of _state.ActiveJobs, because _state.ActiveJobs can be mutated while we're iterating through the jobs.
            List<DatabaseScanJobCoordinationState> activeJobs = new List<DatabaseScanJobCoordinationState>(_state.ActiveJobs);
            foreach (DatabaseScanJobCoordinationState activeJob in activeJobs)
                await UpdateActiveJob(activeJob);
        }

        async Task UpdateActiveJob(DatabaseScanJobCoordinationState activeJob)
        {
            switch (activeJob.Phase)
            {
                case DatabaseScanCoordinationPhase.Initializing:    await UpdateInitializingJob(activeJob); break;
                case DatabaseScanCoordinationPhase.Paused:          await UpdatePausedJob(activeJob);       break;
                case DatabaseScanCoordinationPhase.Resuming:        await UpdateResumingJob(activeJob);     break;
                case DatabaseScanCoordinationPhase.Running:         await UpdateRunningJob(activeJob);      break;
                case DatabaseScanCoordinationPhase.Pausing:         await UpdatePausingJob(activeJob);      break;
                case DatabaseScanCoordinationPhase.Stopping:        await UpdateStoppingJob(activeJob);     break;
                case DatabaseScanCoordinationPhase.Cancelling:      await UpdateCancellingJob(activeJob);   break;
                default:
                    _log.Error("Unhandled DatabaseScanCoordinationPhase {Phase}", activeJob.Phase);
                    break;
            }
        }

        /// <summary>
        /// Initialize each of the workers for the given active job. The task will finish when
        /// each worker has been initialized, and the coordinator's state persisted with
        /// the job in Paused phase.
        /// </summary>
        async Task UpdateInitializingJob(DatabaseScanJobCoordinationState activeJob)
        {
            AssertJobPhase(activeJob, DatabaseScanCoordinationPhase.Initializing);

            foreach ((EntityId workerId, DatabaseScanJobWorkerCoordinationState workerState) in activeJob.Workers)
            {
                DatabaseScanWorkShard           workShard       = new DatabaseScanWorkShard(workerIndex: workerState.WorkerIndex, numWorkers: activeJob.Workers.Count);
                DatabaseScanWorkerInitializedOk initializedOk   = await EntityAskAsync<DatabaseScanWorkerInitializedOk>(workerId, new DatabaseScanWorkerEnsureInitialized(activeJob.Id, activeJob.Spec, workShard));
                workerState.LastKnownStatus = initializedOk.ActiveJobStatus;
            }
            CheckInvariants();

            activeJob.Phase = DatabaseScanCoordinationPhase.Paused;
            CheckInvariants();
            await PersistStateIntermediate();
            _log.Info("All workers are now initialized for job {JobId}", activeJob.Id);
        }

        /// <summary>
        /// Check whether the given active job can be resumed from pause, and update phase accordingly.
        /// Namely, if global pause is disabled and the job has precedence over all other active jobs
        /// and all other active jobs are Paused or Initializing, put it into Resuming phase.
        /// </summary>
        async Task UpdatePausedJob(DatabaseScanJobCoordinationState activeJob)
        {
            AssertJobPhase(activeJob, DatabaseScanCoordinationPhase.Paused);

            if (!_state.GlobalPauseIsEnabled)
            {
                IEnumerable<DatabaseScanJobCoordinationState>   otherJobs               = _state.ActiveJobs.Where(job => job != activeJob);
                bool                                            thisJobHasPrecedence    = otherJobs.All(otherJob => activeJob.HasPrecedenceOver(otherJob));
                bool                                            otherJobsPhasesOk       = otherJobs.All(otherJob => otherJob.Phase == DatabaseScanCoordinationPhase.Initializing
                                                                                                                 || otherJob.Phase == DatabaseScanCoordinationPhase.Paused);

                bool                                            canResume               = thisJobHasPrecedence && otherJobsPhasesOk;

                if (canResume)
                {
                    activeJob.Phase = DatabaseScanCoordinationPhase.Resuming;
                    CheckInvariants();
                    await PersistStateIntermediate();
                    _log.Info("Job {JobId} has precedence over all other active jobs, resuming it from pause", activeJob.Id);
                }
            }
        }

        /// <summary>
        /// Resume each of the workers of the given active job. The task will finish when
        /// each of the workers has been resumed, and the coordinator's state persisted
        /// with the job in Running phase.
        /// </summary>
        async Task UpdateResumingJob(DatabaseScanJobCoordinationState activeJob)
        {
            AssertJobPhase(activeJob, DatabaseScanCoordinationPhase.Resuming);

            foreach ((EntityId workerId, DatabaseScanJobWorkerCoordinationState workerState) in activeJob.Workers)
            {
                DatabaseScanWorkerResumedOk resumedOk = await EntityAskAsync<DatabaseScanWorkerResumedOk>(workerId, new DatabaseScanWorkerEnsureResumed());
                workerState.LastKnownStatus = resumedOk.ActiveJobStatus;
            }
            CheckInvariants();

            activeJob.Phase = DatabaseScanCoordinationPhase.Running;
            CheckInvariants();
            await PersistStateIntermediate();
            _log.Info("All workers are now resumed for job {JobId}", activeJob.Id);
        }

        /// <summary>
        /// Handle a status report from a worker. We should assume that status reports
        /// can arrive at any time; therefore we need to tolerate them in any state.
        /// In particular, we may get status reports involving jobs that are no longer active.
        /// </summary>
        [MessageHandler]
        void HandleDatabaseScanWorkerStatusReport(EntityId workerId, DatabaseScanWorkerStatusReport statusReport)
        {
            DatabaseScanJobCoordinationState activeJob = TryGetActiveJob(statusReport.ActiveJob.Id);
            if (activeJob == null)
                return;
            if (activeJob.Phase != DatabaseScanCoordinationPhase.Running)
                return;

            // Update LastKnownStatus only if statusReport is newer (based on StatusObservationIndex).
            // It can be older because we also update LastKnownStatus based on some other channels
            // (namely, EntityAsks) which are not synchronized with these status reports.
            DatabaseScanJobWorkerCoordinationState workerState = activeJob.Workers[workerId];
            if (statusReport.ActiveJob.StatusObservationIndex > workerState.LastKnownStatus.StatusObservationIndex)
                workerState.LastKnownStatus = statusReport.ActiveJob;

            CheckInvariants();
        }

        /// <summary>
        /// Check the statuses of the workers of the given active job, and update phase accordingly.
        /// Namely, if all workers are finished, put the job into Stopping phase.
        /// Also, if global pause is enabled, or another active job has precedence over the given job,
        /// put this one into Pausing phase.
        /// </summary>
        async Task UpdateRunningJob(DatabaseScanJobCoordinationState activeJob)
        {
            AssertJobPhase(activeJob, DatabaseScanCoordinationPhase.Running);

            IEnumerable<DatabaseScanJobCoordinationState>   otherJobs   = _state.ActiveJobs.Where(job => job != activeJob);
            bool                                            shouldPause = _state.GlobalPauseIsEnabled || otherJobs.Any(otherJob => otherJob.HasPrecedenceOver(activeJob));

            if (shouldPause)
            {
                activeJob.Phase = DatabaseScanCoordinationPhase.Pausing;
                CheckInvariants();
                await PersistStateIntermediate();

                if (_state.GlobalPauseIsEnabled)
                    _log.Info("Global pause is enabled, pausing job {JobId}", activeJob.Id);
                else
                    _log.Info("Another job has precedence over {JobId}, pausing it", activeJob.Id);
            }
            else
            {
                bool allWorkersAreFinished = activeJob.Workers.Values.All(worker => worker.LastKnownStatus.Phase == DatabaseScanWorkPhase.Finished);

                if (allWorkersAreFinished)
                {
                    activeJob.Phase = DatabaseScanCoordinationPhase.Stopping;
                    CheckInvariants();
                    await PersistStateIntermediate();
                    _log.Info("All workers are now finished for job {JobId}", activeJob.Id);
                }
            }
        }

        /// <summary>
        /// Pause each of the workers of the given active job. The task will finish when
        /// each of the workers has been paused, and the coordinator's state persisted
        /// with the job in Paused phase.
        /// </summary>
        async Task UpdatePausingJob(DatabaseScanJobCoordinationState activeJob)
        {
            AssertJobPhase(activeJob, DatabaseScanCoordinationPhase.Pausing);

            foreach ((EntityId workerId, DatabaseScanJobWorkerCoordinationState workerState) in activeJob.Workers)
            {
                DatabaseScanWorkerPausedOk pausedOk = await EntityAskAsync<DatabaseScanWorkerPausedOk>(workerId, new DatabaseScanWorkerEnsurePaused());
                workerState.LastKnownStatus = pausedOk.ActiveJobStatus;
            }
            CheckInvariants();

            activeJob.Phase = DatabaseScanCoordinationPhase.Paused;
            CheckInvariants();
            await PersistStateIntermediate();
            _log.Info("All workers are now paused for job {JobId}", activeJob.Id);
        }

        /// <summary>
        /// Stop each of the (assertedly finished) workers of the given active job. The task will finish when
        /// each worker has been stopped, and the coordinator's state persisted with
        /// the active job removed.
        /// </summary>
        async Task UpdateStoppingJob(DatabaseScanJobCoordinationState activeJob)
        {
            AssertJobPhase(activeJob, DatabaseScanCoordinationPhase.Stopping);

            foreach ((EntityId workerId, DatabaseScanJobWorkerCoordinationState workerState) in activeJob.Workers)
            {
                DatabaseScanWorkerStoppedOk stoppedOk = await EntityAskAsync<DatabaseScanWorkerStoppedOk>(workerId, new DatabaseScanWorkerEnsureStopped(DatabaseScanWorkerStopFlavor.Finished));
                // \note ActiveJobJustBefore may be null, if coordinator had crashed during a previous StopWorkersAsync.
                //       If that's the case, then LastKnownStatus will contain the appropriate value instead.
                workerState.FinalActiveStatus = stoppedOk.ActiveJobJustBefore ?? workerState.LastKnownStatus;
                workerState.LastKnownStatus = null;
            }

            CheckInvariants();

            DatabaseScanJobManager  jobManager  = GetJobManagerOfKind(activeJob.ManagerKind);
            MetaTime                currentTime = MetaTime.Now;
            await jobManager.OnJobStoppedAsync(this, activeJob.Spec, currentTime, wasCancelled: false, workerStatusMaybes: activeJob.Workers.Values.Select(w => w.FinalActiveStatus));
            RemoveActiveJob(activeJob, currentTime);
            CheckInvariants();
            await PersistStateIntermediate();
            _log.Info("All workers are now stopped for job {JobId}, job is done: spec={JobSpec}", activeJob.Id, PrettyPrint.Compact(activeJob.Spec));
        }

        /// <summary>
        /// Cancel each of the workers of the given active job. The task will finish when
        /// each worker has been stopped with cancellation, and the coordinator's state persisted with
        /// the active job removed.
        /// </summary>
        async Task UpdateCancellingJob(DatabaseScanJobCoordinationState activeJob)
        {
            AssertJobPhase(activeJob, DatabaseScanCoordinationPhase.Cancelling);

            foreach ((EntityId workerId, DatabaseScanJobWorkerCoordinationState workerState) in activeJob.Workers)
            {
                DatabaseScanWorkerStoppedOk stoppedOk = await EntityAskAsync<DatabaseScanWorkerStoppedOk>(workerId, new DatabaseScanWorkerEnsureStopped(DatabaseScanWorkerStopFlavor.Cancel));
                // \note ActiveJobJustBefore may be null, if coordinator had crashed during a previous CancelWorkersAsync.
                //       \todo [nuutti] If that's the case, then LastKnownStatus won't necessarily contain an up-to-date value.
                workerState.FinalActiveStatus = stoppedOk.ActiveJobJustBefore ?? workerState.LastKnownStatus;
                workerState.LastKnownStatus = null;
            }

            CheckInvariants();

            DatabaseScanJobManager  jobManager  = GetJobManagerOfKind(activeJob.ManagerKind);
            MetaTime                currentTime = MetaTime.Now;
            await jobManager.OnJobStoppedAsync(this, activeJob.Spec, currentTime, wasCancelled: true, workerStatusMaybes: activeJob.Workers.Values.Select(w => w.FinalActiveStatus));
            RemoveActiveJob(activeJob, currentTime);
            CheckInvariants();
            await PersistStateIntermediate();
            _log.Info("All workers are now stopped (due to cancellation) for job {JobId}, job is cancelled: spec={JobSpec}", activeJob.Id, PrettyPrint.Compact(activeJob.Spec));
        }

        void AssertJobPhase(DatabaseScanJobCoordinationState activeJob, DatabaseScanCoordinationPhase wantedPhase)
        {
            MetaDebug.Assert(activeJob.Phase == wantedPhase, "Job phase is {0}, expected {1}", activeJob.Phase, wantedPhase);
        }

        [EntityAskHandler]
        void HandleListDatabaseScanJobsRequest(EntityAsk ask, ListDatabaseScanJobsRequest request)
        {
            ReplyToAsk(ask, new ListDatabaseScanJobsResponse(
                activeJobs:
                    _state.ActiveJobs
                    .OrderByDescending(job => job.Id.SequentialId)
                    .ToList(),
                jobHistory:
                    _state.JobHistory
                    .OrderByDescending(job => job.FinalState.Id.SequentialId)
                    .Take(request.HistoryLimit)
                    .ToList(),
                hasMoreJobHistory:
                    _state.JobHistory.Count() > request.HistoryLimit,
                latestFinishedJobsOnePerKind:
                    _state.LatestFinishedJobsOnePerKind
                    .Concat(ConstructLatestFinishedJobsFromMaintenanceLatestJobsDeprecated())
                    .OrderByDescending(job => job.FinalState.Id.SequentialId)
                    .ToList(),
                upcomingJobs:
                    GatherUpcomingJobs(),
                globalPauseIsEnabled:
                    _state.GlobalPauseIsEnabled)
            );
        }

        /// <summary>
        /// Construct a list of entries for reporting <see cref="ListDatabaseScanJobsResponse.LatestFinishedJobsOnePerKind"/>,
        /// based on the deprecated list <see cref="MaintenanceJob.MaintenanceJobManager.LatestJobsDeprecated"/>.
        /// This is relevant when those maintenance jobs were run before <see cref="DatabaseScanCoordinatorState.LatestFinishedJobsOnePerKind"/>
        /// was implemented. Note that this is necessarily a bit hacky and fakey because the maintenance job
        /// bookkeeping does not hold all of the same data that <see cref="DatabaseScanJobHistoryEntry"/> does.
        /// </summary>
        IEnumerable<DatabaseScanJobHistoryEntry> ConstructLatestFinishedJobsFromMaintenanceLatestJobsDeprecated()
        {
            return _state.MaintenanceJobManager.LatestJobsDeprecated
                .Select((MaintenanceJob.PastJob pastJob) =>
                {
                    OrderedDictionary<EntityId, DatabaseScanJobWorkerCoordinationState> workerInfos = new();

                    foreach ((DatabaseScanWorkStatus workerFinalStatus, int workerIndex) in pastJob.FinalWorkStatusMaybes.ZipWithIndex())
                    {
                        // Fake entity id: we don't actually know the real entity id, as it's not stored in MaintenanceJob.PastJob.
                        EntityId fakeWorkerEntityId = EntityId.Create(EntityKindCloudCore.DatabaseScanWorker, (ulong)workerIndex);

                        workerInfos.Add(fakeWorkerEntityId, new DatabaseScanJobWorkerCoordinationState(
                            workerIndex: workerIndex,
                            lastKnownStatus: null,
                            finalActiveStatus: workerFinalStatus));
                    }

                    return new DatabaseScanJobHistoryEntry(
                        new DatabaseScanJobCoordinationState(
                            id:             pastJob.DatabaseScanJobId,
                            managerKind:    DatabaseScanJobManagerKind.MaintenanceJobManager,
                            spec:           pastJob.Spec,
                            startTime:      pastJob.StartTime,
                            phase:          DatabaseScanCoordinationPhase.Stopping,
                            workers:        workerInfos),
                        endTime: pastJob.EndTime);
                });
        }

        List<DatabaseScanJobUpcomingEntry> GatherUpcomingJobs()
        {
            MetaTime currentTime = MetaTime.Now;

            List<DatabaseScanJobUpcomingEntry> upcomingJobs = new List<DatabaseScanJobUpcomingEntry>();

            foreach (JobManagerInfo managerInfo in GetJobManagerInfos())
            {
                IEnumerable<DatabaseScanJobUpcomingEntry> jobs =
                    managerInfo.Manager.GetUpcomingJobs(currentTime)
                    .Select((UpcomingDatabaseScanJob job) =>
                        new DatabaseScanJobUpcomingEntry(managerInfo.ManagerKind, job));

                upcomingJobs.AddRange(jobs);

            }

            return upcomingJobs
                   .OrderBy(entry => entry.Job.EarliestStartTime ?? MetaTime.Epoch)
                   .ToList();
        }

        [EntityAskHandler]
        async Task HandleBeginCancelDatabaseScanJobRequest(EntityAsk ask, BeginCancelDatabaseScanJobRequest request)
        {
            (bool isSuccess, DatabaseScanJobCoordinationState jobState, string error) = await TryBeginCancelActiveJobImplAsync(request.JobId);

            BeginCancelDatabaseScanJobResponse response = isSuccess
                                                          ? BeginCancelDatabaseScanJobResponse.CreateSuccess(jobState)
                                                          : BeginCancelDatabaseScanJobResponse.CreateFailure(error);

            ReplyToAsk(ask, response);
        }

        /// <summary>
        /// Try to begin the cancellation of the given job.
        /// </summary>
        /// <returns>
        /// A tuple of:
        /// - isSuccess: whether cancellation was successfully started
        /// - jobState: the state of the given job, if it's currently active, or null otherwise
        /// - error: error description if !isSuccess, null otherwise
        /// </returns>
        async Task<(bool isSuccess, DatabaseScanJobCoordinationState jobState, string error)> TryBeginCancelActiveJobImplAsync(DatabaseScanJobId jobId)
        {
            DatabaseScanJobCoordinationState activeJob = TryGetActiveJob(jobId);
            if (activeJob == null)
                return (isSuccess: false, activeJob, "No such active job");

            if (activeJob.Phase == DatabaseScanCoordinationPhase.Stopping
             || activeJob.Phase == DatabaseScanCoordinationPhase.Cancelling)
                return (isSuccess: false, activeJob, $"Cannot cancel job in phase {activeJob.Phase}");

            activeJob.Phase = DatabaseScanCoordinationPhase.Cancelling;
            CheckInvariants();

            DatabaseScanJobManager jobManager = GetJobManagerOfKind(activeJob.ManagerKind);
            await jobManager.OnJobCancellationBeganAsync(this, activeJob.Spec, MetaTime.Now);

            await PersistStateIntermediate();
            _log.Info("Started cancellation of job {JobId}", jobId);

            return (isSuccess: true, activeJob, error: null);
        }

        [EntityAskHandler]
        async Task<SetGlobalDatabaseScanPauseResponse> HandleSetGlobalDatabaseScanPauseRequest(SetGlobalDatabaseScanPauseRequest request)
        {
            _state.GlobalPauseIsEnabled = request.IsPaused;
            await PersistStateIntermediate();

            if (request.IsPaused)
                _log.Info("Enabled global pause");
            else
                _log.Info("Disabled global pause");

            // After updating global pause flag, update active jobs right away.
            // This is not fundamentally necessary as the jobs would update eventually
            // in the periodic ticking, but this gives a nicer view to an observer,
            // as the jobs will be seen affected immediately (in particular, when enabling
            // the pause, Running jobs will immediately become Pausing, though not Paused
            // because workers are not updated synchronously).
            await UpdateActiveJobsAsync();

            return new SetGlobalDatabaseScanPauseResponse();
        }

        void ReportMetrics()
        {
            // \todo [nuutti] Add MetricsTag label. Need to track also ended jobs so can set the metrics for those labels to 0?
            c_activeJobs.Set(_state.ActiveJobs.Count);
            c_activeWorkers.Set(_state.ActiveJobs.Sum(job => job.Workers.Count));
            c_runningJobs.Set(_state.ActiveJobs.Count(job => job.Phase == DatabaseScanCoordinationPhase.Running));
            c_runningWorkers.Set(_state.ActiveJobs.Sum(job => job.Workers.Values.Count(worker => worker.LastKnownStatus?.Phase == DatabaseScanWorkPhase.Running)));
        }

        void CheckInvariants()
        {
            // Invariants:
            // (- If coordinator has no active jobs, then no worker has a job)
            // - For each active job the coordinator has, consider the phase of the job and the workers assigned to that job:
            //   - Each worker either has that same job or no job
            //   - If the job is in Initializing phase, each worker either has no job, or has the job in Paused phase
            //   - If the job is in Running phase, each worker has the job in Running or Finished phase
            //   - If the job is in Pausing phase, each worker has the job
            //   - If the job is in Paused phase, each worker has the job in Paused or Finished phase
            //   - If the job is in Resuming phase, each worker has the job
            //   - If the job is in Stopping phase, each worker either has the job in Finished phase or has no job
            // - At most one active job has phase other than Initializing, Paused, or Cancelling
            // - At most one active job has a worker in Running phase (mostly covered by the above invariant, except for Cancelling phase)
            // For the sake of performance, and not messing around too much just for the sake of some debug checks,
            // we don't actually ask workers' statuses here, but just use the last known statuses that we already
            // happen to know for other reasons.
            // Also for that reason we cannot check the first invariant in parentheses above.

            bool invariantsViolated = false;
            void OnInvariantViolated(string logFormat, params object[] logArgs)
            {
                _log.Error(logFormat, logArgs);
                invariantsViolated = true;
            }

            // Check worker statuses for each active job

            foreach (DatabaseScanJobCoordinationState activeJob in _state.ActiveJobs)
            {
                foreach ((EntityId workerId, DatabaseScanJobWorkerCoordinationState workerState) in activeJob.Workers)
                {
                    DatabaseScanWorkStatus workStatus = workerState.LastKnownStatus;

                    if (workStatus != null)
                    {
                        if (activeJob.Id != workStatus.Id)
                            OnInvariantViolated("Coordinator's active job id {CoordinatorActiveJobId} ({CoordinatorActiveJobPhase}) differs from worker's ({WorkerId}) active job id {WorkerActiveJobId} ({WorkerActiveJobPhase})", activeJob.Id, activeJob.Phase, workerId, workStatus.Id, workStatus.Phase);
                    }

                    switch (activeJob.Phase)
                    {
                        case DatabaseScanCoordinationPhase.Initializing:
                            if (workStatus != null
                             && workStatus.Phase != DatabaseScanWorkPhase.Paused)
                            {
                                OnInvariantViolated("Active job {JobId} is Initializing, but worker {WorkerId} has job in phase {WorkerPhase} when it should either have no job or have it in Paused phase", activeJob.Id, workerId, workStatus.Phase);
                            }
                            break;

                        case DatabaseScanCoordinationPhase.Running:
                            if (workStatus == null)
                                OnInvariantViolated("Active job {JobId} is Running, but worker {WorkerId} has no job", activeJob.Id, workerId);
                            else if (workStatus.Phase != DatabaseScanWorkPhase.Running
                                  && workStatus.Phase != DatabaseScanWorkPhase.Finished)
                            {
                                OnInvariantViolated("Active job {JobId} is Running, but worker {WorkerId} has job in phase {WorkerPhase} when it should be either Running or Finished", activeJob.Id, workerId, workStatus.Phase);
                            }
                            break;

                        case DatabaseScanCoordinationPhase.Pausing:
                            if (workStatus == null)
                                OnInvariantViolated("Active job {JobId} is Pausing, but worker {WorkerId} has no job", activeJob.Id, workerId);
                            break;

                        case DatabaseScanCoordinationPhase.Paused:
                            if (workStatus == null)
                                OnInvariantViolated("Active job {JobId} is Paused, but worker {WorkerId} has no job", activeJob.Id, workerId);
                            else if (workStatus.Phase != DatabaseScanWorkPhase.Paused
                                  && workStatus.Phase != DatabaseScanWorkPhase.Finished)
                            {
                                OnInvariantViolated("Active job {JobId} is Paused, but worker {WorkerId} has job in phase {WorkerPhase} when it should be either Paused or Finished", activeJob.Id, workerId, workStatus.Phase);
                            }
                            break;

                        case DatabaseScanCoordinationPhase.Resuming:
                            if (workStatus == null)
                                OnInvariantViolated("Active job {JobId} is Resuming, but worker {WorkerId} has no job", activeJob.Id, workerId);
                            break;

                        case DatabaseScanCoordinationPhase.Stopping:
                            if (workStatus != null
                             && workStatus.Phase != DatabaseScanWorkPhase.Finished)
                            {
                                OnInvariantViolated("Active job {JobId} is Stopping, but worker {WorkerId} has job in phase {WorkerPhase} when it should either have no job or have it in Finished phase", activeJob.Id, workerId, workStatus.Phase);
                            }
                            break;

                        case DatabaseScanCoordinationPhase.Cancelling:
                            // no further requirements
                            break;

                        default:
                            _log.Error("Invalid DatabaseScanCoordinationPhase {ActiveJobPhase}", activeJob.Phase);
                            break;
                    }
                }
            }

            // Check that at most one active job is in a phase other than Initializing, Paused, or Cancelling.

            int numNonInitializingPausedJobs = _state.ActiveJobs.Count(job => job.Phase != DatabaseScanCoordinationPhase.Initializing
                                                                           && job.Phase != DatabaseScanCoordinationPhase.Paused
                                                                           && job.Phase != DatabaseScanCoordinationPhase.Cancelling);

            if (numNonInitializingPausedJobs > 1)
                OnInvariantViolated("More than one active job is in phase other than Initializing, Paused, or Cancelling");

            // Check that at most one active job has a worker in Running phase

            int numJobsWithRunningWorker = _state.ActiveJobs.Count(job => job.Workers.Values.Any(worker => worker.LastKnownStatus?.Phase == DatabaseScanWorkPhase.Running));

            if (numJobsWithRunningWorker > 1)
                OnInvariantViolated("More than one active job has a worker in Running phase");

            if (invariantsViolated)
            {
                _log.Error("Invariants violated with {NumActiveJobs} active job(s)", _state.ActiveJobs.Count);
                foreach (DatabaseScanJobCoordinationState activeJob in _state.ActiveJobs)
                {
                    _log.Error("  Active job: id={JobId}, phase={JobPhase}, startTime={JobStartTime}, priority={JobPriority}, spec={JobSpec}", activeJob.Id, activeJob.Phase, activeJob.StartTime, activeJob.Spec.Priority, PrettyPrint.Compact(activeJob.Spec));
                    foreach ((EntityId workerId, DatabaseScanJobWorkerCoordinationState workerState) in activeJob.Workers)
                        _log.Error("    Worker {WorkerId}: last known phase is {WorkerPhase}", workerId, workerState.LastKnownStatus?.Phase.ToString() ?? "<unknown>");
                }

                throw new InvalidOperationException("DatabaseScan job coordination invariants violated");
            }
        }

        DatabaseScanJobId CreateNextJobId()
        {
            int runningId = _state.RunningJobId++;
            return DatabaseScanJobId.CreateNew(runningId);
        }

        IEnumerable<JobManagerInfo> GetJobManagerInfos()
        {
            return EnumUtil.GetValues<DatabaseScanJobManagerKind>()
                           .Select(kind => new JobManagerInfo(kind, GetJobManagerOfKind(kind)));
        }

        DatabaseScanJobManager GetJobManagerOfKind(DatabaseScanJobManagerKind kind)
        {
            switch (kind)
            {
                case DatabaseScanJobManagerKind.NotificationCampaign: return _state.NotificationCampaignManager;
                case DatabaseScanJobManagerKind.ScheduledPlayerDeletion: return _state.ScheduledPlayerDeletionManager;
                case DatabaseScanJobManagerKind.MaintenanceJobManager: return _state.MaintenanceJobManager;
                case DatabaseScanJobManagerKind.TestManager: return _state.TestManager;
                default:
                    throw new InvalidEnumArgumentException(nameof(kind), (int)kind, typeof(DatabaseScanJobManagerKind));
            }
        }

        struct JobManagerInfo
        {
            public DatabaseScanJobManagerKind   ManagerKind;
            public DatabaseScanJobManager       Manager;

            public JobManagerInfo(DatabaseScanJobManagerKind managerKind, DatabaseScanJobManager manager)
            {
                ManagerKind = managerKind;
                Manager     = manager;
            }
        }

        /// <summary>
        /// Return ids of <paramref name="numWorkersWanted"/> workers that aren't currently assigned to any active job.
        /// </summary>
        List<EntityId> ResolveIdleWorkerIds(int numWorkersWanted)
        {
            HashSet<EntityId> activeWorkerIds = _state.ActiveJobs.SelectMany(job => job.Workers.Keys).ToHashSet();

            // Starting from id value 0, find the N first idle workers
            List<EntityId>  idleWorkerIds   = new List<EntityId>();
            int             candidateIndex  = 0;
            while (idleWorkerIds.Count < numWorkersWanted)
            {
                EntityId candidateId = EntityId.Create(EntityKindCloudCore.DatabaseScanWorker, (ulong)candidateIndex);
                if (!activeWorkerIds.Contains(candidateId))
                    idleWorkerIds.Add(candidateId);
                candidateIndex++;
            }

            return idleWorkerIds;
        }

        DatabaseScanJobCoordinationState TryGetActiveJob(DatabaseScanJobId jobId)
        {
            return _state.ActiveJobs.SingleOrDefault(job => job.Id == jobId);
        }

        void RemoveActiveJob(DatabaseScanJobCoordinationState activeJob, MetaTime currentTime)
        {
            bool removedOk = _state.ActiveJobs.Remove(activeJob);
            if (!removedOk)
                throw new ArgumentException("Job is not in _state.ActiveJobs", nameof(activeJob));

            DatabaseScanJobHistoryEntry historyEntry = AddJobToHistory(activeJob, currentTime);

            if (activeJob.Phase == DatabaseScanCoordinationPhase.Stopping)
            {
                _state.LatestFinishedJobsOnePerKind.RemoveAll(pastJob => pastJob.FinalState.Spec.IsOfSameKind(activeJob.Spec));
                _state.LatestFinishedJobsOnePerKind.Add(historyEntry);
            }
        }

        DatabaseScanJobHistoryEntry AddJobToHistory(DatabaseScanJobCoordinationState jobState, MetaTime currentTime)
        {
            DatabaseScanJobHistoryEntry historyEntry = new DatabaseScanJobHistoryEntry(
                finalState: jobState,
                endTime:    currentTime);

            _state.JobHistory.Add(historyEntry);
            while (_state.JobHistory.Count > MaxNumJobHistoryEntries)
                _state.JobHistory.RemoveAt(0);
            return historyEntry;
        }

        IMetaLogger DatabaseScanJobManager.IContext.Log => _log;

        Task DatabaseScanJobManager.IContext.PersistStateAsync()
        {
            return PersistStateIntermediate();
        }

        void DatabaseScanJobManager.IContext.ReplyToAsk(EntityAsk ask, MetaMessage reply)
        {
            ReplyToAsk(ask, reply);
        }

        async Task<bool> DatabaseScanJobManager.IContext.TryBeginCancelActiveJobAsync(DatabaseScanJobId jobId)
        {
            (bool isSuccess, DatabaseScanJobCoordinationState /*jobState*/_, string /*error*/_) = await TryBeginCancelActiveJobImplAsync(jobId);
            return isSuccess;
        }

        IEnumerable<DatabaseScanWorkStatus> DatabaseScanJobManager.IContext.TryGetActiveJobWorkerStatusMaybes(DatabaseScanJobId jobId)
        {
            DatabaseScanJobCoordinationState activeJob = TryGetActiveJob(jobId);
            if (activeJob == null)
                return null;

            return activeJob.Workers.Values.Select(w => w.LastKnownStatus);
        }
    }
}
