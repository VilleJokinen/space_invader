// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to reset an individual player.
    /// </summary>
    public class PlayerResetController : GameAdminApiController
    {
        public PlayerResetController(ILogger<PlayerResetController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerReset)]
        public class PlayerEventReset : PlayerEventPayloadBase
        {
            [MetaMember(1)] public bool WasForceReset { get; private set; }
            PlayerEventReset() { }
            public PlayerEventReset(bool wasForceReset)
            {
                WasForceReset = wasForceReset;
            }
            override public string EventTitle => "Reset";
            override public string EventDescription => WasForceReset ? "Player state reset by force." : "Player reset.";
        }

        /// <summary>
        /// API endpoint to trigger player state reset
        /// Usage:  POST /api/players/{PLAYERID}/resetState
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/resetState -X POST -d ""
        /// </summary>
        [HttpPost("players/{playerIdStr}/resetState")]
        [RequirePermission(MetaplayPermissions.ApiPlayersResetPlayer)]
        public async Task ResetState(string playerIdStr)
        {
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);
            _logger.LogInformation("Resetting {0} state!", playerId);

            await TellEntityAsync(playerId, PlayerResetState.Instance);

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEventReset(false)));
        }

        /// <summary>
        /// API endpoint to trigger force player state reset
        /// Usage:  POST /api/players/{PLAYERID}/forceResetState
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/forceResetState -X POST -d ""
        /// </summary>
        [HttpPost("players/{playerIdStr}/forceResetState")]
        [RequirePermission(MetaplayPermissions.ApiPlayersResetPlayer)]
        public async Task ForceResetState(string playerIdStr)
        {
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);

            _logger.LogInformation("Force resetting {0} state!", playerId);
            bool wasForceReset = false;

            try
            {
                // First attempt soft reset
                await TellEntityAsync(playerId, PlayerResetState.Instance);
            }
            catch (Exception)
            {
                // Soft reset failed and the player actor has been terminated, force reset by
                // clearing the database entry.
                await DatabaseEntityUtil.PersistEmptyPlayerAsync(playerId, true);

                // Ask to persist state, which triggers creation of the new model
                await AskEntityAsync<PersistStateRequestResponse>(playerId, PersistStateRequestRequest.Instance);

                wasForceReset = true;
            }

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEventReset(wasForceReset)));
        }

    }
}
