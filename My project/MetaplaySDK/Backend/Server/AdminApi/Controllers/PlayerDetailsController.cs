// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Server.GameConfig;
using Metaplay.Server.InAppPurchase;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Metaplay.Core.Web3;
using Metaplay.Server.Web3;
using Metaplay.Core.Serialization;

#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Server.Guild.InternalMessages;
#endif

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK routes that deal with an individual player's details.
    /// </summary>
    public class PlayerDetailsController : GameAdminApiController
    {
        public PlayerDetailsController(ILogger<PlayerDetailsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerViewed)]
        public class PlayerEventViewed : PlayerEventPayloadBase
        {
            public PlayerEventViewed() { }
            override public string EventTitle => "Viewed";
            override public string EventDescription => "Player details viewed.";
        }

        /// <summary>
        /// HTTP response for an individual player's details
        /// </summary>
        public class PlayerDetailsItem
        {
            public string                                                           Id                      { get; set; }
            public IPlayerModelBase                                                 Model                   { get; set; }
            public int                                                              PersistedSize           { get; set; }
            public List<PlayerIncidentHeader>                                       IncidentHeaders         { get; set; }
            public PlayerDetailsItemGuildInfo                                       Guild                   { get; set; }
            public List<PlayerSegmentInfoBase>                                      MatchingSegments        { get; set; }
            public OrderedDictionary<PlayerExperimentId, PlayerExperimentDetails>   Experiments             { get; set; }
            public PlayerDetailsIAPSubscriptionsExtraInfo                           IAPSubscriptionsExtra   { get; set; }
            public List<PlayerDetailsNftInfo>                                       Nfts                    { get; set; }
        }
        public class PlayerDetailsItemGuildInfo
        {
            public string                       Id              { get; set; }
            public string                       DisplayName     { get; set; }
            public string                       Role            { get; set; }
        }

        public class PlayerDetailsIAPSubscriptionsExtraInfo
        {
            /// <remarks>
            /// The key is the originalTransactionId of the subscription instance,
            /// the same as in <see cref="SubscriptionModel.SubscriptionInstances"/>.
            /// </remarks>
            public OrderedDictionary<string, InstanceInfo> Instances { get; set; }

            public PlayerDetailsIAPSubscriptionsExtraInfo(OrderedDictionary<string, InstanceInfo> instances) { Instances = instances; }

            public class InstanceInfo
            {
                public List<EntityId> OtherPlayers { get; set; }

                public InstanceInfo(List<EntityId> otherPlayers) { OtherPlayers = otherPlayers; }
            }
        }

        public struct PlayerDetailsNftInfo
        {
            public NftCollectionId CollectionId;
            public NftId TokenId;
            public string OwnerAddress;
            public bool IsMinted;
            public string TypeName;
            public string Name;
            public string Description;

            public PlayerDetailsNftInfo(MetaNft nft)
            {
                NftMetadataSpec metadataSpec = NftTypeRegistry.Instance.GetSpec(nft.GetType()).MetadataSpec;
                CollectionId = NftTypeRegistry.Instance.GetCollectionId(nft);
                TokenId = nft.TokenId;
                OwnerAddress = nft.OwnerAddress.ToString();
                IsMinted = nft.IsMinted;
                TypeName = nft.GetType().Name;
                Name = metadataSpec.TryGetName(nft);
                Description = metadataSpec.TryGetDescription(nft);
            }
        }

        /// <summary>
        /// API endpoint to return detailed information about a single player
        /// Usage:  GET /api/players/{PLAYERID}
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}
        /// </summary>
        [HttpGet("players/{playerIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersView)]
        public async Task<ActionResult<PlayerDetailsItem>> Get(string playerIdStr)
        {
            // Parse parameters
            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);

            // Query incident reports for player
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Low);
            List<PlayerIncidentHeader> incidentHeaders = await db.QueryPlayerIncidentHeadersAsync(details.PlayerId, pageSize: 50).ConfigureAwait(false);

            // Query Guild data
            PlayerDetailsItemGuildInfo guild = null;
            #if !METAPLAY_DISABLE_GUILDS
            if (details.Model.GuildState.GuildId != EntityId.None)
            {
                var guildResponse = await AskEntityAsync<InternalGuildPlayerDashboardInfoResponse>(details.Model.GuildState.GuildId, new InternalGuildPlayerDashboardInfoRequest(EntityId.ParseFromString(playerIdStr))).ConfigureAwait(false);
                if (guildResponse.IsMember)
                {
                    guild = new PlayerDetailsItemGuildInfo()
                    {
                        Id          = details.Model.GuildState.GuildId.ToString(),
                        DisplayName = guildResponse.DisplayName,
                        Role        = guildResponse.Role.ToString(),
                    };
                }
            }
            #endif

            // Query experiment data
            InternalPlayerGetPlayerExperimentDetailsResponse experimentDetails = await AskEntityAsync<InternalPlayerGetPlayerExperimentDetailsResponse>(details.PlayerId, InternalPlayerGetPlayerExperimentDetailsRequest.Instance);

            // Resolve matching player segments
            ISharedGameConfig           sharedGameConfig = details.Model.GameConfig;
            List<PlayerSegmentInfoBase> matchingSegments;
            try
            {
                matchingSegments = sharedGameConfig.PlayerSegments.Values.Where(segmentInfo => segmentInfo.MatchesPlayer(details.Model)).Cast<PlayerSegmentInfoBase>().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("Got exception when trying to resolve matching segments for player {PlayerId}: {Exception}", details.PlayerId, ex);
                matchingSegments = null;
            }

            // The Dashboard polls this endpoint every few seconds when we are looking at
            // a player's details - we don't want to log all of these events
            if (ShouldWriteAuditLogEvent(GetUserId(), details.PlayerId))
                await WriteAuditLogEventAsync(new PlayerEventBuilder(details.PlayerId, new PlayerEventViewed()));

            // For each IAP subscription instance, look up other players who have used the same original transaction.
            OrderedDictionary<string, PlayerDetailsIAPSubscriptionsExtraInfo.InstanceInfo> iapSubscriptionInstanceExtraInfos = new OrderedDictionary<string, PlayerDetailsIAPSubscriptionsExtraInfo.InstanceInfo>();
            foreach (SubscriptionModel subscription in details.Model.IAPSubscriptions.Subscriptions.Values)
            {
                foreach ((string originalTransactionId, SubscriptionInstanceModel instance) in subscription.SubscriptionInstances)
                {
                    List<EntityId> otherPlayers = new List<EntityId>();

                    List<PersistedInAppPurchaseSubscription> subscriptionMetadatas = await db.GetIAPSubscriptionMetadatas(originalTransactionId).ConfigureAwait(false);
                    foreach (PersistedInAppPurchaseSubscription subscriptionMetadata in subscriptionMetadatas)
                    {
                        EntityId playerId = subscriptionMetadata.PlayerEntityId;
                        if (playerId != details.PlayerId)
                            otherPlayers.Add(playerId);
                    }

                    iapSubscriptionInstanceExtraInfos.Add(originalTransactionId, new PlayerDetailsIAPSubscriptionsExtraInfo.InstanceInfo(otherPlayers));
                }
            }
            PlayerDetailsIAPSubscriptionsExtraInfo iapSubscriptionsExtraInfo = new PlayerDetailsIAPSubscriptionsExtraInfo(iapSubscriptionInstanceExtraInfos);

            List<PlayerDetailsNftInfo> nftInfos = new List<PlayerDetailsNftInfo>();
            if (new Web3EnabledCondition().IsEnabled)
            {
                List<MetaNft> nfts = (await AskEntityAsync<QueryNftsResponse>(NftManager.EntityId, new QueryNftsRequest(owner: details.PlayerId)))
                                     .Items
                                     .Where(item => item.IsSuccess)
                                     .Select(item => item.Nft)
                                     .ToList();
                IGameConfigDataResolver resolver = GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;
                MetaSerialization.ResolveMetaRefs(ref nfts, resolver);

                nftInfos = nfts.Select(nft => new PlayerDetailsNftInfo(nft)).ToList();
            }

            // Respond to browser
            return new PlayerDetailsItem
            {
                Id                      = playerIdStr,
                Model                   = details.Model,
                PersistedSize           = details.Persisted.Payload.Length,
                IncidentHeaders         = incidentHeaders,
                Guild                   = guild,
                MatchingSegments        = matchingSegments,
                Experiments             = experimentDetails.Details,
                IAPSubscriptionsExtra   = iapSubscriptionsExtraInfo,
                Nfts                    = nftInfos,
            };
        }

        /// <summary>
        /// Wrapper class to hold information to be returned by the /raw endpoint. This information has a title (which
        /// is displayed in the Dashboard) and either Data or Error (only one of these is returned to the Dashboard).
        /// </summary>
        public class RawResult<T>
        {
            public RawResult(string title)
            {
                Title = title;
                Error = new RawError("Data or Error not set");
            }

            public string Title { get; private set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public T Data { get; private set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public RawError Error { get; private set; }

            public void SetData(T data)
            {
                Data  = data;
                Error = null;
            }

            public void SetError(RawError error)
            {
                Data  = default;
                Error = error;
            }
        }

        /// <summary>
        /// Holds information about an error.
        /// </summary>
        public class RawError
        {
            public RawError(string title, string details = null)
            {
                Title   = title;
                Details = details;
            }
            public string Title   { get; private set; }
            public string Details { get; private set; }
        }

        /// <summary>
        /// Holds metadata about a player.
        /// </summary>
        public class PlayerMetaData
        {
            public PlayerMetaData(EntityId id, bool isDeveloper)
            {
                Id          = id;
                IsDeveloper = isDeveloper;
            }
            public EntityId Id          { get; private  set; }
            public bool     IsDeveloper { get; private  set; }
        }

        /// <summary>
        /// API endpoint to return detailed information about a single player in a safe way, ie:
        /// handling potential errors and returning as much useful information as possible in all
        /// cases
        /// Usage:  GET /api/players/{PLAYERID}/raw
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/raw
        /// </summary>
        [HttpGet("players/{playerIdStr}/raw")]
        [RequirePermission(MetaplayPermissions.ApiPlayersView)]
        public async Task<ActionResult<object>> GetRaw(string playerIdStr)
        {
            // Create holders for all the different types of information that we want to return to the Dashboard.
            RawResult<PlayerMetaData>               playerMetaData            = new RawResult<PlayerMetaData>("Player Metadata");
            RawResult<PersistedPlayerBase>          persistedPlayer           = new RawResult<PersistedPlayerBase>("Persisted Player");
            RawResult<InternalEntityStateResponse>  playerStateResponse       = new RawResult<InternalEntityStateResponse>("Player State Response");
            RawResult<FullGameConfig>               specializedConfig         = new RawResult<FullGameConfig>("Specialized Config");
            RawResult<GameConfigMetaData>           specializedConfigMetadata = new RawResult<GameConfigMetaData>("Specialized Config Metadata");
            RawResult<IPlayerModelBase>             playerModel               = new RawResult<IPlayerModelBase>("Player Model");

            // Get metadata about the player
            try
            {
                EntityId playerId = EntityId.ParseFromString(playerIdStr);
                if (!playerId.IsOfKind(EntityKindCore.Player))
                    throw new Exception($"Player ID {playerIdStr} is not a player entity.");

                bool isDeveloper = GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(playerId);
                playerMetaData.SetData(new PlayerMetaData(playerId, isDeveloper));
            }
            catch (Exception ex)
            {
                playerMetaData.SetError(new RawError("PlayerId was not valid", ex.ToString()));
            }

            // Get PersistedPlayer
            if (playerMetaData.Data != null)
            {
                try
                {
                    persistedPlayer.SetData(await MetaDatabase.Get().TryGetAsync<PersistedPlayerBase>(playerIdStr).ConfigureAwait(false));
                    if (persistedPlayer.Data != null)
                    {
                        // The Dashboard polls this endpoint every few seconds when we are looking at
                        // a player's details - we don't want to log all of these events
                        if (ShouldWriteAuditLogEvent(GetUserId(), playerMetaData.Data.Id))
                            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerMetaData.Data.Id, new PlayerEventViewed()));

                        if (persistedPlayer.Data.Payload == null)
                            persistedPlayer.SetError(new RawError("PlayerId is allocated but player has not yet been initialized"));
                    }
                    else
                        persistedPlayer.SetError(new RawError("No such player in database"));
                }
                catch (Exception ex)
                {
                    persistedPlayer.SetError(new RawError("Failed to get PersistedPlayerBase", ex.ToString()));
                }
            }
            else
                persistedPlayer.SetError(new RawError("Cannot get PersistedPlayer without a valid PlayerMetaData"));

            // Get PlayerStateResponse
            if (persistedPlayer.Data != null)
            {
                try
                {
                    playerStateResponse.SetData(await AskEntityAsync<InternalEntityStateResponse>(playerMetaData.Data.Id, InternalEntityStateRequest.Instance));
                }
                catch (Exception ex)
                {
                    playerStateResponse.SetError(new RawError("Failed to get playerStateResponse", ex.ToString()));
                }
            }
            else
                playerStateResponse.SetError(new RawError("Cannot get playerStateResponse without a valid PersistedPlayer"));

            // Get SpecializedConfig
            if (playerStateResponse.Data != null)
            {
                try
                {
                    specializedConfig.SetData(await ServerGameConfigProvider.Instance.GetSpecializedGameConfigAsync(playerStateResponse.Data.StaticGameConfigId, playerStateResponse.Data.DynamicGameConfigId, playerStateResponse.Data.SpecializationKey));
                }
                catch (Exception ex)
                {
                    specializedConfig.SetError(new RawError("Could not get Game Config for player", ex.ToString()));
                }
            }
            else
                specializedConfig.SetError(new RawError("Cannot get Game Config without a valid PlayerStateResponse"));

            // We don't want to send the whole SpecializedConfig to the Dashboard, just the Metadata part
            if (specializedConfig.Data != null)
                specializedConfigMetadata.SetData(specializedConfig.Data.MetaData);
            else
                specializedConfigMetadata.SetError(specializedConfig.Error);

            // Get PlayerModel
            if (specializedConfig.Data != null)
            {
                try
                {
                    playerModel.SetData((IPlayerModelBase)playerStateResponse.Data.Model.Deserialize(resolver: specializedConfig.Data.SharedConfig, playerStateResponse.Data.LogicVersion));
                    playerModel.Data.GameConfig = specializedConfig.Data.SharedConfig;
                    playerModel.Data.LogicVersion = playerStateResponse.Data.LogicVersion;
                }
                catch (Exception ex)
                {
                    playerModel.SetError(new RawError("Failed to deserialize PlayerModel", ex.ToString()));
                }
            }
            else
                playerModel.SetError(new RawError("Cannot deserialize PlayerModel without a valid SpecializedConfig"));

            // Return the results
            return new JsonResult(
                new
                {
                    PlayerMetaData            = playerMetaData,
                    PersistedPlayer           = persistedPlayer,
                    PlayerStateResponse       = playerStateResponse,
                    SpecializedConfigMetadata = specializedConfigMetadata,
                    PlayerModel               = playerModel,
                },
                AdminApiJsonSerialization.UntypedSerializationSettings);
        }

        /// <summary>
        /// Maintains a list of which playerIds each user has viewed and uses this
        /// list to deny creating new audit log events too frequently. Writing is
        /// allowed the first time a user views a player, and then not allowed again
        /// until that user has /not/ viewed the player for at least 5 minutes.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="playerId"></param>
        /// <returns></returns>
        private bool ShouldWriteAuditLogEvent(string userId, EntityId playerId)
        {
            lock (_playerViewCacheLock)
            {
                // Remove all expired entries
                MetaTime timeNow = MetaTime.Now;
                MetaDuration timeoutDuration = MetaDuration.FromMinutes(5);
                foreach ((string user, _) in PlayerViewCache)
                {
                    PlayerViewCache[user] = PlayerViewCache[user]
                        .Where(idTimePair => (timeNow - PlayerViewCache[user][idTimePair.Key]) < timeoutDuration)
                        .ToDictionary(item => item.Key, item => item.Value);
                }

                // Figure out if we need to write the log event for this player view
                bool shouldWriteEvent = true;
                if (PlayerViewCache.ContainsKey(userId))
                    // If we already have an existing entry for this userId and this
                    // playerId then we don't want to log the view
                    shouldWriteEvent = !PlayerViewCache[userId].ContainsKey(playerId);
                else
                    // We've not seen this userId before so we need to create a new
                    // entry for it
                    PlayerViewCache[userId] = new Dictionary<EntityId, MetaTime>();

                // Remember when this userId last wanted to log the view of this playerId
                PlayerViewCache[userId][playerId] = timeNow;

                return shouldWriteEvent;
            }
        }

        /// <summary>
        /// Dictionary of <user-id, <player-entity-id, last-viewed-timestamp>> to tell us
        /// when a user last viewed a player
        /// </summary>
        private static Dictionary<string, Dictionary<EntityId, MetaTime>> PlayerViewCache = new Dictionary<string, Dictionary<EntityId, MetaTime>>();
        private static readonly object _playerViewCacheLock = new object();
    }
}
