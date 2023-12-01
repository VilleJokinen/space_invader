// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Metaplay.Server.ScheduledPlayerDeletion;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Parquet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class PlayerRedeleteController : GameAdminApiController
    {
        public PlayerRedeleteController(ILogger<PlayerRedeleteController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerPlayerRedeleteExecuted)]
        public class GameServerEventPlayerRedeletedExecute : GameServerEventPayloadBase
        {
            [MetaMember(1)] public MetaTime CutoffTime { get; private set; }
            [MetaMember(2)] public List<EntityId> PlayerIds { get; private set; }
            public GameServerEventPlayerRedeletedExecute() { }
            public GameServerEventPlayerRedeletedExecute(MetaTime cutoffTime, List<EntityId> playerIds)
            {
                CutoffTime = cutoffTime;
                PlayerIds = playerIds;
            }
            override public string SubsystemName => "PlayerRedelete";
            override public string EventTitle =>"Executed";
            override public string EventDescription => Invariant($"Player-redelete executed for {PlayerIds.Count()} players.");
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerRedeleted)]
        public class PlayerEventRedeleted : PlayerEventPayloadBase
        {
            public PlayerEventRedeleted() { }
            override public string EventTitle => "Redeleted";
            override public string EventDescription => "Player redeleted.";
        }


        /// <summary>
        /// Form data for the redeletePlayers/list endpoint
        /// </summary>
        [JsonObject(ItemRequired = Required.Always)]
        public class RedeleteListFormData
        {
            public List<IFormFile> File { get; set; } // A parquet file containing a snapshot of the PlayerDeletionRecords table. Supports .parquet and .gz.parquet files
            public string CutoffTime { get; set; } // Data/time of the backup that we restored from
        }

        /// <summary>
        /// After a database rollback we might have players who asked to be deleted but who have
        /// now been restored. We use this endpoint to get a list of those players. The endpoint
        /// takes a file containing a snapshot of the most recent PlayerDeletionRecords table
        /// and compares it against the current state of the player database. It then returns a
        /// list of players who *were* scheduled for deletion but are no longer.
        ///
        /// Further information on how this endpoint is used by the LiveOps Dashboard can be found
        /// in the Metaplay documentation.
        ///
        /// Usage:  POST /api/redeletePlayers/list
        ///
        /// Test:   curl --location --request POST 'localhost:5550/api/redeletePlayers/list' \
        ///              --form 'file=@path/to/file.gz.parquet' \
        ///              --form 'cutoffTime=2020-10-10T00:00'
        /// </summary>
        /// <returns></returns>
        [HttpPost("redeletePlayers/list")]
        [RequirePermission(MetaplayPermissions.ApiSystemPlayerRedelete)]
        public async Task<ActionResult> PostRedeletePlayersList([FromForm] RedeleteListFormData formData)
        {
            // Parse form parameters to get player list
            List<PlayerRedeleteInfo> requiredDeletions = await GenerateRequiredDeletionsFromFormParameters(formData.File, formData.CutoffTime);

            // Success
            return Ok(new
            {
                PlayerInfos = requiredDeletions,
            });
        }


        /// <summary>
        /// Form data for the redeletePlayers/execute endpoint
        /// </summary>
        [JsonObject(ItemRequired = Required.Always)]
        public class RedeleteExecuteFormData
        {
            public List<IFormFile> File { get; set; } // A parquet file containing a snapshot of the PlayerDeletionRecords table. Supports .parquet and .gz.parquet files
            public string CutoffTime { get; set; } // Data/time of the backup that we restored from
            public List<string> PlayerIds { get; set; } // List of Player Ids that we want to redelete
        }

        /// <summary>
        /// After calling the redeletePlayers/list endpoint to obtain a list of players, call this
        /// endpoint to actually schedule those players for deletion. You must explictly pass in
        /// the list of players that you want deleted. This serves two purposes: firstly, it makes
        /// it harder to abuse this endpoint to arbitrarily delete players and, secondly, it
        /// allows you to skip some of those players from being redeleted.
        ///
        /// Further information on how this endpoint is used by the LiveOps Dashboard can be found
        /// in the Metaplay documentation.
        ///
        /// Usage:  POST /api/redeletePlayers/execute
        ///
        /// Test:   curl --location --request POST 'localhost:5550/api/redeletePlayers/execute' \
        ///              --form 'file=@path/to/file.gz.parquet' \
        ///              --form 'cutoffTime=2020-10-10T00:00' \
        ///              --form 'playerIds=Player:2345623456' \
        ///              --form 'PlayerIds=Player:3456734567'
        /// </summary>
        /// <returns></returns>
        [HttpPost("redeletePlayers/execute")]
        [RequirePermission(MetaplayPermissions.ApiSystemPlayerRedelete)]
        public async Task<ActionResult> PostRedeletePlayersExecute([FromForm] RedeleteExecuteFormData formData)
        {
            // Parse form parameters to get player list
            List<PlayerRedeleteInfo> requiredDeletions = await GenerateRequiredDeletionsFromFormParameters(formData.File, formData.CutoffTime);

            // Parse and validate playerId list
            if (formData.PlayerIds == null)
            {
                throw new MetaplayHttpException(400, "Could not generate result.", "No Player Ids were supplied.");
            }
            List<EntityId> requestedPlayerIds = formData.PlayerIds
                .Select(playerIdStr =>
                {
                    try
                    {
                        return ParsePlayerIdStr(playerIdStr);
                    }
                    catch (MetaplayHttpException ex)
                    {
                        throw new MetaplayHttpException(ex.ExStatusCode, "Could not generate result", $"At least one playerId is invalid: {ex.ExDetails}");
                    }
                })
                .ToList();
            if (requestedPlayerIds.Distinct().Count() != requestedPlayerIds.Count())
            {
                throw new MetaplayHttpException(400, "Could not generate result", "Player Id list contains duplicated Player Ids.");
            }
            requestedPlayerIds.ForEach(requestedPlayerId =>
            {
                if (requiredDeletions.Find(player => requestedPlayerId == player.PlayerId) == null)
                {
                    throw new MetaplayHttpException(400, "Could not generate result", $"At least one of the Player Ids you supplied ('{requestedPlayerId}') is not in the list of expected Player Ids.");
                }
            });

            // Process the requests for all players
            _logger.LogInformation("Attempting to redelete players: {playerList}", requestedPlayerIds.ToList());
            await Task.WhenAll(
                requestedPlayerIds.Select(async requestedPlayerId =>
                {
                    // Request player to mark itself for deletion
                    PlayerRedeleteInfo playerInfo = requiredDeletions.Find(player => requestedPlayerId == player.PlayerId);
                    await AskEntityAsync<InternalPlayerScheduleDeletionResponse>(requestedPlayerId, new InternalPlayerScheduleDeletionRequest(playerInfo.ScheduledDeletionTime, $"Redelete API {GetUserId()} (was: '{playerInfo.DeletionSource}')"));
                })
            );

            // Ask the deletion worker to do a sweep right now so that these players get deleted asap
            await TellEntityAsync(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, DeletionSweepRequest.Instance);

            // Audit log events
            MetaTime metaTimeCutoffTime = MetaTime.Parse(formData.CutoffTime);
            await WriteParentWithChildrenAuditLogEventsAsync(
                new GameServerEventBuilder(new GameServerEventPlayerRedeletedExecute(metaTimeCutoffTime, requestedPlayerIds)),
                requestedPlayerIds.Select(playerId =>
                   (EventBuilder)new PlayerEventBuilder(playerId, new PlayerEventRedeleted())
                ).ToList());

            // Finished successfully
            return NoContent();
        }


        /// <summary>
        /// Simple helper function for both /list and /execute endpoints. Contains the common
        /// code from both endpoints to parse the form parameters and call GetRequestedDeletionsFromParquetStream
        /// and GetRequiredDeletions to get a list of players that can be redeleted
        ///
        /// </summary>
        /// <param name="files"></param>
        /// <param name="cutoffTime"></param>
        /// <returns></returns>
        private async Task<List<PlayerRedeleteInfo>> GenerateRequiredDeletionsFromFormParameters(List<IFormFile> files, string cutoffTime)
        {
            // Parse form parameters
            if (files == null)
            {
                throw new MetaplayHttpException(400, "Could not generate result.", "No file was supplied.");
            }
            if (files.Count != 1)
            {
                throw new MetaplayHttpException(400, "Could not generate result.", "Multiple files not supported.");
            }
            MetaTime metaTimeCutoffTime;
            try
            {
                metaTimeCutoffTime = MetaTime.Parse(cutoffTime);
            }
            catch (Exception ex)
            {
                throw new MetaplayHttpException(400, "Could not generate result.", $"cutoffTime was not valid: {ex.Message}");
            }

            // Generate player list
            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    await files[0].CopyToAsync(stream);
                    List<PlayerRedeleteRecord> requestedDeletions = await GetRequestedDeletionsFromParquetStreamAsync(stream, metaTimeCutoffTime);
                    List<PlayerRedeleteInfo> requiredDeletions = await GetRequiredDeletions(requestedDeletions);
                    return requiredDeletions;
                }
            }
            catch (Exception ex)
            {
                throw new MetaplayHttpException(400, "Failed to process snapshot file.", ex.Message);
            }
        }


        /// <summary>
        /// Represents the information about a player as it was extracted from the
        /// PlayerDeletionRecords table snapshot
        /// </summary>
        public class PlayerRedeleteRecord
        {
            public EntityId PlayerId { get; private set; }
            public MetaTime ScheduledDeletionTime { get; private set; }
            public string DeletionSource { get; private set; }

            public PlayerRedeleteRecord(EntityId entityId, MetaTime scheduledDeletionTime, string deletionSource)
            {
                PlayerId = entityId;
                ScheduledDeletionTime = scheduledDeletionTime;
                DeletionSource = deletionSource;
            }
        }

        /// <summary>
        /// Given a snapshot of the PlayerDeletionRecords table as a Parquet stream, find all players
        /// who were scheduled to be deleted after the given cutoff time
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cutoffTime"></param>
        /// <returns>(errorString or List of PlayerRedeleteInfo)</returns>
        private async Task<List<PlayerRedeleteRecord>> GetRequestedDeletionsFromParquetStreamAsync(Stream stream, MetaTime cutoffTime)
        {
            using (ParquetReader parquetReader = await ParquetReader.CreateAsync(stream))
            {
                // Read all rows of the table and keep the ones that have dates
                // ahead of our cutoff time
                long cutoffTimeInMsSinceEpoch = cutoffTime.MillisecondsSinceEpoch;
                Parquet.Rows.Table table = await parquetReader.ReadAsTableAsync();
                _logger.LogInformation("Read re-delete snapshot stream with {rowCount} rows.", table.Count);
                List<PlayerRedeleteRecord> records = table
                    .Where(row => MetaTime.FromDateTime(row.GetDateTime(1)).MillisecondsSinceEpoch >= cutoffTimeInMsSinceEpoch)
                    .Select(row =>
                    {
                        // /todo [paul] the direct addressing of row[column] is messy here. can it be improved? or at least can the schema be verified?
                        EntityId playerId              = EntityId.ParseFromString(row.GetString(0));
                        MetaTime scheduledDeletionTime = MetaTime.FromDateTime(row.GetDateTime(1));
                        string   deletionSource        = row.GetString(2);
                        return new PlayerRedeleteRecord(playerId, scheduledDeletionTime, deletionSource);
                    })
                    .ToList();
                _logger.LogInformation("Filtered re-delete snapshot stream by cutoff time to {count} rows.", records.Count);
                return records;
            }
        }


        /// <summary>
        /// Represents the information about a player who needs to be redeleted
        /// </summary>
        public class PlayerRedeleteInfo
        {
            public EntityId PlayerId { get; private set; }
            public string PlayerName { get; private set; }
            public MetaTime ScheduledDeletionTime { get; private set; }
            public string DeletionSource { get; private set; }

            public PlayerRedeleteInfo(EntityId entityId, string playerName, MetaTime scheduledDeletionTime, string deletionSource)
            {
                PlayerId = entityId;
                PlayerName = playerName;
                ScheduledDeletionTime = scheduledDeletionTime;
                DeletionSource = deletionSource;
            }
        }

        /// <summary>
        /// Given a list of players with scheduled deletion times, return a filtered copy
        /// of the list that contains only those players who still exist in the current
        /// PlayerDatabse
        /// </summary>
        /// <param name="playerRedeleteRecords"></param>
        /// <returns></returns>
        private async Task<List<PlayerRedeleteInfo>> GetRequiredDeletions(List<PlayerRedeleteRecord> playerRedeleteRecords)
        {
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Normal);
            List<PlayerRedeleteInfo> infos = new List<PlayerRedeleteInfo>();
            foreach (PlayerRedeleteRecord playerRedeleteRecord in playerRedeleteRecords)
            {
                // Look up player details (if the player exists)
                PersistedPlayerBase player = await db.TryGetAsync<PersistedPlayerBase>(playerRedeleteRecord.PlayerId.ToString());
                if (player != null && player.Payload != null)
                {
                    EntityId playerId = playerRedeleteRecord.PlayerId;
                    InternalEntityStateResponse response = await AskEntityAsync<InternalEntityStateResponse>(playerId, InternalEntityStateRequest.Instance);
                    FullGameConfig baselineConfig = await ServerGameConfigProvider.Instance.GetBaselineGameConfigAsync(response.StaticGameConfigId, response.DynamicGameConfigId);

                    // \note: we deserialize with the BaselineConfig instead of the specialized config. For DeletionStatus, it does not matter which we use.
                    IPlayerModelBase playerModel = (IPlayerModelBase)response.Model.Deserialize(resolver: baselineConfig.SharedConfig, response.LogicVersion);

                    // Add player to the list if they are not already deleted
                    if (playerModel.DeletionStatus != PlayerDeletionStatus.Deleted)
                        infos.Add(new PlayerRedeleteInfo(playerRedeleteRecord.PlayerId, playerModel.PlayerName, playerRedeleteRecord.ScheduledDeletionTime, playerRedeleteRecord.DeletionSource));
                }
            }
            _logger.LogInformation("Filtered re-delete player list by player status to {count} rows.", infos.Count);
            return infos;
        }
    }
}
