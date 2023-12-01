// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to trigger a generic server action.
    /// </summary>
    public class PlayerGenericActionController : GameAdminApiController
    {
        public PlayerGenericActionController(ILogger<PlayerGenericActionController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerGenericServerActionPerformed)]
        public class PlayerEventGenericActionPerformed : PlayerEventPayloadBase
        {
            [MetaMember(1)] public string Name { get; private set; }
            [MetaMember(2)] public PlayerUnsynchronizedServerActionBase Data { get; private set; }
            public PlayerEventGenericActionPerformed() { }
            public PlayerEventGenericActionPerformed(string name, PlayerUnsynchronizedServerActionBase data) { Name = name; Data = data; }
            override public string EventTitle => "Action performed";
            override public string EventDescription => $"Generic server action '{Name}' performed.";
        }


        /// <summary>
        /// API endpoint utility action to quickly trigger any action in the game without having to create a dedicated route for it.
        /// Usage:  POST /api/players/{PLAYERID}/sendServerAction/{ACTION}
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/sendServerAction/{ACTION} -X POST -d ""
        /// </summary>
        [HttpPost("players/{playerIdStr}/sendServerAction/{serverActionName}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersTriggerGenericAction)]
        public async Task SendServerAction(string playerIdStr, string serverActionName)
        {
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);
            _logger.LogInformation("Sending ServerAction to {0}: {1}", playerId, serverActionName);

            // Resolve type for mail class (and check that its a valid type)
            if (!ModelActionRepository.Instance.SpecFromName.TryGetValue(serverActionName, out ModelActionSpec actionSpec))
                throw new InvalidOperationException($"Invalid action class name: {serverActionName}");

            // Deserialize server action & send to player
            PlayerUnsynchronizedServerActionBase serverAction = await ParseBodyAsync<PlayerUnsynchronizedServerActionBase>(actionSpec.Type);
            await ExecutePlayerServerActionAsync(playerId, serverAction);

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEventGenericActionPerformed(serverActionName, serverAction)));
        }
    }
}
