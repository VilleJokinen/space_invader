// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK routes to change an individual player's name.
    /// </summary>
    public class PlayerChangeNameController : GameAdminApiController
    {
        public PlayerChangeNameController(ILogger<PlayerChangeNameController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerNameChanged)]
        public class PlayerEventNameChanged : PlayerEventPayloadBase
        {
            [MetaMember(1)] public string NewName { get; private set; }
            public PlayerEventNameChanged() { }
            public PlayerEventNameChanged(string newName)
            {
                NewName = newName;
            }
            override public string EventTitle => "Name changed";
            override public string EventDescription => $"Player name changed to {NewName}.";
        }

        /// <summary>
        /// HTTP request and response for player name change and validation endpoints
        /// </summary>
        public class PlayerChangeNameBody
        {
            public string NewName { get; private set; }
        }
        public class PlayerChangeNameReturn
        {
            public bool NameWasValid { get; private set; }
            public PlayerChangeNameReturn(bool nameWasValid) { NameWasValid = nameWasValid; }
        }

        /// <summary>
        /// API endpoint to validate a change to a player's name
        /// Usage:  POST /api/players/{PLAYERID}/validateName
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/validateName -X POST -H "Content-Type:application/json" -d '{"NewName":"bob"}'
        /// </summary>
        [HttpPost("players/{playerIdStr}/validateName")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersEditName)]
        public async Task<IActionResult> ValidateName(string playerIdStr, [FromBody] PlayerChangeNameBody body)
        {
            // Resolve the player Id
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);

            // Validate the name
            PlayerChangeNameRequest request = new PlayerChangeNameRequest(body.NewName, validateOnly: true);
            PlayerChangeNameResponse response = await AskEntityAsync<PlayerChangeNameResponse>(playerId, request);

            return Ok(new PlayerChangeNameReturn(response.NameWasValid));
        }

        /// <summary>
        /// API endpoint to change a player's name
        /// Usage:  POST /api/players/{PLAYERID}/changeName
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/changeName -X POST -H "Content-Type:application/json" -d '{"NewName":"bob"}'
        /// </summary>
        [HttpPost("players/{playerIdStr}/changeName")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersEditName)]
        public async Task<IActionResult> ChangeName(string playerIdStr, [FromBody] PlayerChangeNameBody body)
        {
            // Resolve the player Id
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);

            // Try to perform the name change. Validation will happen server-side
            PlayerChangeNameRequest request = new PlayerChangeNameRequest(body.NewName, validateOnly: false);
            PlayerChangeNameResponse response = await AskEntityAsync<PlayerChangeNameResponse>(playerId, request);
            if (response.NameWasValid)
            {
                await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEventNameChanged(body.NewName)));
                return Ok();
            }
            else
            {
                return StatusCode(400);
            }
        }
    }
}
