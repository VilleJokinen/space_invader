// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.DatabaseScan;
using Metaplay.Server.DatabaseScan.Priorities;
using Metaplay.Server.DatabaseScan.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Server.ScheduledPlayerDeletion
{
    /// <summary>
    /// Message to start a deletion sweep right now
    /// </summary>
    [MetaMessage(MessageCodesCore.DeletionSweepRequest, MessageDirection.ServerInternal)]
    public class DeletionSweepRequest : MetaMessage
    {
        public DeletionSweepRequest() { }
        public static readonly DeletionSweepRequest Instance = new DeletionSweepRequest();
    }

    /// <summary>
    /// Parameters of a scheduled player deletion job.
    /// </summary>
    [MetaSerializable]
    public class ScheduledPlayerDeletionJobSpecParams
    {
        // This is the time that the job was started at. Any players who have a scheduled deletion
        // time that is older than this will get deleted
        [MetaMember(1)] public MetaTime CutoffTime { get; private set; }

        public ScheduledPlayerDeletionJobSpecParams() { }
        public ScheduledPlayerDeletionJobSpecParams(MetaTime cutoffTime)
        {
            CutoffTime = cutoffTime;
        }
    }



    /// <summary>
    /// Scheduled Player Deletion manager
    /// </summary>
    [MetaSerializable]
    public class ScheduledPlayerDeletionManager : DatabaseScanJobManager
    {
        [MetaMember(1)] private MetaTime    _lastStartTime      { get; set; }           // Time that the manager last ran a job.
        [MetaMember(2)] private bool        _forceStart         { get; set; } = false;  // Set when the user wants to manually start a job.

        public override Task InitializeAsync(IContext context)
        {
            return Task.CompletedTask;
        }

        public override (DatabaseScanJobSpec jobSpec, bool canStart) TryGetNextDueJob(IContext context, MetaTime currentTime)
        {
            (bool isExplicitlyStarted, MetaTime startTime) = GetUpcomingJobInfo(currentTime);

            if (currentTime >= startTime)
                return (new ScheduledPlayerDeletionJobSpec(new ScheduledPlayerDeletionJobSpecParams(cutoffTime: currentTime)), true);
            else
                return (null, false);
        }

        public override Task OnJobDidNotStartAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            return Task.CompletedTask;
        }

        public override IEnumerable<UpcomingDatabaseScanJob> GetUpcomingJobs(MetaTime currentTime)
        {
            (bool isExplicitlyStarted, MetaTime startTime) = GetUpcomingJobInfo(currentTime);

            return new UpcomingDatabaseScanJob[]
            {
                new UpcomingDatabaseScanJob(
                    id: "single",
                    new ScheduledPlayerDeletionJobSpec(new ScheduledPlayerDeletionJobSpecParams(cutoffTime: startTime)),
                    earliestStartTime: isExplicitlyStarted ? null : startTime)
            };
        }

        (bool IsExplicitlyStarted, MetaTime StartTime) GetUpcomingJobInfo(MetaTime currentTime)
        {
            if (_forceStart)
                return (IsExplicitlyStarted: true, StartTime: currentTime);
            else
            {
                MetaTime startTime = GetNextPeriodicStartTime(currentTime);
                return (IsExplicitlyStarted: false, StartTime: startTime);
            }
        }

        MetaTime GetNextPeriodicStartTime(MetaTime currentTime)
        {
            SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();

            // \note Actual implementation is in public static method for testability.
            return GetNextPeriodicStartTime(
                currentTime: currentTime,
                lastStartTime: _lastStartTime,
                deletionSweepTimeOfDay: systemOpts.PlayerDeletionSweepTimeOfDay);
        }

        public static MetaTime GetNextPeriodicStartTime(MetaTime currentTime, MetaTime lastStartTime, TimeSpan deletionSweepTimeOfDay)
        {
            // Construct a recurring schedule consisting of the daily 1-hour windows starting at time-of-day deletionSweepTimeOfDay.
            MetaRecurringCalendarSchedule periodicWindowSchedule = new MetaRecurringCalendarSchedule(
                timeMode: MetaScheduleTimeMode.Utc,
                // Start: Date is arbitrary as long as it's in the past - only the time of day matters for us (because of the daily recurrence).
                start: new MetaCalendarDateTime(2000, 1, 1, deletionSweepTimeOfDay.Hours, deletionSweepTimeOfDay.Minutes, deletionSweepTimeOfDay.Seconds),
                // Window lasts 1 hour.
                duration: new MetaCalendarPeriod(0, 0, 0, hours: 1, 0, 0),
                // Ending soon, preview, and review do not apply here.
                endingSoon: new MetaCalendarPeriod(),
                preview: new MetaCalendarPeriod(),
                review: new MetaCalendarPeriod(),
                // Daily recurrence.
                recurrence: new MetaCalendarPeriod(0, 0, days: 1, 0, 0, 0),
                // Repeat indefinitely.
                numRepeats: null);

            // Lower bound for start time:
            // Start earliest at current time, except always leave at least 6 hours between runs,
            // ie: if the user recently started a job manually then don't try to run a scheduled job too soon afterwards.
            MetaTime lowerBound = Util.Max(currentTime, lastStartTime + MetaDuration.FromHours(6));

            // Query the relevant daily window - either the one lowerBound is in (if any),
            // or the next one.
            MetaScheduleOccasion window = periodicWindowSchedule
                                              .TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(time: lowerBound, utcOffset: MetaDuration.Zero))
                                              .Value;

            if (window.IsEnabledAt(lowerBound))
            {
                // lowerBound is within a daily window - start immediately at lowerBound.
                return lowerBound;
            }
            else
            {

                // lowerBound is not within a daily window, so `window` is the next window
                // after lowerBound. So use the window's start time.
                return window.EnabledRange.Start;
            }
        }

        public override Task OnJobStartedAsync(IContext context, DatabaseScanJobSpec jobSpec, DatabaseScanJobId jobId, MetaTime currentTime)
        {
            // Clear any pending user requests
            _forceStart = false;

            // Remember the time that the job was started
            _lastStartTime = currentTime;

            return Task.CompletedTask;
        }

        public override Task OnJobCancellationBeganAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            // No specific action required here
            return Task.CompletedTask;
        }

        public override Task OnJobStoppedAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime, bool wasCancelled, IEnumerable<DatabaseScanWorkStatus> workerStatusMaybes)
        {
            // No specific action required here
            return Task.CompletedTask;
        }

        public override bool TryGetMessageHandler(IContext context, MetaMessage message, out Task handleAsync)
        {
            switch (message)
            {
                case DeletionSweepRequest requestDeletionSweepMsg: HandleDeletionSweepRequest(context, requestDeletionSweepMsg); handleAsync = Task.CompletedTask; return true;

                default:
                    handleAsync = default;
                    return false;
            }
        }

        /// <summary>
        /// Handle a message that is requesting us to start a deletion sweep asap
        /// </summary>
        /// <param name="context"></param>
        /// <param name="_"></param>
        /// <returns></returns>
        public void HandleDeletionSweepRequest(IContext context, DeletionSweepRequest _)
        {
            _forceStart = true;
        }

        public override bool TryGetEntityAskHandler(IContext context, EntityAsk ask, MetaMessage message, out Task handleAsync)
        {
            // No asks to handle
            handleAsync = default;
            return false;
        }
    }



    /// <summary>
    /// Scheduled Player Deletion job specification
    /// </summary>
    [MetaSerializableDerived(2)]
    public class ScheduledPlayerDeletionJobSpec : MaintenanceJob.MaintenanceJobSpec
    {
        [MetaMember(1)] public ScheduledPlayerDeletionJobSpecParams DeletionParams { get; private set; }

        public override string       JobTitle    => "Scheduled Player Deletion";
        public override string       JobDescription => "Scans through the database, permanently deleting any players whose deletion time has passed. This job normally runs on a schedule but can also be run on demand.";
        public override string       MetricsTag  => "ScheduledPlayerDeletion";
        public override int          Priority    => DatabaseScanJobPriorities.ScheduledPlayerDeletion;
        public override EntityKind   EntityKind  => EntityKindCore.Player;

        public override object JobKindDiscriminator => EntityKind;

        public ScheduledPlayerDeletionJobSpec() { }
        public ScheduledPlayerDeletionJobSpec(ScheduledPlayerDeletionJobSpecParams deletionParams)
        {
            DeletionParams = deletionParams;
        }

        // \note Invoked when created via [EntityMaintenanceJob], should use static interfaces in the future
        public static ScheduledPlayerDeletionJobSpec CreateDefault()
        {
            return new ScheduledPlayerDeletionJobSpec(new ScheduledPlayerDeletionJobSpecParams(MetaTime.Now));
        }

        public override DatabaseScanProcessor CreateProcessor(DatabaseScanProcessingStatistics initialStatisticsBaseMaybe)
        {
            return new ScheduledPlayerDeletionProcessor(DeletionParams, (ScheduledPlayerDeletionProcessingStatistics)initialStatisticsBaseMaybe);
        }

        public override DatabaseScanProcessingStatistics ComputeAggregateStatistics(IEnumerable<DatabaseScanProcessingStatistics> parts)
        {
            return ScheduledPlayerDeletionProcessingStatistics.ComputeAggregate(parts.Cast<ScheduledPlayerDeletionProcessingStatistics>());
        }

        public override OrderedDictionary<string, object> CreateSummary(DatabaseScanProcessingStatistics statsParam)
        {
            ScheduledPlayerDeletionProcessingStatistics stats = (ScheduledPlayerDeletionProcessingStatistics)statsParam;

            return new OrderedDictionary<string, object>
            {
                { "Deletion cutoff time", DeletionParams.CutoffTime },
                { "Deletions succeeded", stats.DeleteSucceededPlayer.Count },
                { "Deletions failed", stats.DeleteFailedPlayers.Count },
                { "Deserializations failed", stats.DeserializationFailedPlayers.Count },
            };
        }
    }



    /// <summary>
    /// Custom statistics
    /// </summary>
    [MetaSerializableDerived(2)]
    public class ScheduledPlayerDeletionProcessingStatistics : DatabaseScanProcessingStatistics
    {
        [MetaMember(1)] public ListWithBoundedRecall<EntityId> DeserializationFailedPlayers { get; set; } = new ListWithBoundedRecall<EntityId>();
        [MetaMember(2)] public ListWithBoundedRecall<EntityId> DeleteSucceededPlayer { get; set; } = new ListWithBoundedRecall<EntityId>();
        [MetaMember(3)] public ListWithBoundedRecall<EntityId> DeleteFailedPlayers { get; set; } = new ListWithBoundedRecall<EntityId>();

        public static ScheduledPlayerDeletionProcessingStatistics ComputeAggregate(IEnumerable<ScheduledPlayerDeletionProcessingStatistics> parts)
        {
            ScheduledPlayerDeletionProcessingStatistics aggregate = new ScheduledPlayerDeletionProcessingStatistics();

            foreach (ScheduledPlayerDeletionProcessingStatistics part in parts)
            {
                aggregate.DeserializationFailedPlayers  .AddAllFrom(part.DeserializationFailedPlayers);
                aggregate.DeleteSucceededPlayer         .AddAllFrom(part.DeleteSucceededPlayer);
                aggregate.DeleteFailedPlayers           .AddAllFrom(part.DeleteFailedPlayers);
            }

            return aggregate;
        }
    }



    /// <summary>
    /// Scheduled Player Deletion processor. Note that all work is done inside the call
    /// to StartProcessItemBatchAsync, ie: we don't batch it up like the Notifications job
    /// does. This is because deleting player is pretty instant and doesn't need to handle
    /// retries, so it's not particularly async behaviour
    /// </summary>
    [MetaSerializableDerived(2)]
    public class ScheduledPlayerDeletionProcessor : DatabaseScanProcessor<PersistedPlayerBase>
    {
        public override int DesiredScanBatchSize => 100;
        public override TimeSpan ScanInterval => TimeSpan.FromSeconds(0.1);
        public override TimeSpan PersistInterval => TimeSpan.FromMilliseconds(1000);
        public override TimeSpan TickInterval => TimeSpan.FromSeconds(1.0 * 0.5f);

        public override bool CanCurrentlyProcessMoreItems => true;
        public override bool HasCompletedAllWorkSoFar => true;
        public override DatabaseScanProcessingStatistics Stats => _statistics;

        [MetaMember(1)] public ScheduledPlayerDeletionJobSpecParams _deletionParams;
        [MetaMember(2)] public ScheduledPlayerDeletionProcessingStatistics _statistics = new ScheduledPlayerDeletionProcessingStatistics();

        public ScheduledPlayerDeletionProcessor() { }
        public ScheduledPlayerDeletionProcessor(ScheduledPlayerDeletionJobSpecParams deletionParams, ScheduledPlayerDeletionProcessingStatistics initialStatisticsMaybe)
        {
            _deletionParams = deletionParams;
            _statistics = initialStatisticsMaybe ?? new ScheduledPlayerDeletionProcessingStatistics();
        }

        public override async Task StartProcessItemBatchAsync(IContext context, IEnumerable<PersistedPlayerBase> persistedPlayers)
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            IGameConfigDataResolver resolver = activeGameConfig.BaselineGameConfig.SharedConfig;
            int logicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;

            foreach (PersistedPlayerBase persistedPlayer in persistedPlayers)
            {
                // Retreive the player
                if (persistedPlayer.Payload == null)
                {
                    // Silently tolerate initial empty-payload entries.
                    continue;
                }

                EntityId entityId = EntityId.ParseFromString(persistedPlayer.EntityId);
                IPlayerModelBase playerModel;
                try
                {
                    PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKindCore.Player);
                    playerModel = entityConfig.DeserializeDatabasePayload<IPlayerModelBase>(persistedPlayer.Payload, resolver, logicVersion);
                }
                catch (Exception ex)
                {
                    context.Log.Warning($"Failed to deserialize {entityId}: {ex}");
                    _statistics.DeserializationFailedPlayers.Add(entityId);
                    continue;
                }

                if (playerModel.DeletionStatus.IsScheduled() && playerModel.ScheduledForDeletionAt <= _deletionParams.CutoffTime)
                {
                    context.Log.Info($"Player {entityId} is being deleted");

                    // Delete the player and let the game code do some clean up
                    PlayerCompleteScheduledDeletionResponse response = await context.ActorEntityAskAsync<PlayerCompleteScheduledDeletionResponse>(entityId, PlayerCompleteScheduledDeletionRequest.Instance);
                    if (response.Success)
                    {
                        context.Log.Info($"Player {entityId} has been deleted");
                        _statistics.DeleteSucceededPlayer.Add(entityId);
                    }
                    else
                    {
                        context.Log.Info($"Player {entityId} failed to be deleted");
                        _statistics.DeleteFailedPlayers.Add(entityId);
                    }
                }
            }
        }

        public override Task TickAsync(IContext context)
        {
            // We're not using ticks
            return Task.CompletedTask;
        }

        public override void Cancel(IContext context)
        {
            // No specfic action required here
        }
    }
}
