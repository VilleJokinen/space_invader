// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.Forms;
using Metaplay.Server.Matchmaking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    [MetaSerializableDerived(MetaplayAuditLogEventCodes.MatchmakerRebalanced)]
    public class AsyncMatchmakerRebalanced : MatchmakerEventPayloadBase
    {
        public AsyncMatchmakerRebalanced() { }
        public override string EventTitle       => "Rebalanced";
        public override string EventDescription => "Matchmaker manually rebalanced.";
    }

    [MetaSerializableDerived(MetaplayAuditLogEventCodes.MatchmakerReset)]
    public class AsyncMatchmakerReset : MatchmakerEventPayloadBase
    {
        public AsyncMatchmakerReset() { }
        public override string EventTitle       => "Reset";
        public override string EventDescription => "Matchmaker state reset.";
    }

    [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerEnrolledToMatchmaker)]
    public class PlayerEnrolledToAsyncMatchmaker : PlayerEventPayloadBase
    {
        [MetaMember(1)] public EntityId MatchmakerId { get; private set; }
        PlayerEnrolledToAsyncMatchmaker() { }

        public PlayerEnrolledToAsyncMatchmaker(EntityId matchmakerId)
        {
            MatchmakerId = matchmakerId;
        }

        public override string EventTitle       => "Added to matchmaker";
        public override string EventDescription => $"Player added to matchmaker {MatchmakerId}.";
    }

    [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerRemovedFromMatchmaker)]
    public class PlayerRemovedFromAsyncMatchmaker : PlayerEventPayloadBase
    {
        [MetaMember(1)] public EntityId MatchmakerId { get; private set; }

        PlayerRemovedFromAsyncMatchmaker() { }

        public PlayerRemovedFromAsyncMatchmaker(EntityId matchmakerId)
        {
            MatchmakerId = matchmakerId;
        }

        public override string EventTitle       => "Removed from matchmaker";
        public override string EventDescription => $"Player removed from matchmaker {MatchmakerId}.";
    }


    [AsyncMatchmakingEnabledCondition]
    public class AsyncMatchmakerController : GameAdminApiController
    {
        public class MatchmakingTestResponse
        {
            public int                 NumTries;
            public AsyncMatchmakingResponse Response;
            public MatchmakingTestResponse() { }

            public MatchmakingTestResponse(int numTries, AsyncMatchmakingResponse response)
            {
                NumTries = numTries;
                Response = response;
            }
        }

        public class AsyncMatchmakerInfoResponseValue
        {
            public EntityId                    Id;
            public AsyncMatchmakerInfoResponse Data;
            public string                      QueryJsonType;
            public string                      ModelJsonType;

            public AsyncMatchmakerInfoResponseValue(EntityId id, AsyncMatchmakerInfoResponse data, string queryJsonType, string modelJsonType)
            {
                Id            = id;
                Data          = data;
                QueryJsonType = queryJsonType;
                ModelJsonType = modelJsonType;
            }
        }

        public class MatchmakingBucketInfoResponseValue
        {
            public EntityId               MatchmakerId;
            public int                    BucketPlayerCount;
            public int                    BucketMaxPlayers;
            public int                    MmrLow;
            public int                    MmrHigh;
            public int                    PageIdx;
            public int                    PlayersPerPage;
            public IBucketLabel[]         Labels;
            public int                    LabelHash;
            public List<MatchmakerPlayer> Players;

            public MatchmakingBucketInfoResponseValue(
                EntityId matchmakerId,
                int bucketPlayerCount,
                int bucketMaxPlayers,
                int mmrLow,
                int mmrHigh,
                int pageIdx,
                int playersPerPage,
                IBucketLabel[] labels,
                int labelHash,
                List<MatchmakerPlayer> players)
            {
                MatchmakerId      = matchmakerId;
                BucketPlayerCount = bucketPlayerCount;
                BucketMaxPlayers  = bucketMaxPlayers;
                MmrLow            = mmrLow;
                MmrHigh           = mmrHigh;
                PageIdx           = pageIdx;
                PlayersPerPage    = playersPerPage;
                Players           = players;
                Labels            = labels;
                LabelHash         = labelHash;
            }
        }

        public class MatchmakerPlayer
        {
            public string                      Name    { get; set; }
            public string                      Summary { get; set; }
            public IAsyncMatchmakerPlayerModel Model   { get; set; }

            public MatchmakerPlayer(string name, string summary, IAsyncMatchmakerPlayerModel model)
            {
                Name    = name;
                Summary = summary;
                Model   = model;
            }
        }

        public AsyncMatchmakerController(ILogger<AsyncMatchmakerController> logger, IActorRef adminApi) : base(logger, adminApi) { }

        /// <summary>
        /// API endpoint to get example matchmaker recommendations. Intended for testing, debugging and diagnosing live player matchmaking results.
        ///
        /// Usage: POST /api/matchmakers/{mmId}/test
        ///        with body of type <see cref="AsyncMatchmakerQueryBase"/>
        /// </summary>
        [HttpPost("matchmakers/{mmId}/test")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersTest)]
        public async Task<IActionResult> TestMatchmaking(string mmId)
        {
            EntityId matchmakerId = ParseMatchmakerIdStr(mmId);
            Type queryType = AsyncMatchmakerEntityKindRegistry.ForEntityKind(matchmakerId.Kind).QueryType;
            // Parse body
            AsyncMatchmakerQueryBase requestBody = await ParseBodyAsync<AsyncMatchmakerQueryBase>(queryType);
            if (requestBody.AttackerId == EntityId.None)
                requestBody.AttackerId = EntityId.Create(EntityKindCore.Player, 0);

            AsyncMatchmakingRequest request = new AsyncMatchmakingRequest();
            request.NumRetries = 0;
            request.Query      = requestBody;

            AsyncMatchmakingResponse response = null;

            for (int i = 0; i < 10; i++)
            {
                request.NumRetries = i;
                response           = await AskEntityAsync<AsyncMatchmakingResponse>(matchmakerId, request);

                if (response.ResponseType == MatchmakingResponseType.Success)
                {
                    return Ok(
                        new MatchmakingTestResponse(
                            numTries: request.NumRetries + 1,
                            response: response));
                }
            }

            return Ok(
                new MatchmakingTestResponse(
                    numTries: request.NumRetries + 1,
                    response: response));
        }


        /// <summary>
        /// API endpoint to get info from all matchmakers
        /// Usage: GET /api/matchmakers
        /// </summary>
        [HttpGet("matchmakers")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersView)]
        public async Task<IActionResult> GetMatchmakers()
        {
            (AsyncMatchmakerEntityKind Kind, EntityId Id)[] matchmakers = AsyncMatchmakerEntityKindRegistry.AllMatchmakerKinds.SelectMany(
                kind => kind.GetQueryableMatchmakersOrdered()
                    .Select(mmId => (kind, mmId))).ToArray();

            Task<AsyncMatchmakerInfoResponse[]> responses = Task.WhenAll(
                matchmakers.Select(mm =>
                    AskEntityAsync<AsyncMatchmakerInfoResponse>(mm.Id, AsyncMatchmakerInfoRequest.Instance)
                ));

            List<AsyncMatchmakerInfoResponseValue> responseValues = matchmakers.Zip(
                await responses,
                (mm, infoResponse) =>
                    new AsyncMatchmakerInfoResponseValue(
                        id: mm.Id,
                        data: infoResponse,
                        queryJsonType: MetaFormTypeRegistry.TypeToJsonName(mm.Kind.QueryType),
                        modelJsonType: MetaFormTypeRegistry.TypeToJsonName(mm.Kind.ModelType))).ToList();

            return Ok(responseValues);
        }

        /// <summary>
        /// API endpoint to get info from all matchmakers for a certain player
        /// Usage: GET /api/matchmakers/player/{playerId}
        /// </summary>
        [HttpGet("matchmakers/player/{playerIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersView)]
        public async Task<IActionResult> GetMatchmakersForPlayer(string playerIdStr)
        {
            (AsyncMatchmakerEntityKind Kind, EntityId Id)[] matchmakers = AsyncMatchmakerEntityKindRegistry.AllMatchmakerKinds.SelectMany(
                kind => kind.GetQueryableMatchmakersOrdered()
                    .Select(mmId => (kind, mmId))).ToArray();

            EntityId playerId = ParsePlayerIdStr(playerIdStr);

            Task<AsyncMatchmakerPlayerInfoResponse[]> responses = Task.WhenAll(
                matchmakers.Select(mm =>
                    AskEntityAsync<AsyncMatchmakerPlayerInfoResponse>(mm.Id, new AsyncMatchmakerPlayerInfoRequest(playerId))
                ));

            AsyncMatchmakerPlayerInfoResponse[] responseValues = await responses;

            return Ok(responseValues);
        }

        /// <summary>
        /// API endpoint to get info about a single matchmaker
        /// Usage: GET /api/matchmakers/mmId
        /// </summary>
        [HttpGet("matchmakers/{mmId}")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersView)]
        public async Task<IActionResult> GetMatchmakerInfo(string mmId)
        {
            EntityId matchmakerId = ParseMatchmakerIdStr(mmId);
            AsyncMatchmakerInfoResponse response = await AskEntityAsync<AsyncMatchmakerInfoResponse>(
                matchmakerId,
                AsyncMatchmakerInfoRequest.Instance);

            AsyncMatchmakerInfoResponseValue responseValue = new AsyncMatchmakerInfoResponseValue(
                id: matchmakerId,
                data: response,
                queryJsonType: MetaFormTypeRegistry.TypeToJsonName(AsyncMatchmakerEntityKindRegistry.ForEntityKind(matchmakerId.Kind).QueryType),
                modelJsonType: MetaFormTypeRegistry.TypeToJsonName(AsyncMatchmakerEntityKindRegistry.ForEntityKind(matchmakerId.Kind).ModelType));

            return Ok(responseValue);
        }

        /// <summary>
        /// API endpoint to get info about a single matchmaker bucket
        /// Usage: GET /api/matchmakers/{mmId}/bucket/{bIdx}?page=0&numEntries=20
        /// </summary>
        [HttpGet("matchmakers/{mmId}/bucket/{bHash}")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersView)]
        public async Task<IActionResult> GetMatchmakerBucketInfo(string mmId, int bHash, [FromQuery] int page = 0, [FromQuery] int numEntries = 20)
        {
            EntityId    matchmakerId = ParseMatchmakerIdStr(mmId);
            System.Type modelType    = AsyncMatchmakerEntityKindRegistry.ForEntityKind(matchmakerId.Kind).ModelType;

            AsyncMatchmakerInspectBucketResponse response = await AskEntityAsync<AsyncMatchmakerInspectBucketResponse>(
                matchmakerId,
                new AsyncMatchmakerInspectBucketRequest(bHash, numEntries, page));


            return Ok(
                new MatchmakingBucketInfoResponseValue(
                    matchmakerId: matchmakerId,
                    bucketPlayerCount: response.BucketPlayerCount,
                    bucketMaxPlayers: response.BucketMaxSize,
                    mmrHigh: response.MmrHigh,
                    mmrLow: response.MmrLow,
                    pageIdx: page,
                    playersPerPage: numEntries,
                    labels: response.Labels,
                    labelHash: response.LabelHash,
                    players: await GetPlayerNames(response.GetDeserializedPlayers(modelType))));
        }

        /// <summary>
        /// API endpoint to get the best players of a matchmaker.
        /// Usage: GET /api/matchmakers/{mmId}/top
        /// </summary>
        [HttpGet("matchmakers/{mmId}/top")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersView)]
        public async Task<IActionResult> GetMatchmakerTopPlayers(string mmId)
        {
            const int   playersToReturn = 100;
            EntityId    matchmakerId    = ParseMatchmakerIdStr(mmId);
            System.Type modelType       = AsyncMatchmakerEntityKindRegistry.ForEntityKind(matchmakerId.Kind).ModelType;

            AsyncMatchmakerInfoResponse infoResponse = await AskEntityAsync<AsyncMatchmakerInfoResponse>(
                matchmakerId,
                AsyncMatchmakerInfoRequest.Instance);

            int totalTaken = 0;

            // Get top buckets until we have enough buckets to have more than playersToReturn players.
            IEnumerable<AsyncMatchmakerInfoResponse.BucketInfo> mmrSorted = infoResponse.BucketInfos
                .GroupBy(x => x.MmrHigh)
                .OrderByDescending(x => x.Key)
                .TakeWhile(
                    group =>
                    {
                        int numPlayers = group.Sum(bucket => bucket.NumPlayers);
                        bool needsMore = totalTaken < playersToReturn;
                        totalTaken += numPlayers;
                        return needsMore;
                    }).SelectMany(group => group);

            // Fetch all buckets infos.
            AsyncMatchmakerInspectBucketResponse[] bucketInfos = await Task.WhenAll(
                mmrSorted.Select(
                    bucket => AskEntityAsync<AsyncMatchmakerInspectBucketResponse>(
                        matchmakerId,
                        new AsyncMatchmakerInspectBucketRequest(bucket.LabelHash, playersToReturn, 0))));

            // Get best players from buckets.
            IAsyncMatchmakerPlayerModel[] bestPlayers = bucketInfos
                .SelectMany(x => x.GetDeserializedPlayers(modelType))
                .OrderByDescending(x => x.DefenseMmr)
                .Take(playersToReturn).ToArray();

            List<MatchmakerPlayer> bestPlayersWithNames = await GetPlayerNames(bestPlayers);

            return Ok(bestPlayersWithNames);
        }

        /// <summary>
        /// API endpoint to reset the state of a matchmaker
        /// Usage: POST /api/matchmakers/mmId/reset
        /// </summary>
        [HttpPost("matchmakers/{mmId}/reset")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersAdmin)]
        public async Task<IActionResult> ResetMatchmaker(string mmId)
        {
            EntityId matchmakerId = ParseMatchmakerIdStr(mmId);
            await AskEntityAsync<EntityAskOk>(matchmakerId, AsyncMatchmakerClearStateRequest.Instance);

            // Log into audit logs
            await WriteAuditLogEventAsync(new MatchmakerEventBuilder(matchmakerId, new AsyncMatchmakerReset()));

            return Ok();
        }

        /// <summary>
        /// API endpoint to rebalance the buckets of a matchmaker
        /// Usage: POST /api/matchmakers/{mmId}/rebalance
        /// </summary>
        [HttpPost("matchmakers/{mmId}/rebalance")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersAdmin)]
        public async Task<IActionResult> RebalanceMatchmaker(string mmId)
        {
            EntityId matchmakerId = ParseMatchmakerIdStr(mmId);
            await AskEntityAsync<EntityAskOk>(matchmakerId, AsyncMatchmakerRebalanceBucketsRequest.Instance);

            // Log into audit logs
            await WriteAuditLogEventAsync(new MatchmakerEventBuilder(matchmakerId, new AsyncMatchmakerRebalanced()));

            return Ok();
        }

        /// <summary>
        /// API endpoint to add a player to a matchmaker
        /// Usage: POST /api/matchmakers/{mmId}/add/{playerId}
        /// </summary>
        [HttpPost("matchmakers/{mmId}/add/{playerIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersAdmin)]
        public async Task<IActionResult> EnrollPlayer(string mmId, string playerIdStr)
        {
            EntityId matchmakerId = ParseMatchmakerIdStr(mmId);
            EntityId playerId     = ParsePlayerIdStr(playerIdStr);

            AsyncMatchmakerPlayerEnrollResponse response = await AskEntityAsync<AsyncMatchmakerPlayerEnrollResponse>(matchmakerId, new AsyncMatchmakerPlayerEnrollRequest(playerId: playerId, isRemoval: false));

            if (response.IsSuccess)
            {
                // Log into audit logs
                await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEnrolledToAsyncMatchmaker(matchmakerId)));
                return Ok();
            }

            throw new Exceptions.MetaplayHttpException((int)HttpStatusCode.Conflict, "Unable to add player to matchmaker.", "The player was unable to be added to the matchmaker. Most likely the matchmaking rules prevented this.");
        }

        /// <summary>
        /// API endpoint to remove a player from a matchmaker
        /// Usage: POST /api/matchmakers/{mmId}/remove/{playerId}
        /// </summary>
        [HttpPost("matchmakers/{mmId}/remove/{playerIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiMatchmakersAdmin)]
        public async Task<IActionResult> RemovePlayer(string mmId, string playerIdStr)
        {
            EntityId matchmakerId = ParseMatchmakerIdStr(mmId);
            EntityId playerId     = ParsePlayerIdStr(playerIdStr);

            AsyncMatchmakerPlayerEnrollResponse response = await AskEntityAsync<AsyncMatchmakerPlayerEnrollResponse>(matchmakerId, new AsyncMatchmakerPlayerEnrollRequest(playerId: playerId, isRemoval: true));

            if (response.IsSuccess)
            {
                // Log into audit logs
                await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerRemovedFromAsyncMatchmaker(matchmakerId)));
                return Ok();
            }

            throw new Exceptions.MetaplayHttpException((int)HttpStatusCode.Conflict, "Unable to remove player from matchmaker.", "The player was unable to be removed from the matchmaker.");
        }

        public static EntityId ParseMatchmakerIdStr(string matchmakerId)
        {
            EntityId entityId;
            try
            {
                entityId = EntityId.ParseFromString(matchmakerId);
            }
            catch (FormatException ex)
            {
                throw new Exceptions.MetaplayHttpException(400, $"Matchmaker not found.", $"{matchmakerId} is not a valid entity ID: {ex.Message}");
            }
            return entityId;
        }

        async Task<List<MatchmakerPlayer>> GetPlayerNames(IEnumerable<IAsyncMatchmakerPlayerModel> players)
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            if (activeGameConfig?.BaselineGameConfig == null)
                return new List<MatchmakerPlayer>();

            List<MatchmakerPlayer> playersWithNames = new List<MatchmakerPlayer>();

            // Very inefficiently fetching whole player from db and deserializing.
            // \todo: Optimize this?
            foreach (IAsyncMatchmakerPlayerModel mmPlayer in players)
            {
                PlayerDetails details = await GetPlayerDetailsAsync(mmPlayer.PlayerId.ToString());
                playersWithNames.Add(new MatchmakerPlayer(details.Model.PlayerName, mmPlayer.GetDashboardSummary(), mmPlayer));
            }
            return playersWithNames;
        }
    }
}
