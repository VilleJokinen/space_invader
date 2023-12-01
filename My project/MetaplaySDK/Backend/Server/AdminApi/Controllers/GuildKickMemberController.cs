// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.Model;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.Guild.InternalMessages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for route to kick a guild member
    /// </summary>
    [GuildsEnabledCondition]
    public class GuildKickMemberController : GameAdminApiController
    {
        public GuildKickMemberController(ILogger<GuildKickMemberController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GuildPlayerKicked)]
        public class GuildEventPlayerKicked : GuildEventPayloadBase
        {
            [MetaMember(1)] public EntityId PlayerId { get; private set; }
            public GuildEventPlayerKicked() { }
            public GuildEventPlayerKicked(EntityId playerId) { PlayerId = playerId; }
            override public string EventTitle => "Player kicked";
            override public string EventDescription => $"{PlayerId} kicked.";
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerKickedFromGuild)]
        public class PlayerEventKickedFromGuild : PlayerEventPayloadBase
        {
            [MetaMember(1)] public EntityId GuildId { get; private set; }
            public PlayerEventKickedFromGuild() { }
            public PlayerEventKickedFromGuild(EntityId guildId) { GuildId = guildId; }
            override public string EventTitle => "Kicked from guild";
            override public string EventDescription => $"Player kicked from guild {GuildId}.";
        }

        /// <summary>
        /// HTTP request body for member kick
        /// </summary>
        [JsonObject(ItemRequired = Required.Always)]
        public class MemberKickBody
        {
            public string PlayerId;
        }


        /// <summary>
        /// API endpoint to kick a member
        /// Usage:  POST /api/guilds/{GUILDID}/kickMember
        /// Test:   curl http://localhost:5550/api/guilds/Guild:0000000000/kickMember -X POST -H "Content-Type: application/json" -d '{"PlayerId":"Player:0000000000"}'
        /// </summary>
        [HttpPost("guilds/{guildIdStr}/kickMember")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGuildsKickMember)]
        public async Task<IActionResult> Kick(string guildIdStr)
        {
            // Parse parameters
            EntityId        guildId     = ParseEntityIdStr(guildIdStr, EntityKindCore.Guild);
            MemberKickBody  request     = await ParseBodyAsync<MemberKickBody>();
            EntityId        playerId    = await ParsePlayerIdStrAndCheckForExistenceAsync(request.PlayerId);
            IGuildModelBase guildModel  = (IGuildModelBase)await GetEntityStateAsync(guildId);

            if (!guildModel.TryGetMember(playerId, out GuildMemberBase _))
                throw new MetaplayHttpException(400, "Player not in guild.", $"Player with ID {playerId} does not exist in guild with ID {guildId}.");

            // Get kickin'
            _logger.LogInformation($"Kicking guild {guildId} member {request.PlayerId}");
            await TellEntityAsync(guildId, new InternalGuildAdminKickMember(playerId));

            // Audit log events
            await WriteRelatedAuditLogEventsAsync(new List<EventBuilder>
            {
                new GuildEventBuilder(guildId, new GuildEventPlayerKicked(playerId)),
                new PlayerEventBuilder(playerId, new PlayerEventKickedFromGuild(guildId)),
            });

            return NoContent();
        }
    }
}

#endif
