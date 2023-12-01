// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.AdminApi.AuditLog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to move authentication methods from one player to another.
    /// </summary>
    public class PlayerReconnectAccountController : GameAdminApiController
    {
        public PlayerReconnectAccountController(ILogger<PlayerReconnectAccountController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerAccountReconnectedFrom)]
        public class PlayerEventAccountReconnectedFrom : PlayerEventPayloadBase
        {
            [MetaMember(1)] public EntityId FromPlayerId { get; private set; }
            public PlayerEventAccountReconnectedFrom() { }
            public PlayerEventAccountReconnectedFrom(EntityId fromPlayerId) { FromPlayerId = fromPlayerId; }
            override public string EventTitle => "Reconnected account";
            override public string EventDescription => $"Authentication moved from player {FromPlayerId}.";
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerAccountReconnectedTo)]
        public class PlayerEventAccountReconnectedTo : PlayerEventPayloadBase
        {
            [MetaMember(1)] public EntityId ToPlayerId { get; private set; }
            public PlayerEventAccountReconnectedTo() { }
            public PlayerEventAccountReconnectedTo(EntityId toPlayerId) { ToPlayerId = toPlayerId; }
            override public string EventTitle => "Reconnected account";
            override public string EventDescription => $"Authentication moved to player {ToPlayerId}.";
        }

        /// <summary>
        /// API endpoint to remove the authentication from one player and move it to another
        /// Usage:  POST /api/players/{FROMPLAYERID}/moveAuthTo/{TOPLAYERID}
        /// Test:   curl http://localhost:5550/api/players/{FROMPLAYERID}/moveAuthTo/{TOPLAYERID} -X POST -d ""
        /// </summary>
        [HttpPost("players/{fromPlayerIdStr}/moveAuthTo/{toPlayerIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersReconnectAccount)]
        public async Task<IActionResult> PostMoveAuthTo(string fromPlayerIdStr, string toPlayerIdStr)
        {
            // Resolve the player Ids
            PlayerDetails fromDetails = await GetPlayerDetailsAsync(fromPlayerIdStr);
            EntityId toPlayerId = await ParsePlayerIdStrAndCheckForExistenceAsync(toPlayerIdStr);

            // Can't move to the same player
            if (fromPlayerIdStr == toPlayerIdStr)
                throw new MetaplayHttpException(400, "Cannot move auth.", "Cannot move auth when player Ids are the same.");

            // Now we can move the auth across
            _logger.LogInformation("Moving auth from {fromPlayerIdStr} to {toPlayerIdStr}", fromPlayerIdStr, toPlayerIdStr);

            // First, attach all auth to new player
            await AskEntityAsync<EntityAskOk>(toPlayerId, new PlayerCopyAuthFromRequest(fromDetails.Model.AttachedAuthMethods));

            // Second, remove all auth from old player
            await AskEntityAsync<EntityAskOk>(fromDetails.PlayerId, new PlayerDetachAllAuthRequest());

            // Audit log events
            await WriteRelatedAuditLogEventsAsync(new List<EventBuilder>
            {
                new PlayerEventBuilder(toPlayerId, new PlayerEventAccountReconnectedFrom(fromDetails.PlayerId)),
                new PlayerEventBuilder(fromDetails.PlayerId, new PlayerEventAccountReconnectedTo(toPlayerId)),
            });

            _logger.LogInformation("Auth moved from {fromPlayerIdStr} to {toPlayerIdStr} and new state persisted", fromPlayerIdStr, toPlayerIdStr);
            return Ok();
        }
    }
}
