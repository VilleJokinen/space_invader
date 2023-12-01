// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Json;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using Metaplay.Server.Database;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Common utilities for both <see cref="ActivablesController"/>
    /// and <see cref="MetaOffersController"/>.
    ///
    /// MetaOffers are a specific type of activables, and their admin
    /// api returns data similar to the activables admin api, but
    /// augmented with offers-specific information.
    /// This base class contains functionality shared by the
    /// general activables api and the offers api.
    /// </summary>
    public class ActivablesControllerBase : GameAdminApiController
    {
        public ActivablesControllerBase(ILogger<ActivablesControllerBase> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        public enum ActivablePhase
        {
            // Common phases (phases possible for an activable generally, as well as an activable state specific to a player)
            Inactive,
            Preview,
            Active,
            EndingSoon,
            Review,

            // Phases for an activable for a specific player
            Ineligible,
            TotalLimitsReached,
            InCooldown,
            Tentative,

            // Error, could not resolve phase. It's a bug
            ServerError,
        }

        public class GeneralActivableKindData
        {
            public OrderedDictionary<object, GeneralActivableData>  Activables;
            public List<string>                                     IncompleteIntegrationErrors;

            public GeneralActivableKindData(OrderedDictionary<object, GeneralActivableData> activables, List<string> incompleteIntegrationErrors)
            {
                Activables = activables ?? throw new ArgumentNullException(nameof(activables));
                IncompleteIntegrationErrors = incompleteIntegrationErrors ?? throw new ArgumentNullException(nameof(incompleteIntegrationErrors));
            }
        }

        public class GeneralActivableData
        {
            [ForceSerializeByValue]
            public IMetaActivableConfigData Config;
            public MetaTime                 EvaluatedAt;
            public ScheduleStatus?          ScheduleStatus;
            public long?                    AudienceSizeEstimate;
            public MetaActivableStatistics  Statistics;

            public GeneralActivableData(GeneralActivableData other)
                : this(
                    other.Config,
                    other.EvaluatedAt,
                    other.ScheduleStatus,
                    other.AudienceSizeEstimate,
                    other.Statistics)
            {
            }

            public GeneralActivableData(IMetaActivableConfigData config, MetaTime evaluatedAt, ScheduleStatus? scheduleStatus, long? audienceSizeEstimate, MetaActivableStatistics statistics)
            {
                Config = config ?? throw new ArgumentNullException(nameof(config));
                EvaluatedAt = evaluatedAt;
                ScheduleStatus = scheduleStatus;
                AudienceSizeEstimate = audienceSizeEstimate;
                Statistics = statistics;
            }
        }

        protected class GeneralActivablesQueryContext
        {
            public ISharedGameConfig            SharedGameConfig;
            public StatsCollectorState          StatsCollectorState;
            public int                          TotalPlayerCount;
            public SegmentSizeEstimateResponse  SegmentSizeResponse;
            public PlayerLocalTime              PseudoPlayerLocalTime;

            public FullMetaActivableStatistics ActivableStatistics => StatsCollectorState.ActivableStatistics;

            public GeneralActivablesQueryContext(ISharedGameConfig sharedGameConfig, StatsCollectorState statsCollectorState, int totalPlayerCount, SegmentSizeEstimateResponse segmentSizeResponse, PlayerLocalTime pseudoPlayerLocalTime)
            {
                SharedGameConfig = sharedGameConfig;
                StatsCollectorState = statsCollectorState;
                TotalPlayerCount = totalPlayerCount;
                SegmentSizeResponse = segmentSizeResponse;
                PseudoPlayerLocalTime = pseudoPlayerLocalTime;
            }
        }

        protected async Task<GeneralActivablesQueryContext> GetGeneralActivablesQueryContextAsync(string time)
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            ISharedGameConfig sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;

            // Get StatsCollector state & resolve totalPlayerCount
            StatsCollectorStateResponse statsCollectorStateResponse = await AskEntityAsync<StatsCollectorStateResponse>(StatsCollectorManager.EntityId, StatsCollectorStateRequest.Instance);
            StatsCollectorState         statsCollectorState         = statsCollectorStateResponse.State.Deserialize(resolver: null, logicVersion: null);
            int                         totalPlayerCount            = statsCollectorState.DatabaseShardItemCounts[DatabaseTypeRegistry.GetItemSpec<PersistedPlayerBase>().TableName].Sum();

            SegmentSizeEstimateResponse segmentSizeResponse = await AskEntityAsync<SegmentSizeEstimateResponse>(PlayerSegmentSizeEstimatorActor.EntityId, SegmentSizeEstimateRequest.Instance);

            PlayerLocalTime pseudoPlayerLocalTime = ParseActivablesEvaluationTimeOrDefault(time);

            return new GeneralActivablesQueryContext(sharedGameConfig, statsCollectorState, totalPlayerCount, segmentSizeResponse, pseudoPlayerLocalTime);
        }

        protected GeneralActivableData CreateGeneralActivableData(object activableId, IMetaActivableConfigData activableInfo, MetaActivableKindId activableKindId, GeneralActivablesQueryContext context)
        {
            ScheduleStatus? scheduleStatus = GetActivableScheduleStatus(activableInfo, context.PseudoPlayerLocalTime);

            long? audienceSizeEstimate = PlayerTargetingUtil.TryEstimateAudienceSize(
                context.TotalPlayerCount,
                includeTargetPlayers: null,
                includeTargetSegments: activableInfo.ActivableParams.Segments?.Select(segment => segment.Ref.SegmentId).ToList(),
                context.SegmentSizeResponse.SegmentEstimates);

            MetaActivableKindStatistics kindStatisticsMaybe = context.ActivableStatistics.KindStatistics.GetValueOrDefault(activableKindId, defaultValue: null);

            string activableIdString = ((IStringId)activableId).Value; // \note #activable-id-type
            MetaActivableStatistics statistics = kindStatisticsMaybe?.ActivableStatistics.GetValueOrDefault(activableIdString) ?? new MetaActivableStatistics();

            return new GeneralActivableData(activableInfo, context.PseudoPlayerLocalTime.Time, scheduleStatus, audienceSizeEstimate, statistics);
        }

        protected PlayerLocalTime ParseActivablesEvaluationTimeOrDefault(string time)
        {
            // Evaluate at either the current server time or the time that the user passed in.
            MetaTime checkTime = MetaTime.Now;
            MetaDuration timezoneOffset = MetaDuration.FromSeconds(0);
            if (!string.IsNullOrEmpty(time))
            {
                try
                {
                    DateTimeOffset dto = DateTimeOffset.ParseExact(time, new string[] { "yyyy-MM-dd'T'HH:mm:ss.FFFK" }, CultureInfo.InvariantCulture);
                    checkTime = MetaTime.FromDateTime(dto.UtcDateTime);
                    timezoneOffset = MetaDuration.FromTimeSpan(dto.Offset);
                }
                catch (Exception ex)
                {
                    throw new MetaplayHttpException(400, "Could not generate result.", $"time was not valid: {ex.Message}");
                }
            }

            return new PlayerLocalTime(checkTime, timezoneOffset);
        }

        public struct ScheduleStatus
        {
            public SchedulePhaseInfo    CurrentPhase;
            public SchedulePhaseInfo?   NextPhase;
            public MetaTimeRange?       RelevantEnabledRange;

            public ScheduleStatus(SchedulePhaseInfo currentPhase, SchedulePhaseInfo? nextPhase, MetaTimeRange? relevantEnabledRange)
            {
                CurrentPhase = currentPhase;
                NextPhase = nextPhase;
                RelevantEnabledRange = relevantEnabledRange;
            }
        }

        public struct SchedulePhaseInfo
        {
            public ActivablePhase   Phase;
            public MetaTime?        StartTime;
            public MetaTime?        EndTime;

            public SchedulePhaseInfo(ActivablePhase phase, MetaTime? startTime, MetaTime? endTime)
            {
                Phase = phase;
                StartTime = startTime;
                EndTime = endTime;
            }
        }

        protected static ScheduleStatus? GetActivableScheduleStatus(IMetaActivableConfigData info, PlayerLocalTime localTime)
        {
            MetaScheduleBase schedule = info.ActivableParams.Schedule;
            if (schedule != null)
                return GetScheduleStatusAt(schedule, localTime);
            else
                return null;
        }

        internal static ScheduleStatus GetScheduleStatusAt(MetaScheduleBase schedule, PlayerLocalTime localTime)
        {
            SchedulePhaseInfo   currentPhase    = GetSchedulePhaseInfoAt(schedule, localTime);
            SchedulePhaseInfo?  nextPhase       = currentPhase.EndTime.HasValue
                                                  ? GetSchedulePhaseInfoAt(schedule, new PlayerLocalTime(currentPhase.EndTime.Value, localTime.UtcOffset))
                                                  : null;

            MetaTimeRange? relevantEnabledRange;
            if (currentPhase.Phase == ActivablePhase.Review)
                relevantEnabledRange = schedule.QueryOccasions(localTime).PreviousEnabledOccasion.Value.EnabledRange;
            else
                relevantEnabledRange = schedule.QueryOccasions(localTime).CurrentOrNextEnabledOccasion?.EnabledRange;

            return new ScheduleStatus(currentPhase, nextPhase, relevantEnabledRange);
        }

        static SchedulePhaseInfo GetSchedulePhaseInfoAt(MetaScheduleBase schedule, PlayerLocalTime localTime)
        {
            MetaScheduleOccasionsQueryResult    queryResult     = schedule.QueryOccasions(localTime);
            MetaScheduleOccasion?               previous        = queryResult.PreviousEnabledOccasion;
            MetaScheduleOccasion?               currentOrNext   = queryResult.CurrentOrNextEnabledOccasion;
            MetaTime                            time            = localTime.Time;

            if (currentOrNext?.IsEnabledAt(time) ?? false)
            {
                if (currentOrNext.Value.IsEndingSoonAt(time))
                    return new SchedulePhaseInfo(ActivablePhase.EndingSoon, startTime: currentOrNext.Value.EndingSoonStartsAt, endTime: currentOrNext.Value.EnabledRange.End);
                else
                    return new SchedulePhaseInfo(ActivablePhase.Active, startTime: currentOrNext.Value.EnabledRange.Start, endTime: currentOrNext.Value.EndingSoonStartsAt);
            }
            else if (currentOrNext?.IsPreviewedAt(time) ?? false)
                return new SchedulePhaseInfo(ActivablePhase.Preview, startTime: currentOrNext.Value.VisibleRange.Start, endTime: currentOrNext.Value.EnabledRange.Start);
            else if (previous?.IsReviewedAt(time) ?? false)
                return new SchedulePhaseInfo(ActivablePhase.Review, startTime: previous.Value.EnabledRange.End, endTime: previous.Value.VisibleRange.End);
            else
                return new SchedulePhaseInfo(ActivablePhase.Inactive, startTime: previous?.VisibleRange.End, endTime: currentOrNext?.VisibleRange.Start);
        }

        protected class PlayerActivablesQueryContext
        {
            public ISharedGameConfig    SharedGameConfig;
            public IPlayerModelBase     PlayerModel;

            public PlayerActivablesQueryContext(ISharedGameConfig sharedGameConfig, IPlayerModelBase playerModel)
            {
                SharedGameConfig = sharedGameConfig;
                PlayerModel = playerModel;
            }
        }

        protected async Task<PlayerActivablesQueryContext> GetPlayerActivablesQueryContextAsync(string playerIdStr)
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            ISharedGameConfig sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;

            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);
            IPlayerModelBase player = details.Model;
            // Run OnStateRestored the same way as when loading the model in the PlayerActor.
            // This way an appropriate PlayerModel.CurrentTime will be available in the segment condition evaluation.
            MetaTime now = MetaTime.Now;
            MetaDuration elapsedTime = now - MetaTime.FromDateTime(details.Persisted.PersistedAt);
            player.OnRestoredFromPersistedState(now, elapsedTime);

            return new PlayerActivablesQueryContext(sharedGameConfig, player);
        }

        protected PlayerActivableData CreatePlayerActivableData(IMetaActivableConfigData info, IMetaActivableSet playerActivables, PlayerActivablesQueryContext context)
        {
            MetaActivableState activableStateMaybe = playerActivables.TryGetState(info);

            return new PlayerActivableData(
                config:         info,
                phase:          GetPlayerActivablePhase(context.PlayerModel, playerActivables, info),
                scheduleStatus: GetActivableScheduleStatus(info, context.PlayerModel.GetCurrentLocalTime()),
                state:          GetPlayerActivableStateData(activableStateMaybe, context.PlayerModel.CurrentTime),
                debugState:     TryGetPlayerActivableDebugStateData(activableStateMaybe));
        }

        public class PlayerActivableKindData
        {
            public OrderedDictionary<object, PlayerActivableData>   Activables;
            public List<string>                                     IncompleteIntegrationErrors;

            public PlayerActivableKindData(OrderedDictionary<object, PlayerActivableData> activables, List<string> incompleteIntegrationErrors)
            {
                Activables = activables ?? throw new ArgumentNullException(nameof(activables));
                IncompleteIntegrationErrors = incompleteIntegrationErrors ?? throw new ArgumentNullException(nameof(incompleteIntegrationErrors));
            }
        }

        public class PlayerActivableData
        {
            [ForceSerializeByValue]
            public IMetaActivableConfigData Config;
            public ActivablePhase           Phase;
            public ScheduleStatus?          ScheduleStatus;
            public ActivableStateData       State;
            public ActivableDebugStateData? DebugState;

            public PlayerActivableData(PlayerActivableData other)
                : this(
                    other.Config,
                    other.Phase,
                    other.ScheduleStatus,
                    other.State,
                    other.DebugState)
            {
            }

            public PlayerActivableData(IMetaActivableConfigData config, ActivablePhase phase, ScheduleStatus? scheduleStatus, ActivableStateData state, ActivableDebugStateData? debugState)
            {
                Config = config ?? throw new ArgumentNullException(nameof(config));
                Phase = phase;
                ScheduleStatus = scheduleStatus;
                State = state;
                DebugState = debugState;
            }
        }

        protected static ActivableStateData GetPlayerActivableStateData(MetaActivableState activableStateMaybe, MetaTime currentTime)
        {
            MetaActivableState.Activation?  activation      = activableStateMaybe?.LatestActivation;
            bool                            isOngoing       = activableStateMaybe?.HasOngoingActivation(currentTime) ?? false;
            bool                            isInCooldown    = activableStateMaybe?.IsInCooldown(currentTime) ?? false;

            return new ActivableStateData(
                hasOngoingActivation:           isOngoing,
                isInCooldown:                   activableStateMaybe?.IsInCooldown(currentTime) ?? false,
                activationRemaining:            isOngoing ? activation.Value.EndAt - currentTime : null,
                cooldownRemaining:              isInCooldown ? activation.Value.CooldownEndAt - currentTime : null,
                numActivated:                   activableStateMaybe?.NumActivated ?? 0,
                currentActivationNumConsumed:   activation?.NumConsumed,
                totalNumConsumed:               activableStateMaybe?.TotalNumConsumed ?? 0);
        }

        public struct ActivableStateData
        {
            public bool             HasOngoingActivation;
            public bool             IsInCooldown;
            public MetaDuration?    ActivationRemaining;
            public MetaDuration?    CooldownRemaining;
            public int              NumActivated;
            public int?             CurrentActivationNumConsumed;
            public int              TotalNumConsumed;

            public ActivableStateData(bool hasOngoingActivation, bool isInCooldown, MetaDuration? activationRemaining, MetaDuration? cooldownRemaining, int numActivated, int? currentActivationNumConsumed, int totalNumConsumed)
            {
                HasOngoingActivation = hasOngoingActivation;
                IsInCooldown = isInCooldown;
                ActivationRemaining = activationRemaining;
                CooldownRemaining = cooldownRemaining;
                NumActivated = numActivated;
                CurrentActivationNumConsumed = currentActivationNumConsumed;
                TotalNumConsumed = totalNumConsumed;
            }
        }

        protected ActivableDebugStateData? TryGetPlayerActivableDebugStateData(MetaActivableState activableStateMaybe)
        {
            MetaActivableState.DebugState debug = activableStateMaybe?.Debug;
            if (debug != null)
                return new ActivableDebugStateData(debug.Phase);
            else
                return null;
        }

        public struct ActivableDebugStateData
        {
            public MetaActivableState.DebugPhase Phase;

            public ActivableDebugStateData(MetaActivableState.DebugPhase phase)
            {
                Phase = phase;
            }
        }

        protected ActivablePhase GetPlayerActivablePhase(IPlayerModelBase playerModel, IMetaActivableSet playerActivables, IMetaActivableConfigData info)
        {
            if (!playerActivables.TryGetVisibleStatus(info, playerModel, out MetaActivableVisibleStatus visibleStatus))
            {
                if (info.ActivableParams.IsEnabled && !info.ActivableParams.PlayerConditionsAreFulfilled(playerModel))
                    return ActivablePhase.Ineligible;

                MetaActivableState activableStateMaybe = playerActivables.TryGetState(info);

                if (activableStateMaybe != null)
                {
                    if (activableStateMaybe.TotalLimitsAreReached())
                        return ActivablePhase.TotalLimitsReached;

                    if (activableStateMaybe.IsInCooldown(playerModel.CurrentTime))
                        return ActivablePhase.InCooldown;
                }

                return ActivablePhase.Inactive;
            }

            switch (visibleStatus)
            {
                case MetaActivableVisibleStatus.Tentative:  return ActivablePhase.Tentative;
                case MetaActivableVisibleStatus.Active:     return ActivablePhase.Active;
                case MetaActivableVisibleStatus.EndingSoon: return ActivablePhase.EndingSoon;
                case MetaActivableVisibleStatus.InPreview:  return ActivablePhase.Preview;
                case MetaActivableVisibleStatus.InReview:   return ActivablePhase.Review;
                default:
                    return ActivablePhase.ServerError;
            }
        }
    }
}
