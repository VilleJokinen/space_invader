// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Server.GuildDiscovery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for requesting guild search in the context of a certain player.
    /// </summary>
    [GuildsEnabledCondition]
    public class PlayerGuildSearchController : GameAdminApiController
    {
        public PlayerGuildSearchController(ILogger<PlayerGuildSearchController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// HTTP response for guild searches
        /// </summary>
        public class GuildSearchReturn
        {
            public bool IsError;
            public List<GuildDiscoveryInfoBase> GuildInfos;
            public GuildSearchReturn(bool isError, List<GuildDiscoveryInfoBase> guildInfos)
            {
                IsError = isError;
                GuildInfos = guildInfos;
            }
        }


        /// <summary>
        /// API endpoint for guild search
        /// Usage:  POST /api/players/{PLAYERID}/guildSearch
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/guildSearch -X POST -H "Content-Type: application/json" -d '{"SearchString":"sometext"}'
        /// </summary>
        [HttpPost("players/{playerIdStr}/guildSearch")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersGuildTools)]
        public async Task<ActionResult<GuildSearchReturn>> DoGuildSearch(string playerIdStr)
        {
            // Parse parameters
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);
            Type paramsType = IntegrationRegistry.Create<GuildSearchParamsBase>().GetType();
            GuildSearchParamsBase requestParams = await ParseBodyAsync<GuildSearchParamsBase>(paramsType);

            // Get context from player
            InternalGuildDiscoveryPlayerContextResponse contextResponse = await AskEntityAsync<InternalGuildDiscoveryPlayerContextResponse>(playerId, InternalGuildDiscoveryPlayerContextRequest.Instance);
            GuildDiscoveryPlayerContextBase context = contextResponse.Result;

            // Perform the search
            InternalGuildSearchResponse response = await AskEntityAsync<InternalGuildSearchResponse>(GuildSearchActorBase.EntityIdOnCurrentNode, new InternalGuildSearchRequest(requestParams, context));
            return Ok(new GuildSearchReturn(response.IsError, response.GuildInfos));
        }
    }
}

#endif
