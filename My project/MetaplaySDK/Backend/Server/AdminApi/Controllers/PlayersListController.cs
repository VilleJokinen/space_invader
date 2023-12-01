// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for routes that display and/or search for multiple players.
    /// </summary>
    public class PlayersListController : GameAdminApiController
    {
        public PlayersListController(ILogger<PlayersListController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// HTTP response for listing players
        /// </summary>
        public class PlayerListItem
        {
            public string       Id                          { get; set; }
            public bool         DeserializedSuccessfully    { get; set; }

            // When DeserializedSuccessfully is true
            public string       Name                        { get; set; }
            public int          Level                       { get; set; }
            public DateTime     CreatedAt                   { get; set; }
            public DateTime     LastLoginAt                 { get; set; }
            public string       DeletionStatus              { get; set; }
            public bool         IsBanned                    { get; set; }
            public F64          TotalIapSpend               { get; set; }
            public bool         IsDeveloper                 { get; set; }

            // When DeserializedSuccessfully is false
            public string       DeserializationException    { get; set; } // \note Specifically stringifying and *not* using Exception as member the type, since that appears to break json serialization. \todo Investigate?
        }

        PlayerListItem MakePlayerListItem(PersistedPlayerBase player, IGameConfigDataResolver resolver, int activeLogicVersion)
        {
            if (player.Payload == null)
            {
                return new PlayerListItem
                {
                    Id = player.EntityId.ToString(),
                    DeserializedSuccessfully = true,
                    Name = "<Uninitialized>"
                };
            }

            try
            {
                // \note[jarkko]: should not use global resolver. Each Model should be deserialized with the appropriate resolver. In practice
                //                this is a bit involved (should wake actor to run migrations et al.), so let's just hope for the best :)
                PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKindCore.Player);
                IPlayerModelBase model = entityConfig.DeserializeDatabasePayload<IPlayerModelBase>(player.Payload, resolver, activeLogicVersion);
                return new PlayerListItem
                {
                    Id = player.EntityId.ToString(),
                    DeserializedSuccessfully = true,
                    Name = model.PlayerName,
                    Level = model.PlayerLevel,
                    CreatedAt = model.Stats.CreatedAt.ToDateTime(),
                    LastLoginAt = model.Stats.LastLoginAt.ToDateTime(),
                    DeletionStatus = model.DeletionStatus.ToString(),
                    IsBanned = model.IsBanned,
                    TotalIapSpend = model.TotalIapSpend,
                    IsDeveloper = GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(model.PlayerId),
                };
            }
            catch (Exception exception)
            {
                // Catch all exceptions, not just MetaSerializationException.
                // In particular, anything could be thrown from a [MetaOnDeserialized] hook.
                return new PlayerListItem
                {
                    Id = player.EntityId.ToString(),
                    DeserializedSuccessfully = false,
                    DeserializationException = exception.ToString(), // \note Specifically stringifying and *not* using Exception as member the type, since that appears to break json serialization. \todo Investigate?
                };
            }
        }

        /// <summary>
        /// API endpoint to return basic information about all players in the system, with an optional query string that
        /// works to filter players based on entity id and player name
        /// Usage:  GET /api/players
        /// Test:   curl http://localhost:5550/api/players?query=QUERY&count=LIMIT
        /// </summary>
        [HttpGet("players")]
        [RequirePermission(MetaplayPermissions.ApiPlayersView)]
        public async Task<ActionResult<IEnumerable<PlayerListItem>>> Players([FromQuery] string query = "", [FromQuery] int count = 10)
        {
            // `query` can be null if passed as 'query='. The expected behaviour here is that `query` should be set to the default value '""',
            // so let's fix it up ourselves.
            if (query == null)
                query = "";

            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            IGameConfigDataResolver resolver = activeGameConfig.BaselineGameConfig.SharedConfig;
            int activeLogicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;

            // Search by Id
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Low);
            // \note the "name search" also searches by case-insensitive player id so this query is technically not needed
            List<PersistedPlayerBase> persistedPlayers = await db.SearchEntitiesByIdAsync<PersistedPlayerBase>(EntityKindCore.Player, count, query);

            // Fill the rest with search by Name fragments
            // \todo [petri] better algorithm for resolving/sorting matches
            if (persistedPlayers.Count < count)
            {
                List<string> searchStrings = SearchUtil.ComputeSearchStringsForQuery(query, maxLengthCodepoints: PersistedPlayerSearch.MaxPartLengthCodepoints, limit: PersistedPlayerSearch.MaxNameSearchQueries);
                string kindPrefix = EntityKindCore.Player.Name + ':';
                // Case-insensitive player id is stored without EntityKind prefix
                if (query.StartsWith(kindPrefix, StringComparison.InvariantCulture))
                {
                    searchStrings.Add(query.Remove(0, kindPrefix.Length));
                }
                foreach (string searchStr in searchStrings)
                {
                    int remainingCount = count - persistedPlayers.Count;
                    if (remainingCount <= 0)
                        break;

                    List<EntityId> nameResults = await db.SearchPlayerIdsByNameAsync(remainingCount, searchStr);
                    HashSet<EntityId> existingResults = new HashSet<EntityId>(persistedPlayers.Select(g => EntityId.ParseFromString(g.EntityId)));

                    persistedPlayers.AddRange(
                        await Task.WhenAll(nameResults
                            .Except(existingResults)
                            .Select((EntityId id) => db.TryGetAsync<PersistedPlayerBase>(id.ToString()))
                            )
                        );
                }
            }

            IEnumerable<PlayerListItem> result =
                persistedPlayers
                .Where(player => player != null) // Only keep players that exists (read replica out of sync?)
                .Take(count)
                .Select(persisted => MakePlayerListItem(persisted, resolver, activeLogicVersion));

            return Ok(result);
        }

        /// <summary>
        /// API Endpoint to return information about active players - these are players who are actively using the game right now
        /// Usage:  GET /api/players/activePlayers
        /// Test:   curl http://localhost:5550/api/players/activePlayers
        /// </summary>
        [HttpGet("players/activePlayers")]
        [RequirePermission(MetaplayPermissions.ApiPlayersView)]
        public async Task<ActionResult<IEnumerable<IActiveEntityInfo>>> ActivePlayers()
        {
            ActiveEntitiesRequest request = new ActiveEntitiesRequest(EntityKindCore.Player);
            ActiveEntitiesResponse response = await AskEntityAsync<ActiveEntitiesResponse>(StatsCollectorManager.EntityId, request);
            return new ActionResult<IEnumerable<IActiveEntityInfo>>(response.ActiveEntities);
        }

        /// <summary>
        /// API endpoint to get developer players
        /// Usage:  GET /api/players/developers
        /// Test:   curl http://localhost:5550/api/players/developers
        /// </summary>
        [HttpGet("players/developers")]
        [RequirePermission(MetaplayPermissions.ApiPlayersView)]
        public async Task<ActionResult<IEnumerable<PlayerListItem>>> GetDeveloperPlayers()
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            if (activeGameConfig?.BaselineGameConfig == null)
                return Ok(Array.Empty<PlayerListItem>());

            IGameConfigDataResolver resolver           = activeGameConfig.BaselineGameConfig.SharedConfig;
            int                     activeLogicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;
            IEnumerable<EntityId>   developerPlayerIds = GlobalStateProxyActor.ActiveDevelopers.Get().DeveloperPlayers;

            // Get each developer player's details
            List<PlayerListItem> developerPlayerInfos = new List<PlayerListItem>();
            foreach (EntityId entityId in developerPlayerIds)
            {
                try
                {
                    PlayerDetails details = await GetPlayerDetailsAsync(entityId.ToString());
                    developerPlayerInfos.Add(MakePlayerListItem(details.Persisted, resolver, activeLogicVersion));
                }
                catch (Exception ex)
                {
                    developerPlayerInfos.Add(new PlayerListItem
                    {
                        Id                       = entityId.ToString(),
                        DeserializedSuccessfully = false,
                        DeserializationException = ex.ToString()
                    });
                }
            }

            return Ok(developerPlayerInfos.ToArray());
        }

        /// <summary>
        /// HTTP response for bulk validating a list of player Ids.
        /// </summary>
        public class BulkListRequest
        {
            public List<string> PlayerIds;
        }
        public struct BulkListInfo
        {
            public string PlayerIdQuery;
            public bool ValidId;
            public PlayerListItem PlayerData;
        }

        /// <summary>
        /// API endpoint to validate a bunch of Player Ids in one go.
        /// </summary>
        /// Usage:  POST /api/players/bulkValidate
        /// Test:   curl --request POST localhost:5550/api/players/bulkValidate --header 'Content-Type: application/json' --data-raw '{"playerIds":["Player:000000000c"]}'
        [HttpPost("players/bulkValidate")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersView)]
        public async Task<ActionResult<IEnumerable<BulkListInfo>>> BulkList()
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            IGameConfigDataResolver resolver = activeGameConfig.BaselineGameConfig.SharedConfig;
            int activeLogicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;

            MetaDatabase db = MetaDatabase.Get(QueryPriority.Low);
            BulkListRequest request = await ParseBodyAsync<BulkListRequest>();
            List<BulkListInfo> results = (await Task.WhenAll(request.PlayerIds.Distinct().Select(async playerIdStr =>
            {
                BulkListInfo info = new();
                info.PlayerIdQuery = playerIdStr;

                EntityId playerId = EntityId.None;
                try
                {
                    playerId = EntityId.ParseFromString(playerIdStr);
                }
                catch (FormatException) {}

                if (!playerId.IsOfKind(EntityKindCore.Player))
                    return info;

                info.ValidId = true;

                // Note: using low-priority db throttles number of simultaneous requests
                PersistedPlayerBase player = await db.TryGetAsync<PersistedPlayerBase>(playerIdStr).ConfigureAwait(false);
                if (player != null)
                    info.PlayerData = MakePlayerListItem(player, resolver, activeLogicVersion);

                return info;
            }))).ToList();

            return new ActionResult<IEnumerable<BulkListInfo>>(results);
        }
    }
}
