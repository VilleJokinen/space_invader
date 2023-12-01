// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for route to ban/unban a player
    /// </summary>
    public class PlayerBanController : GameAdminApiController
    {
        public PlayerBanController(ILogger<PlayerBanController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerBanned)]
        public class PlayerEventBanned : PlayerEventPayloadBase
        {
            public PlayerEventBanned() { }
            override public string EventTitle => "Banned";
            override public string EventDescription => "Player banned.";
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerUnbanned)]
        public class PlayerEventUnbanned : PlayerEventPayloadBase
        {
            public PlayerEventUnbanned() { }
            override public string EventTitle => "Unbanned";
            override public string EventDescription => "Player unbanned.";
        }


        /// <summary>
        /// HTTP request body for player ban endpoint
        /// </summary>
        [JsonObject(ItemRequired = Required.Always)]
        public class BanBody
        {
            public bool IsBanned;
        }


        /// <summary>
        /// API endpoint to ban/unban a player
        /// Usage:  POST /api/players/{PLAYERID}/ban
        /// Test:   curl http://localhost:5550/api/players/Player:0000000000/ban -X POST -H "Content-Type: application/json" -d '{"IsBanned":true}'
        /// </summary>
        [HttpPost("players/{playerIdStr}/ban")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersBan)]
        public async Task<IActionResult> Ban(string playerIdStr)
        {
            // Parse parameters
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);
            BanBody request = await ParseBodyAsync<BanBody>();

            // Set player's ban status with a server action
            _logger.LogInformation($"Setting player {playerId} ban status to {request.IsBanned}");
            PlayerSetIsBanned serverAction = new PlayerSetIsBanned(request.IsBanned);
            await ExecutePlayerServerActionAsync(playerId, serverAction);

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, request.IsBanned ? new PlayerEventBanned() : new PlayerEventUnbanned()));

            return NoContent();
        }
    }
}
