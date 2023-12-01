// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Server.DatabaseScan;
using Metaplay.Server.DatabaseScan.Priorities;
using Metaplay.Server.DatabaseScan.User;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;
using static System.FormattableString;

namespace Metaplay.Server.NotificationCampaign
{
    /// <summary>
    /// The state of a notification campaign.
    /// May be an upcoming, a currently running, or a finished campaign.
    /// </summary>
    [MetaSerializable]
    public class NotificationCampaignState
    {
        [MetaMember(1)] public int                              Id                  { get; private set; }
        [MetaMember(2)] public NotificationCampaignParams       Params              { get; set; }
        [MetaMember(3)] public NotificationCampaignStatus       Status              { get; set; }
        [MetaMember(5)] public DatabaseScanJobId?               DatabaseScanJobId   { get; set; } // \note Assigned when scan job is started, i.e. when Status.Phase becomes Running.
        [MetaMember(4)] public NotificationCampaignStatistics   FinalStats          { get; set; }

        public NotificationCampaignState(){ }
        public NotificationCampaignState(int id, NotificationCampaignParams campaignParams, NotificationCampaignStatus status, DatabaseScanJobId? databaseScanJobId, NotificationCampaignStatistics finalStats)
        {
            Id                  = id;
            Params              = campaignParams;
            Status              = status;
            DatabaseScanJobId   = databaseScanJobId;
            FinalStats          = finalStats;
        }
    }

    [MetaSerializable]
    public class NotificationCampaignStatus
    {
        [MetaMember(1)] public NotificationCampaignPhase    Phase       { get; private set; }
        [MetaMember(2)] public MetaTime?                    StartTime   { get; private set; }   // Time when campaign job actually started. Ideally this'll not be long after TargetTime, if other jobs are not running.
        [MetaMember(3)] public MetaTime?                    StopTime    { get; private set; }   // Time when campaign job stopped.

        public NotificationCampaignStatus(){ }
        public NotificationCampaignStatus(NotificationCampaignPhase phase, MetaTime? startTime, MetaTime? stopTime)
        {
            Phase = phase;
            StartTime = startTime;
            StopTime = stopTime;
        }
    }

    [MetaSerializable]
    public class NotificationCampaignStatistics
    {
        [MetaMember(1)] public DatabaseScanStatistics                   ScanStats           { get; private set; }
        [MetaMember(2)] public NotificationCampaignProcessingStatistics NotificationStats   { get; private set; }

        public NotificationCampaignStatistics(){ }
        public NotificationCampaignStatistics(DatabaseScanStatistics scanStats, NotificationCampaignProcessingStatistics notificationStats)
        {
            ScanStats           = scanStats;
            NotificationStats   = notificationStats;
        }
    }

    /// <summary>
    /// Notification campaign manager is a type of database scan job manager, owned
    /// by database scan coordinator. It does bookkeeping of scheduled notification
    /// campaigns and provides them as database scan jobs when the time comes.
    /// It handles the communication with Admin API (with database scan coordinator
    /// being the actor formally doing the messaging) related to campaign job requests.
    /// </summary>
    [MetaSerializable]
    public class NotificationCampaignManager : DatabaseScanJobManager
    {
        [MetaMember(1)] private int                                               _runningNotificationCampaignId { get; set; } = 1;
        [MetaMember(2)] private OrderedDictionary<int, NotificationCampaignState> _notificationCampaigns         { get; set; } = new OrderedDictionary<int, NotificationCampaignState>();

        public override async Task InitializeAsync(IContext context)
        {
            var pushOpts = RuntimeOptionsRegistry.Instance.GetCurrent<PushNotificationOptions>();

            if (!pushOpts.Enabled)
            {
                context.Log.Info("Push notifications are disabled, cancelling all running campaigns.");
                foreach ((int _, NotificationCampaignState campaign) in _notificationCampaigns)
                {
                    if (campaign.Status.Phase == NotificationCampaignPhase.Running && campaign.DatabaseScanJobId != null)
                    {
                        await context.TryBeginCancelActiveJobAsync(campaign.DatabaseScanJobId.Value);
                        context.Log.Info("Cancelled push notification campaign: \"{CampaignName}\".", campaign.Params.Name);
                    }
                }
            }
        }

        public void MigrateTargetSegments()
        {
            foreach (NotificationCampaignState campaignState in _notificationCampaigns.Values)
            {
                campaignState.Params.MigrateTargetSegments();
            }
        }

        public void MigrateLocalizations()
        {
            foreach (NotificationCampaignState campaignState in _notificationCampaigns.Values)
            {
                campaignState.Params.MigrateContentLocalization();
            }
        }

        public override (DatabaseScanJobSpec jobSpec, bool canStart) TryGetNextDueJob(IContext context, MetaTime currentTime)
        {
            NotificationCampaignState campaign = TryFindEarliestStartableCampaign(currentTime);
            if (campaign == null)
                return (null, false);

            var pushOpts = RuntimeOptionsRegistry.Instance.GetCurrent<PushNotificationOptions>();
            return (new NotificationCampaignJobSpec(campaign.Id, campaign.Params), pushOpts.Enabled);
        }

        public override Task OnJobDidNotStartAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            NotificationCampaignJobSpec campaignJobSpec = jobSpec as NotificationCampaignJobSpec ?? throw new ArgumentException("Expected a NotificationCampaignJobSpec", nameof(jobSpec));
            NotificationCampaignState   campaign        = _notificationCampaigns[campaignJobSpec.CampaignId];

            campaign.Status = new NotificationCampaignStatus(
                NotificationCampaignPhase.DidNotRun,
                startTime:  currentTime,
                stopTime:   currentTime);

            return Task.CompletedTask;
        }

        public override IEnumerable<UpcomingDatabaseScanJob> GetUpcomingJobs(MetaTime currentTime)
        {
            return _notificationCampaigns.Values
                .Where(campaign => campaign.Status.Phase == NotificationCampaignPhase.Scheduled)
                .Select(campaign =>
                {
                    return new UpcomingDatabaseScanJob(
                        id: campaign.Id.ToString(CultureInfo.InvariantCulture),
                        new NotificationCampaignJobSpec(campaign.Id, campaign.Params),
                        earliestStartTime: campaign.Params.TargetTime);
                });
        }

        public override Task OnJobStartedAsync(IContext context, DatabaseScanJobSpec jobSpec, DatabaseScanJobId jobId, MetaTime currentTime)
        {
            NotificationCampaignJobSpec campaignJobSpec = jobSpec as NotificationCampaignJobSpec ?? throw new ArgumentException("Expected a NotificationCampaignJobSpec", nameof(jobSpec));
            NotificationCampaignState   campaign        = _notificationCampaigns[campaignJobSpec.CampaignId];

            campaign.DatabaseScanJobId = jobId;
            campaign.Status = new NotificationCampaignStatus(
                NotificationCampaignPhase.Running,
                startTime:  currentTime,
                stopTime:   null);

            return Task.CompletedTask;
        }

        public override Task OnJobCancellationBeganAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            NotificationCampaignJobSpec campaignJobSpec = jobSpec as NotificationCampaignJobSpec ?? throw new ArgumentException("Expected a NotificationCampaignJobSpec", nameof(jobSpec));
            NotificationCampaignState   campaign        = _notificationCampaigns[campaignJobSpec.CampaignId];

            campaign.Status = new NotificationCampaignStatus(
                NotificationCampaignPhase.Cancelling,
                campaign.Status.StartTime,
                campaign.Status.StopTime);

            return Task.CompletedTask;
        }

        public override Task OnJobStoppedAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime, bool wasCancelled, IEnumerable<DatabaseScanWorkStatus> workerStatusMaybes)
        {
            NotificationCampaignJobSpec campaignJobSpec = jobSpec as NotificationCampaignJobSpec ?? throw new ArgumentException("Expected a NotificationCampaignJobSpec", nameof(jobSpec));
            NotificationCampaignState   campaign        = _notificationCampaigns[campaignJobSpec.CampaignId];

            campaign.Status = new NotificationCampaignStatus(
                phase:      wasCancelled ? NotificationCampaignPhase.Cancelled : NotificationCampaignPhase.Sent,
                startTime:  campaign.Status.StartTime,
                stopTime:   currentTime);

            campaign.FinalStats = TryComputeTotalStatistics(workerStatusMaybes);
            return Task.CompletedTask;
        }

        public override bool TryGetMessageHandler(IContext context, MetaMessage message, out Task handleAsync)
        {
            switch (message)
            {
                default:
                    handleAsync = default;
                    return false;
            }
        }

        public override bool TryGetEntityAskHandler(IContext context, EntityAsk ask, MetaMessage message, out Task handleAsync)
        {
            switch (message)
            {
                case ListNotificationCampaignsRequest listReq:          handleAsync = HandleAskAsync(context, ask, listReq);      return true;
                case AddNotificationCampaignRequest addReq:             handleAsync = HandleAskAsync(context, ask, addReq);       return true;
                case GetNotificationCampaignRequest getReq:             handleAsync = HandleAskAsync(context, ask, getReq);       return true;
                case UpdateNotificationCampaignRequest updateReq:       handleAsync = HandleAskAsync(context, ask, updateReq);    return true;
                case BeginCancelNotificationCampaignRequest cancelReq:  handleAsync = HandleAskAsync(context, ask, cancelReq);    return true;
                case DeleteNotificationCampaignRequest deleteReq:       handleAsync = HandleAskAsync(context, ask, deleteReq);    return true;

                default:
                    handleAsync = default;
                    return false;
            }
        }

        public Task HandleAskAsync(IContext context, EntityAsk ask, ListNotificationCampaignsRequest _)
        {
            context.ReplyToAsk(ask, new ListNotificationCampaignsResponse(
                _notificationCampaigns.Values
                    .Select(campaign => GetCampaignSummary(context, campaign))
                    .ToList()));

            return Task.CompletedTask;
        }

        public async Task HandleAskAsync(IContext context, EntityAsk ask, AddNotificationCampaignRequest addReq)
        {
            AddNotificationCampaignResponse reply;

            if (!addReq.CampaignParams.Validate(out string paramsValidationError))
                reply = AddNotificationCampaignResponse.Failure($"Invalid campaign params: {paramsValidationError}");
            else
            {
                int id = _runningNotificationCampaignId++;
                NotificationCampaignStatus status = new NotificationCampaignStatus(NotificationCampaignPhase.Scheduled, startTime: null, stopTime: null);
                _notificationCampaigns.Add(id, new NotificationCampaignState(id, addReq.CampaignParams, status, databaseScanJobId: null, finalStats: null));
                await context.PersistStateAsync();

                reply = AddNotificationCampaignResponse.Ok(id);
            }

            context.ReplyToAsk(ask, reply);
        }

        public Task HandleAskAsync(IContext context, EntityAsk ask, GetNotificationCampaignRequest getReq)
        {
            GetNotificationCampaignResponse reply;

            if (!_notificationCampaigns.TryGetValue(getReq.Id, out NotificationCampaignState campaign))
                reply = GetNotificationCampaignResponse.Failure("No such campaign");
            else
            {
                NotificationCampaignStatistics stats;
                if (CampaignJobIsActive(campaign))
                {
                    if (campaign.DatabaseScanJobId.HasValue) // \note DatabaseScanJobId should always have value after campaign has been started, but let's be defensive
                        stats = TryComputeTotalStatistics(context.TryGetActiveJobWorkerStatusMaybes(campaign.DatabaseScanJobId.Value));
                    else
                        stats = null;
                }
                else
                    stats = campaign.FinalStats;

                NotificationCampaignStatisticsInfo statisticsInfo;
                if (stats != null)
                {
                    statisticsInfo = new NotificationCampaignStatisticsInfo(
                        startTime:          campaign.Status.StartTime.Value,
                        stopTime:           campaign.Status.StopTime,
                        scanStats:          stats.ScanStats,
                        notificationStats:  stats.NotificationStats);
                }
                else
                    statisticsInfo = null;

                reply = GetNotificationCampaignResponse.Ok(new NotificationCampaignInfo(campaign.Id, campaign.Params, campaign.Status.Phase, statisticsInfo));
            }

            context.ReplyToAsk(ask, reply);

            return Task.CompletedTask;
        }

        public async Task HandleAskAsync(IContext context, EntityAsk ask, UpdateNotificationCampaignRequest updateReq)
        {
            UpdateNotificationCampaignResponse reply;

            if (!updateReq.CampaignParams.Validate(out string paramsValidationError))
                reply = UpdateNotificationCampaignResponse.Failure($"Invalid campaign params: {paramsValidationError}");
            else if (!_notificationCampaigns.TryGetValue(updateReq.Id, out NotificationCampaignState existingCampaign))
                reply = UpdateNotificationCampaignResponse.Failure("No such campaign");
            else if (existingCampaign.Status.Phase != NotificationCampaignPhase.Scheduled)
                reply = UpdateNotificationCampaignResponse.Failure($"Cannot edit a campaign in phase {existingCampaign.Status.Phase}");
            else
            {
                existingCampaign.Params = updateReq.CampaignParams;
                await context.PersistStateAsync();

                reply = UpdateNotificationCampaignResponse.Ok();
            }

            context.ReplyToAsk(ask, reply);
        }

        public async Task HandleAskAsync(IContext context, EntityAsk ask, BeginCancelNotificationCampaignRequest beginCancelReq)
        {
            BeginCancelNotificationCampaignResponse reply;

            if (!_notificationCampaigns.TryGetValue(beginCancelReq.Id, out NotificationCampaignState existingCampaign))
                reply = BeginCancelNotificationCampaignResponse.Failure("No such campaign");
            else if (existingCampaign.Status.Phase != NotificationCampaignPhase.Running)
                reply = BeginCancelNotificationCampaignResponse.Failure($"Cannot cancel a campaign in phase {existingCampaign.Status.Phase}");
            else if (!existingCampaign.DatabaseScanJobId.HasValue) // \note DatabaseScanJobId should always have value after campaign has been started, but let's be defensive
                reply = BeginCancelNotificationCampaignResponse.Failure($"Campaign is Running, but is missing the DatabaseScanJobId!");
            else
            {
                await context.TryBeginCancelActiveJobAsync(existingCampaign.DatabaseScanJobId.Value);
                reply = BeginCancelNotificationCampaignResponse.Ok();
            }

            context.ReplyToAsk(ask, reply);
        }

        public async Task HandleAskAsync(IContext context, EntityAsk ask, DeleteNotificationCampaignRequest deleteReq)
        {
            DeleteNotificationCampaignResponse reply;

            if (!_notificationCampaigns.TryGetValue(deleteReq.Id, out NotificationCampaignState existingCampaign))
                reply = DeleteNotificationCampaignResponse.Failure("No such campaign");
            else if (existingCampaign.Status.Phase != NotificationCampaignPhase.Scheduled
                  && existingCampaign.Status.Phase != NotificationCampaignPhase.Sent
                  && existingCampaign.Status.Phase != NotificationCampaignPhase.Cancelled
                  && existingCampaign.Status.Phase != NotificationCampaignPhase.DidNotRun)
            {
                reply = DeleteNotificationCampaignResponse.Failure($"Cannot delete a campaign in phase {existingCampaign.Status.Phase}");
            }
            else
            {
                _notificationCampaigns.Remove(deleteReq.Id);
                await context.PersistStateAsync();

                reply = DeleteNotificationCampaignResponse.Ok();
            }

            context.ReplyToAsk(ask, reply);
        }

        NotificationCampaignState TryFindEarliestStartableCampaign(MetaTime currentTime)
        {
            NotificationCampaignState earliestSoFar = null;

            foreach ((int _, NotificationCampaignState campaign) in _notificationCampaigns)
            {
                if (campaign.Status.Phase == NotificationCampaignPhase.Scheduled && currentTime >= campaign.Params.TargetTime)
                {
                    if (earliestSoFar == null || campaign.Params.TargetTime < earliestSoFar.Params.TargetTime)
                        earliestSoFar = campaign;
                }
            }

            return earliestSoFar;
        }

        NotificationCampaignSummary GetCampaignSummary(IContext context, NotificationCampaignState campaign)
        {
            float scannedRatioEstimate;
            if (CampaignJobIsActive(campaign))
            {
                if (campaign.DatabaseScanJobId.HasValue) // \note DatabaseScanJobId should always have value after campaign has been started, but let's be defensive
                    scannedRatioEstimate = TryComputeScannedRatioEstimate(context.TryGetActiveJobWorkerStatusMaybes(campaign.DatabaseScanJobId.Value)).GetValueOrDefault(0f);
                else
                    scannedRatioEstimate = 0f;
            }
            else
                scannedRatioEstimate = (campaign.FinalStats?.ScanStats.ScannedRatioEstimate).GetValueOrDefault(0f);

            return new NotificationCampaignSummary(campaign.Id, campaign.Params, campaign.Status.Phase, scannedRatioEstimate);
        }

        bool CampaignJobIsActive(NotificationCampaignState campaign)
        {
            return campaign.Status.Phase == NotificationCampaignPhase.Running
                || campaign.Status.Phase == NotificationCampaignPhase.Cancelling;
        }

        static NotificationCampaignStatistics TryComputeTotalStatistics(IEnumerable<DatabaseScanWorkStatus> maybeWorkerStatusMaybes)
        {
            if (maybeWorkerStatusMaybes != null)
            {
                DatabaseScanStatistics                      totalScanStats          = ComputeTotalScanStatistics(maybeWorkerStatusMaybes);
                NotificationCampaignProcessingStatistics    totalNotificationStats  = ComputeTotalNotificationStatistics(maybeWorkerStatusMaybes);

                return new NotificationCampaignStatistics(totalScanStats, totalNotificationStats);
            }
            else
                return null;
        }

        static float? TryComputeScannedRatioEstimate(IEnumerable<DatabaseScanWorkStatus> maybeWorkerStatusMaybes)
        {
            if (maybeWorkerStatusMaybes != null)
                return ComputeTotalScanStatistics(maybeWorkerStatusMaybes).ScannedRatioEstimate;
            else
                return null;
        }

        static DatabaseScanStatistics ComputeTotalScanStatistics(IEnumerable<DatabaseScanWorkStatus> workerStatusMaybes)
        {
            IEnumerable<DatabaseScanStatistics> parts = workerStatusMaybes.Select(s => s?.ScanStatistics ?? new DatabaseScanStatistics());
            return DatabaseScanStatistics.ComputeAggregate(parts);
        }

        static NotificationCampaignProcessingStatistics ComputeTotalNotificationStatistics(IEnumerable<DatabaseScanWorkStatus> workerStatusMaybes)
        {
            IEnumerable<NotificationCampaignProcessingStatistics> parts = workerStatusMaybes.Select(s => (NotificationCampaignProcessingStatistics)s?.ProcessingStatistics ?? new NotificationCampaignProcessingStatistics());
            return NotificationCampaignProcessingStatistics.ComputeAggregate(parts);
        }

        #region DatabaseScanCoordinator schema migrations

        public void MigrationSetActiveJobId(DatabaseScanJobSpec jobSpec, DatabaseScanJobId jobId)
        {
            NotificationCampaignJobSpec campaignJobSpec = jobSpec as NotificationCampaignJobSpec ?? throw new ArgumentException("Expected a NotificationCampaignJobSpec", nameof(jobSpec));
            NotificationCampaignState   campaign        = _notificationCampaigns[campaignJobSpec.CampaignId];

            campaign.DatabaseScanJobId = jobId;
        }

        #endregion
    }

    [MetaSerializableDerived(1)]
    public class NotificationCampaignJobSpec : DatabaseScanJobSpec
    {
        [MetaMember(1)] public int                          CampaignId  { get; private set; }
        [MetaMember(2)] public NotificationCampaignParams   Params      { get; private set; }

        public override string       JobTitle                => Invariant($"Notification Campaign #{CampaignId}");
        public override string       JobDescription          => $"Push notification campaign '{Params.Name}'.";
        public override string       MetricsTag              => "NotificationCampaign";
        public override int          Priority                => DatabaseScanJobPriorities.NotificationCampaign;
        public override EntityKind   EntityKind              => EntityKindCore.Player;
        public override ulong        EntityIdValueUpperBound => Params.DebugEntityIdValueUpperBound > 0 ? Params.DebugEntityIdValueUpperBound : base.EntityIdValueUpperBound;
        public override List<EntityId> ExplicitEntityList    => GetTargetPlayerListForScan();

        public NotificationCampaignJobSpec(){ }
        public NotificationCampaignJobSpec(int campaignId, NotificationCampaignParams campaignParams)
        {
            CampaignId  = campaignId;
            Params      = campaignParams;
        }

        public override DatabaseScanProcessor CreateProcessor(DatabaseScanProcessingStatistics initialStatisticsBaseMaybe)
        {
            return new NotificationCampaignProcessor(Params, (NotificationCampaignProcessingStatistics)initialStatisticsBaseMaybe);
        }

        public override DatabaseScanProcessingStatistics ComputeAggregateStatistics(IEnumerable<DatabaseScanProcessingStatistics> parts)
        {
            return NotificationCampaignProcessingStatistics.ComputeAggregate(parts.Cast<NotificationCampaignProcessingStatistics>());
        }

        public override OrderedDictionary<string, object> CreateSummary(DatabaseScanProcessingStatistics statsParam)
        {
            NotificationCampaignProcessingStatistics stats = (NotificationCampaignProcessingStatistics)statsParam;

            return new OrderedDictionary<string, object>
            {
                { "Campaign name", Params.Name },
                { "Players notified", stats.NumPlayersNotified },
                { "Player notifications failed", stats.NotificationFailedPlayers.Count },
                { "Deserializations failed", stats.RestoreFailedPlayers.Count },
            };
        }

        /// <summary>
        /// Return list of targeted players if known (no other targeting conditions have been specified).
        /// </summary>
        List<EntityId> GetTargetPlayerListForScan()
        {
            if (Params.TargetPlayers != null && Params.TargetPlayers.Count > 0 && Params.TargetCondition == null)
                return Params.TargetPlayers;
            return null;
        }
    }

    [MetaSerializableDerived(1)]
    public class NotificationCampaignProcessingStatistics : DatabaseScanProcessingStatistics
    {
        [MetaMember(1)]  public ListWithBoundedRecall<string> RestoreFailedPlayers                  { get; set; } = new ListWithBoundedRecall<string>();
        [MetaMember(2)]  public int                         NumPlayersForcedToDefaultLanguage       { get; set; } = 0;
        [MetaMember(3)]  public int                         NumPlayersWithoutTokens                 { get; set; } = 0;
        [MetaMember(13)] public int                         NumPlayersWithoutDevices                { get; set; } = 0;
        [MetaMember(14)] public int                         NumPlayersFilteredOut                   { get; set; } = 0;
        [MetaMember(15)] public int                         NumPlayersSegmentEvaluationFailed       { get; set; } = 0;
        [MetaMember(16)] public ListWithBoundedRecall<EntityId> PlayersWithTokenListTruncated       { get; set; } = new ListWithBoundedRecall<EntityId>();
        [MetaMember(4)]  public int                         NumPlayersAttempted                     { get; set; } = 0; // Players for which we've attempted to send at least 1 notification
        [MetaMember(5)]  public int                         NumPlayersNotified                      { get; set; } = 0; // Players for which at least 1 notification succeeded
        [MetaMember(6)]  public ListWithBoundedRecall<EntityId> NotificationFailedPlayers           { get; set; } = new ListWithBoundedRecall<EntityId>(); // Players that had at least 1 notification token but 0 notifications succeeded
        [MetaMember(7)]  public int                         NumTokensAttempted                      { get; set; } = 0; // Tokens to which we've attempted to send a notification
        [MetaMember(8)]  public int                         NumTokensNotified                       { get; set; } = 0; // Tokens to which a notification succeeded
        [MetaMember(9)]  public int                         NumNotificationsAttempted               { get; set; } = 0; // Notifications we've attempted to send (multiple notifications can be attempted per token)
        [MetaMember(10)] public int                         NumBatchesAttempted                     { get; set; } = 0; // Batches we've attempted to send at least once
        [MetaMember(11)] public int                         NumBatchesSent                          { get; set; } = 0; // Batches we've successfully sent (individual notifications in a batch can still fail)
        [MetaMember(12)] public int                         NumBatchSendAttempts                    { get; set; } = 0; // Total batch send attempts (multiple send attempts can be made for a batch)

        [MetaMember(100)] public int                        NumBadFirebaseResponses                 { get; set; } = 0;
        [MetaMember(101)] public Dictionary<string, int>    FirebaseSendErrors                      { get; set; } = new Dictionary<string, int>();
        [MetaMember(108)] public ListWithBoundedRecall<string> FirebaseSendExceptionMessages        { get; set; } = new ListWithBoundedRecall<string>();
        [MetaMember(102)] public long                       FirebaseSendSuccessDurationTotalMS      { get; set; } = 0;
        [MetaMember(103)] public int                        FirebaseSendSuccessDurationNumSamples   { get; set; } = 0;
        [MetaMember(104)] public long                       FirebaseSendFailureDurationTotalMS      { get; set; } = 0;
        [MetaMember(105)] public int                        FirebaseSendFailureDurationNumSamples   { get; set; } = 0;
        [MetaMember(106)] public long                       TotalRetryWaitMS                        { get; set; } = 0;
        [MetaMember(107)] public ListWithBoundedRecall<string> FirebaseBatchErrors                  { get; set; } = new ListWithBoundedRecall<string>();
        [MetaMember(109)] public int                        TokenRemovalsStarted                    { get; set; } = 0;
        [MetaMember(110)] public int                        TokenRemovalsOmitted                    { get; set; } = 0;
        [MetaMember(111)] public int                        TokenRemovalsSucceeded                  { get; set; } = 0;

        public static NotificationCampaignProcessingStatistics ComputeAggregate(IEnumerable<NotificationCampaignProcessingStatistics> parts)
        {
            NotificationCampaignProcessingStatistics aggregate = new NotificationCampaignProcessingStatistics();

            foreach (NotificationCampaignProcessingStatistics part in parts)
            {
                aggregate.RestoreFailedPlayers                  .AddAllFrom(part.RestoreFailedPlayers);
                aggregate.NumPlayersForcedToDefaultLanguage     += part.NumPlayersForcedToDefaultLanguage;
                aggregate.NumPlayersWithoutTokens               += part.NumPlayersWithoutTokens;
                aggregate.NumPlayersWithoutDevices              += part.NumPlayersWithoutDevices;
                aggregate.NumPlayersFilteredOut                 += part.NumPlayersFilteredOut;
                aggregate.NumPlayersSegmentEvaluationFailed     += part.NumPlayersSegmentEvaluationFailed;
                aggregate.PlayersWithTokenListTruncated         .AddAllFrom(part.PlayersWithTokenListTruncated);
                aggregate.NumPlayersAttempted                   += part.NumPlayersAttempted;
                aggregate.NumPlayersNotified                    += part.NumPlayersNotified;
                aggregate.NotificationFailedPlayers             .AddAllFrom(part.NotificationFailedPlayers);
                aggregate.NumTokensAttempted                    += part.NumTokensAttempted;
                aggregate.NumTokensNotified                     += part.NumTokensNotified;
                aggregate.NumNotificationsAttempted             += part.NumNotificationsAttempted;
                aggregate.NumBatchesAttempted                   += part.NumBatchesAttempted;
                aggregate.NumBatchesSent                        += part.NumBatchesSent;
                aggregate.NumBatchSendAttempts                  += part.NumBatchSendAttempts;

                aggregate.NumBadFirebaseResponses               += part.NumBadFirebaseResponses;

                foreach ((string error, int otherCount) in part.FirebaseSendErrors)
                    aggregate.FirebaseSendErrors[error] = aggregate.FirebaseSendErrors.GetValueOrDefault(error, 0) + otherCount;

                aggregate.FirebaseSendExceptionMessages         .AddAllFrom(part.FirebaseSendExceptionMessages);
                aggregate.FirebaseSendSuccessDurationTotalMS    += part.FirebaseSendSuccessDurationTotalMS;
                aggregate.FirebaseSendSuccessDurationNumSamples += part.FirebaseSendSuccessDurationNumSamples;
                aggregate.FirebaseSendFailureDurationTotalMS    += part.FirebaseSendFailureDurationTotalMS;
                aggregate.FirebaseSendFailureDurationNumSamples += part.FirebaseSendFailureDurationNumSamples;
                aggregate.TotalRetryWaitMS                      += part.TotalRetryWaitMS;
                aggregate.FirebaseBatchErrors                   .AddAllFrom(part.FirebaseBatchErrors);
                aggregate.TokenRemovalsStarted                  += part.TokenRemovalsStarted;
                aggregate.TokenRemovalsOmitted                  += part.TokenRemovalsOmitted;
                aggregate.TokenRemovalsSucceeded                += part.TokenRemovalsSucceeded;
            }

            return aggregate;
        }
    }

    [RuntimeOptions("NotificationCampaign", isStatic: true, "Configuration options for the push notification campaigns.")]
    public class NotificationCampaignOptions : RuntimeOptionsBase
    {
        // \note When tweaking these parameters, please take into account that these are per-worker.
        //       There may be multiple workers per job; please see DatabaseScanCoordinatorActor.DesiredNumWorkersPerJob.

        [MetaDescription("The number of push notifications sent in a single Firebase request.")]
        public int      MaxTokensPerSendBatch           { get; private set; } = 100;
        /// <summary>
        /// Increasing this may speed up the execution of the campaign, but a too high
        /// value may result in throttling by Firebase.
        /// </summary>
        [MetaDescription("The maximum number of Firebase requests that can be in flight at the same time.")]
        public int      MaxSimultaneousBatchesInFlight  { get; private set; } = 3;
        /// <summary>
        /// This is used in estimating the throughput of notifications in order to use a matching database scan rate.
        /// </summary>
        [MetaDescription("The expected time for the Firebase request response to arrive after the request has been sent. Used to calculate `TokenSendThroughput`.")]
        public TimeSpan ExpectedBatchSendDuration       { get; private set; } = TimeSpan.FromSeconds(0.1);
        /// <summary>
        /// This is used in estimating an appropriate database scan rate.
        /// </summary>
        [MetaDescription("The expected number of push notification tokens per player. Used to calculate `PlayersPerScanBatch`.")]
        public float    ExpectedAverageTokensPerPlayer  { get; private set; } = 1f;
        [MetaDescription("A multiplier for the number of players to be scanned per batch. Increase this value to scan players in bigger batches.")]
        public float    ScanBatchSizeFactor             { get; private set; } = 1f;

        /// <summary>
        /// Also max number of simultaneous in-flight requests.
        /// </summary>
        [MetaDescription("The maximum number of push notification tokens removed from `PlayerActor`s per second.")]
        public int      PlayerTokenRemovalRateLimit     { get; private set; } = 3;

        #region Helpers

        // Naive maths based on the desired goal that TokenScanThroughput = TokenSendThroughput, where TokenScanThroughput = ExpectedTokensPerScanBatch / ScanInterval .
        // Does not account for retrying failed things.
        // Also does not account for other artifacts in worker/processor behavior.
        [MetaDescription("The estimated number of push notification tokens per batch of scanned players.")]
        public int      ExpectedTokensPerScanBatch      => (int)(MaxTokensPerSendBatch * ScanBatchSizeFactor);
        [MetaDescription("The estimated number of push notifications sent per second.")]
        public float    TokenSendThroughput             => (MaxTokensPerSendBatch * MaxSimultaneousBatchesInFlight) / (float)ExpectedBatchSendDuration.TotalSeconds;
        [MetaDescription("The number of players per scanned batch.")]
        public int      PlayersPerScanBatch             => (int)(ExpectedTokensPerScanBatch / ExpectedAverageTokensPerPlayer);
        [MetaDescription("The period of time between database queries when scanning for players.")]
        public TimeSpan ScanInterval                    => TimeSpan.FromSeconds(ExpectedTokensPerScanBatch / TokenSendThroughput);

        #endregion

        public override Task OnLoadedAsync()
        {
            if (MaxTokensPerSendBatch < 1)
                throw new InvalidOperationException($"{nameof(MaxTokensPerSendBatch)} must be at least 1; is {MaxTokensPerSendBatch}");
            if (MaxTokensPerSendBatch > 100)
                throw new InvalidOperationException($"{nameof(MaxTokensPerSendBatch)} is {MaxTokensPerSendBatch}, which is higher than which is Firebase's limit (100).");

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A database scan processor that sends notifications to scanned players
    /// according to a notification campaign's parameters.
    /// Takes in batches of players from the database scan worker (in <see cref="StartProcessItemBatchAsync"/>),
    /// collecting info from the players for sending the notifications, and combines
    /// players' notifications into batches for sending with Firebase.
    ///
    /// <para>
    /// Caveat:
    /// The processor keeps pending notification batches as persistent state,
    /// which gets persisted every now and then (<see cref="PersistInterval"/>).
    /// A more economical way would be to keep those batches as transient state,
    /// and persist a database iterator specifying a point up to which all
    /// players have been handled. This would keep the persisted state quite a
    /// bit smaller. It would require more intricate logic and some communication
    /// to the worker actor about said iterator. It might also cause more data
    /// loss in the case of a crash, resulting in more duplicate notifications sent,
    /// but perhaps not catastrophically so.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1)]
    public class NotificationCampaignProcessor : DatabaseScanProcessor<PersistedPlayerBase>
    {
        static NotificationCampaignOptions GetOptions() => RuntimeOptionsRegistry.Instance.GetCurrent<NotificationCampaignOptions>();

        /// <summary>
        /// Represents a batch of notifications to be sent to a single player.
        /// Each entry in <see cref="DeviceTokens"/> stands for a single notification.
        ///
        /// <para>
        /// All of a player's notifications are grouped together like this in order to
        /// make it easier to collect some player-related statistics.
        /// </para>
        /// </summary>
        [MetaSerializable]
        public class PlayerNotificationBatch
        {
            [MetaMember(1)] public EntityId     PlayerId                            { get; private set; }
            [MetaMember(2)] public LanguageId   LanguageId                          { get; private set; }
            [MetaMember(3)] public List<string> DeviceTokens                        { get; private set; }
            [MetaMember(4)] public int          AttemptIndex                        { get; private set; }
            [MetaMember(5)] public bool         AnyTokensHaveSucceededForThisPlayer { get; private set; }

            public PlayerNotificationBatch(){ }
            public PlayerNotificationBatch(EntityId playerId, LanguageId languageId, List<string> deviceTokens, int attemptIndex, bool anyTokensHaveSucceededForThisPlayer)
            {
                PlayerId                            = playerId;
                LanguageId                          = languageId;
                DeviceTokens                        = deviceTokens;
                AttemptIndex                        = attemptIndex;
                AnyTokensHaveSucceededForThisPlayer = anyTokensHaveSucceededForThisPlayer;
            }
        }

        /// <summary>
        /// Represents a batch of notifications sent to a group of players.
        /// Contains a list of <see cref="PlayerNotificationBatch"/>es.
        /// As an implementation detail, a given player's notifications are
        /// always sent together in a <see cref="NotificationBatch"/>.
        /// </summary>
        [MetaSerializable]
        public class NotificationBatch
        {
            [MetaMember(1)] public int                              Id              { get; private set; }
            [MetaMember(2)] public List<PlayerNotificationBatch>    Players         { get; set; }
            [MetaMember(3)] public int                              AttemptIndex    { get; set; }

            /// <summary> Runtime only. Batches may be re-sent after unpersisting. </summary>
            [IgnoreDataMember] public bool IsInFlight = false;

            public NotificationBatch(){ }
            public NotificationBatch(int id)
            {
                Id              = id;
                Players         = new List<PlayerNotificationBatch>();
                AttemptIndex    = 0;
            }
        }

        /// <summary>
        /// Transient state tracking an EntityAsk sent to a player entity, requesting it to remove a messaging token.
        /// </summary>
        class PlayerTokenRemoval
        {
            public Task<PlayerRemovePushNotificationTokenResponse> Request;

            public PlayerTokenRemoval(Task<PlayerRemovePushNotificationTokenResponse> request)
            {
                Request = request ?? throw new ArgumentNullException(nameof(request));
            }
        }

        public override int         DesiredScanBatchSize            => GetOptions().PlayersPerScanBatch;
        public override TimeSpan    ScanInterval                    => GetOptions().ScanInterval;
        public override TimeSpan    PersistInterval                 => TimeSpan.FromMilliseconds(1000);
        public override TimeSpan    TickInterval                    => GetOptions().ScanInterval * 0.5;

        /// <summary>
        /// Only take in more work in order to fill player notification batch buffer enough to have
        /// a reasonable amount of tokens to create new notification batches to send.
        ///
        /// \note This can underestimate the amount of tokens in the buffered player notification batches;
        ///       a player can have multiple tokens. This is not very harmful, it might just cause the
        ///       buffer to be a bit bigger than needed.
        /// </summary>
        public override bool                                CanCurrentlyProcessMoreItems    => TotalNumBufferedPlayerNotificationBatches < CapacityForAdditionalSendBatches * GetOptions().MaxTokensPerSendBatch;
        public override bool                                HasCompletedAllWorkSoFar        => _currentlyAttemptedNotificationBatches.Count == 0 && TotalNumBufferedPlayerNotificationBatches == 0;
        public override DatabaseScanProcessingStatistics    Stats                           => _statistics;

        int TotalNumBufferedPlayerNotificationBatches   => _retryPlayerNotificationBatches.Count + _scannedPlayerNotificationBatches.Count;
        int CapacityForAdditionalSendBatches            => GetOptions().MaxSimultaneousBatchesInFlight - _currentlyAttemptedNotificationBatches.Count;

        [MetaMember(1)] NotificationCampaignParams                  _campaignParams;
        [MetaMember(2)] NotificationCampaignProcessingStatistics    _statistics                 = new NotificationCampaignProcessingStatistics();
        [MetaMember(3)] int                                         _runningNotificationBatchId = 1;
        /// <summary>
        /// Notification batches that are currently being sent.
        /// A batch here can be temporarily not-in-flight if it has failed but we've not yet re-sent it.
        /// This contains at most <see cref="MaxSimultaneousBatchesInFlight"/> batches.
        /// </summary>
        [MetaMember(4)] List<NotificationBatch>         _currentlyAttemptedNotificationBatches  = new List<NotificationBatch>();
        /// <summary>
        /// The player notification batches, created from scanned players, that are used to
        /// create new notification batches into <see cref="_currentlyAttemptedNotificationBatches"/>.
        /// </summary>
        [MetaMember(5)] List<PlayerNotificationBatch>   _scannedPlayerNotificationBatches   = new List<PlayerNotificationBatch>();
        /// <summary>
        /// Like <see cref="_scannedPlayerNotificationBatches"/>, but is created from failed
        /// player notification batches to retry, rather than directly from scan.
        /// This has priority over <see cref="_scannedPlayerNotificationBatches"/> in the sense
        /// that these, if any, are consumed before the buffered ones.
        /// </summary>
        [MetaMember(6)] List<PlayerNotificationBatch>   _retryPlayerNotificationBatches         = new List<PlayerNotificationBatch>();
        [MetaMember(7)] bool                            _workHasBeenCancelled                   = false;

        // Transient
        [IgnoreDataMember] DateTime                 _earliestNextBatchSendAt    = DateTime.MinValue;
        [IgnoreDataMember] int                      _retryBackoffCounter        = 0;
        [IgnoreDataMember] List<PlayerTokenRemoval> _ongoingPlayerTokenRemovals = new List<PlayerTokenRemoval>();

        static readonly TimeSpan    RetryBackoffFirstDelayBase  = TimeSpan.FromMilliseconds(100);
        static readonly TimeSpan    RetryBackoffMaxDelayBase    = TimeSpan.FromSeconds(4);
        const float                 RetryBackoffMaxJitterFactor = 0.2f;

        /// <summary>
        /// How many times a specific player's failed tokens can be retried. (0 means just do the one initial attempt.)
        /// </summary>
        const int                   MaxSendRetriesPerPlayer     = 5;
        /// <summary>
        /// How many times a failed batch can be retried. (0 means just do the one initial attempt.)
        /// A batch partially failing does not count as a batch send failure. As long as we get a BatchResponse, the batch didn't fail in this sense.
        /// </summary>
        const int                   MaxBatchSendRetries         = 5;

        public NotificationCampaignProcessor(){ }
        public NotificationCampaignProcessor(NotificationCampaignParams campaignParams, NotificationCampaignProcessingStatistics initialStatisticsMaybe)
        {
            _campaignParams = campaignParams;
            _statistics     = initialStatisticsMaybe ?? new NotificationCampaignProcessingStatistics();
        }

        /// <summary>
        /// Deserialize the given players, gathering the necessary information for notifications,
        /// and put them in the <see cref="_scannedPlayerNotificationBatches"/> buffer.
        /// </summary>
        public override Task StartProcessItemBatchAsync(IContext context, IEnumerable<PersistedPlayerBase> persistedPlayers)
        {
            NotificationCampaignOptions opts = GetOptions();

            ActiveGameConfig        activeGameConfig    = GlobalStateProxyActor.ActiveGameConfig.Get();
            ISharedGameConfig       sharedGameConfig    = activeGameConfig.BaselineGameConfig.SharedConfig;
            IGameConfigDataResolver resolver            = sharedGameConfig;
            int                     logicVersion        = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;

            foreach (PersistedPlayerBase persistedPlayer in persistedPlayers)
            {
                // Silently tolerate initial empty-payload entries.
                if (persistedPlayer.Payload == null)
                    continue;

                IPlayerModelBase playerModel;
                try
                {
                    PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKindCore.Player);
                    playerModel = entityConfig.DeserializeDatabasePayload<IPlayerModelBase>(persistedPlayer.Payload, resolver, logicVersion);
                    playerModel.GameConfig = sharedGameConfig;
                    playerModel.LogicVersion = logicVersion;

                    // Run OnStateRestored the same way as when loading the model in the PlayerActor.
                    // This way an appropriate PlayerModel.CurrentTime will be available in the segment condition evaluation.
                    MetaTime now = MetaTime.Now;
                    MetaDuration elapsedTime = now - MetaTime.FromDateTime(persistedPlayer.PersistedAt);
                    playerModel.OnRestoredFromPersistedState(now, elapsedTime);
                }
                catch (Exception)
                {
                    _statistics.RestoreFailedPlayers.Add(persistedPlayer.EntityId);
                    continue;
                }

                if (!playerModel.PassesFilter(_campaignParams.PlayerFilter, out bool evalError))
                {
                    if (evalError)
                        _statistics.NumPlayersSegmentEvaluationFailed++;
                    else
                        _statistics.NumPlayersFilteredOut++;
                    continue;
                }

                EntityId            playerId            = EntityId.ParseFromString(persistedPlayer.EntityId);
                IEnumerable<string> playerDeviceTokens  = playerModel.GetAllFirebaseMessagingTokens();

                // Fake tokens in debug mode, to get some nontrivial statistics
                if (_campaignParams.DebugFakeNotificationMode && !playerDeviceTokens.Any())
                {
                    List<string> fakeTokens = new List<string>();
                    int n = playerId.GetHashCode() % 4;
                    for(int i = 0; i < n; i++)
                        fakeTokens.Add(Invariant($"fakeToken/{playerId}/{i}/{SecureTokenUtil.GenerateRandomStringTokenUnsafe(new Random(playerId.GetHashCode()), 150)}"));
                    playerDeviceTokens = fakeTokens;
                }

                LanguageId playerNotificationLanguageId;
                if (playerModel.Language == null)
                    playerNotificationLanguageId = MetaplayCore.Options.DefaultLanguage;
                else if (_campaignParams.Content.ContainsLocalizationForLanguage(playerModel.Language))
                    playerNotificationLanguageId = playerModel.Language;
                else
                {
                    _statistics.NumPlayersForcedToDefaultLanguage++;
                    playerNotificationLanguageId = MetaplayCore.Options.DefaultLanguage;
                }

                if (!playerDeviceTokens.Any())
                {
                    _statistics.NumPlayersWithoutTokens++;
                    continue;
                }

                // If player has more tokens than can fit in a single send batch, truncate the token list.
                // #notification-campaign-oversized-batch
                // \todo [nuutti] Nicer fix. Split into multiple send batches? Cap number of tokens already in player state?
                if (playerDeviceTokens.Count() > opts.MaxTokensPerSendBatch)
                {
                    _statistics.PlayersWithTokenListTruncated.Add(playerId);
                    playerDeviceTokens = playerDeviceTokens.Take(opts.MaxTokensPerSendBatch);
                }

                // Skip players who don't have any devices.
                // This is done to avoid sending duplicate notifications to players who switched to another account via social auth.
                // PlayerModel.FirebaseMessagingTokensLegacy doesn't keep track of which device each token corresponds to, so we cannot properly remove
                // tokens when detaching devices. This is a "best effort" workaround for that.
                //
                // Note that the new PlayerModel.PushNotifications does keep track of the device mapping,
                // and entries from there are removed when a device is detached.
                // PlayerModel.FirebaseMessagingTokensLegacy should eventually be removed.
                // #legacy #notification
                int numDevices = playerModel.AttachedAuthMethods.Keys.Count(authKey => authKey.Platform == AuthenticationPlatform.DeviceId);
                if (numDevices == 0)
                {
                    _statistics.NumPlayersWithoutDevices++;
                    continue;
                }

                _scannedPlayerNotificationBatches.Add(new PlayerNotificationBatch(
                    playerId,
                    playerNotificationLanguageId,
                    playerDeviceTokens.ToList(),
                    attemptIndex: 0,
                    anyTokensHaveSucceededForThisPlayer: false));
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Send notification batches if possible.
        /// </summary>
        public override Task TickAsync(IContext context)
        {
            CleanupCompletedPlayerTokenRemovals();

            // Start attempting a new notification batch, if we've got room for it and if we've got buffered player batches for the new batch.
            if (_currentlyAttemptedNotificationBatches.Count < GetOptions().MaxSimultaneousBatchesInFlight)
            {
                if (TryConsumeBufferedPlayersIntoNewNotificationBatch(context, out NotificationBatch newNotificationBatch))
                    _currentlyAttemptedNotificationBatches.Add(newNotificationBatch);
            }

            // Send any currently-attempted batches that aren't already in flight,
            // but wait until send cooldown (caused by retry) has elapsed.
            if (DateTime.UtcNow >= _earliestNextBatchSendAt)
            {
                foreach (NotificationBatch batch in _currentlyAttemptedNotificationBatches)
                {
                    if (!batch.IsInFlight)
                        SendNotificationBatch(context, batch.Id);
                }
            }

            return Task.CompletedTask;
        }

        bool TryConsumeBufferedPlayersIntoNewNotificationBatch(IContext context, out NotificationBatch newNotificationBatch)
        {
            if (TotalNumBufferedPlayerNotificationBatches == 0)
            {
                newNotificationBatch = default;
                return false;
            }

            NotificationBatch   batch       = new NotificationBatch(_runningNotificationBatchId++);
            int                 batchSize   = 0;
            ConsumePlayersIntoNotificationBatchInPlace(context, _retryPlayerNotificationBatches, batch, ref batchSize);
            ConsumePlayersIntoNotificationBatchInPlace(context, _scannedPlayerNotificationBatches, batch, ref batchSize);

            if (batchSize == 0)
            {
                context.Log.Warning("Buffered notification batches were present but 0 tokens were consumed into send batch; ignoring.");
                newNotificationBatch = default;
                return false;
            }

            newNotificationBatch = batch;
            return true;
        }

        void ConsumePlayersIntoNotificationBatchInPlace(IContext context, List<PlayerNotificationBatch> playerNotificationBatchesToConsume, NotificationBatch batch, ref int batchSize)
        {
            NotificationCampaignOptions opts = GetOptions();

            int playerBatchNdx;
            for (playerBatchNdx = 0; playerBatchNdx < playerNotificationBatchesToConsume.Count; playerBatchNdx++)
            {
                PlayerNotificationBatch playerBatch = playerNotificationBatchesToConsume[playerBatchNdx];
                if (playerBatch.DeviceTokens.Count > opts.MaxTokensPerSendBatch)
                {
                    // #notification-campaign-oversized-batch
                    context.Log.Error("PlayerNotificationBatch for {PlayerId} has more device tokens ({PlayerDeviceTokenCount}) than can fit into a send batch ({MaxTokensPerSendBatch}); this shouldn't happen as it should already be handled earlier.",
                        playerBatch.PlayerId, playerBatch.DeviceTokens.Count, opts.MaxTokensPerSendBatch);
                    continue;
                }
                if (batchSize + playerBatch.DeviceTokens.Count > opts.MaxTokensPerSendBatch)
                    break;

                batch.Players.Add(playerBatch);
                batchSize += playerBatch.DeviceTokens.Count;
            }

            playerNotificationBatchesToConsume.RemoveRange(0, playerBatchNdx);
        }

        public override void Cancel(IContext context)
        {
            _workHasBeenCancelled = true;
        }

        static readonly FirebaseAdmin.Messaging.AndroidConfig DefaultFirebaseAndroidConfig = new FirebaseAdmin.Messaging.AndroidConfig
        {
            Priority = FirebaseAdmin.Messaging.Priority.High,
            Notification = new FirebaseAdmin.Messaging.AndroidNotification
            {
                Sound = "default",
            }
        };

        static readonly FirebaseAdmin.Messaging.ApnsConfig DefaultFirebaseApnsConfig = new FirebaseAdmin.Messaging.ApnsConfig
        {
            Aps = new FirebaseAdmin.Messaging.Aps
            {
                Sound = "default",
            }
        };

        void SendNotificationBatch(IContext context, int batchId)
        {
            NotificationBatch batch = GetNotificationBatchById(batchId);

            foreach (PlayerNotificationBatch playerBatch in batch.Players)
            {
                if (batch.AttemptIndex == 0 && playerBatch.AttemptIndex == 0)
                {
                    _statistics.NumPlayersAttempted++;
                    _statistics.NumTokensAttempted += playerBatch.DeviceTokens.Count;
                }

                _statistics.NumNotificationsAttempted += playerBatch.DeviceTokens.Count;
            }

            if (batch.AttemptIndex == 0)
                _statistics.NumBatchesAttempted++;

            _statistics.NumBatchSendAttempts++;

            // \note AttemptIndex is increased already now, so that it stays in sync
            //       with respect to stats like NumBatchesAttempted etc. in the case
            //       of crashes.
            batch.AttemptIndex++;
            batch.IsInFlight = true;

            List<FirebaseAdmin.Messaging.Message>   firebaseMessages        = CreateFirebaseMessagesForBatch(batch);
            int                                     numNotificationsInBatch = firebaseMessages.Count;

            DateTime sendStartTime = DateTime.UtcNow;

            context.ActorContinueTaskOnActorContext(
                SendFirebaseMessagesAsync(firebaseMessages),
                batchResponse   => HandleFirebaseBatchResponse(context, batchId, sendStartTime, numNotificationsInBatch, batchResponse),
                failure         => HandleFirebaseBatchSendFailure(batchId, sendStartTime, failure));
        }

        async Task<object> SendFirebaseMessagesAsync(List<FirebaseAdmin.Messaging.Message> firebaseMessages)
        {
            if (_campaignParams.DebugFakeNotificationMode)
                return await DebugFakeSendFirebaseMessagesAsync(firebaseMessages);
            else
                return await FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance.SendAllAsync(firebaseMessages);
        }

        class DebugFakeBatchResponse
        {
            public List<DebugFakeSendResponse> Responses;
            public DebugFakeBatchResponse(List<DebugFakeSendResponse> responses) { Responses = responses; }
        }

        class DebugFakeSendResponse
        {
            public bool                 IsSuccess;
            public SendErrorReaction?   Error;
        }

        async Task<DebugFakeBatchResponse> DebugFakeSendFirebaseMessagesAsync(List<FirebaseAdmin.Messaging.Message> firebaseMessages)
        {
            RandomPCG rnd = RandomPCG.CreateNew();

            if (rnd.NextDouble() < 0.1)
                throw new InvalidOperationException();

            await Task.Delay(new Random().Next(90, 150));

            return new DebugFakeBatchResponse(firebaseMessages.Select(_ =>
                {
                    if (rnd.NextDouble() < 0.5)
                    {
                        SendErrorReaction error = rnd.Choice(EnumUtil.GetValues<SendErrorReaction>());
                        return new DebugFakeSendResponse{ IsSuccess = false, Error = error };
                    }
                    else
                        return new DebugFakeSendResponse{ IsSuccess = true };
                }).ToList());
        }

        struct SendErrorInfo
        {
            public string               ErrorCodeString;
            public SendErrorReaction    Reaction;
            public string               ExceptionMessageForStatistics;

            public SendErrorInfo(string errorCodeString, SendErrorReaction reaction, string exceptionMessageForStatistics)
            {
                ErrorCodeString                 = errorCodeString;
                Reaction                        = reaction;
                ExceptionMessageForStatistics   = exceptionMessageForStatistics;
            }
        }

        /// <summary>
        /// Describes what should be done as a reaction to a notification send error.
        /// </summary>
        enum SendErrorReaction
        {
            /// <summary> Attempt to re-send. </summary>
            Retry,
            /// <summary> Do not attempt to re-send. </summary>
            DoNotRetry,
            /// <summary> Do not attempt to re-send. Try to remove the notification token from the player; the token is no longer valid. </summary>
            RemoveToken,
        }

        void HandleFirebaseBatchResponse(IContext context, int batchId, DateTime sendStartTime, int numNotificationsInBatch, object batchResponseObject)
        {
            if (_workHasBeenCancelled)
                return;

            NotificationBatch batch = GetNotificationBatchById(batchId);

            DateTime currentTime    = DateTime.UtcNow;
            TimeSpan sendDuration   = currentTime - sendStartTime;
            _statistics.FirebaseSendSuccessDurationTotalMS += (long)sendDuration.TotalMilliseconds;
            _statistics.FirebaseSendSuccessDurationNumSamples++;

            FirebaseAdmin.Messaging.BatchResponse   firebaseBatchResponse     = batchResponseObject as FirebaseAdmin.Messaging.BatchResponse;
            DebugFakeBatchResponse                  debugFakeBatchResponse    = batchResponseObject as DebugFakeBatchResponse;

            if (firebaseBatchResponse != null)
            {
                if (firebaseBatchResponse?.Responses == null)
                {
                    _statistics.NumBadFirebaseResponses++;
                    return;
                }
            }
            else if (debugFakeBatchResponse != null)
            {
                if (debugFakeBatchResponse?.Responses == null)
                {
                    _statistics.NumBadFirebaseResponses++;
                    return;
                }
            }
            else
                throw new ArgumentException($"Batch response type is neither {nameof(FirebaseAdmin.Messaging.BatchResponse)} nor {nameof(DebugFakeBatchResponse)}");

            int messageResponsesCount = firebaseBatchResponse?.Responses.Count ?? debugFakeBatchResponse.Responses.Count;

            bool MessageResponseIsSuccess(int index)
            {
                return firebaseBatchResponse != null
                     ? firebaseBatchResponse.Responses[index].IsSuccess
                     : debugFakeBatchResponse.Responses[index].IsSuccess;
            }

            SendErrorInfo GetMessageResponseErrorInfo(int index)
            {
                if (firebaseBatchResponse != null)
                {
                    FirebaseAdmin.Messaging.SendResponse        response            = firebaseBatchResponse.Responses[index];
                    FirebaseAdmin.Messaging.MessagingErrorCode? messagingErrorCode  = response.Exception?.MessagingErrorCode;
                    FirebaseAdmin.ErrorCode?                    generalErrorCode    = response.Exception?.ErrorCode;

                    string errorCodeString;
                    if (messagingErrorCode.HasValue)
                        errorCodeString = "Messaging_" + messagingErrorCode.Value.ToString();
                    else if (generalErrorCode.HasValue)
                        errorCodeString = "General_" + generalErrorCode.Value.ToString();
                    else
                        errorCodeString = "ErrorCodeMissing";

                    string exceptionMessageForStatistics;
                    if (response.Exception != null && ShouldKeepFirebaseExceptionMessage(messagingErrorCode, generalErrorCode))
                        exceptionMessageForStatistics = response.Exception.Message;
                    else
                        exceptionMessageForStatistics = null;

                    return new SendErrorInfo(
                        errorCodeString:                errorCodeString,
                        reaction:                       GetReactionForFirebaseSendError(messagingErrorCode, generalErrorCode),
                        exceptionMessageForStatistics:  exceptionMessageForStatistics);
                }
                else
                {
                    DebugFakeSendResponse   response    = debugFakeBatchResponse.Responses[index];
                    SendErrorReaction       error       = response.Error.Value;

                    return new SendErrorInfo(
                        errorCodeString:                "Debug_" + error.ToString(),
                        reaction:                       error,
                        exceptionMessageForStatistics:  error == SendErrorReaction.RemoveToken ? null : "DebugMessage_" + error.ToString());
                }
            }

            // \note Collect stats about but tolerate mismatch in response count.
            if (messageResponsesCount != numNotificationsInBatch)
                _statistics.NumBadFirebaseResponses++;

            // Iterate player notification batches in this notification batch, matching them with the responses.
            // Handle each notification according to response.

            int baseResponseNdx = 0;
            foreach (PlayerNotificationBatch playerBatch in batch.Players)
            {
                int             numResponsesForThisPlayer   = Math.Min(playerBatch.DeviceTokens.Count, messageResponsesCount - baseResponseNdx);

                int             numSuccessfulTokens         = 0;
                // This will contain this player's tokens to retry, if any.
                // None will be retried if retry limit was reached for this player.
                List<string>    tokensToRetry               = null;

                for (int subResponseNdx = 0; subResponseNdx < numResponsesForThisPlayer; subResponseNdx++)
                {
                    int responseNdx = baseResponseNdx + subResponseNdx;

                    if (MessageResponseIsSuccess(responseNdx))
                    {
                        numSuccessfulTokens++;

                        // Success resets retry backoff
                        _retryBackoffCounter = 0;
                    }
                    else
                    {
                        SendErrorInfo errorInfo = GetMessageResponseErrorInfo(responseNdx);

                        _statistics.FirebaseSendErrors[errorInfo.ErrorCodeString] = _statistics.FirebaseSendErrors.GetValueOrDefault(errorInfo.ErrorCodeString, 0) + 1;
                        if (errorInfo.ExceptionMessageForStatistics != null)
                            _statistics.FirebaseSendExceptionMessages.Add(errorInfo.ExceptionMessageForStatistics);

                        if (errorInfo.Reaction == SendErrorReaction.Retry)
                        {
                            if (playerBatch.AttemptIndex < MaxSendRetriesPerPlayer)
                            {
                                if (tokensToRetry == null)
                                    tokensToRetry = new List<string>();
                                tokensToRetry.Add(playerBatch.DeviceTokens[subResponseNdx]);
                            }

                            // Bump retry wait. (but only if not already bumped)
                            TryBumpSendWaitTimerDueToRetry(currentTime);
                        }
                        else
                        {
                            // Non-retryable error resets retry backoff
                            // \note The thinking is that the non-retryable errors are ones that can only
                            //       happen after we already got through firebase's throttling.
                            // \todo [nuutti] To what extent this is true is arguable.
                            _retryBackoffCounter = 0;

                            if (errorInfo.Reaction == SendErrorReaction.RemoveToken)
                            {
                                bool beganRemoval = TryBeginRemoveTokenFromPlayer(context, playerBatch.PlayerId, playerBatch.DeviceTokens[subResponseNdx]);
                                if (beganRemoval)
                                    _statistics.TokenRemovalsStarted++;
                                else
                                    _statistics.TokenRemovalsOmitted++;
                            }
                        }
                    }
                }

                bool wasFirstSuccessForThisPlayer = numSuccessfulTokens > 0 && !playerBatch.AnyTokensHaveSucceededForThisPlayer;
                if (wasFirstSuccessForThisPlayer)
                    _statistics.NumPlayersNotified++;

                _statistics.NumTokensNotified += numSuccessfulTokens;

                // Add a player notification batch for this player's retries, if any.
                if (tokensToRetry != null)
                {
                    _retryPlayerNotificationBatches.Add(new PlayerNotificationBatch(
                        playerBatch.PlayerId,
                        playerBatch.LanguageId,
                        tokensToRetry,
                        attemptIndex: playerBatch.AttemptIndex + 1,
                        anyTokensHaveSucceededForThisPlayer: playerBatch.AnyTokensHaveSucceededForThisPlayer || numSuccessfulTokens > 0));
                }
                else
                {
                    // If nothing has succeeded but we're also not retrying anymore, count the player as fully failed.
                    if (numSuccessfulTokens == 0 && !playerBatch.AnyTokensHaveSucceededForThisPlayer)
                        _statistics.NotificationFailedPlayers.Add(playerBatch.PlayerId);
                }

                baseResponseNdx += numResponsesForThisPlayer;
                // Break early if there's a mismatch in response vs request count.
                if (baseResponseNdx >= messageResponsesCount)
                    break;
            }

            // This batch is now done.
            // Any retries that may have been caused by failures in this batch were added to _retryPlayerNotificationBatches.
            RemoveNotificationBatchById(batchId);

            _statistics.NumBatchesSent++;
        }

        void HandleFirebaseBatchSendFailure(int batchId, DateTime sendStartTime, Exception failure)
        {
            if (_workHasBeenCancelled)
                return;

            _statistics.FirebaseBatchErrors.Add(failure.Message);

            NotificationBatch batch = GetNotificationBatchById(batchId);

            DateTime currentTime    = DateTime.UtcNow;
            TimeSpan sendDuration   = currentTime - sendStartTime;
            _statistics.FirebaseSendFailureDurationTotalMS += (long)sendDuration.TotalMilliseconds;
            _statistics.FirebaseSendFailureDurationNumSamples++;

            batch.IsInFlight = false;

            // Unless retry limit is reached, keep the batch for retrying.
            // \note AttemptIndex was increased earlier.
            if (batch.AttemptIndex <= MaxBatchSendRetries)
                TryBumpSendWaitTimerDueToRetry(currentTime);
            else
            {
                foreach (PlayerNotificationBatch playerBatch in batch.Players)
                {
                    if (!playerBatch.AnyTokensHaveSucceededForThisPlayer)
                        _statistics.NotificationFailedPlayers.Add(playerBatch.PlayerId);
                }

                RemoveNotificationBatchById(batchId);
            }
        }

        NotificationBatch GetNotificationBatchById(int id)
        {
            return _currentlyAttemptedNotificationBatches.Single(b => b.Id == id);
        }

        void RemoveNotificationBatchById(int id)
        {
            _currentlyAttemptedNotificationBatches.RemoveAll(b => b.Id == id);
        }

        void TryBumpSendWaitTimerDueToRetry(DateTime currentTime)
        {
            // Bump wait timer, but only if we're not already waiting
            if (currentTime >= _earliestNextBatchSendAt)
            {
                TimeSpan retryDelay = GetRetryDelay(_retryBackoffCounter, new Random());
                _earliestNextBatchSendAt = currentTime + retryDelay;
                _retryBackoffCounter++;

                _statistics.TotalRetryWaitMS += (long)retryDelay.TotalMilliseconds;
            }
        }

        List<FirebaseAdmin.Messaging.Message> CreateFirebaseMessagesForBatch(NotificationBatch batch)
        {
            FirebaseAdmin.Messaging.FcmOptions fcmOptionsMaybe;
            if (!string.IsNullOrEmpty(_campaignParams.FirebaseAnalyticsLabel))
                fcmOptionsMaybe = new FirebaseAdmin.Messaging.FcmOptions{ AnalyticsLabel = _campaignParams.FirebaseAnalyticsLabel };
            else
                fcmOptionsMaybe = null;

            List<FirebaseAdmin.Messaging.Message> firebaseMessages = new List<FirebaseAdmin.Messaging.Message>();

            foreach (PlayerNotificationBatch playerBatch in batch.Players)
            {
                string localizedTitle = _campaignParams.Content.Title.Localize(playerBatch.LanguageId);
                string localizedBody = _campaignParams.Content.Body.Localize(playerBatch.LanguageId);

                foreach (string deviceToken in playerBatch.DeviceTokens)
                {
                    firebaseMessages.Add(new FirebaseAdmin.Messaging.Message
                    {
                        Token = deviceToken,
                        Notification = new FirebaseAdmin.Messaging.Notification
                        {
                            Title = localizedTitle,
                            Body = localizedBody,
                        },
                        Android = DefaultFirebaseAndroidConfig,
                        Apns = DefaultFirebaseApnsConfig,
                        FcmOptions = fcmOptionsMaybe,
                    });
                }
            }

            return firebaseMessages;
        }

        bool TryBeginRemoveTokenFromPlayer(IContext context, EntityId playerId, string deviceToken)
        {
            CleanupCompletedPlayerTokenRemovals();

            if (_ongoingPlayerTokenRemovals.Count < GetOptions().PlayerTokenRemovalRateLimit)
            {
                Task<PlayerRemovePushNotificationTokenResponse> removalTask = RemoveTokenFromPlayerAsync(context, playerId, deviceToken);
                _ongoingPlayerTokenRemovals.Add(new PlayerTokenRemoval(removalTask));
                return true;
            }
            else
                return false;
        }

        static async Task<PlayerRemovePushNotificationTokenResponse> RemoveTokenFromPlayerAsync(IContext context, EntityId playerId, string deviceToken)
        {
            // Perform the EntityAsk to the player, but also wait at least 1 second.
            // This minimum wait achieves rate limiting specified by PlayerTokenRemovalRateLimit.

            PlayerRemovePushNotificationTokenRequest        request     = new PlayerRemovePushNotificationTokenRequest(deviceToken);
            Task<PlayerRemovePushNotificationTokenResponse> requestTask = context.ActorEntityAskAsync<PlayerRemovePushNotificationTokenResponse>(playerId, request);

            await Task.WhenAll(requestTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

            return requestTask.GetCompletedResult();
        }

        void CleanupCompletedPlayerTokenRemovals()
        {
            foreach (PlayerTokenRemoval removal in _ongoingPlayerTokenRemovals)
            {
                if (removal.Request.IsCompleted)
                {
                    if (removal.Request.IsCompletedSuccessfully)
                    {
                        _statistics.TokenRemovalsSucceeded++;
                    }
                    else if (removal.Request.IsFaulted)
                    {
                        // Just observe the exception
                        _ = removal.Request.Exception;
                    }

                    removal.Request = null; // For the RemoveAll below
                }
            }

            _ongoingPlayerTokenRemovals.RemoveAll(r => r.Request == null);
        }

        static SendErrorReaction GetReactionForFirebaseSendError(FirebaseAdmin.Messaging.MessagingErrorCode? messagingErrorCodeMaybe, FirebaseAdmin.ErrorCode? generalErrorCodeMaybe)
        {
            if (messagingErrorCodeMaybe.HasValue)
            {
                switch (messagingErrorCodeMaybe.Value)
                {
                    case FirebaseAdmin.Messaging.MessagingErrorCode.ThirdPartyAuthError:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.InvalidArgument:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.SenderIdMismatch:
                        return SendErrorReaction.DoNotRetry;

                    case FirebaseAdmin.Messaging.MessagingErrorCode.Unregistered:
                        return SendErrorReaction.RemoveToken;

                    case FirebaseAdmin.Messaging.MessagingErrorCode.Internal:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.QuotaExceeded:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.Unavailable:
                        return SendErrorReaction.Retry;

                    default:
                        // Unknown
                        return SendErrorReaction.DoNotRetry;
                }
            }
            else if (generalErrorCodeMaybe.HasValue)
            {
                switch (generalErrorCodeMaybe.Value)
                {
                    case FirebaseAdmin.ErrorCode.InvalidArgument:
                    case FirebaseAdmin.ErrorCode.FailedPrecondition:
                    case FirebaseAdmin.ErrorCode.OutOfRange:
                    case FirebaseAdmin.ErrorCode.Unauthenticated:
                    case FirebaseAdmin.ErrorCode.PermissionDenied:
                    case FirebaseAdmin.ErrorCode.NotFound:
                    case FirebaseAdmin.ErrorCode.AlreadyExists:
                    case FirebaseAdmin.ErrorCode.Cancelled:
                    case FirebaseAdmin.ErrorCode.DataLoss:
                    case FirebaseAdmin.ErrorCode.Unknown:
                        return SendErrorReaction.DoNotRetry;

                    case FirebaseAdmin.ErrorCode.Conflict:
                    case FirebaseAdmin.ErrorCode.Aborted:
                    case FirebaseAdmin.ErrorCode.ResourceExhausted:
                    case FirebaseAdmin.ErrorCode.Internal:
                    case FirebaseAdmin.ErrorCode.Unavailable:
                    case FirebaseAdmin.ErrorCode.DeadlineExceeded:
                        return SendErrorReaction.Retry;

                    default:
                        // Unknown
                        return SendErrorReaction.DoNotRetry;
                }
            }
            else
                return SendErrorReaction.DoNotRetry;
        }

        static bool ShouldKeepFirebaseExceptionMessage(FirebaseAdmin.Messaging.MessagingErrorCode? messagingErrorCodeMaybe, FirebaseAdmin.ErrorCode? generalErrorCodeMaybe)
        {
            if (messagingErrorCodeMaybe.HasValue)
            {
                switch (messagingErrorCodeMaybe.Value)
                {
                    case FirebaseAdmin.Messaging.MessagingErrorCode.ThirdPartyAuthError:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.InvalidArgument:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.SenderIdMismatch:
                        return true;

                    case FirebaseAdmin.Messaging.MessagingErrorCode.Internal:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.QuotaExceeded:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.Unregistered:
                    case FirebaseAdmin.Messaging.MessagingErrorCode.Unavailable:
                        return false;

                    default:
                        // Unknown
                        return true;
                }
            }
            else if (generalErrorCodeMaybe.HasValue)
            {
                switch (generalErrorCodeMaybe.Value)
                {
                    case FirebaseAdmin.ErrorCode.InvalidArgument:
                    case FirebaseAdmin.ErrorCode.FailedPrecondition:
                    case FirebaseAdmin.ErrorCode.OutOfRange:
                    case FirebaseAdmin.ErrorCode.Unauthenticated:
                    case FirebaseAdmin.ErrorCode.PermissionDenied:
                    case FirebaseAdmin.ErrorCode.NotFound:
                    case FirebaseAdmin.ErrorCode.AlreadyExists:
                    case FirebaseAdmin.ErrorCode.Cancelled:
                    case FirebaseAdmin.ErrorCode.DataLoss:
                    case FirebaseAdmin.ErrorCode.Unknown:
                    case FirebaseAdmin.ErrorCode.Conflict:
                    case FirebaseAdmin.ErrorCode.Aborted:
                        return true;

                    case FirebaseAdmin.ErrorCode.ResourceExhausted:
                    case FirebaseAdmin.ErrorCode.Internal:
                    case FirebaseAdmin.ErrorCode.Unavailable:
                    case FirebaseAdmin.ErrorCode.DeadlineExceeded:
                        return false;

                    default:
                        // Unknown
                        return true;
                }
            }
            else
                return false;
        }

        static TimeSpan GetRetryDelay(int backoffCounter, Random rnd)
        {
            double firstBaseSeconds = RetryBackoffFirstDelayBase.TotalSeconds;
            double maxBaseSeconds   = RetryBackoffMaxDelayBase.TotalSeconds;

            double baseSeconds      = Math.Min(maxBaseSeconds, firstBaseSeconds * Math.Pow(2d, (double)backoffCounter));

            double seconds          = baseSeconds * (1d + rnd.NextDouble()*RetryBackoffMaxJitterFactor);

            return TimeSpan.FromSeconds(seconds);
        }
    }
}
