// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.MaintenanceJob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Server.DatabaseScan.User
{
    /// <summary>
    /// Base class for specifications of database scan jobs.
    /// A job specification is a thing that specifies what exactly a database scan job is meant to do.
    /// </summary>
    [MetaSerializable]
    public abstract class DatabaseScanJobSpec
    {
        /// <summary>
        /// Title of the job, for dashboard.
        /// </summary>
        public abstract string      JobTitle                { get; }

        /// <summary>
        /// Description of the job, for dashboard.
        /// </summary>
        public abstract string      JobDescription          { get; }

        /// <summary>
        /// Tag to identify the job in metrics.
        /// Should be the name of the job kind, such as "NotificationCampaign".
        /// </summary>
        public abstract string      MetricsTag              { get; }

        /// <summary>
        /// Used as opName for metrics about general database queries.
        /// This is in contrast with metrics that are per se specific
        /// to database scan jobs; those use plain MetricsTag.
        /// </summary>
        public string DatabaseQueryOpName => $"Scan_{MetricsTag}";

        /// <summary>
        /// Priority of the job. Higher value means higher priority.
        ///
        /// <para>
        /// When there are multiple jobs available at a time, only the
        /// highest-priority job is run. If a lower-priority job is already
        /// running when a higher-priority job becomes available, the
        /// lower-priority job is paused until the higher-priority job is finished.
        /// </para>
        /// </summary>
        public abstract int                     Priority                { get; }

        public abstract EntityKind              EntityKind              { get; }
        public virtual  ulong                   EntityIdValueUpperBound => EntityId.ValueMask + 1; // By default, scan all entities (of the specified kind)
        public virtual  List<EntityId>          ExplicitEntityList => null;

        /// <summary>
        /// Used to determine if two specs of the same type are of the same "kind";
        /// the scan job history keeps the latest completed job of each kind.
        /// If all jobs of a specific <see cref="DatabaseScanJobSpec"/> type
        /// should be considered "of the same kind" (for informative history-keeping
        /// purposes), then this can just return null.
        /// </summary>
        public virtual object JobKindDiscriminator => null;

        public bool IsOfSameKind(DatabaseScanJobSpec other)
        {
            if (GetType() != other.GetType())
                return false;

            object d0 = JobKindDiscriminator;
            object d1 = other.JobKindDiscriminator;
            if (ReferenceEquals(d0, null))
                return ReferenceEquals(d1, null);
            return d0.Equals(d1);
        }

        /// <summary>
        /// Create the appropriate kind of <see cref="DatabaseScanProcessor"/> for this job.
        /// </summary>
        /// <param name="initialStatisticsMaybe">
        /// If not null, these are statistics that the processor should start with.
        /// These are statistics from a previous processor that was discarded
        /// due to crash stall.
        /// \todo [nuutti] This is not a very elegant mechanism.
        /// </param>
        /// <returns>A new processor</returns>
        public abstract DatabaseScanProcessor CreateProcessor(DatabaseScanProcessingStatistics initialStatisticsMaybe);

        /// <summary>
        /// Aggregate multiple processing statistics into one. This is used to combine
        /// the statistics produced by the multiple workers of a scan job.
        /// Each item in <paramref name="parts"/> is taken from the <see cref="DatabaseScanProcessor.Stats"/>
        /// property of a processor created from this spec.
        /// </summary>
        public abstract DatabaseScanProcessingStatistics ComputeAggregateStatistics(IEnumerable<DatabaseScanProcessingStatistics> parts);

        /// <summary>
        /// Create human-readable summary information based on the processing statistics.
        /// <paramref name="workerStats"/> is produced by <see cref="ComputeAggregateStatistics"/>.
        /// </summary>
        public abstract OrderedDictionary<string, object> CreateSummary(DatabaseScanProcessingStatistics workerStats);
    }

    /// <summary>
    /// Base class for processor-specific statistics.
    /// </summary>
    [MetaSerializable]
    public abstract class DatabaseScanProcessingStatistics
    {
    }

    /// <summary>
    /// Base class for processors of database scans.
    /// A processor is a thing that takes in (batches of) database items, and does something with them.
    /// </summary>
    /// <remarks>
    /// You'll probably want to derive from the helper class <see cref="DatabaseScanProcessor{TScannedItem}"/>,
    /// rather than directly from this.
    /// </remarks>
    [MetaSerializable]
    public abstract class DatabaseScanProcessor
    {
        /// <summary>
        /// Limited interface to the database scan worker, i.e. the actor that owns the processor.
        /// </summary>
        public interface IContext
        {
            IMetaLogger Log { get; }

            void            ActorContinueTaskOnActorContext<TResult>    (Task<TResult> asyncTask, Action<TResult> handleSuccess, Action<Exception> handleFailure);
            Task<TResult>   ActorEntityAskAsync<TResult>                (EntityId targetEntityId, MetaMessage message) where TResult : MetaMessage;
        }

        /// <summary>
        /// How many items should be in each batch scanned by the worker.
        /// The scanned batches are given to the processor with <see cref="StartProcessItemBatchAsync(IContext, IEnumerable{IPersistedEntity})" />.
        /// </summary>
        public abstract int             DesiredScanBatchSize            { get; }
        /// <summary> Desired interval between scans by the worker. </summary>
        public abstract TimeSpan        ScanInterval                    { get; }
        /// <summary> Desired interval between persists by the worker. </summary>
        public abstract TimeSpan        PersistInterval                 { get; }
        /// <summary> Desired interval between calls to <see cref="TickAsync(IContext)"/>. </summary>
        public abstract TimeSpan        TickInterval                    { get; }

        /// <summary>
        /// Whether the processor feels like it could take in more items right now, i.e. doesn't have too much on its plate already.
        /// Basically controls whether <see cref="StartProcessItemBatchAsync(IContext, IEnumerable{IPersistedEntity})"/> will get called.
        /// </summary>
        public abstract bool            CanCurrentlyProcessMoreItems    { get; }
        /// <summary>
        /// Whether the processor has completed all work that's so far been given to it.
        /// Influences whether a scan job is considered done.
        /// </summary>
        public abstract bool            HasCompletedAllWorkSoFar        { get; }
        /// <summary>
        /// Statistics, of job-specific kind, collected so far from the work done by this processor.
        /// </summary>
        public abstract DatabaseScanProcessingStatistics Stats { get; }

        /// <summary>
        /// Start processing a new batch items.
        /// </summary>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="items">Batch of items to start processing</param>
        /// <remarks>
        /// The helper class <see cref="DatabaseScanProcessor{TScannedItem}"/>
        /// wraps this behind a more conveniently typed variant.
        /// </remarks>
        public abstract Task StartProcessItemBatchAsync (IContext context, IEnumerable<IPersistedEntity> items);
        /// <summary>
        /// Update whatever internal state.
        /// <see cref="TickInterval"/> controls the desired interval for calls to this.
        /// </summary>
        /// <param name="context">Context provided by the owning actor</param>
        public abstract Task TickAsync                  (IContext context);
        /// <summary>
        /// Job has been cancelled.
        /// Processor should cancel any ongoing stuff, if needed.
        /// </summary>
        /// <param name="context">Context provided by the owning actor</param>
        public abstract void Cancel                     (IContext context);
    }

    /// <summary>
    /// Helper class implementing some boilerplate parts of <see cref="DatabaseScanProcessor"/>
    /// based on the concrete <see cref="IPersistedEntity"/>-implementing type of the
    /// scanned items.
    /// </summary>
    /// <typeparam name="TScannedItem">The persisted entity type to scan</typeparam>
    [MetaSerializable]
    public abstract class DatabaseScanProcessor<TScannedItem> : DatabaseScanProcessor
        where TScannedItem : IPersistedEntity
    {
        public sealed override Task StartProcessItemBatchAsync(IContext context, IEnumerable<IPersistedEntity> items)
        {
            return StartProcessItemBatchAsync(context, items.Cast<TScannedItem>());
        }

        public abstract Task StartProcessItemBatchAsync(IContext context, IEnumerable<TScannedItem> items);
    }

    /// <summary>
    /// Base class for managers that keep track of database scan jobs of a specific kind,
    /// and can be requested to provide such a job that can be started, if any.
    /// Database scan coordinator contains managers and calls to them when it decides
    /// that a job can be run.
    /// </summary>
    public abstract class DatabaseScanJobManager
    {
        /// <summary>
        /// Limited interface to the database scan coordinator, i.e. the actor that owns the manager.
        /// </summary>
        public interface IContext
        {
            IMetaLogger Log { get; }

            Task                                PersistStateAsync                   ();
            void                                ReplyToAsk                          (EntityAsk ask, MetaMessage reply);
            /// <summary>
            /// Try to begin cancelling the given job. If successful, <see cref="DatabaseScanJobManager.OnJobCancellationBeganAsync"/>
            /// will be called, and the state will be persisted.
            /// Can be unsuccessful if the job is not currently active and in a state where it can be cancelled.
            /// </summary>
            /// <param name="jobId">The id of the job to cancel</param>
            /// <returns>Whether cancellation was successfully started</returns>
            Task<bool>                          TryBeginCancelActiveJobAsync        (DatabaseScanJobId jobId);
            /// <summary>
            /// Try to get an enumerable containing the statuses of the workers of the given job.
            /// Returns null if the job is not currently active. Otherwise returns an enumerable of potentially-null statuses,
            /// each corresponding to a worker. If a worker's status is null, it means the worker hasn't been
            /// started yet.
            /// </summary>
            /// <param name="jobId">The id of the job whose workers' statuses to get</param>
            /// <returns>Enumerable of potentially-null worker statuses of the active job, if any; otherwise null</returns>
            // \todo [nuutti] This is awkward
            IEnumerable<DatabaseScanWorkStatus> TryGetActiveJobWorkerStatusMaybes   (DatabaseScanJobId jobId);
        }

        /// <summary>
        /// Notifies the manager when the actor is initialized
        /// </summary>
        /// <param name="context">The actor that is being initialized</param>
        public abstract Task InitializeAsync(IContext context);

        /// <summary>
        /// Get a job, if any, that can be started at the given time.
        /// </summary>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="currentTime">Current time</param>
        /// <returns>The next job that is due, if any, and null otherwise, and whether it can be started, if false <see cref="OnJobDidNotStartAsync"/> will be invoked and the job will be recorded in history</returns>
        public abstract (DatabaseScanJobSpec jobSpec, bool canStart)     TryGetNextDueJob      (IContext context, MetaTime currentTime);

        /// <summary>
        /// Notifies the manager that the given job could not be started.
        /// </summary>
        /// <remarks>State will be persisted automatically, no need for manager to do it via <paramref name="context"/>.</remarks>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="jobSpec">The job that could not start. This is a job specification that was previously returned by this manager's <see cref="TryGetNextDueJob"/></param>
        /// <param name="currentTime">Current time</param>
        public abstract Task                    OnJobDidNotStartAsync            (IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime);

        /// <summary>
        /// Notifies the manager that the given job has started.
        /// </summary>
        /// <remarks>State will be persisted automatically, no need for manager to do it via <paramref name="context"/>.</remarks>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="jobSpec">The job that has started. This is a job specification that was previously returned by this manager's <see cref="TryGetNextDueJob"/></param>
        /// <param name="jobId">The unique database scan job id assigned for this job</param>
        /// <param name="currentTime">Current time</param>
        public abstract Task                    OnJobStartedAsync            (IContext context, DatabaseScanJobSpec jobSpec, DatabaseScanJobId jobId, MetaTime currentTime);
        /// <summary>
        /// Notifies the manager that the cancellation of the given job has started.
        /// <see cref="OnJobStoppedAsync"/> will eventually be called with wasCancelled=true.
        /// </summary>
        /// <remarks>
        /// State will be persisted automatically, no need for manager to do it via <paramref name="context"/>.
        /// <para>
        /// This is called when the cancellation began for any reason:
        /// when the manager itself calls <see cref="IContext.TryBeginCancelActiveJobAsync"/>,
        /// but also when the cancellation was initiated from the outside.
        /// </para>
        /// </remarks>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="jobSpec">The job whose cancellation was started. This is a job specification that was previously returned by this manager's <see cref="TryGetNextDueJob"/></param>
        /// <param name="currentTime">Current time</param>
        public abstract Task                    OnJobCancellationBeganAsync  (IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime);
        /// <summary>
        /// Notifies the manager that the given job has ended, either due to finishing normally or due to cancellation.
        /// </summary>
        /// <remarks>State will be persisted automatically, no need for manager to do it via <paramref name="context"/>.</remarks>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="jobSpec">The job that has stopped. This is a job specification that was previously returned by this manager's <see cref="TryGetNextDueJob"/></param>
        /// <param name="currentTime">Current time</param>
        public abstract Task                    OnJobStoppedAsync            (IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime, bool wasCancelled, IEnumerable<DatabaseScanWorkStatus> workerStatusMaybes);
        /// <summary>
        /// Try to get a Task to handle the given message, which was sent to the database scan coordinator.
        /// The message should be handled if it is of a type meant for this manager.
        /// </summary>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="message">The message to try to handle</param>
        /// <param name="handleAsync">Output parameter for the handler of the message, if any</param>
        /// <returns>Whether the message is handled by <paramref name="handleAsync"/>.</returns>
        public abstract bool                    TryGetMessageHandler    (IContext context, MetaMessage message, out Task handleAsync);
        /// <summary>
        /// Try to get a Task to handle the given entity ask, which was sent to the database scan coordinator.
        /// The ask should be handled if it is of a type meant for this manager.
        /// </summary>
        /// <param name="context">Context provided by the owning actor</param>
        /// <param name="ask">The ask to try to handle</param>
        /// <param name="message">The message accompanying the ask</param>
        /// <param name="handleAsync">Output parameter for the handler of the ask, if any</param>
        /// <returns>Whether the ask is handled by <paramref name="handleAsync"/>.</returns>
        public abstract bool                    TryGetEntityAskHandler  (IContext context, EntityAsk ask, MetaMessage message, out Task handleAsync);
        /// <summary>
        /// Get known jobs that are expected to be started in the future.
        /// This is used for informative purposes in the dashboard, and
        /// there are no strict requirements for what jobs this returns.
        /// Depending on the type of the job, it may not make sense to return
        /// _all_ future jobs. For example, if a job is infinitely automatically
        /// recurring, it doesn't make sense to return an infinite number of
        /// jobs, but rather just the first occurrence.
        /// </summary>
        public abstract IEnumerable<UpcomingDatabaseScanJob> GetUpcomingJobs(MetaTime currentTime);

        /// <summary>
        /// Whether multiple jobs from this manager are allowed to run simultaneously.
        /// The scan coordinator tracks which manager each active job came from, and
        /// respects this property.
        /// This being false could be equivalently implemented in the manager itself,
        /// by tracking if a job is active and then always returning null from
        /// <see cref="TryGetNextDueJob"/> if a job is active.
        /// This property is thus just a convenience for a common behavior.
        /// </summary>
        public virtual bool AllowMultipleSimultaneousJobs => false;
    }

    [MetaSerializable]
    public class UpcomingDatabaseScanJob
    {
        /// <summary>
        /// An identifier for this upcoming job.
        /// Note that this is not a <see cref="DatabaseScanJobId"/>, because
        /// a job gets a <see cref="DatabaseScanJobId"/> only when it becomes
        /// active, and an upcoming job is not yet active.
        /// Instead, the meaning of this id is up to the specific <see cref="DatabaseScanJobManager"/>
        /// which reported this upcoming job.
        /// </summary>
        [MetaMember(1)] public string Id;
        [MetaMember(2)] public DatabaseScanJobSpec Spec;
        /// <summary>
        /// The time when the job is expected to start, if the job has
        /// time-based scheduling.
        ///
        /// If the job has no time-based scheduling, this is null, and
        /// the job is considered to be "enqueued" to start as soon as
        /// there are no blockers (such as another job of the same kind
        /// already active).
        /// </summary>
        [MetaMember(3)] public MetaTime? EarliestStartTime;

        UpcomingDatabaseScanJob() { }
        public UpcomingDatabaseScanJob(string id, DatabaseScanJobSpec spec, MetaTime? earliestStartTime)
        {
            Id = id;
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            EarliestStartTime = earliestStartTime;
        }
    }

    // \todo [nuutti] Move elsewhere?
    [MetaSerializable]
    public struct EntityIdRange
    {
        [MetaMember(1)] public EntityKind   Kind                { get; private set; }
        [MetaMember(2)] public ulong        FirstValueInclusive { get; private set; }
        [MetaMember(3)] public ulong        LastValueInclusive  { get; private set; }

        [IgnoreDataMember] public EntityId  FirstInclusive  => EntityId.Create(Kind, FirstValueInclusive);
        [IgnoreDataMember] public EntityId  LastInclusive   => EntityId.Create(Kind, LastValueInclusive);

        public EntityIdRange(EntityKind kind, ulong firstValueInclusive, ulong lastValueInclusive)
        {
            MetaDebug.Assert(firstValueInclusive <= lastValueInclusive, "EntityIdRange must have first no greater than last");

            Kind                = kind;
            FirstValueInclusive = firstValueInclusive;
            LastValueInclusive  = lastValueInclusive;
        }

        public override string ToString()
        {
            return $"EntityIdRange(firstInclusive: {FirstInclusive}, lastInclusive: {LastInclusive})";
        }
    }
}
