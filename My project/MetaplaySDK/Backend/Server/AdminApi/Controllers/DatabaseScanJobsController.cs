// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.DatabaseScan;
using Metaplay.Server.DatabaseScan.User;
using Metaplay.Server.MaintenanceJob;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static Metaplay.Server.AdminApi.Controllers.MaintenanceJobController;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for endpoints related to database scan jobs
    /// </summary>
    public class DatabaseScanJobsController : GameAdminApiController
    {
        public DatabaseScanJobsController(ILogger<DatabaseScanJobsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log event for when a database scan job cancellation was started.
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerDatabaseScanJobCancelled)]
        public class GameServerEventDatabaseScanJobCancelled : GameServerEventPayloadBase
        {
            /// <summary>
            /// State of the job just after the cancellation was started.
            /// </summary>
            [MetaMember(1)] public DatabaseScanJobCoordinationState JobState { get; private set; }
            GameServerEventDatabaseScanJobCancelled() { }
            public GameServerEventDatabaseScanJobCancelled(DatabaseScanJobCoordinationState jobState){ JobState = jobState; }
            public override string SubsystemName => "DatabaseScan";
            public override string EventTitle => "Job cancelled";
            public override string EventDescription => Invariant($"Database scan job '{JobState.Spec.JobTitle}' (job #{JobState.Id.SequentialId}) cancelled.");
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerDatabaseScanGlobalPauseSet)]
        public class GameServerEventDatabaseScanGlobalPauseSet : GameServerEventPayloadBase
        {
            [MetaMember(1)] public bool IsPaused { get; private set; }

            GameServerEventDatabaseScanGlobalPauseSet() { }
            public GameServerEventDatabaseScanGlobalPauseSet(bool isPaused){ IsPaused = isPaused; }
            public override string SubsystemName => "DatabaseScan";
            public override string EventTitle => "Global pause state set";
            public override string EventDescription => Invariant($"Global pause was {(IsPaused ? "enabled" : "disabled")} for database scan jobs.");
        }

        public enum JobPhaseCategory
        {
            Active,
            Past,
            Upcoming,
        }

        public enum JobPhase
        {
            // Phases that match exactly with DatabaseScanCoordinationPhases for active jobs
            Initializing,
            Paused,
            Resuming,
            Running,
            Pausing,
            Stopping,
            Cancelling,

            // Phases that match with the Stopping and Cancelling DatabaseScanCoordinationPhases for past jobs
            Stopped,
            Cancelled,

            // Phases for upcoming jobs
            /// <summary> Upcoming, with a specific intended start time.</summary>
            Scheduled,
            /// <summary> Upcoming, but with no specific start time, instead waiting for its turn after some other jobs.</summary>
            Enqueued,
            /// <summary>
            /// The job could not be started due to matching the start conditions (e.g. notifications are disabled but a job is scheduled)
            /// </summary>
            DidNotRun,
        }

        public static JobPhase GetActiveJobPhase(DatabaseScanCoordinationPhase phase)
        {
            switch (phase)
            {
                case DatabaseScanCoordinationPhase.Initializing:            return JobPhase.Initializing;
                case DatabaseScanCoordinationPhase.Paused:                  return JobPhase.Paused;
                case DatabaseScanCoordinationPhase.Resuming:                return JobPhase.Resuming;
                case DatabaseScanCoordinationPhase.Running:                 return JobPhase.Running;
                case DatabaseScanCoordinationPhase.Pausing:                 return JobPhase.Pausing;
                case DatabaseScanCoordinationPhase.Stopping:                return JobPhase.Stopping;
                case DatabaseScanCoordinationPhase.Cancelling:              return JobPhase.Cancelling;
                default:
                    throw new InvalidEnumArgumentException(nameof(phase), (int)phase, typeof(DatabaseScanCoordinationPhase));
            }
        }

        public static JobPhase GetPastJobPhase(DatabaseScanCoordinationPhase phase)
        {
            switch (phase)
            {
                // Note that it can be in the initializing phase when TryGetNextDueJob returns false,
                // most notably this is used in notification campaign when jobs are schedule but push notifications have been disabled
                case DatabaseScanCoordinationPhase.Initializing:            return JobPhase.DidNotRun;
                case DatabaseScanCoordinationPhase.Stopping:                return JobPhase.Stopped;
                case DatabaseScanCoordinationPhase.Cancelling:              return JobPhase.Cancelled;
                default:
                    // \note Shouldn't happen (past jobs should be either stopped or cancelled), being defensive
                    return JobPhase.Cancelled;
            }
        }

        public static JobPhase GetUpcomingJobPhase(UpcomingDatabaseScanJob job)
        {
            if (job.EarliestStartTime.HasValue)
                return JobPhase.Scheduled;
            else
                return JobPhase.Enqueued;
        }

        /// <summary>
        /// Job id used for this admin api.
        ///
        /// Compared to <see cref="DatabaseScanJobId"/>, this is generalized to
        /// concern also jobs that haven't actually been created yet, i.e. upcoming jobs.
        /// Upcoming jobs aren't "jobs" in the strictest sense, and don't have a
        /// <see cref="DatabaseScanJobId"/> yet - they're identified in a somewhat
        /// ad hoc manner, see <see cref="DatabaseScanJobUpcomingEntry"/>.
        /// </summary>
        public abstract class JobApiId
        {
            public class ProperDatabaseScanJobId : JobApiId
            {
                public DatabaseScanJobId Id;

                public ProperDatabaseScanJobId(DatabaseScanJobId id)
                {
                    Id = id;
                }

                public override string ToString() => Invariant($"ScanJob_{Id.SequentialId}_{Id.RandomTag}");
            }

            public class UpcomingJobId : JobApiId
            {
                public DatabaseScanJobManagerKind ManagerKind;
                public string Id;

                public UpcomingJobId(DatabaseScanJobManagerKind managerKind, string id)
                {
                    ManagerKind = managerKind;
                    Id = id;
                }

                public override string ToString() => $"Upcoming_{ManagerKind}_{Id}";
            }

            public abstract override string ToString();

            public static JobApiId Parse(string str)
            {
                if (str.StartsWith("ScanJob_", StringComparison.Ordinal))
                {
                    string[] parts = str.Split("_");
                    if (parts.Length != 3)
                        throw new FormatException($"Expected 3 underscore-separated parts: {str}");

                    return new ProperDatabaseScanJobId(new DatabaseScanJobId(
                        sequentialId:   int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture),
                        randomTag:      uint.Parse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture)));
                }
                else if (str.StartsWith("Upcoming_", StringComparison.Ordinal))
                {
                    string[] parts = str.Split("_");
                    if (parts.Length != 3)
                        throw new FormatException($"Expected 3 underscore-separated parts: {str}");

                    return new UpcomingJobId(
                        managerKind:    EnumUtil.Parse<DatabaseScanJobManagerKind>(parts[1]),
                        id:             parts[2]);
                }
                else
                    throw new FormatException($"Unknown format for {nameof(JobApiId)}: {str}");
            }
        }

        public class JobInfo
        {
            public string                               Id;
            public JobPhaseCategory                     PhaseCategory;
            public int                                  Priority;
            public string                               JobTitle;
            public string                               JobDescription;
            public string                               MetricsTag;
            public MetaTime?                            StartTime;
            public JobPhase                             Phase;
            public MetaTime?                            EndTime;
            public DatabaseScanStatistics               ScanStatistics;
            public DatabaseScanProcessingStatistics     ProcessingStatistics;
            public int?                                 NumWorkers;
            public OrderedDictionary<string, object>    Summary;
            public bool                                 CanCancel;
            public string                               CannotCancelReason;

            JobInfo(JobApiId id, JobPhaseCategory phaseCategory, DatabaseScanJobSpec spec, MetaTime? startTime, JobPhase phase, MetaTime? endTime, DatabaseScanStatistics scanStatistics, DatabaseScanProcessingStatistics processingStatistics, int? numWorkers, OrderedDictionary<string, object> summary, bool canCancel, string cannotCancelReason)
            {
                Id = id.ToString();
                PhaseCategory = phaseCategory;
                Priority = spec.Priority;
                JobTitle = spec.JobTitle;
                JobDescription = spec.JobDescription;
                MetricsTag = spec.MetricsTag;
                StartTime = startTime;
                Phase = phase;
                EndTime = endTime;
                ScanStatistics = scanStatistics;
                ProcessingStatistics = processingStatistics;
                NumWorkers = numWorkers;
                Summary = summary;
                CanCancel = canCancel;
                CannotCancelReason = cannotCancelReason;
            }

            public static JobInfo FromActiveJob(DatabaseScanJobCoordinationState job)
            {
                IEnumerable<DatabaseScanWorkStatus> workStatuses = job.Workers.Values.Select(w => w.LastKnownStatus).Where(status => status != null);
                DatabaseScanProcessingStatistics processingStatistics = job.Spec.ComputeAggregateStatistics(workStatuses.Select(status => status.ProcessingStatistics));

                return new JobInfo(
                    id: new JobApiId.ProperDatabaseScanJobId(job.Id),
                    phaseCategory: JobPhaseCategory.Active,
                    spec: job.Spec,
                    startTime: job.StartTime,
                    phase: GetActiveJobPhase(job.Phase),
                    endTime: null,
                    scanStatistics: DatabaseScanStatistics.ComputeAggregate(workStatuses.Select(status => status.ScanStatistics)),
                    processingStatistics: processingStatistics,
                    numWorkers: job.Workers.Count,
                    summary: job.Spec.CreateSummary(processingStatistics),
                    canCancel: true,
                    cannotCancelReason: null);
            }

            public static JobInfo FromJobHistoryEntry(DatabaseScanJobHistoryEntry historyEntry)
            {
                DatabaseScanJobCoordinationState job = historyEntry.FinalState;

                IEnumerable<DatabaseScanWorkStatus> workStatuses = job.Workers.Values.Select(w => w.FinalActiveStatus).Where(status => status != null);
                DatabaseScanProcessingStatistics processingStatistics = job.Spec.ComputeAggregateStatistics(workStatuses.Select(status => status.ProcessingStatistics));

                return new JobInfo(
                    id: new JobApiId.ProperDatabaseScanJobId(job.Id),
                    phaseCategory: JobPhaseCategory.Past,
                    spec: job.Spec,
                    startTime: job.StartTime,
                    phase: GetPastJobPhase(job.Phase),
                    endTime: historyEntry.EndTime,
                    scanStatistics: DatabaseScanStatistics.ComputeAggregate(workStatuses.Select(status => status.ScanStatistics)),
                    processingStatistics: processingStatistics,
                    numWorkers: job.Workers.Count,
                    summary: job.Spec.CreateSummary(processingStatistics),
                    canCancel: false,
                    cannotCancelReason: "Past jobs cannot be cancelled.");
            }

            public static JobInfo FromUpcomingJobEntry(DatabaseScanJobUpcomingEntry upcomingEntry)
            {
                UpcomingDatabaseScanJob job = upcomingEntry.Job;

                bool canCancel;
                string cannotCancelReason;
                if (upcomingEntry.JobManagerKind == DatabaseScanJobManagerKind.MaintenanceJobManager)
                {
                    canCancel = true;
                    cannotCancelReason = null;
                }
                else
                {
                    canCancel = false;

                    // \todo This is hard-codey and speaks of dashboard UIs pretty explicitly
                    switch (upcomingEntry.JobManagerKind)
                    {
                        case DatabaseScanJobManagerKind.ScheduledPlayerDeletion:
                            cannotCancelReason = "Automatically recurring player deletion jobs cannot be manually cancelled until they've started.";
                            break;

                        case DatabaseScanJobManagerKind.NotificationCampaign:
                            cannotCancelReason = "Upcoming notification campaign jobs can only be deleted from the dedicated notification campaign page.";
                            break;

                        default:
                            cannotCancelReason = "Upcoming jobs of this type cannot be deleted via this interface.";
                            break;
                    }
                }

                return new JobInfo(
                    id: new JobApiId.UpcomingJobId(upcomingEntry.JobManagerKind, job.Id),
                    phaseCategory: JobPhaseCategory.Upcoming,
                    spec: job.Spec,
                    startTime: job.EarliestStartTime,
                    phase: GetUpcomingJobPhase(job),
                    endTime: null,
                    scanStatistics: null,
                    processingStatistics: null,
                    numWorkers: null,
                    summary: null,
                    canCancel: canCancel,
                    cannotCancelReason: cannotCancelReason);
            }
        }

        public class DatabaseScanJobsResponse
        {
            public List<JobInfo>                ActiveJobs;
            public List<JobInfo>                JobHistory;
            public bool                         HasMoreJobHistory;
            public List<JobInfo>                LatestFinishedJobsOnePerKind;
            public List<JobInfo>                UpcomingJobs;
            public bool                         GlobalPauseIsEnabled;

            public DatabaseScanJobsResponse(List<JobInfo> activeJobs, List<JobInfo> jobHistory, bool hasMoreJobHistory, List<JobInfo> latestFinishedJobsOnePerKind, List<JobInfo> upcomingJobs, bool globalPauseIsEnabled)
            {
                ActiveJobs = activeJobs;
                JobHistory = jobHistory;
                HasMoreJobHistory = hasMoreJobHistory;
                LatestFinishedJobsOnePerKind = latestFinishedJobsOnePerKind;
                UpcomingJobs = upcomingJobs;
                GlobalPauseIsEnabled = globalPauseIsEnabled;
            }
        }

        public class CancelDatabaseScanJobResponse
        {
            public string Message;

            public CancelDatabaseScanJobResponse(string message)
            {
                Message = message;
            }
        }

        /// <summary>
        /// Get info about database scan jobs
        /// Usage:  GET /api/databaseScanJobs
        /// Test:   curl http://localhost:5550/api/databaseScanJobs
        /// </summary>
        /// <param name="jobHistoryLimit">Optional: Number of historical jobs to return, default value is 10</param>
        [HttpGet("databaseScanJobs")]
        [RequirePermission(MetaplayPermissions.ApiScanJobsView)]
        public async Task<ActionResult> GetDatabaseScanJobs([FromQuery] int jobHistoryLimit = 10)
        {
            ListDatabaseScanJobsRequest request = new ListDatabaseScanJobsRequest(jobHistoryLimit);
            ListDatabaseScanJobsResponse response = await AskEntityAsync<ListDatabaseScanJobsResponse>(DatabaseScanCoordinatorActor.EntityId, request);

            // Return results
            return Ok(new DatabaseScanJobsResponse(
                activeJobs: response.ActiveJobs.Select(JobInfo.FromActiveJob).ToList(),
                jobHistory: response.JobHistory.Select(JobInfo.FromJobHistoryEntry).ToList(),
                hasMoreJobHistory: response.HasMoreJobHistory,
                latestFinishedJobsOnePerKind: response.LatestFinishedJobsOnePerKind.Select(JobInfo.FromJobHistoryEntry).ToList(),
                upcomingJobs: response.UpcomingJobs.Select(JobInfo.FromUpcomingJobEntry).ToList(),
                globalPauseIsEnabled: response.GlobalPauseIsEnabled
            ));
        }

        /// <summary>
        /// Cancel an active database scan job, or attempt to delete an
        /// upcoming job.
        ///
        /// Not all types of upcoming jobs can be deleted using this
        /// endpoint - currently, maintenance jobs are the only kind
        /// for which upcoming jobs can be deleted. Other kinds of
        /// jobs need to be deleted by job-specific means, if any -
        /// for example, upcoming notification campaigns should be deleted
        /// via the apis defined in <see cref="NotificationsController"/>.
        ///
        /// Usage:  PUT /api/databaseScanJobs/{JOBID}/cancel
        /// </summary>
        /// <param name="jobIdStr">A job id as in <see cref="JobInfo.Id"/>; a stringified <see cref="JobApiId"/></param>
        [HttpPut("databaseScanJobs/{jobIdStr}/cancel")]
        [RequirePermission(MetaplayPermissions.ApiScanJobsCancel)]
        public async Task<ActionResult> CancelDatabaseScanJob(string jobIdStr)
        {
            JobApiId jobApiId;
            try
            {
                jobApiId = JobApiId.Parse(jobIdStr);
            }
            catch (Exception ex)
            {
                throw new MetaplayHttpException(400, "Failed to parse job id.", $"Job id {jobIdStr} is not valid: {ex.Message}");
            }

            if (jobApiId is JobApiId.ProperDatabaseScanJobId properDatabaseScanJobId)
            {
                DatabaseScanJobId scanJobId = properDatabaseScanJobId.Id;

                BeginCancelDatabaseScanJobRequest request = new BeginCancelDatabaseScanJobRequest(scanJobId);
                BeginCancelDatabaseScanJobResponse response = await AskEntityAsync<BeginCancelDatabaseScanJobResponse>(DatabaseScanCoordinatorActor.EntityId, request);
                // \todo [nuutti] Figure out proper statusCode
                if (!response.IsSuccess)
                    throw new MetaplayHttpException(400, "Failed to cancel job.", response.Error);

                await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerEventDatabaseScanJobCancelled(response.JobState)));

                return Ok(new CancelDatabaseScanJobResponse(Invariant($"Started cancellation of database scan job '{response.JobState.Spec.JobTitle}' (job #{response.JobState.Id.SequentialId}).")));
            }
            else if (jobApiId is JobApiId.UpcomingJobId upcomingJobId)
            {
                // Only maintenance jobs can be deleted via this endpoint.
                // \todo This is really hard-codey. Could implement this more generally in DatabaseScanCoordinator.
                if (upcomingJobId.ManagerKind == DatabaseScanJobManagerKind.MaintenanceJobManager)
                {
                    int maintenanceJobId = int.Parse(upcomingJobId.Id, NumberStyles.None, CultureInfo.InvariantCulture);

                    RemoveMaintenanceJobRequest request = new RemoveMaintenanceJobRequest(maintenanceJobId);
                    RemoveMaintenanceJobResponse response = await AskEntityAsync<RemoveMaintenanceJobResponse>(DatabaseScanCoordinatorActor.EntityId, request);
                    // \todo [nuutti] Figure out proper statusCode
                    if (!response.IsSuccess)
                        throw new MetaplayHttpException(400, "Failed to delete job.", response.Error);

                    await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerMaintenanceJobDeleted(maintenanceJobId, response.JobSpec)));

                    return Ok(new CancelDatabaseScanJobResponse(Invariant($"Deleted upcoming job '{response.JobSpec.JobTitle}'.")));
                }
                else
                    throw new MetaplayHttpException(400, "Cannot delete job.", $"Upcoming {upcomingJobId.ManagerKind} jobs cannot be deleted using this API.");
            }
            else
                throw new MetaAssertException($"Unknown job id type {jobApiId.GetType()}.");
        }

        [JsonObject(ItemRequired = Required.Always)]
        public class SetGlobalPauseBody
        {
            public bool IsPaused;
        }

        /// <summary>
        /// Set the global pause state for database scan jobs
        /// </summary>
        [HttpPost("databaseScanJobs/setGlobalPause")]
        [RequirePermission(MetaplayPermissions.ApiScanJobsCancel)]
        public async Task<ActionResult> SetGlobalPause()
        {
            SetGlobalPauseBody body = await ParseBodyAsync<SetGlobalPauseBody>();
            bool isPaused = body.IsPaused;

            SetGlobalDatabaseScanPauseRequest request = new SetGlobalDatabaseScanPauseRequest(isPaused: isPaused);
            _ = await AskEntityAsync<SetGlobalDatabaseScanPauseResponse>(DatabaseScanCoordinatorActor.EntityId, request);

            await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerEventDatabaseScanGlobalPauseSet(isPaused: isPaused)));

            return Ok();
        }
    }
}
