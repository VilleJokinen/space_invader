// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Server.Guild.InternalMessages;
using Metaplay.Server.GuildDiscovery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for routes that display and/or search for multiple guilds.
    /// </summary>
    [GuildsEnabledCondition]
    public class GuildRecommenderInspectorController : GameAdminApiController
    {
        public GuildRecommenderInspectorController(ILogger<GuildRecommenderInspectorController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        public struct PoolDeclaration
        {
            public string   PoolId      { get; set; }
            public int      EntryCount  { get; set; }

            public PoolDeclaration(string poolId, int entryCount)
            {
                PoolId = poolId;
                EntryCount = entryCount;
            }
        }

        /// <summary>
        /// API endpoint to return basic information of all recommendation pools in the
        /// recommendation system.
        /// Usage:  GET /api/guildrecommender/pools
        /// Test:   curl http://localhost:5550/api/guildrecommender/pools/
        /// </summary>
        [HttpGet("guildrecommender/pools")]
        [RequirePermission(MetaplayPermissions.ApiGuildsInspectRecommender)]
        public async Task<ActionResult<IEnumerable<PoolDeclaration>>> GetPools()
        {
            InternalGuildRecommenderInspectPoolsResponse response = await AskEntityAsync<InternalGuildRecommenderInspectPoolsResponse>(GuildRecommenderActorBase.EntityId, new InternalGuildRecommenderInspectPoolsRequest());
            List<PoolDeclaration> results = new List<PoolDeclaration>();
            foreach (InternalGuildRecommenderInspectPoolsResponse.PoolInfo pool in response.PoolInfos)
                results.Add(new PoolDeclaration(pool.PoolId, pool.Count));
            return results;
        }

        public struct PoolEntry
        {
            public GuildDiscoveryInfoBase           PublicInfo      { get; set; }
            public GuildDiscoveryServerOnlyInfoBase ServerInfo      { get; set; }
            public bool                             PassesFilter    { get; set; }
            public MetaTime                         LastRefreshedAt { get; set; }

            public PoolEntry(GuildDiscoveryInfoBase publicInfo, GuildDiscoveryServerOnlyInfoBase serverInfo, bool passesFilter, MetaTime lastRefreshedAt)
            {
                PublicInfo = publicInfo;
                ServerInfo = serverInfo;
                PassesFilter = passesFilter;
                LastRefreshedAt = lastRefreshedAt;
            }
        }

        /// <summary>
        /// API endpoint to return (partial) contents of a singled recommendation pool.
        /// Usage:  GET /api/guildrecommender/pools/{POOLID}?count=LIMIT
        /// Test:   curl http://localhost:5550/api/guildrecommender/pools/SmallGuilds?limit=10
        /// </summary>
        [HttpGet("guildrecommender/pools/{poolIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiGuildsInspectRecommender)]
        public async Task<ActionResult<IEnumerable<PoolEntry>>> InspectPool(string poolIdStr, [FromQuery] int count = 100)
        {
            InternalGuildRecommenderInspectPoolResponse response = await AskEntityAsync<InternalGuildRecommenderInspectPoolResponse>(GuildRecommenderActorBase.EntityId, new InternalGuildRecommenderInspectPoolRequest(poolIdStr, maxCount: count));
            List<PoolEntry> results = new List<PoolEntry>();
            foreach (InternalGuildRecommenderInspectPoolResponse.PoolEntry pool in response.Entries)
                results.Add(new PoolEntry(pool.PublicInfo, pool.ServerInfo, pool.PassesFilter, pool.LastRefreshedAt));
            return results;
        }

        public struct GuildStatus
        {
            public struct PoolStatus
            {
                public bool IncludedInPool;
                public bool PoolDataPassesFilter;
                public bool FreshDataPassesFilter;

                public PoolStatus(bool includedInPool, bool poolDataPassesFilter, bool freshDataPassesFilter)
                {
                    IncludedInPool = includedInPool;
                    PoolDataPassesFilter = poolDataPassesFilter;
                    FreshDataPassesFilter = freshDataPassesFilter;
                }
            }

            public OrderedDictionary<string, PoolStatus> Pools;
        }

        [HttpGet("guildrecommender/guilds/{guildIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiGuildsInspectRecommender)]
        public async Task<ActionResult<GuildStatus>> InspectGuild(string guildIdStr)
        {
            IGuildModelBase guildModel = (IGuildModelBase)await GetEntityStateAsync(ParseEntityIdStr(guildIdStr, EntityKindCore.Guild));
            InternalGuildDiscoveryGuildDataResponse guildInfo = await AskEntityAsync<InternalGuildDiscoveryGuildDataResponse>(guildModel.GuildId, InternalGuildDiscoveryGuildDataRequest.Instance);

            if (!guildInfo.IsSuccess())
            {
                if (guildInfo.IsErrorTemporary)
                    throw new Exceptions.MetaplayHttpException(500, "Guild inspection error", "temporary refusal when getting guild discovery information" );
                else
                    throw new Exceptions.MetaplayHttpException(500, "Guild inspection error", "permanent refusal when getting guild discovery information" );
            }

            InternalGuildRecommenderTestGuildResponse recommenderStatus = await AskEntityAsync<InternalGuildRecommenderTestGuildResponse>(GuildRecommenderActorBase.EntityId, new InternalGuildRecommenderTestGuildRequest(guildInfo.PublicDiscoveryInfo, guildInfo.ServerOnlyDiscoveryInfo));
            OrderedDictionary<string, GuildStatus.PoolStatus> pools = new OrderedDictionary<string, GuildStatus.PoolStatus>();
            foreach ((string poolId, InternalGuildRecommenderTestGuildResponse.PoolStatus poolStatus) in recommenderStatus.Pools)
            {
                pools[poolId] = new GuildStatus.PoolStatus(poolStatus.IncludedInPool, poolStatus.PoolDataPassesFilter, poolStatus.FreshDataPassesFilter);
            }

            return new GuildStatus() { Pools = pools };
        }
    }
}

#endif
