// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Server.Guild;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK routes that deal with an individual guild's details.
    /// </summary>
    [GuildsEnabledCondition]
    public class GuildDetailsController : GameAdminApiController
    {
        public GuildDetailsController(ILogger<GuildDetailsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// HTTP response for an individual guild's details
        /// </summary>
        public class GuildDetailsItem
        {
            public string                       Id              { get; set; }
            public IGuildModelBase              Model           { get; set; }
            public int                          PersistedSize   { get; set; }
            public byte[]                       PersistedModel  { get; set; }   // Raw bytes of GuildModel as persisted in the database
        }

        /// <summary>
        /// API endpoint to return detailed information about a single guild
        /// Usage:  GET /api/guilds/{GUILDID}
        /// Test:   curl http://localhost:5550/api/guilds/{GUILDID}
        /// </summary>
        [HttpGet("guilds/{guildIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiGuildsView)]
        public async Task<ActionResult<GuildDetailsItem>> Get(string guildIdStr)
        {
            // Parse parameters
            EntityId            guildId         = ParseEntityIdStr(guildIdStr, EntityKindCore.Guild);
            PersistedGuildBase  persistedGuild  = (PersistedGuildBase)await GetPersistedEntityAsync(guildId);
            IGuildModelBase     guildModel      = (IGuildModelBase)await GetEntityStateAsync(guildId);

            // Respond to browser
            return new GuildDetailsItem
            {
                Id              = guildIdStr,
                Model           = guildModel,
                PersistedSize   = persistedGuild.Payload.Length,
            };
        }
    }
}

#endif
