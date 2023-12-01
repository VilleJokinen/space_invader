// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.EntityArchive;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.EntityArchiveController;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to overwrite a player with data from another player.
    /// </summary>
    public class PlayerOverwriteController : GameAdminApiController
    {
        public PlayerOverwriteController(ILogger<PlayerOverwriteController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerOverwritten)]
        public class PlayerEventOverwritten : PlayerEventPayloadBase
        {
            [MetaMember(1)] public ImportRequest Request { get; private set; }
            public PlayerEventOverwritten() { }
            public PlayerEventOverwritten(ImportRequest request) { Request = request; }
            override public string EventTitle => "Overwritten";
            override public string EventDescription => "Player overwritten.";
        }


        /// <summary>
        /// HTTP response to import player data and validation endpoints. Response will contain either a Error object or
        /// a Data object.
        /// </summary>
        public class OverwriteResponse
        {
            // If there were errors during the import process then the reason will be returned here.
            public class ErrorResponse {
                public string Message { get; private set; }
                public string Details { get; private set; }

                public ErrorResponse(string message, string details)
                {
                    Message = message;
                    Details = details;
                }
            }
            ErrorResponse Error;

            // If the import worked then this contains the observed differences.
            public class DataResponse
            {
                public string Diff { get; private set; }

                public DataResponse(string diff)
                {
                    Diff = diff;
                }
            }
            DataResponse Data;

            public bool WasSuccessful()
            {
                return this.Error == null;
            }

            public OverwriteResponse(ErrorResponse error)
            {
                Error = error;
            }
            public OverwriteResponse(DataResponse data)
            {
                Data = data;
            }
        }


        /// <summary>
        /// API endpoint to validate an overwrite of player's data. Use this before overwriting if you
        /// want to validate that the overwrite action will actually succeed
        /// Usage:  POST /api/players/{PLAYERID}/validateOverwrite
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/validateOverwrite -X POST -H "Content-Type:application/json" -d '{ENTITY-ARCHIVE}"}'
        /// </summary>
        /// <param name="playerIdStr"></param>
        /// <returns></returns>
        [HttpPost("players/{playerIdStr}/validateOverwrite")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersOverwrite)]
        [RequestFormLimits(KeyLengthLimit = 9000000)]
        public async Task<IActionResult> ValidateOverwrite(string playerIdStr)
        {
            // Parse parameters
            await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);

            // Do the validation
            (OverwriteResponse result, _) = await ValidateOrOverwriteAsync(playerIdStr, validateOnly: true);
            return Ok(result);
        }


        /// <summary>
        /// API endpoint to overwrite player data
        /// Usage:  POST /api/players/{PLAYERID}/importOverwrite
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/importOverwrite -X POST -H "Content-Type:application/json" -d '{ENTITY-ARCHIVE}"}'
        /// </summary>
        [HttpPost("players/{playerIdStr}/importOverwrite")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersOverwrite)]
        [RequestFormLimits(KeyLengthLimit = 9000000)]
        public async Task<IActionResult> ImportOverwrite(string playerIdStr)
        {
            // Request state for player that we are migrating from
            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);

            // Do the overwrite
            (OverwriteResponse result, ImportRequest bodyRequest) = await ValidateOrOverwriteAsync(playerIdStr, validateOnly: false);
            if (result.WasSuccessful())
            {
                // Audit log event
                await WriteAuditLogEventAsync(new PlayerEventBuilder(details.PlayerId, new PlayerEventOverwritten(bodyRequest)));
            }

            // Done
            return Ok(result);
        }


        /// <summary>
        /// Utility function for validating and overwriting player data
        /// </summary>
        /// <param name="targetPlayerIdStr">Id of player to overwrite</param>
        /// <param name="validateOnly">True to validate, false to overwrite</param>
        /// <returns></returns>
        async Task<(OverwriteResponse, ImportRequest)> ValidateOrOverwriteAsync(string targetPlayerIdStr, bool validateOnly)
        {
            // Check that the target player exists
            await ParsePlayerIdStrAndCheckForExistenceAsync(targetPlayerIdStr);

            // Parse parameters
            ImportRequest bodyRequest = await ParseBodyAsync<ImportRequest>();

            // Validate that the entity archive is good for our purposes
            bodyRequest.Entities.TryGetValue("player", out var entities);
            if (entities.Count != 1)
            {
                OverwriteResponse.ErrorResponse error = new OverwriteResponse.ErrorResponse("Entity archive is not correct format.", "Player entity count must be 1.");
                return (new OverwriteResponse(error), bodyRequest);
            }
            bodyRequest.Remaps.TryGetValue("player", out var remaps);
            if (remaps == null || remaps.Count != 1)
            {
                OverwriteResponse.ErrorResponse error = new OverwriteResponse.ErrorResponse("Entity archive is not correct format.", "Player remap count must be 1.");
                return (new OverwriteResponse(error), bodyRequest);
            }
            string[] entityKeys = new string[entities.Count];
            entities.CopyKeysTo(entityKeys, 0);
            string sourcePlayerIdStr = entityKeys[0];
            string[] remapKeys = new string[remaps.Count];
            remaps.CopyKeysTo(remapKeys, 0);
            string remapSourcePlayerIdStr = remapKeys[0];
            string remapTargetPlayerIdStr = remaps.GetValueOrDefault(remapSourcePlayerIdStr);
            if (sourcePlayerIdStr != remapSourcePlayerIdStr || remapTargetPlayerIdStr != targetPlayerIdStr)
            {
                OverwriteResponse.ErrorResponse error = new OverwriteResponse.ErrorResponse("Entity archive is not correct format.", "Ids do not match.");
                return (new OverwriteResponse(error), bodyRequest);
            }
            bodyRequest.Entities.RemoveWhere(kv => kv.Key != "player");

            EntityArchive.ImportResponse result;
            ImportResponse.EntityResult entityResult;
            try
            {
                result = await EntityArchiveUtils.Import(bodyRequest.FormatVersion, bodyRequest.Entities, bodyRequest.Remaps, OverwritePolicy.Overwrite, validateOnly, this);
                entityResult = result.Entities[0];
            }
            catch (ImportException ex)
            {
                OverwriteResponse.ErrorResponse error = new OverwriteResponse.ErrorResponse(ex.ImportMessage, ex.ImportDetails);
                return (new OverwriteResponse(error), bodyRequest);
            }

            if (entityResult.Status == ImportResponse.EntityResultStatus.Error)
            {
                OverwriteResponse.ErrorResponse error = new OverwriteResponse.ErrorResponse(entityResult.Error.Message, entityResult.Error.Details);
                return (new OverwriteResponse(error), bodyRequest);
            }

            // Extract the diff from the single player result
            string diff = entityResult.OverwriteDiff;

            // Done
            OverwriteResponse.DataResponse data = new OverwriteResponse.DataResponse(diff);
            return (new OverwriteResponse(data), bodyRequest);
        }
    }
}
