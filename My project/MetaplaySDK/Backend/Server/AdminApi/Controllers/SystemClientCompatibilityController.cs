// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to update client compatibility settings.
    /// </summary>
    public class SystemClientCompatibilityController : GameAdminApiController
    {
        public SystemClientCompatibilityController(ILogger<SystemClientCompatibilityController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerClientCompatibilitySettingsUpdated)]
        public class GameServerEventClientCompatibilitySettingsUpdated : GameServerEventPayloadBase
        {
            [MetaMember(1)] public ClientCompatibilitySettings ClientCompatibilitySettings { get; private set; }
            public GameServerEventClientCompatibilitySettingsUpdated() { }
            public GameServerEventClientCompatibilitySettingsUpdated(ClientCompatibilitySettings clientCompatibilitySettings)
            {
                ClientCompatibilitySettings = clientCompatibilitySettings;
            }
            override public string SubsystemName => "ClientCompatibility";
            override public string EventTitle => "Updated";
            override public string EventDescription => "Client compatibility settings updated.";
        }


        /// <summary>
        /// API endpoint to update client compatability settings
        /// Usage:  POST /api/clientCompatibilitySettings
        /// Test:   curl http://localhost:5550/api/clientCompatibilitySettings -X POST -d ""
        /// </summary>
        [HttpPost("clientCompatibilitySettings")]
        [RequirePermission(MetaplayPermissions.ApiSystemEditLogicVersioning)]
        public async Task<IActionResult> UpdateClientCompatibilitySettings()
        {
            ClientCompatibilitySettings compatibilitySettings = await ParseBodyAsync<ClientCompatibilitySettings>();
            var request = new UpdateClientCompatibilitySettingsRequest(compatibilitySettings);
            var response = await AskEntityAsync<UpdateClientCompatibilitySettingsResponse>(GlobalStateManager.EntityId, request);

            if (response.IsSuccess)
            {
                await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerEventClientCompatibilitySettingsUpdated(compatibilitySettings)));
                return Ok();
            }
            else
                return Problem(title: "Failed to update ClientCompatibilitySettings", detail: response.ErrorMessage);
        }
    }
}
