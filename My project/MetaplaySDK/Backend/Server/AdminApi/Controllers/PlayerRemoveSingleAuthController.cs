// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to remove single authentication methods from a player.
    /// </summary>
    public class PlayerDisconnectAuthController : GameAdminApiController
    {
        public PlayerDisconnectAuthController(ILogger<PlayerDisconnectAuthController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerDisconnectSingleAuth)]
        public class PlayerEventRemoveSingleAuth : PlayerEventPayloadBase
        {
            [MetaMember(1)] public AuthenticationKey AuthKey { get; private set; }
            public PlayerEventRemoveSingleAuth() { }
            public PlayerEventRemoveSingleAuth(AuthenticationKey authKey) { AuthKey = authKey; }
            override public string EventTitle => "Removed auth method";
            override public string EventDescription => $"Removed single auth method {AuthKey}.";
        }

        /// <summary>
        /// API endpoint to remove a single authentication method from a player
        /// Usage:  DELETE /api/players/{PLAYERID}/auths/{AUTHPLATFORM}/{AUTHID}
        /// </summary>
        [HttpDelete("players/{playerIdStr}/auths/{authPlatformString}/{authIdString}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersAuth)]
        public async Task<IActionResult> RemoveSingleAuth(string playerIdStr, string authPlatformString, string authIdString)
        {
            // Resolve the player Id
            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);

            // Parse request data
            AuthenticationPlatform authPlatform;
            try
            {
                authPlatform = EnumUtil.Parse<AuthenticationPlatform>(authPlatformString);
            }
            catch
            {
                throw new MetaplayHttpException(400, "Failed to remove auth.", $"Unknown auth platform {authPlatformString}");
            }
            AuthenticationKey authKey = new AuthenticationKey(authPlatform, authIdString);
            if (!details.Model.AttachedAuthMethods.ContainsKey(authKey))
                throw new MetaplayHttpException(400, "Failed to remove auth.", $"Unknown auth method {authKey}");

            // Remove the auth
            PlayerRemoveSingleAuthResponse response = await AskEntityAsync<PlayerRemoveSingleAuthResponse>(details.PlayerId, new PlayerRemoveSingleAuthRequest(authKey));
            if (!response.Success)
                throw new MetaplayHttpException(400, "Failed to remove auth.", $"The auth method {authKey} could not be removed");

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(details.PlayerId, new PlayerEventRemoveSingleAuth(authKey)));

            // Done
            return NoContent();
        }
    }
}
