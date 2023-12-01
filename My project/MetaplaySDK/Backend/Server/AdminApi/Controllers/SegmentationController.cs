// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Json;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Server.NotificationCampaign;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class SegmentationController : GameAdminApiController
    {

        public SegmentationController(ILogger<SegmentationController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// HTTP response
        /// </summary>
        public class SegmentationResponse
        {
            public List<SegmentData>        Segments        { get; private set; }
            public MetaTime?                LastUpdateTime { get; private set; }
            public SegmentationResponse(List<SegmentData> segments, MetaTime? lastUpdateTime)
            {
                Segments = segments;
                LastUpdateTime = lastUpdateTime;
            }
        }

        /// <summary>
        /// HTTP response for single segment
        /// </summary>
        public class SingleSegmentResponse
        {
            public SegmentData Details        { get; private set; }
            public MetaTime?   LastUpdateTime { get; private set; }
            public SingleSegmentResponse(SegmentData segmentData, MetaTime? lastUpdateTime)
            {
                Details        = segmentData;
                LastUpdateTime = lastUpdateTime;
            }
        }

        /// <summary>
        /// Container for a single segment's data
        /// </summary>
        public struct SegmentData
        {
            public SegmentData(PlayerSegmentInfoBase info, long? sizeEstimate)
            {
                Info = info;
                UsedBy = new List<UsedByData>();
                SizeEstimate = sizeEstimate;
            }

            [ForceSerializeByValue]
            public PlayerSegmentInfoBase Info;
            public List<UsedByData> UsedBy;
            public long? SizeEstimate;
        }

        /// <summary>
        /// Contains Dashboard-facing information about how a segment is referenced by another feature
        /// </summary>
        public class UsedByData
        {
            public string Type;
            public string Subtype;
            public string Id;
            public string DisplayName;
            // Activable-only info
            public string ActivableCategoryId;
            public string ActivableCategoryName;

            public UsedByData(string type, string subtype, string id, string displayName)
            {
                Type         = type;
                Subtype      = subtype;
                Id           = id;
                DisplayName  = displayName;
                ActivableCategoryId   = "";
                ActivableCategoryName = "";
            }

            public UsedByData(string type, string subtype, string id, string displayName, string activableCategoryId, string activableCategoryName)
            {
                Type         = type;
                Subtype      = subtype;
                Id           = id;
                DisplayName  = displayName;
                ActivableCategoryId   = activableCategoryId;
                ActivableCategoryName = activableCategoryName;
            }

            public static UsedByData FromBroadcast(BroadcastMessage broadcast)
            {
                return new UsedByData("Broadcast", "", broadcast.Params.Id.ToString(CultureInfo.InvariantCulture), broadcast.Params.Name);
            }

            public static UsedByData FromNotification(NotificationCampaignSummary notification)
            {
                return new UsedByData("Notification", "", notification.Id.ToString(CultureInfo.InvariantCulture), notification.Params.Name);
            }

            public static UsedByData FromSegment(PlayerSegmentInfoBase segment)
            {
                return new UsedByData("Segment", "", segment.SegmentId.ToString(), segment.DisplayName);
            }

            public static UsedByData FromActivable(MetaActivableRepository.KindSpec kind, object activableId, IMetaActivableConfigData info, ActivablesUtil.ActivablesMetadata metaData)
            {
                return new UsedByData("Activable", kind.Id.ToString(), Util.ObjectToStringInvariant(activableId), info.DisplayName, kind.CategoryId.ToString(), metaData.Categories[kind.CategoryId].ShortSingularDisplayName);
            }

            public static UsedByData FromMetaOffer(MetaOfferInfoBase offer)
            {
                return new UsedByData("Offer", "", offer.OfferId.ToString(), offer.DisplayName);
            }

            public static UsedByData FromExperiment(PlayerExperimentId id, PlayerExperimentGlobalStatistics statistics)
            {
                return new UsedByData("Experiment", "", id.ToString(), statistics.DisplayName);
            }
        }

        /// <summary>
        /// API endpoint to get segmentation info
        /// Usage: GET /api/segmentation
        /// </summary>
        [HttpGet("segmentation")]
        [RequirePermission(MetaplayPermissions.ApiSegmentationView)]
        public async Task<IActionResult> GetSegmentation()
        {
            // \note For now, segments live simply in GameConfig. So we just return that, along with some info about where each segment is used.

            // Get totalPlayerCount from StatsCollector
            int totalPlayerCount = (await AskEntityAsync<StatsCollectorDatabaseEntityCountResponse>(StatsCollectorManager.EntityId, new StatsCollectorDatabaseEntityCountRequest(EntityKindCore.Player))).EntityCount;

            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            ISharedGameConfig sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;

            SegmentSizeEstimateResponse segmentSizeResponse = await AskEntityAsync<SegmentSizeEstimateResponse>(PlayerSegmentSizeEstimatorActor.EntityId, SegmentSizeEstimateRequest.Instance);

            // Populate segments
            Dictionary<PlayerSegmentId, SegmentData> segments = sharedGameConfig.PlayerSegments.Values.ToDictionary(x => x.SegmentId, x =>
            {
                long? numPlayersInSegment = PlayerTargetingUtil.TryEstimateSegmentAudienceSize(totalPlayerCount, x.SegmentId, segmentSizeResponse.SegmentEstimates);
                return new SegmentData(x, numPlayersInSegment);
            });

            // Fill in usedByInfos...
            await FillSegmentUsedByReferencesAsync(segments, sharedGameConfig, activeGameConfig);

            return Ok(new SegmentationResponse(segments.Values.ToList(), segmentSizeResponse.LastEstimateAt));
        }

        /// <summary>
        /// API endpoint to get the info of a single segment with the SegmentId: {segmentIdStr}
        /// Usage: GET /api/segmentation/{segmentIdStr}
        /// </summary>
        [HttpGet("segmentation/{segmentIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiSegmentationView)]
        public async Task<IActionResult> GetSegmentAsync(string segmentIdStr)
        {
            if (string.IsNullOrEmpty(segmentIdStr))
                throw new Exceptions.MetaplayHttpException(400, "Failed to get segment.", $"Invalid segment Id: {segmentIdStr}");

            PlayerSegmentId segmentId = PlayerSegmentId.FromString(segmentIdStr);

            // Get totalPlayerCount from StatsCollector
            int totalPlayerCount = (await AskEntityAsync<StatsCollectorDatabaseEntityCountResponse>(StatsCollectorManager.EntityId, new StatsCollectorDatabaseEntityCountRequest(EntityKindCore.Player))).EntityCount;

            ActiveGameConfig  activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            ISharedGameConfig sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;

            SegmentSizeEstimateResponse segmentSizeResponse = await AskEntityAsync<SegmentSizeEstimateResponse>(PlayerSegmentSizeEstimatorActor.EntityId, SegmentSizeEstimateRequest.Instance);

            // Get size estimate
            long? numPlayersInSegment = PlayerTargetingUtil.TryEstimateSegmentAudienceSize(totalPlayerCount, segmentId, segmentSizeResponse.SegmentEstimates);

            // Get segment data
            if (!sharedGameConfig.PlayerSegments.TryGetValue(segmentId, out PlayerSegmentInfoBase foundSegment))
                throw new Exceptions.MetaplayHttpException(404, "Segment not found", $"The segment {segmentIdStr} was not found in the game config.");

            SegmentData segment = new SegmentData(foundSegment, numPlayersInSegment);

            // Fill in UsedBy references
            await FillSegmentUsedByReferencesAsync(segment, sharedGameConfig, activeGameConfig);

            return Ok(new SingleSegmentResponse(segment, segmentSizeResponse.LastEstimateAt));
        }

        // Helper for calling with a single segment argument.
        /// <inheritdoc cref="FillSegmentUsedByReferencesAsync(SegmentData, ISharedGameConfig, ActiveGameConfig)"/>
        protected async Task FillSegmentUsedByReferencesAsync(SegmentData segment, ISharedGameConfig sharedGameConfig, ActiveGameConfig activeGameConfig)
        {
            await FillSegmentUsedByReferencesAsync(new Dictionary<PlayerSegmentId, SegmentData>() { { segment.Info.SegmentId, segment } }, sharedGameConfig, activeGameConfig);
        }

        /// <summary>
        /// Fills in the <see cref="UsedByData"/> references into the given <see cref="SegmentData"/>s. Finds references in all broadcasts, notification campaigns,
        /// other segments, activables, metaoffers and experiments to the given segments and adds the <see cref="UsedByData"/> reference info into the <see cref="SegmentData"/>.
        /// </summary>
        protected async Task FillSegmentUsedByReferencesAsync(Dictionary<PlayerSegmentId, SegmentData> segments, ISharedGameConfig sharedGameConfig, ActiveGameConfig activeGameConfig)
        {
            // UsedBy: Broadcasts
            GlobalStateSnapshot snapshot = await AskEntityAsync<GlobalStateSnapshot>(GlobalStateManager.EntityId, GlobalStateRequest.Instance);
            GlobalState globalState = snapshot.GlobalState.Deserialize(resolver: null, logicVersion: null);
            globalState.BroadcastMessages.Values
                .Where(broadcastMessage => broadcastMessage.Params.TargetCondition != null)
                .ToList()
                .ForEach(broadcastMessage =>
                {
                    UsedByData broadcastData = UsedByData.FromBroadcast(broadcastMessage);
                    foreach (PlayerSegmentId segmentId in broadcastMessage.Params.TargetCondition.GetSegmentReferences())
                    {
                        if (segments.ContainsKey(segmentId))
                            segments[segmentId].UsedBy.Add(broadcastData);
                    }
                });

            // UsedBy: Notifications
            ListNotificationCampaignsRequest request = new ListNotificationCampaignsRequest();
            ListNotificationCampaignsResponse response = await AskEntityAsync<ListNotificationCampaignsResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);
            response.NotificationCampaigns
                .Where(notification => notification.Params.TargetCondition != null)
                .ToList()
                .ForEach(notification =>
                {
                    UsedByData notificationData = UsedByData.FromNotification(notification);
                    foreach (PlayerSegmentId segmentId in notification.Params.TargetCondition.GetSegmentReferences())
                    {
                        if (segments.ContainsKey(segmentId))
                            segments[segmentId].UsedBy.Add(notificationData);
                    }
                });

            sharedGameConfig.PlayerSegments.Values
                .ToList()
                .ForEach(segment =>
                {
                    UsedByData segmentData = UsedByData.FromSegment(segment);
                    foreach (PlayerSegmentId segmentId in segment.PlayerCondition.GetSegmentReferences())
                    {
                        if (segments.ContainsKey(segmentId))
                            segments[segmentId].UsedBy.Add(segmentData);
                    }
                });

            // UsedBy: Activables
            ActivablesUtil.ActivablesMetadata activablesMetadata = ActivablesUtil.GetMetadata();

            foreach (MetaActivableRepository.KindSpec kind in MetaActivableRepository.Instance.AllKinds.Values)
            {
                if (kind.GameConfigLibrary == null)
                    continue;

                IGameConfigLibraryEntry library = kind.GameConfigLibrary.GetMemberValue(sharedGameConfig);
                foreach ((object activableId, object activableInfoObject) in library.EnumerateAll())
                {
                    IMetaActivableConfigData    activableInfo       = (IMetaActivableConfigData)activableInfoObject;
                    UsedByData                  activableUsedByData = UsedByData.FromActivable(kind, activableId, activableInfo, activablesMetadata);

                    foreach (PlayerSegmentInfoBase segmentInfo in activableInfo.ActivableParams.Segments?.MetaRefUnwrap() ?? Enumerable.Empty<PlayerSegmentInfoBase>())
                    {
                        if (segments.ContainsKey(segmentInfo.SegmentId))
                            segments[segmentInfo.SegmentId].UsedBy.Add(activableUsedByData);
                    }
                }
            }

            // UsedBy: MetaOffers
            foreach (MetaOfferInfoBase offerInfo in sharedGameConfig.MetaOffers.Values)
            {
                UsedByData offerUsedByData = UsedByData.FromMetaOffer(offerInfo);

                foreach (PlayerSegmentInfoBase segmentInfo in offerInfo.Segments?.MetaRefUnwrap() ?? Enumerable.Empty<PlayerSegmentInfoBase>())
                {
                    if (segments.ContainsKey(segmentInfo.SegmentId))
                        segments[segmentInfo.SegmentId].UsedBy.Add(offerUsedByData);
                }
            }

            // UsedBy: Experiments
            // \note: we use the Tester set as it is a superset of the Player set.
            foreach ((PlayerExperimentId experimentKey, PlayerExperimentAssignmentPolicy experimentValue) in activeGameConfig.VisibleExperimentsForTesters)
            {
                GlobalStateExperimentStateResponse experimentResponse = await AskEntityAsync<GlobalStateExperimentStateResponse>(GlobalStateManager.EntityId, new GlobalStateExperimentStateRequest(experimentKey));
                UsedByData usedBy = UsedByData.FromExperiment(experimentKey, experimentResponse.Statistics);
                foreach (PlayerSegmentId segmentId in experimentResponse.State.TargetCondition?.GetSegmentReferences() ?? Enumerable.Empty<PlayerSegmentId>())
                {
                    if (segments.ContainsKey(segmentId))
                        segments[segmentId].UsedBy.Add(usedBy);
                }
            }
        }
    }
}
