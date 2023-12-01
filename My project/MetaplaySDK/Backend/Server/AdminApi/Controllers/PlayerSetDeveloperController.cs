// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to set player developer status.
    /// </summary>
    public class PlayerSetDeveloperController : GameAdminApiController
    {
        public PlayerSetDeveloperController(ILogger<PlayerSetDeveloperController> logger, IActorRef adminApi) : base(logger, adminApi) { }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerChangeDeveloperStatus)]
        public class PlayerEventChangeDeveloperStatus : PlayerEventPayloadBase
        {
            [MetaMember(1)] public bool IsDeveloper { get; private set; }

            public PlayerEventChangeDeveloperStatus() { }

            public PlayerEventChangeDeveloperStatus(bool isDeveloper)
            {
                IsDeveloper = isDeveloper;
            }

            public override string EventTitle => "Player developer status changed";
            public override string EventDescription
            {
                get
                {
                    return IsDeveloper ?
                        "Player has been given developer privileges" :
                        "Player has had their developer privileges revoked";
                }
            }
        }

        /// <summary>
        /// API endpoint to set a player as a developer
        /// Usage:  POST /api/players/{PLAYERID}/developerStatus
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/developerStatus?newStatus=true -X POST -d ""
        /// </summary>
        [HttpPost("players/{playerIdStr}/developerStatus")]
        [RequirePermission(MetaplayPermissions.ApiPlayersSetDeveloper)]
        public async Task<ActionResult> ToggleDeveloperStatus(string playerIdStr, [FromQuery, BindRequired] bool newStatus)
        {
            if (!ModelState.IsValid)
                return BadRequest($"Parameter {nameof(newStatus)} is required.");

            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);

            _logger.LogInformation("Setting player {0} as a developer: {1}!", playerId, newStatus);

            SetDeveloperPlayerResponse response = await AskEntityAsync<SetDeveloperPlayerResponse>(GlobalStateManager.EntityId, new SetDeveloperPlayerRequest(playerId, newStatus));

            if (response.IsSuccess)
            {
                // Audit log event
                await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEventChangeDeveloperStatus(newStatus)));
            }

            return new JsonResult(new {IsDeveloper = newStatus});
        }
    }
}
