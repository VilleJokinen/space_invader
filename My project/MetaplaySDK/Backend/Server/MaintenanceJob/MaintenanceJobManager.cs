// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.DatabaseScan;
using Metaplay.Server.DatabaseScan.User;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.MaintenanceJob
{
    [MetaMessage(MessageCodesCore.AddMaintenanceJobRequest, MessageDirection.ServerInternal)]
    public class AddMaintenanceJobRequest : MetaMessage
    {
        public MaintenanceJobSpec Spec { get; private set; }

        AddMaintenanceJobRequest(){ }
        public AddMaintenanceJobRequest(MaintenanceJobSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        }
    }
    [MetaMessage(MessageCodesCore.AddMaintenanceJobResponse, MessageDirection.ServerInternal)]
    public class AddMaintenanceJobResponse : MetaMessage
    {
        public bool     IsSuccess           { get; private set; }
        public int      MaintenanceJobId    { get; private set; }
        public string   Error               { get; private set; }

        AddMaintenanceJobResponse(){ }
        public static AddMaintenanceJobResponse Ok      (int maintenanceJobId)  => new AddMaintenanceJobResponse{ IsSuccess = true, MaintenanceJobId = maintenanceJobId };
        public static AddMaintenanceJobResponse Failure (string error)          => new AddMaintenanceJobResponse{ IsSuccess = false, Error = error };
    }

    [MetaMessage(MessageCodesCore.RemoveMaintenanceJobRequest, MessageDirection.ServerInternal)]
    public class RemoveMaintenanceJobRequest : MetaMessage
    {
        public int MaintenanceJobId { get; private set; }

        RemoveMaintenanceJobRequest(){ }
        public RemoveMaintenanceJobRequest(int maintenanceJobId)
        {
            MaintenanceJobId = maintenanceJobId;
        }
    }
    [MetaMessage(MessageCodesCore.RemoveMaintenanceJobResponse, MessageDirection.ServerInternal)]
    public class RemoveMaintenanceJobResponse : MetaMessage
    {
        public bool                 IsSuccess   { get; private set; }
        public MaintenanceJobSpec   JobSpec     { get; private set; }
        public string               Error       { get; private set; }

        RemoveMaintenanceJobResponse(){ }
        public static RemoveMaintenanceJobResponse Ok        (MaintenanceJobSpec jobSpec)   => new RemoveMaintenanceJobResponse{ IsSuccess = true, JobSpec = jobSpec };
        public static RemoveMaintenanceJobResponse Failure   (string error)                 => new RemoveMaintenanceJobResponse{ IsSuccess = false, Error = error };
    }

    [MetaSerializable]
    public class EnqueuedJob
    {
        [MetaMember(1)] public int                  MaintenanceJobId    { get; private set; }
        [MetaMember(2)] public MaintenanceJobSpec   Spec                { get; private set; }
        [MetaMember(3)] public MetaTime             EnqueuedAt          { get; private set; }

        EnqueuedJob(){ }
        public EnqueuedJob(int maintenanceJobId, MaintenanceJobSpec spec, MetaTime enqueuedAt)
        {
            MaintenanceJobId = maintenanceJobId;
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            EnqueuedAt = enqueuedAt;
        }
    }

    [MetaSerializable]
    public class ActiveJob
    {
        [MetaMember(1)] public int                  MaintenanceJobId    { get; private set; }
        [MetaMember(2)] public MaintenanceJobSpec   Spec                { get; private set; }
        [MetaMember(3)] public DatabaseScanJobId    DatabaseScanJobId   { get; private set; }
        [MetaMember(4)] public MetaTime             StartTime           { get; private set; }

        ActiveJob(){ }
        public ActiveJob(int maintenanceJobId, MaintenanceJobSpec spec, DatabaseScanJobId databaseScanJobId, MetaTime startTime)
        {
            MaintenanceJobId = maintenanceJobId;
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            DatabaseScanJobId = databaseScanJobId;
            StartTime = startTime;
        }
    }

    [MetaSerializable]
    public class PastJob
    {
        [MetaMember(1)] public int                          MaintenanceJobId        { get; private set; }
        [MetaMember(2)] public MaintenanceJobSpec           Spec                    { get; private set; }
        [MetaMember(3)] public DatabaseScanJobId            DatabaseScanJobId       { get; private set; }
        [MetaMember(4)] public MetaTime                     StartTime               { get; private set; }
        [MetaMember(5)] public MetaTime                     EndTime                 { get; private set; }
        [MetaMember(6)] public List<DatabaseScanWorkStatus> FinalWorkStatusMaybes   { get; private set; }

        public OrderedDictionary<string, object> Summary
        {
            get
            {
                IEnumerable<DatabaseScanProcessingStatistics> processingStatistics =
                    FinalWorkStatusMaybes
                    .Where(status => status != null)
                    .Select(status => status.ProcessingStatistics);

                return Spec.CreateSummary(Spec.ComputeAggregateStatistics(processingStatistics));
            }
        }

        PastJob(){ }
        public PastJob(int maintenanceJobId, MaintenanceJobSpec spec, DatabaseScanJobId databaseScanJobId, MetaTime startTime, MetaTime endTime, List<DatabaseScanWorkStatus> finalWorkStatusMaybes)
        {
            MaintenanceJobId = maintenanceJobId;
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            DatabaseScanJobId = databaseScanJobId;
            StartTime = startTime;
            EndTime = endTime;
            FinalWorkStatusMaybes = finalWorkStatusMaybes ?? throw new ArgumentNullException(nameof(finalWorkStatusMaybes));
        }
    }

    [MetaSerializable]
    public class MaintenanceJobManager : DatabaseScanJobManager
    {
        [MetaMember(1)] int                 _runningMaintanceJobId  { get; set; } = 0;
        [MetaMember(2)] List<EnqueuedJob>   _enqueuedJobs           { get; set; } = new List<EnqueuedJob>();
        [MetaMember(3)] ActiveJob           _activeJob              { get; set; } = null;
        [MetaMember(4)] List<PastJob>       _latestJobsDeprecated   { get; set; } = new List<PastJob>();

        public List<PastJob> LatestJobsDeprecated => _latestJobsDeprecated;

        public override Task InitializeAsync(IContext context)
        {
            return Task.CompletedTask;
        }

        public override (DatabaseScanJobSpec jobSpec, bool canStart) TryGetNextDueJob(IContext context, MetaTime currentTime)
        {
            if (_activeJob != null)
                return (null, false);

            if (_enqueuedJobs.Count == 0)
                return (null, false);

            return (_enqueuedJobs.First().Spec, true);
        }

        public override Task OnJobDidNotStartAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            return Task.CompletedTask;
        }

        public override Task OnJobStartedAsync(IContext context, DatabaseScanJobSpec jobSpec, DatabaseScanJobId jobId, MetaTime currentTime)
        {
            EnqueuedJob enqueuedJob = _enqueuedJobs.First();
            _enqueuedJobs.RemoveAt(0);

            _activeJob = new ActiveJob(enqueuedJob.MaintenanceJobId, enqueuedJob.Spec, jobId, currentTime);

            return Task.CompletedTask;
        }

        public override IEnumerable<UpcomingDatabaseScanJob> GetUpcomingJobs(MetaTime currentTime)
        {
            return _enqueuedJobs.Select(enqueuedJob =>
            {
                return new UpcomingDatabaseScanJob(
                    id: enqueuedJob.MaintenanceJobId.ToString(CultureInfo.InvariantCulture),
                    enqueuedJob.Spec,
                    earliestStartTime: null);
            });
        }

        public override Task OnJobCancellationBeganAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            return Task.CompletedTask;
        }

        public override Task OnJobStoppedAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime, bool wasCancelled, IEnumerable<DatabaseScanWorkStatus> workerStatusMaybes)
        {
            ActiveJob job = _activeJob ?? throw new InvalidOperationException("Got OnStoppedJob but _activeJob is null");
            _activeJob = null;

            if (!wasCancelled)
            {
                // \note This code used to add an entry to _latestJobs (now _latestJobsDeprecated).
                //       But now, DatabaseScanCoordinator keeps a similar list for scan jobs generally,
                //       so here we merely remove the entry from _latestJobsDeprecated, eventually leading
                //       to the list being empty (as soon as each maintenance job kind has been finished
                //       at least once, after the deprecation).
                _latestJobsDeprecated.RemoveAll(pastJob => pastJob.Spec.IsOfSameKind(job.Spec));
            }
            return Task.CompletedTask;
        }

        public override bool TryGetMessageHandler(IContext context, MetaMessage message, out Task handleAsync)
        {
            handleAsync = default;
            return false;
        }

        public override bool TryGetEntityAskHandler(IContext context, EntityShard.EntityAsk ask, MetaMessage message, out Task handleAsync)
        {
            switch (message)
            {
                case AddMaintenanceJobRequest addReq:       handleAsync = HandleAskAsync(context, ask, addReq);     return true;
                case RemoveMaintenanceJobRequest removeReq: handleAsync = HandleAskAsync(context, ask, removeReq);  return true;

                default:
                    handleAsync = default;
                    return false;
            }
        }

        public Task HandleAskAsync(IContext context, EntityShard.EntityAsk ask, AddMaintenanceJobRequest enqueueReq)
        {
            int maintanceJobId = _runningMaintanceJobId++;
            _enqueuedJobs.Add(new EnqueuedJob(maintanceJobId, enqueueReq.Spec, MetaTime.Now));
            context.ReplyToAsk(ask, AddMaintenanceJobResponse.Ok(maintanceJobId));
            return Task.CompletedTask;
        }

        public Task HandleAskAsync(IContext context, EntityShard.EntityAsk ask, RemoveMaintenanceJobRequest removeReq)
        {
            RemoveMaintenanceJobResponse reply;

            EnqueuedJob enqueuedJob = _enqueuedJobs.SingleOrDefault(enqueuedJob => enqueuedJob.MaintenanceJobId == removeReq.MaintenanceJobId);

            if (enqueuedJob == null)
                reply = RemoveMaintenanceJobResponse.Failure(Invariant($"No maintenance job with id {removeReq.MaintenanceJobId} in the queue"));
            else
            {
                _enqueuedJobs.Remove(enqueuedJob);
                reply = RemoveMaintenanceJobResponse.Ok(enqueuedJob.Spec);
            }

            context.ReplyToAsk(ask, reply);
            return Task.CompletedTask;
        }
    }
}
