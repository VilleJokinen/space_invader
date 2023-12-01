// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Server.Database;
using Metaplay.Server.League;
using Metaplay.Server.League.InternalMessages;
using Metaplay.Server.MultiplayerEntity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for basic routes for division entities.
    /// </summary>
    [LeaguesEnabledCondition]
    public class DivisionsController : MultiplayerEntityControllerBase
    {
        public DivisionsController(ILogger<DivisionsController> logger, IActorRef adminApi) : base(logger, adminApi) { }

        protected override EntityKind EntityKind => EntityKindCore.Division;

        class DivisionEntityDetailsItem : EntityDetailsItem
        {
            public DivisionSeasonPhase SeasonPhase;
            public string              RankName;
            public int                 DesiredParticipants;
            public int                 MaxParticipants;

            public DivisionEntityDetailsItem(string id, IModel model, int persistedSize, DivisionSeasonPhase seasonPhase,
                string rankName, int desiredParticipants, int maxParticipants) : base(id, model, persistedSize)
            {
                SeasonPhase         = seasonPhase;
                RankName            = rankName;
                DesiredParticipants = desiredParticipants;
                MaxParticipants     = maxParticipants;
            }
        }

        public class DivisionEntityListItem : EntityListItem
        {
            public DivisionIndex Index;
        }

        [MetaSerializableDerived(104)]
        [LeaguesEnabledCondition]
        public class ActiveDivisionEntityInfo : IActiveEntityInfo
        {
            [MetaMember(1)] public EntityId      EntityId      { get; private set; }
            [MetaMember(2)] public string        DisplayName   { get; private set; }
            [MetaMember(3)] public MetaTime      CreatedAt     { get; private set; }
            [MetaMember(4)] public MetaTime      ActivityAt    { get; private set; }
            [MetaMember(5)] public DivisionIndex DivisionIndex { get; private set; }

            [MetaMember(6)] public int Participants    { get; set; }
            [MetaMember(7)] public int MaxParticipants { get; set; }

            public ActiveDivisionEntityInfo(EntityId entityId, string displayName, MetaTime createdAt, MetaTime activityAt,
                DivisionIndex divisionIndex, int participants, int maxParticipants)
            {
                EntityId         = entityId;
                DisplayName      = displayName;
                CreatedAt        = createdAt;
                ActivityAt       = activityAt;
                DivisionIndex    = divisionIndex;
                Participants     = participants;
                MaxParticipants  = maxParticipants;
            }

            ActiveDivisionEntityInfo() { }
        }

        [HttpGet("divisions/{entityIdString}")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public override async Task<ActionResult<EntityDetailsItem>> GetEntityDetails(string entityIdString)
        {
            ActionResult<EntityDetailsItem> detailsResult   = await DefaultGetEntityDetailsAsync(entityIdString);
            EntityDetailsItem               details         = detailsResult.Value;
            IDivisionModel                  divisionModel   = (IDivisionModel)details.Model;

            divisionModel?.RefreshScores();
            DivisionSeasonPhase currentPhase = divisionModel.ComputeSeasonPhaseAt(ModelUtil.TimeAtTick(divisionModel.CurrentTick, divisionModel.TimeAtFirstTick, divisionModel.TicksPerSecond));

            InternalLeagueStateResponse leagueState = await AskEntityAsync<InternalLeagueStateResponse>(EntityId.Create(EntityKindCloudCore.LeagueManager, 0), InternalLeagueStateRequest.Instance);

            DivisionIndex divisionIndex = divisionModel.DivisionIndex;
            string        rankName      = null;

            if (leagueState.LeagueManagerState.CurrentSeason != null &&
                leagueState.CurrentSeasonRankDetails != null &&
                leagueState.LeagueManagerState.CurrentSeason.SeasonId == divisionIndex.Season)
                rankName = leagueState.CurrentSeasonRankDetails[divisionIndex.Rank].DisplayName;
            else
                rankName = leagueState.LeagueManagerState.HistoricSeasons.FirstOrDefault(s => s.SeasonId == divisionIndex.Season)?
                    .Ranks[divisionIndex.Rank].RankDetails.DisplayName ?? FormattableString.Invariant($"Rank {divisionIndex.Rank}");

            return new ActionResult<EntityDetailsItem>(new DivisionEntityDetailsItem(
                id: details.Id,
                model: divisionModel,
                persistedSize: details.PersistedSize,
                seasonPhase: currentPhase,
                rankName: rankName,
                RuntimeOptionsRegistry.Instance.GetCurrent<LeagueManagerOptions>().DivisionDesiredParticipantCount,
                RuntimeOptionsRegistry.Instance.GetCurrent<LeagueManagerOptions>().DivisionMaxParticipantCount));
        }

        [HttpGet("divisions/season")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public async Task<ActionResult<IEnumerable<EntityListItem>>> ListSeasonEntities([FromQuery] int season = -1, [FromQuery] int rank = -1, [FromQuery] int count = 100, [FromQuery] int page = 0)
        {
            EntityId leagueManagerId = EntityId.Create(EntityKindCloudCore.LeagueManager, 0);

            InternalLeagueStateResponse response = await AskEntityAsync<InternalLeagueStateResponse>(leagueManagerId, InternalLeagueStateRequest.Instance);

            LeagueManagerActorStateBase state = response.LeagueManagerState;

            IEnumerable<DivisionIndex> divisions = EnumerateDivisionsOfASeason(state, season, rank);

            return new ActionResult<IEnumerable<EntityListItem>>(divisions.Skip(count * page).Take(count).Select(
                index => new DivisionEntityListItem()
                {
                    CreatedAt   = state.CurrentSeason.StartTime.ToDateTime(),
                    DisplayName = FormattableString.Invariant($"Season {index.Season} rank {index.Rank} division {index.Division}"),
                    EntityId    = index.ToEntityId().ToString(),
                    Index       = index,
                }));
        }

        public static IEnumerable<DivisionIndex> EnumerateDivisionsOfASeason(LeagueManagerActorStateBase state, int season, int rank = -1)
        {
            if (state.CurrentSeason != null && (season == -1 || season == state.CurrentSeason.SeasonId))
            {
                if (rank >= 0)
                {
                    if (rank < state.CurrentSeason.Ranks.Count)
                    {
                        LeagueManagerCurrentSeasonRankState rankState = state.CurrentSeason.Ranks[rank];
                        for (int i = 0; i < rankState.NumDivisions; i++)
                            yield return new DivisionIndex(0, state.CurrentSeason.SeasonId, rank, i);
                    }
                }
                else
                {
                    foreach (LeagueManagerCurrentSeasonRankState rankState in ((IEnumerable<LeagueManagerCurrentSeasonRankState>)state.CurrentSeason.Ranks).Reverse())
                    {
                        int rankIdx = state.CurrentSeason.Ranks.IndexOf(rankState);
                        for (int i = 0; i < rankState.NumDivisions; i++)
                            yield return new DivisionIndex(0, state.CurrentSeason.SeasonId, rankIdx, i);
                    }
                }
            }
            else
            {
                LeagueManagerHistoricSeasonState historyState = state.HistoricSeasons.FirstOrDefault(x => x.SeasonId == season);

                if (historyState == null)
                    yield break;

                if (rank >= 0)
                {
                    if (rank < historyState.Ranks.Count)
                    {
                        LeagueManagerHistoricSeasonRankState rankState = historyState.Ranks[rank];
                        for (int i = 0; i < rankState.NumDivisions; i++)
                            yield return new DivisionIndex(0, historyState.SeasonId, rank, i);
                    }
                }
                else
                {
                    foreach (LeagueManagerHistoricSeasonRankState rankState in ((IEnumerable<LeagueManagerHistoricSeasonRankState>)historyState.Ranks).Reverse())
                    {
                        int rankIdx = historyState.Ranks.IndexOf(rankState);
                        for (int i = 0; i < rankState.NumDivisions; i++)
                            yield return new DivisionIndex(0, historyState.SeasonId, rankIdx, i);
                    }
                }
            }
        }

        [HttpGet("divisions")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public override async Task<ActionResult<IEnumerable<EntityListItem>>> ListEntities([FromQuery] string query = "", [FromQuery] int count = 10)
            => await DefaultListEntitiesAsync(query, count);

        [HttpGet("divisions/activeDivisions")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public override Task<ActionResult<IEnumerable<IActiveEntityInfo>>> GetActiveEntities() => DefaultGetActiveEntitiesAsync();


        [HttpGet("divisions/active/{leagueIdString}/{seasonId:int}/")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public async Task<ActionResult<IEnumerable<ActiveDivisionEntityInfo>>> GetActiveDivisionsOfLeagueSeason(string leagueIdString, int seasonId)
        {
            EntityId leagueId = ParseEntityIdStr(leagueIdString, EntityKindCloudCore.LeagueManager);
            int leagueNumber = (int)leagueId.Value;

            InternalLeagueStateResponse leagueResponse  = await AskEntityAsync<InternalLeagueStateResponse>(leagueId, InternalLeagueStateRequest.Instance);
            ActiveEntitiesResponse      response        = await AskEntityAsync<ActiveEntitiesResponse>(StatsCollectorManager.EntityId, new ActiveEntitiesRequest(EntityKind));
            int                         maxParticipants = RuntimeOptionsRegistry.Instance.GetCurrent<LeagueManagerOptions>().DivisionMaxParticipantCount;

            IEnumerable<ActiveDivisionEntityInfo> seasonActives = response.ActiveEntities.Select(
                entity =>
                {
                    DivisionIndex index = DivisionIndex.FromEntityId(entity.EntityId);
                    return new ActiveDivisionEntityInfo(
                        entityId: entity.EntityId,
                        displayName: leagueResponse?.CurrentSeasonRankDetails != null ?
                            FormattableString.Invariant($"Division {index.Division} of {leagueResponse.CurrentSeasonRankDetails[index.Rank].DisplayName}") :
                            FormattableString.Invariant($"Division {index.Division} of {index.Rank}"),
                        createdAt: entity.CreatedAt,
                        activityAt: entity.ActivityAt,
                        divisionIndex: index,
                        participants: 0,
                        maxParticipants: maxParticipants);
                }).Where(active => active.DivisionIndex.League == leagueNumber && active.DivisionIndex.Season == seasonId).ToList();

            foreach (ActiveDivisionEntityInfo entityInfo in seasonActives)
            {
                ActionResult<EntityDetailsItem> detailsResult = await DefaultGetEntityDetailsAsync(entityInfo.EntityId.ToString());
                EntityDetailsItem               details       = detailsResult.Value;
                IDivisionModel                  divisionModel = (IDivisionModel)details.Model;

                entityInfo.Participants = divisionModel.EnumerateParticipants().Count();
            }

            return new ActionResult<IEnumerable<ActiveDivisionEntityInfo>>(seasonActives);
        }


        [HttpGet("divisions/id/{leagueIdString}/{seasonId:int}/{rank:int}/{division:int}")]
        [RequirePermission(MetaplayPermissions.ApiLeaguesView)]
        public ActionResult<string> GetDivisionIdOfDivision(string leagueIdString, int seasonId, int rank, int division)
        {
            EntityId leagueId     = ParseEntityIdStr(leagueIdString, EntityKindCloudCore.LeagueManager);
            int      leagueNumber = (int)leagueId.Value;

            return new ActionResult<string>(new DivisionIndex(leagueNumber, seasonId, rank, division).ToEntityId().ToString());
        }
    }
}
