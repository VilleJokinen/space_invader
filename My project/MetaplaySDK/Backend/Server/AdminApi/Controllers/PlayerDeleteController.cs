// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class PlayerDeleteController : GameAdminApiController
    {
        public PlayerDeleteController(ILogger<PlayerDeleteController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerDeletionScheduled)]
        public class PlayerEventDeletionScheduled : PlayerEventPayloadBase
        {
            [MetaMember(1)] public MetaTime ScheduledForDeleteAt { get; private set; }
            public PlayerEventDeletionScheduled() { }
            public PlayerEventDeletionScheduled(MetaTime scheduledForDeleteAt) { ScheduledForDeleteAt = scheduledForDeleteAt; }
            override public string EventTitle => "Deletion scheduled";
            override public string EventDescription => $"Player deletion scheduled at {ScheduledForDeleteAt}.";
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerDeletionUnscheduled)]
        public class PlayerEventDeletionUnscheduled : PlayerEventPayloadBase
        {
            public PlayerEventDeletionUnscheduled() { }
            override public string EventTitle => "Deletion unscheduled";
            override public string EventDescription => "Player deletion unscheduled.";
        }


        /// <summary>
        /// Body for PutScheduledDeletion
        /// </summary>
        [JsonObject(ItemRequired = Required.Always)]
        public class ScheduledDeletion
        {
            public MetaTime ScheduledForDeleteAt;
        }


        /// <summary>
        /// API endpoint to schedule a player for deletion
        /// Usage:  GET /api/players/{PLAYERID}/scheduledDeletion
        /// Test:   curl --location --request PUT 'localhost:5550/api/players/Player:0000000007/scheduledDeletion' \
        ///         --header 'Content-Type: text/plain' \
        ///         --data-raw '{
        ///             "scheduledForDeleteAt": "2021-09-19T08:39:17+0000"
        ///         }'
        /// </summary>
        /// <param name="playerIdStr"></param>
        /// <returns></returns>
        [HttpPut("players/{playerIdStr}/scheduledDeletion")]
        [RequirePermission(MetaplayPermissions.ApiPlayersEditScheduledDeletion)]
        public async Task<ActionResult> PutScheduledDeletion(string playerIdStr)
        {
            // Parse parameters
            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);
            ScheduledDeletion request = await ParseBodyAsync<ScheduledDeletion>();

            if (details.Model.DeletionStatus == PlayerDeletionStatus.Deleted)
            {
                throw new MetaplayHttpException(400, "Cannot schedule deletion of player.", $"Cannot schedule deletion of {playerIdStr}: player has already been deleted.");
            }
            else
            {
                // Schedule the deletion
                await AskEntityAsync<InternalPlayerScheduleDeletionResponse>(details.PlayerId, new InternalPlayerScheduleDeletionRequest(request.ScheduledForDeleteAt, $"Delete API {GetUserId()}"));

                // Audit log event
                await WriteAuditLogEventAsync(new PlayerEventBuilder(details.PlayerId, new PlayerEventDeletionScheduled(request.ScheduledForDeleteAt)));

                // Return results
                return NoContent();
            }
        }


        /// <summary>
        /// API endpoint to unschedule a player from deletion
        /// Usage:  DELETE /api/players/{PLAYERID}/scheduledDeletion
        /// Test:   curl --location --request DELETE 'localhost:5550/api/players/Player:0000000007/scheduledDeletion' \
        ///         --data-raw ''
        /// </summary>
        /// <param name="playerIdStr"></param>
        /// <returns></returns>
        [HttpDelete("players/{playerIdStr}/scheduledDeletion")]
        [RequirePermission(MetaplayPermissions.ApiPlayersEditScheduledDeletion)]
        public async Task<ActionResult> DeleteScheduledDeletion(string playerIdStr)
        {
            // Parse parameters
            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);

            if (details.Model.DeletionStatus == PlayerDeletionStatus.Deleted)
            {
                throw new MetaplayHttpException(400, "Cannot unschedule deletion of player.", $"Cannot unschedule deletion of {playerIdStr}: player has already been deleted.");
            }
            else if (details.Model.DeletionStatus == PlayerDeletionStatus.None)
            {
                // In the case where the player is not already scheduled for deletion, we'll just let it fail silently - it's harmless
                return NoContent();
            }
            else
            {
                // Unschedule the deletion
                await AskEntityAsync<InternalPlayerScheduleDeletionResponse>(details.PlayerId, new InternalPlayerScheduleDeletionRequest(null, $"Delete API {GetUserId()}"));

                // Audit log event
                await WriteAuditLogEventAsync(new PlayerEventBuilder(details.PlayerId, new PlayerEventDeletionUnscheduled()));

                // Return results
                return NoContent();
            }
        }
    }
}
