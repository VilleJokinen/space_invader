// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller to change an individual player's experiment assignment
    /// </summary>
    public class PlayerSetExperimentController : GameAdminApiController
    {
        public PlayerSetExperimentController(ILogger<PlayerSetExperimentController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerExperimentAssignmentChanged)]
        public class PlayerExperimentAssignmentChanged : PlayerEventPayloadBase
        {
            [MetaMember(1)] public PlayerExperimentId ExperimentId { get; private set; }
            [MetaMember(2)] public ExperimentVariantId VariantId { get; private set; }
            [MetaMember(3)] public bool IsTester { get; private set; }

            PlayerExperimentAssignmentChanged() { }
            public PlayerExperimentAssignmentChanged(PlayerExperimentId experimentId, ExperimentVariantId variantId, bool isTester)
            {
                ExperimentId = experimentId;
                VariantId = variantId;
                IsTester = isTester;
            }

            override public string EventTitle => "Experiment group changed";
            override public string EventDescription
            {
                get
                {
                    string groupName = VariantId == null ? "control group" : $"variant group {VariantId}";
                    string role = IsTester ? "Tester" : "normal Player";
                    return $"Player assigned into {ExperimentId} experiment {groupName} as a {role}.";
                }
            }
        }

        /// <summary>
        /// HTTP request for player experiment assignment
        /// </summary>
        public class PlayerChangeExperimentAssignmentBody
        {
            [JsonProperty(Required = Required.Always)]
            public string ExperimentId { get; private set; }

            [JsonProperty(Required = Required.AllowNull)]
            public string VariantId { get; private set; }

            [JsonProperty(Required = Required.Always)]
            public bool IsTester { get; private set; }
        }

        /// <summary>
        /// API endpoint to change a player's experiment assignment
        /// Usage:  POST /api/players/{PLAYERID}/changeExperiment
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/changeExperiment -X POST -H "Content-Type:application/json" -d '{"ExperimentId":"myExperiment", "VariantId":null, "IsTester": true}'
        /// </summary>
        [HttpPost("players/{playerIdStr}/changeExperiment")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersEditExperimentGroups)]
        public async Task<IActionResult> ChangeExperimentAssignment(string playerIdStr)
        {
            // Parse parameters
            EntityId                                    playerId        = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);
            PlayerChangeExperimentAssignmentBody        body            = await ParseBodyAsync<PlayerChangeExperimentAssignmentBody>();
            PlayerExperimentId                          experimentId    = PlayerExperimentId.FromString(body.ExperimentId);
            ExperimentVariantId                         variantId       = ExperimentVariantId.FromString(body.VariantId);

            // Update tester status in experiment state
            GlobalStateEditExperimentTestersRequest     gsmRequest      = new GlobalStateEditExperimentTestersRequest(experimentId, variantId, playerId, body.IsTester);
            GlobalStateEditExperimentTestersResponse    gsmResponse     = await AskEntityAsync<GlobalStateEditExperimentTestersResponse>(GlobalStateManager.EntityId, gsmRequest);
            if (gsmResponse.ErrorStringOrNull != null)
            {
                throw new MetaplayHttpException(400, "Failed to modify player experiment tester status.", gsmResponse.ErrorStringOrNull);
            }

            // Perform the experiment change. GSM has already validated the arguments. Player might not see the updated
            // GSM state (via GSP) yet, but will perform the change anyway. This is tracked with experiment TesterEpochId.
            InternalPlayerSetExperimentGroupRequest     playerRequest   = new InternalPlayerSetExperimentGroupRequest(experimentId, variantId, gsmResponse.TesterEpoch);
            InternalPlayerSetExperimentGroupResponse    playerResponse  = await AskEntityAsync<InternalPlayerSetExperimentGroupResponse>(playerId, playerRequest);

            // Audit log
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerExperimentAssignmentChanged(experimentId, variantId, body.IsTester)));

            // If player did not see the change yet, we wait for it to become ready before returning.
            bool        isWaiting   = playerResponse.IsWaitingForTesterEpochUpdate;
            Stopwatch   sw          = Stopwatch.StartNew();
            while (isWaiting)
            {
                await Task.Delay(100);
                if (sw.Elapsed > TimeSpan.FromSeconds(5))
                    throw new MetaplayHttpException(400, "Timeout while waiting player to settle after experiment change.", "Player actor did not settle to updated config after config was updated.");

                InternalPlayerSetExperimentGroupWaitRequest     waitRequest   = new InternalPlayerSetExperimentGroupWaitRequest(experimentId, gsmResponse.TesterEpoch);
                InternalPlayerSetExperimentGroupWaitResponse    waitResponse  = await AskEntityAsync<InternalPlayerSetExperimentGroupWaitResponse>(playerId, waitRequest);
                isWaiting = waitResponse.IsWaitingForTesterEpochUpdate;
            }

            return Ok();
        }
    }
}
