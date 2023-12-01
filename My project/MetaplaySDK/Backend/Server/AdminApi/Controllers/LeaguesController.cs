// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using Metaplay.Server.AdminApi.AuditLog;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using Metaplay.Server.League;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    [LeaguesEnabledCondition]
    public class LeaguesController : GameAdminApiController
    {
        #region Audit log events
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LeagueSeasonDebugAdvanced)]
        public class LeagueSeasonDebugAdvanced : LeagueEventPayloadBase
        {
            [MetaMember(1)] public bool Ended { get; private set; }

            public LeagueSeasonDebugAdvanced(bool ended)
            {
                Ended = ended;
            }

            LeagueSeasonDebugAdvanced() { }

            public override string EventTitle       => "Season advanced";
            public override string EventDescription => Ended ? "Season was ended manually." : "Season was started manually.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LeagueParticipantRemoved)]
        public class LeagueParticipantRemoved : LeagueEventPayloadBase
        {
            [MetaMember(1)] public EntityId ParticipantId { get; private set; }

            public LeagueParticipantRemoved(EntityId participantId)
            {
                ParticipantId = participantId;
            }

            LeagueParticipantRemoved() { }

            public override string EventTitle       => "Participant removed";
            public override string EventDescription => $"Participant {ParticipantId} was removed from the league.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerDebugRemovedFromLeague)]
        public class PlayerEventDebugRemovedFromLeague : PlayerEventPayloadBase
        {
            [MetaMember(1)] public EntityId LeagueId { get; private set; }

            public PlayerEventDebugRemovedFromLeague(EntityId leagueId)
            {
                LeagueId = leagueId;
            }

            PlayerEventDebugRemovedFromLeague() { }

            public override string EventTitle       => "Removed from league";
            public override string EventDescription => $"Removed from {LeagueId}.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LeagueParticipantAdded)]
        public class LeagueParticipantAdded : LeagueEventPayloadBase
        {
            [MetaMember(1)] public EntityId ParticipantId { get; private set; }
            [MetaMember(2)] public EntityId DivisionId    { get; private set; }

            public LeagueParticipantAdded(EntityId participantId, EntityId divisionId)
            {
                ParticipantId = participantId;
                DivisionId    = divisionId;
            }

            LeagueParticipantAdded() { }

            public override string EventTitle       => "Participant added";
            public override string EventDescription => $"Participant {ParticipantId} was added to the league in {DivisionId}.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerDebugAddedToLeague)]
        public class PlayerEventDebugAddedToLeague : PlayerEventPayloadBase
        {
            [MetaMember(1)] public EntityId LeagueId   { get; private set; }
            [MetaMember(2)] public EntityId DivisionId { get; private set; }

            public PlayerEventDebugAddedToLeague(EntityId leagueId, EntityId divisionId)
            {
                LeagueId   = leagueId;
                DivisionId = divisionId;
            }

            PlayerEventDebugAddedToLeague() { }

            public override string EventTitle       => "Added to league";
            public override string EventDescription => $"Added to {LeagueId} in {DivisionId}.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LeagueParticipantMoved)]
        public class LeagueParticipantMoved : LeagueEventPayloadBase
        {
            [MetaMember(1)] public EntityId ParticipantId { get; private set; }
            [MetaMember(2)] public EntityId DivisionId    { get; private set; }

            public LeagueParticipantMoved(EntityId participantId, EntityId divisionId)
            {
                ParticipantId = participantId;
                DivisionId    = divisionId;
            }

            LeagueParticipantMoved() { }

            public override string EventTitle       => "Participant moved";
            public override string EventDescription => $"Participant {ParticipantId} was moved to the league in {DivisionId}.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerDebugMovedToLeague)]
        public class PlayerEventDebugMovedToLeague : PlayerEventPayloadBase
        {
            [MetaMember(1)] public EntityId LeagueId   { get; private set; }
            [MetaMember(2)] public EntityId DivisionId { get; private set; }

            public PlayerEventDebugMovedToLeague(EntityId leagueId, EntityId divisionId)
            {
                LeagueId   = leagueId;
                DivisionId = divisionId;
            }

            PlayerEventDebugMovedToLeague() { }

            public override string EventTitle       => "Moved to league";
            public override string EventDescription => $"Moved to {LeagueId} in {DivisionId}.";
        }

        #endregion

        public class LeagueEntityState
        {
            public EntityId                                 LeagueId        { get; private set; }
            public bool                                     Enabled         { get; private set; }
            public int                                      NumParticipants { get; private set; }
            public ActivablesControllerBase.ScheduleStatus? ScheduleStatus  { get; private set; }
            public string                                   DisplayName     { get; private set; }
            public string                                   Description     { get; private set; }

            public LeagueEntityState(EntityId leagueId, bool enabled, int numParticipants, ActivablesControllerBase.ScheduleStatus? scheduleStatus,
                string displayName,
                string description)
            {
                LeagueId         = leagueId;
                Enabled          = enabled;
                NumParticipants  = numParticipants;
                ScheduleStatus   = scheduleStatus;
                DisplayName      = displayName;
                Description      = description;
            }
        }

        public class LeagueStateResponse
        {
            public bool                                           Enabled                     { get; private set; }
            public LeagueManagerActorStateBase                    State                       { get; private set; }
            public MetaScheduleBase                               Schedule                    { get; private set; }
            public ActivablesControllerBase.ScheduleStatus?       ScheduleStatus              { get; private set; }
            public bool                                           CurrentSeasonOutOfSync      { get; private set; }
            public LeaguesController.LeagueSeasonScheduleResponse CurrentOrNextSeasonSchedule { get; private set; }
            public int                                            CurrentParticipantCount     { get; private set; }
            public LeagueDetails                                  Details                     { get; private set; }
            public LeagueSeasonMigrationProgressState             MigrationProgress           { get; private set; }

            public LeagueStateResponse(
                LeagueManagerActorStateBase state,
                MetaScheduleBase schedule,
                ActivablesControllerBase.ScheduleStatus? scheduleStatus,
                int currentParticipantCount,
                bool enabled,
                LeagueDetails details,
                bool currentSeasonOutOfSync,
                LeagueSeasonScheduleResponse currentOrNextSeasonSchedule,
                LeagueSeasonMigrationProgressState migrationProgress)
            {
                State                       = state;
                Schedule                    = schedule;
                ScheduleStatus              = scheduleStatus;
                CurrentParticipantCount     = currentParticipantCount;
                Enabled                     = enabled;
                Details                     = details;
                CurrentSeasonOutOfSync      = currentSeasonOutOfSync;
                CurrentOrNextSeasonSchedule = currentOrNextSeasonSchedule;
                MigrationProgress           = migrationProgress;
            }
        }

        public class LeagueSeasonStateResponse
        {
            public class LeagueSeasonRankStateResponse
            {
                public string                                           RankName          { get; private set; }
                public string                                           Description       { get; private set; }
                public int                                              TotalParticipants { get; private set; }
                public int                                              NumPromotions     { get; private set; }
                public int                                              NumDemotions      { get; private set; }
                public int                                              NumDropped        { get; private set; }
                public int                                              NumDivisions      { get; private set; }

                public LeagueSeasonRankStateResponse(
                    int totalParticipants,
                    string rankName,
                    string description,
                    int numPromotions,
                    int numDemotions,
                    int numDropped,
                    int numDivisions)
                {
                    TotalParticipants = totalParticipants;
                    RankName          = rankName;
                    Description       = description;
                    NumPromotions     = numPromotions;
                    NumDemotions      = numDemotions;
                    NumDropped        = numDropped;
                    NumDivisions      = numDivisions;
                }
            }

            public string                                   DisplayName           { get; private set; }
            public string                                   Description           { get; private set; }
            public bool                                     IsCurrent             { get; private set; }
            public bool                                     MigrationInProgress   { get; private set; }
            public MetaTime                                 StartTime             { get; private set; }
            public MetaTime                                 EndTime               { get; private set; }
            public ActivablesControllerBase.ScheduleStatus? ScheduleStatus        { get; private set; }
            public int                                      TotalParticipantCount { get; private set; }
            public int                                      NewParticipantCount   { get; private set; }
            public int                                      NumPromotions         { get; private set; }
            public int                                      NumDemotions          { get; private set; }
            public int                                      NumDropped            { get; private set; }
            public List<LeagueSeasonRankStateResponse>      Ranks                 { get; private set; }
            public bool                                     StartedEarly          { get; private set; }
            public bool                                     EndedEarly            { get; private set; }

            public LeagueSeasonStateResponse(
                bool isCurrent,
                ActivablesControllerBase.ScheduleStatus? scheduleStatus,
                int totalParticipantCount,
                int newParticipantCount,
                List<LeagueSeasonRankStateResponse> ranks,
                MetaTime startTime,
                MetaTime endTime,
                bool migrationInProgress,
                int numPromotions,
                int numDemotions,
                int numDropped,
                string displayName,
                string description,
                bool startedEarly,
                bool endedEarly)
            {
                IsCurrent             = isCurrent;
                ScheduleStatus        = scheduleStatus;
                TotalParticipantCount = totalParticipantCount;
                NewParticipantCount   = newParticipantCount;
                Ranks                 = ranks;
                StartTime             = startTime;
                EndTime               = endTime;
                MigrationInProgress   = migrationInProgress;
                NumPromotions         = numPromotions;
                NumDemotions          = numDemotions;
                NumDropped            = numDropped;
                DisplayName           = displayName;
                Description           = description;
                StartedEarly          = startedEarly;
                EndedEarly            = endedEarly;
            }
        }

        public enum LeagueParticipantStatus
        {
            /// <summary>
            /// Never joined this league.
            /// </summary>
            NeverParticipated,
            /// <summary>
            /// A possibly former participant, but not currently.
            /// </summary>
            NotParticipant,
            /// <summary>
            /// Current participant to this league.
            /// </summary>
            Participant,
        }

        public class LeagueParticipantSpecificDetailsItem
        {
            public EntityId                                 LeagueId          { get; set; }
            public string                                   LeagueName        { get; set; }
            public ActivablesControllerBase.ScheduleStatus? ScheduleStatus    { get; set; }
            public LeagueParticipantStatus                  ParticipantStatus { get; set; }
            public bool                                     HasActiveSeason   { get; set; }
            public DivisionIndex                            DivisionIndex     { get; set; }
            public EntityId                                 DivisionId        { get; set; }
            public int                                      PlaceInDivision   { get; set; }
            public bool                                     IsError           { get; set; }
        }


        public class SeasonAdvanceRequest
        {
            public bool IsSeasonEnd { get; set; }
        }

        public class LeagueControllerRequestResponse
        {
            public bool   Success      { get; set; }
            public string ErrorMessage { get; set; }

            public LeagueControllerRequestResponse(bool success, string errorMessage)
            {
                Success      = success;
                ErrorMessage = errorMessage;
            }
        }

        public class LeagueSeasonScheduleResponse
        {
            public ActivablesControllerBase.SchedulePhaseInfo  CurrentPhase;
            public ActivablesControllerBase.SchedulePhaseInfo? NextPhase;
            public MetaTime                                    Visible;
            public MetaTime                                    Start;
            public MetaTime                                    End;
            public MetaCalendarPeriod                          Duration;
            public MetaCalendarPeriod                          Preview;
            public MetaCalendarPeriod                          EndingSoon;

            public LeagueSeasonScheduleResponse()
            {
                CurrentPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                    ActivablesControllerBase.ActivablePhase.Inactive,
                    default,
                    default);
            }

            public LeagueSeasonScheduleResponse(ActivablesControllerBase.SchedulePhaseInfo currentPhase, ActivablesControllerBase.SchedulePhaseInfo? nextPhase, MetaTime visible, MetaTime start, MetaTime end,
                MetaDuration duration,
                MetaDuration preview,
                MetaDuration endingSoon)
            {
                CurrentPhase = currentPhase;
                NextPhase    = nextPhase;
                Visible      = visible;
                Start        = start;
                End          = end;
                Duration = new MetaCalendarPeriod(
                    0,
                    0,
                    duration.ToTimeSpan().Days,
                    duration.ToTimeSpan().Hours,
                    duration.ToTimeSpan().Minutes,
                    duration.ToTimeSpan().Seconds);
                Preview = new MetaCalendarPeriod(
                    0,
                    0,
                    preview.ToTimeSpan().Days,
                    preview.ToTimeSpan().Hours,
                    preview.ToTimeSpan().Minutes,
                    preview.ToTimeSpan().Seconds);
                EndingSoon = new MetaCalendarPeriod(
                    0,
                    0,
                    endingSoon.ToTimeSpan().Days,
                    endingSoon.ToTimeSpan().Hours,
                    endingSoon.ToTimeSpan().Minutes,
                    endingSoon.ToTimeSpan().Seconds);
            }

            public LeagueSeasonScheduleResponse(ActivablesControllerBase.SchedulePhaseInfo currentPhase, ActivablesControllerBase.SchedulePhaseInfo? nextPhase, MetaTime visible, MetaTime start, MetaTime end,
                MetaCalendarPeriod duration,
                MetaCalendarPeriod preview,
                MetaCalendarPeriod endingSoon)
            {
                CurrentPhase = currentPhase;
                NextPhase    = nextPhase;
                Visible      = visible;
                Start        = start;
                End          = end;
                Duration     = duration;
                Preview      = preview;
                EndingSoon   = endingSoon;
            }

            public static LeagueSeasonScheduleResponse FromSchedule(MetaScheduleBase schedule)
            {
                if (schedule == null)
                    throw new ArgumentNullException(nameof(schedule));

                PlayerLocalTime time = new PlayerLocalTime(MetaTime.Now, MetaDuration.Zero);

                MetaScheduleOccasion? currentOccasion = schedule.TryGetCurrentOrNextEnabledOccasion(time);
                MetaRecurringCalendarSchedule recurringCalendarSchedule = schedule as MetaRecurringCalendarSchedule;

                if (!currentOccasion.HasValue)
                    return null;

                ActivablesControllerBase.ScheduleStatus scheduleStatus  = ActivablesControllerBase.GetScheduleStatusAt(schedule, time);
                if (recurringCalendarSchedule != null)
                {
                    return new LeagueSeasonScheduleResponse(
                        scheduleStatus.CurrentPhase,
                        scheduleStatus.NextPhase,
                        currentOccasion.HasValue ? currentOccasion.Value.VisibleRange.Start : scheduleStatus.RelevantEnabledRange.GetValueOrDefault().Start,
                        scheduleStatus.RelevantEnabledRange.GetValueOrDefault().Start,
                        scheduleStatus.RelevantEnabledRange.GetValueOrDefault().End,
                        recurringCalendarSchedule.Duration,
                        recurringCalendarSchedule.Preview,
                        recurringCalendarSchedule.EndingSoon);
                }
                else
                {
                    return new LeagueSeasonScheduleResponse(
                        scheduleStatus.CurrentPhase,
                        scheduleStatus.NextPhase,
                        currentOccasion.HasValue ? currentOccasion.Value.VisibleRange.Start : scheduleStatus.RelevantEnabledRange.GetValueOrDefault().Start,
                        scheduleStatus.RelevantEnabledRange.GetValueOrDefault().Start,
                        scheduleStatus.RelevantEnabledRange.GetValueOrDefault().End,
                        currentOccasion.HasValue ? currentOccasion.Value.EnabledRange.End - currentOccasion.Value.EnabledRange.Start : MetaDuration.Zero,
                        currentOccasion.HasValue ? currentOccasion.Value.EnabledRange.Start - currentOccasion.Value.VisibleRange.Start : MetaDuration.Zero,
                        currentOccasion.HasValue ? currentOccasion.Value.EnabledRange.End - currentOccasion.Value.EndingSoonStartsAt : MetaDuration.Zero);
                }
            }

            public static LeagueSeasonScheduleResponse NextFromSchedule(MetaScheduleBase schedule)
            {
                if (schedule == null)
                    throw new ArgumentNullException(nameof(schedule));

                PlayerLocalTime time = new PlayerLocalTime(MetaTime.Now, MetaDuration.Zero);

                MetaScheduleOccasion? currentOccasion = schedule.TryGetCurrentOrNextEnabledOccasion(time);

                MetaRecurringCalendarSchedule recurringCalendarSchedule = schedule as MetaRecurringCalendarSchedule;

                if (!currentOccasion.HasValue)
                    return null;

                if(currentOccasion.Value.VisibleRange.Start <= time.Time)
                    time = new PlayerLocalTime(currentOccasion.Value.VisibleRange.End + MetaDuration.FromMinutes(1), MetaDuration.Zero);

                MetaScheduleOccasion?                      nextOccasion = schedule.TryGetCurrentOrNextEnabledOccasion(time);

                if (!nextOccasion.HasValue || nextOccasion.Value.EnabledRange.Start == currentOccasion.Value.EnabledRange.Start)
                    return null;

                ActivablesControllerBase.SchedulePhaseInfo currentPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                    ActivablesControllerBase.ActivablePhase.Inactive,
                    MetaTime.Min(MetaTime.Now, currentOccasion.Value.EnabledRange.End), nextOccasion.Value.VisibleRange.Start);

                ActivablesControllerBase.SchedulePhaseInfo nextPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                    ActivablesControllerBase.ActivablePhase.Preview,
                    nextOccasion.Value.VisibleRange.Start, nextOccasion.Value.EnabledRange.Start);

                // If no preview time, next phase is active
                if (nextOccasion.Value.EnabledRange.Start == nextOccasion.Value.VisibleRange.Start)
                    nextPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.Active,
                        nextOccasion.Value.EnabledRange.Start, nextOccasion.Value.EndingSoonStartsAt);

                if (recurringCalendarSchedule != null)
                {
                    return new LeagueSeasonScheduleResponse(
                        currentPhase,
                        nextPhase,
                        nextOccasion.Value.VisibleRange.Start,
                        nextOccasion.Value.EnabledRange.Start,
                        nextOccasion.Value.EnabledRange.End,
                        recurringCalendarSchedule.Duration,
                        recurringCalendarSchedule.Preview,
                        recurringCalendarSchedule.EndingSoon);
                }
                else
                {
                    return new LeagueSeasonScheduleResponse(
                        currentPhase,
                        nextPhase,
                        nextOccasion.Value.VisibleRange.Start,
                        nextOccasion.Value.EnabledRange.Start,
                        nextOccasion.Value.EnabledRange.End,
                        currentOccasion.Value.EnabledRange.End - currentOccasion.Value.EnabledRange.Start,
                        currentOccasion.Value.EnabledRange.Start - currentOccasion.Value.VisibleRange.Start,
                        currentOccasion.Value.EnabledRange.End - currentOccasion.Value.EndingSoonStartsAt);
                }
            }

            public static LeagueSeasonScheduleResponse FromSeason(LeagueManagerCurrentSeasonState season, MetaScheduleBase schedule)
            {
                if (schedule == null)
                    throw new ArgumentNullException(nameof(schedule));

                MetaTime time = MetaTime.Now;

                ActivablesControllerBase.SchedulePhaseInfo  currentPhase;
                ActivablesControllerBase.SchedulePhaseInfo? nextPhase = null;

                MetaScheduleOccasion? currentOccasion = schedule.TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(time, MetaDuration.Zero));

                if (!currentOccasion.HasValue)
                    return null;

                LeagueSeasonScheduleResponse nextSchedule        = NextFromSchedule(schedule);
                MetaTime                     endestTimeOfAllTime = MetaTime.FromDateTime(DateTime.MaxValue);
                MetaDuration                 previewDuration     = currentOccasion.Value.EnabledRange.Start - currentOccasion.Value.VisibleRange.Start;
                MetaDuration                 endingSoonDuration  = currentOccasion.Value.EnabledRange.End - currentOccasion.Value.EndingSoonStartsAt;

                if (season.EndTime < time)
                {
                    currentPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.Inactive,
                        season.EndTime,
                        nextSchedule?.Visible ?? endestTimeOfAllTime);
                    if(nextSchedule != null)
                        nextPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                            ActivablesControllerBase.ActivablePhase.Preview,
                            nextSchedule.Visible,
                            nextSchedule.Start);
                }else if (season.EndingSoonStartsAt < time)
                {
                    currentPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.EndingSoon,
                        season.EndingSoonStartsAt,
                        season.EndTime);
                    nextPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.Inactive,
                        season.EndTime,
                        nextSchedule?.Visible ?? endestTimeOfAllTime);
                }else if (season.StartTime < time)
                {
                    currentPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.Active,
                        season.StartTime,
                        season.EndingSoonStartsAt);
                    nextPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.EndingSoon,
                        season.EndingSoonStartsAt,
                        season.EndTime);
                }else
                {
                    currentPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.Preview,
                        MetaTime.Min(time, season.StartTime - previewDuration),
                        season.StartTime);
                    nextPhase = new ActivablesControllerBase.SchedulePhaseInfo(
                        ActivablesControllerBase.ActivablePhase.Active,
                        season.StartTime,
                        season.EndingSoonStartsAt);
                }

                return new LeagueSeasonScheduleResponse(
                    currentPhase,
                    nextPhase,
                    season.StartTime - previewDuration,
                    season.StartTime,
                    season.EndTime,
                    season.EndTime - season.StartTime,
                    previewDuration,
                    endingSoonDuration);
            }
        }

        public LeaguesController(ILogger<LeaguesController> logger, IActorRef adminApi) : base(logger, adminApi) { }

        [HttpGet("leagues/{leagueId}")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public async Task<ActionResult<LeagueStateResponse>> GetLeague(string leagueId)
        {
            EntityId entityId = ParseEntityIdStr(leagueId, EntityKindCloudCore.LeagueManager);
            if (entityId.Value > 0)
                throw new MetaplayHttpException(404, "League not found!", $"League with id: {leagueId} not found.");

            InternalLeagueStateResponse response = await AskEntityAsync<InternalLeagueStateResponse>(entityId, InternalLeagueStateRequest.Instance);

            PlayerLocalTime time = new PlayerLocalTime(MetaTime.Now, MetaDuration.Zero);

            ActivablesControllerBase.ScheduleStatus? scheduleStatus              = null;
            LeagueSeasonScheduleResponse             currentOrNextSeasonSchedule = null;
            bool                                     isCurrentSeasonOutOfSync    = false;

            if (response.Schedule != null && response.LeagueManagerState.CurrentSeason != null)
            {
                scheduleStatus = ActivablesControllerBase.GetScheduleStatusAt(response.Schedule, time);

                LeagueSeasonScheduleResponse currentSeasonSchedule = LeagueSeasonScheduleResponse.FromSeason(response.LeagueManagerState.CurrentSeason, response.Schedule);
                LeagueSeasonScheduleResponse nextSeasonSchedule = LeagueSeasonScheduleResponse.NextFromSchedule(response.Schedule);

                if (currentSeasonSchedule == null || currentSeasonSchedule.CurrentPhase.Phase == ActivablesControllerBase.ActivablePhase.Inactive)
                    currentOrNextSeasonSchedule = nextSeasonSchedule;
                else
                    currentOrNextSeasonSchedule = currentSeasonSchedule;

                if (currentSeasonSchedule != null &&
                    (currentSeasonSchedule.Start != scheduleStatus.Value.RelevantEnabledRange.GetValueOrDefault().Start ||
                    currentSeasonSchedule.End != scheduleStatus.Value.RelevantEnabledRange.GetValueOrDefault().End))
                    isCurrentSeasonOutOfSync = true;
            }

            int participants = 0;

            if (response.LeagueManagerState.HistoricSeasons != null && response.LeagueManagerState.HistoricSeasons.Count > 0)
                participants += response.LeagueManagerState.HistoricSeasons[^1].TotalParticipants;

            if (response.LeagueManagerState.CurrentSeason != null)
                participants += response.LeagueManagerState.CurrentSeason.NewParticipants;

            return new ActionResult<LeagueStateResponse>(
                new LeagueStateResponse(
                    response.LeagueManagerState,
                    response.Schedule,
                    scheduleStatus,
                    participants,
                    response.Enabled,
                    response.LeagueDetails,
                    isCurrentSeasonOutOfSync,
                    currentOrNextSeasonSchedule,
                    response.MigrationProgress));
        }

        [HttpGet("leagues/{leagueId}/{seasonId}")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public async Task<ActionResult<LeagueSeasonStateResponse>> GetLeagueSeason(string leagueId, string seasonId)
        {
            // Parse and validate the inputs.
            EntityId  entityId = ParseEntityIdStr(leagueId, EntityKindCloudCore.LeagueManager);
            if (entityId.Value > 0)
                throw new MetaplayHttpException(404, "League not found!", $"League with id: {leagueId} not found.");

            bool seasonIdIsActiveSeason = seasonId == "$active";
            int  seasonIdAsNumber       = 0;
            if (!seasonIdIsActiveSeason && !int.TryParse(seasonId, out seasonIdAsNumber))
                throw new MetaplayHttpException(400, $"{seasonId} not found.", $"{seasonId} is not a valid Season ID. Must be a number.");

            InternalLeagueStateResponse response = await AskEntityAsync<InternalLeagueStateResponse>(entityId, InternalLeagueStateRequest.Instance);

            PlayerLocalTime time = new PlayerLocalTime(MetaTime.Now, MetaDuration.Zero);

            ActivablesControllerBase.ScheduleStatus? scheduleStatus = null;
            if (response.Schedule != null)
                scheduleStatus = ActivablesControllerBase.GetScheduleStatusAt(response.Schedule, time);

            LeagueManagerActorStateBase managerState = response.LeagueManagerState;

            if (managerState?.CurrentSeason != null && (seasonIdIsActiveSeason || seasonIdAsNumber == managerState.CurrentSeason.SeasonId))
            {
                int participants = 0;
                if (response.LeagueManagerState.HistoricSeasons != null && response.LeagueManagerState.HistoricSeasons.Count > 0)
                    participants += response.LeagueManagerState.HistoricSeasons[^1].TotalParticipants;
                if (response.LeagueManagerState.CurrentSeason != null)
                    participants += response.LeagueManagerState.CurrentSeason.NewParticipants;

                LeagueManagerCurrentSeasonState seasonState = managerState.CurrentSeason;

                List<LeagueSeasonStateResponse.LeagueSeasonRankStateResponse> ranks = new List<LeagueSeasonStateResponse.LeagueSeasonRankStateResponse>();

                for (int rank = 0; rank < seasonState.Ranks.Count; rank++)
                {
                    ranks.Add(
                        new LeagueSeasonStateResponse.LeagueSeasonRankStateResponse(
                            seasonState.Ranks[rank].NumParticipants,
                            response.CurrentSeasonRankDetails?[rank].DisplayName,
                            response.CurrentSeasonRankDetails?[rank].Description,
                            0,
                            0,
                            0,
                            seasonState.Ranks[rank].NumDivisions));
                }

                if (MetaTime.Now > seasonState.EndTime)
                {
                    return new ActionResult<LeagueSeasonStateResponse>(
                        new LeagueSeasonStateResponse(
                            false,
                            null,
                            participants,
                            seasonState.NewParticipants,
                            ranks,
                            seasonState.StartTime,
                            seasonState.EndTime,
                            !seasonState.MigrationComplete,
                            0,
                            0,
                            0,
                            response.CurrentSeasonDetails.SeasonDisplayName,
                            response.CurrentSeasonDetails.SeasonDescription,
                            seasonState.StartedEarly,
                            seasonState.EndedEarly));
                }
                else
                {
                    return new ActionResult<LeagueSeasonStateResponse>(
                        new LeagueSeasonStateResponse(
                            true,
                            scheduleStatus,
                            participants,
                            seasonState.NewParticipants,
                            ranks,
                            seasonState.StartTime,
                            seasonState.EndTime,
                            !seasonState.MigrationComplete,
                            0,
                            0,
                            0,
                            response.CurrentSeasonDetails.SeasonDisplayName,
                            response.CurrentSeasonDetails.SeasonDescription,
                            seasonState.StartedEarly,
                            seasonState.EndedEarly));
                }
            }
            else
            {
                LeagueManagerHistoricSeasonState seasonState = managerState.HistoricSeasons.FirstOrDefault(s => seasonIdAsNumber == s.SeasonId);

                if (seasonState != null)
                {
                    List<LeagueSeasonStateResponse.LeagueSeasonRankStateResponse> ranks = new List<LeagueSeasonStateResponse.LeagueSeasonRankStateResponse>();

                    int totalPromoted = 0;
                    int totalDemoted  = 0;

                    for (int rank = 0; rank < seasonState.Ranks.Count; rank++)
                    {
                        LeagueManagerHistoricSeasonRankState             rankState = seasonState.Ranks[rank];

                        ranks.Add(
                            new LeagueSeasonStateResponse.LeagueSeasonRankStateResponse(
                                rankState.NumParticipants,
                                rankState.RankDetails.DisplayName,
                                rankState.RankDetails.Description,
                                rankState.NumPromotions,
                                rankState.NumDemotions,
                                rankState.NumDropped,
                                rankState.NumDivisions));

                        totalPromoted += rankState.NumPromotions;
                        totalDemoted  += rankState.NumDemotions;
                    }

                    return new ActionResult<LeagueSeasonStateResponse>(
                        new LeagueSeasonStateResponse(
                            false,
                            null,
                            seasonState.TotalParticipants,
                            seasonState.NewParticipants,
                            ranks,
                            seasonState.StartTime,
                            seasonState.EndTime,
                            false,
                            totalPromoted,
                            totalDemoted,
                            seasonState.DroppedParticipants,
                            seasonState.SeasonDetails.SeasonDisplayName,
                            seasonState.SeasonDetails.SeasonDescription,
                            seasonState.StartedEarly,
                            seasonState.EndedEarly));
                }
            }

            throw new MetaplayHttpException(404, "Season not found!", $"Season with id: {seasonId} not found.");
        }

        [HttpGet("leagues")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public async Task<ActionResult<IEnumerable<LeagueEntityState>>> GetAllLeagues()
        {
            EntityId entityId = EntityId.Create(EntityKindCloudCore.LeagueManager, 0);

            InternalLeagueStateResponse response = await AskEntityAsync<InternalLeagueStateResponse>(entityId, InternalLeagueStateRequest.Instance);

            PlayerLocalTime time = new PlayerLocalTime(MetaTime.Now, MetaDuration.Zero);

            ActivablesControllerBase.ScheduleStatus? scheduleStatus = null;

            if (response.Schedule != null)
                scheduleStatus = ActivablesControllerBase.GetScheduleStatusAt(response.Schedule, time);

            int participants = 0;

            if (response.LeagueManagerState.HistoricSeasons != null && response.LeagueManagerState.HistoricSeasons.Count > 0)
                participants += response.LeagueManagerState.HistoricSeasons[^1].TotalParticipants;

            if (response.LeagueManagerState.CurrentSeason != null)
                participants += response.LeagueManagerState.CurrentSeason.NewParticipants;

            return new ActionResult<IEnumerable<LeagueEntityState>>(
                new LeagueEntityState[]
                {
                    new LeagueEntityState(
                        entityId,
                        response.Enabled,
                        participants,
                        scheduleStatus,
                        response.LeagueDetails.LeagueDisplayName,
                        response.LeagueDetails.LeagueDescription),
                });
        }


        [HttpGet("leagues/participant/{entityIdString}")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public async Task<ActionResult<IEnumerable<LeagueParticipantSpecificDetailsItem>>> GetDivisionsForParticipant(string entityIdString)
        {
            EntityId leagueId = EntityId.Create(EntityKindCloudCore.LeagueManager, 0);

            InternalLeagueStateResponse response = await AskEntityAsync<InternalLeagueStateResponse>(leagueId, InternalLeagueStateRequest.Instance);

            PlayerLocalTime time = new PlayerLocalTime(MetaTime.Now, MetaDuration.Zero);

            ActivablesControllerBase.ScheduleStatus? scheduleStatus = null;

            if (response.Schedule != null)
                scheduleStatus = ActivablesControllerBase.GetScheduleStatusAt(response.Schedule, time);

            EntityId participantId = EntityId.ParseFromString(entityIdString);

            PersistedParticipantDivisionAssociation association = await MetaDatabase.Get().TryGetAsync<PersistedParticipantDivisionAssociation>(participantId.ToString());

            LeagueParticipantStatus initialStatus = LeagueParticipantStatus.NeverParticipated;
            if (participantId.IsOfKind(EntityKindCore.Player))
            {
                PlayerDetails playerDetails = await GetPlayerDetailsAsync(participantId.ToString());

                if (playerDetails.Model?.PlayerSubClientStates?.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase playerDivisionStateBase) ?? false)
                {
                    IDivisionClientState playerDivisionState = playerDivisionStateBase as IDivisionClientState;
                    if (playerDivisionState?.HistoricalDivisions?.Any(historical => (ulong)historical.DivisionIndex.League == leagueId.Value) ?? false)
                        initialStatus = LeagueParticipantStatus.NotParticipant;
                }
            }
            // \todo[nomi]: Handle guild participants

            LeagueParticipantSpecificDetailsItem result = new LeagueParticipantSpecificDetailsItem();
            result.LeagueId          = leagueId;
            result.LeagueName        = response.LeagueDetails.LeagueDisplayName;
            result.ScheduleStatus    = scheduleStatus;
            result.ParticipantStatus = initialStatus;
            result.HasActiveSeason   = response.LeagueManagerState.CurrentSeason != null && (response.LeagueManagerState.CurrentSeason.EndTime > MetaTime.Now);

            if (association != null && association.LeagueId == leagueId.ToString())
            {
                EntityId divisionId = EntityId.ParseFromString(association.DivisionId);

                if (divisionId.IsValid)
                {
                    try
                    {
                        IDivisionModel divisionModel = await GetEntityStateAsync(divisionId) as IDivisionModel;
                        if (divisionModel != null)
                        {
                            divisionModel.RefreshScores();

                            if (divisionModel.TryGetParticipant(divisionModel.GetParticipantIndexById(participantId), out IDivisionParticipantState state))
                            {
                                result.ParticipantStatus = LeagueParticipantStatus.Participant;
                                result.DivisionId        = divisionId;
                                result.DivisionIndex     = DivisionIndex.FromEntityId(divisionId);
                                result.PlaceInDivision   = state.SortOrderIndex + 1;
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to get entity state for participant's ({participantId}) division {divisionId}.");

                        result.ParticipantStatus = LeagueParticipantStatus.Participant;
                        result.DivisionId        = divisionId;
                        result.DivisionIndex     = DivisionIndex.FromEntityId(divisionId);
                        result.IsError           = true;
                    }
                }
            }

            return new ActionResult<IEnumerable<LeagueParticipantSpecificDetailsItem>>(
                new[]
                {
                    result,
                });
        }

        [HttpPost("leagues/{leagueId}/advance")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesPhaseDebug)]
        public async Task<ActionResult> AdvanceLeagueSeason(string leagueId)
        {
            EntityId             entityId = EntityId.ParseFromString(leagueId);
            SeasonAdvanceRequest request  = await ParseBodyAsync<SeasonAdvanceRequest>();

            try
            {
                await AskEntityAsync<EntityAskOk>(
                    entityId,
                    new InternalLeagueDebugAdvanceSeasonRequest(request.IsSeasonEnd));

                await WriteAuditLogEventAsync(new LeagueEventBuilder(entityId, new LeagueSeasonDebugAdvanced(request.IsSeasonEnd)));
            }
            catch (InvalidEntityAsk refusal)
            {
                return Ok(new LeagueControllerRequestResponse(false, refusal.Message));
            }

            return Ok(new LeagueControllerRequestResponse(true, null));
        }

        [HttpPost("leagues/{leagueIdString}/participant/{participantIdString}/remove")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesEditParticipants)]
        public async Task<ActionResult<IEnumerable<LeagueParticipantSpecificDetailsItem>>> RemoveParticipantFromLeague(string leagueIdString, string participantIdString)
        {
            EntityId leagueId = EntityId.ParseFromString(leagueIdString);
            EntityId participantId = EntityId.ParseFromString(participantIdString);

            try
            {
                await AskEntityAsync<EntityAskOk>(
                    leagueId,
                    new InternalLeagueLeaveRequest(participantId, true));

                // Write audit events
                EventBuilder leagueEvent = new LeagueEventBuilder(leagueId, new LeagueParticipantRemoved(participantId));
                if (participantId.IsOfKind(EntityKindCore.Player))
                {
                    await WriteRelatedAuditLogEventsAsync(new List<EventBuilder>
                    {
                        new PlayerEventBuilder(participantId, new PlayerEventDebugRemovedFromLeague(leagueId)),
                        leagueEvent
                    });
                }
                else
                {
                    // TODO: How do we handle non-player participants?
                    await WriteAuditLogEventAsync(leagueEvent);
                }
            }
            catch (EntityAskRefusal refusal)
            {
                return Ok(new LeagueControllerRequestResponse(false, refusal.Message));
            }

            return Ok(new LeagueControllerRequestResponse(true, null));
        }


        [HttpPost("leagues/{leagueIdString}/participant/{participantIdString}/add/{divisionIdString}")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesEditParticipants)]
        public async Task<ActionResult<IEnumerable<LeagueParticipantSpecificDetailsItem>>> AddParticipantToLeague(string leagueIdString, string participantIdString, string divisionIdString)
        {
            EntityId leagueId      = EntityId.ParseFromString(leagueIdString);
            EntityId participantId = EntityId.ParseFromString(participantIdString);
            EntityId divisionId    = EntityId.ParseFromString(divisionIdString);

            try
            {
                InternalLeagueDebugAddResponse response = await AskEntityAsync<InternalLeagueDebugAddResponse>(
                    leagueId,
                    new InternalLeagueDebugAddRequest(participantId, divisionId));

                // Write audit events
                await AddParticipantToLeagueAuditEvents(response, participantId, divisionId, leagueId);
            }
            catch (EntityAskRefusal refusal)
            {
                return Ok(new LeagueControllerRequestResponse(false, refusal.Message));
            }

            return Ok(new LeagueControllerRequestResponse(true, null));
        }

        [HttpPost("leagues/{leagueIdString}/participant/{participantIdString}/addRank/{rankIndex}")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesEditParticipants)]
        public async Task<ActionResult<IEnumerable<LeagueParticipantSpecificDetailsItem>>> AddParticipantToLeagueRank(string leagueIdString, string participantIdString, int rankIndex)
        {
            EntityId leagueId      = EntityId.ParseFromString(leagueIdString);
            EntityId participantId = EntityId.ParseFromString(participantIdString);

            try
            {
                InternalLeagueJoinResponse response = await AskEntityAsync<InternalLeagueJoinResponse>(
                    leagueId,
                    new InternalLeagueDebugJoinRankRequest(participantId, rankIndex));

                if (!response.Success)
                    return Ok(new LeagueControllerRequestResponse(false, response.RefuseReason.ToString()));

                await WriteAuditLogEventAsync(new LeagueEventBuilder(leagueId, new LeagueParticipantAdded(participantId, response.DivisionToJoin.ToEntityId())));
            }
            catch (EntityAskRefusal refusal)
            {
                return Ok(new LeagueControllerRequestResponse(false, refusal.Message));
            }

            return Ok(new LeagueControllerRequestResponse(true, null));
        }

        async Task AddParticipantToLeagueAuditEvents(InternalLeagueDebugAddResponse response, EntityId participantId, EntityId divisionId, EntityId leagueId)
        {
            LeagueEventPayloadBase leaguePayload = response.WasAlreadyInDivision ? new LeagueParticipantMoved(participantId, divisionId) : new LeagueParticipantAdded(participantId, divisionId);
            EventBuilder           leagueEvent   = new LeagueEventBuilder(leagueId, leaguePayload);

            if (participantId.IsOfKind(EntityKindCore.Player))
            {
                PlayerEventPayloadBase playerPayload = response.WasAlreadyInDivision ? new PlayerEventDebugMovedToLeague(leagueId, divisionId) : new PlayerEventDebugAddedToLeague(leagueId, divisionId);
                await WriteRelatedAuditLogEventsAsync(
                    new List<EventBuilder>
                    {
                        new PlayerEventBuilder(participantId, playerPayload),
                        leagueEvent
                    });
            }
            else
            {
                // TODO: How do we handle non-player participants?
                await WriteAuditLogEventAsync(leagueEvent);
            }
        }
    }
}
