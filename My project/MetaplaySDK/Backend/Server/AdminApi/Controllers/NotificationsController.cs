// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.NotificationCampaign;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class NotificationsController : GameAdminApiController
    {
        public NotificationsController(ILogger<NotificationsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// HTTP response for an individual notification campaigndetails
        /// </summary>
        public class CampaignDetails
        {
            public NotificationCampaignInfo CampaignInfo { get; set; }
            public long? AudienceSizeEstimate { get; set; }
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NotificationCreated)]
        public class EventNotificationCreated : NotificationEventPayloadBase
        {
            [MetaMember(1)] public int NotificationId { get; private set; }
            [MetaMember(2)] public NotificationCampaignParams CampaignParams { get; private set; }
            public EventNotificationCreated() { }
            public EventNotificationCreated(int notificationId, NotificationCampaignParams campaingParams) { NotificationId = notificationId;  CampaignParams = campaingParams; }
            override public string EventTitle => "Created";
            override public string EventDescription => Invariant($"Notification '{CampaignParams.Name}' created.");
            public override int Id => NotificationId;
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NotificationUpdated)]
        public class EventNotificationUpdated : NotificationEventPayloadBase
        {
            [MetaMember(1)] public int NotificationId { get; private set; }
            [MetaMember(2)] public NotificationCampaignParams CampaignParams { get; private set; }
            public EventNotificationUpdated() { }
            public EventNotificationUpdated(int notificationId, NotificationCampaignParams campaingParams) { NotificationId = notificationId; CampaignParams = campaingParams; }
            override public string EventTitle => "Updated";
            override public string EventDescription => Invariant($"Notification '{CampaignParams.Name}' updated.");
            public override int Id => NotificationId;
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NotificationCancelled)]
        public class EventNotificationCancelled : NotificationEventPayloadBase
        {
            [MetaMember(1)] public int NotificationId { get; private set; }
            public EventNotificationCancelled() { }
            public EventNotificationCancelled(int notificationId) { NotificationId = notificationId; }
            override public string EventTitle => "Cancelled";
            override public string EventDescription => Invariant($"Notification #{NotificationId} cancelled.");
            public override int Id => NotificationId;
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NotificationDeleted)]
        public class EventNotificationDeleted : NotificationEventPayloadBase
        {
            [MetaMember(1)] public int NotificationId { get; private set; }
            public EventNotificationDeleted() { }
            public EventNotificationDeleted(int notificationId) { NotificationId = notificationId; }
            override public string EventTitle => "Deleted";
            override public string EventDescription => Invariant($"Notification #{NotificationId} deleted.");
            public override int Id => NotificationId;
        }


        /// <summary>
        /// HTTP request/response formats
        /// </summary>
        public class CreateResponse
        {
            public int Id;
            public CreateResponse(int id) { Id = id; }
        }


        /// <summary>
        /// API endpoint to get all notifications
        /// Usage: GET /api/notifications
        /// </summary>
        [HttpGet("notifications")]
        [RequirePermission(MetaplayPermissions.ApiNotificationsView)]
        public async Task<ActionResult> ListNotifications()
        {
            // Get current notifications
            ListNotificationCampaignsRequest request = new ListNotificationCampaignsRequest();
            ListNotificationCampaignsResponse response = await AskEntityAsync<ListNotificationCampaignsResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);

            // Return results
            return Ok(response.NotificationCampaigns);
        }


        /// <summary>
        /// API endpoint to send a new notification
        /// Usage: POST /api/notifications
        /// </summary>
        [HttpPost("notifications")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiNotificationsEdit)]
        public async Task<ActionResult> CreateNotification()
        {
            // Parse parameters
            NotificationCampaignParams campaignParams = await ParseBodyAsync<NotificationCampaignParams>();

            // Do the creation
            AddNotificationCampaignRequest request = new AddNotificationCampaignRequest(campaignParams);
            AddNotificationCampaignResponse response = await AskEntityAsync<AddNotificationCampaignResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);
            if (!response.Success)
                throw new MetaplayHttpException(400, "Failed to create notification.", response.Error ?? "Unknown.");

            // Audit log event
            await WriteAuditLogEventAsync(new NotificationEventBuilder(new EventNotificationCreated(response.Id, campaignParams)));

            // Return results
            return Ok(new CreateResponse(response.Id));
        }


        /// <summary>
        /// API endpoint to get a previously sent notification
        /// Usage: GET /api/notifications/{notificationId}
        /// </summary>
        [HttpGet("notifications/{notificationIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiNotificationsView)]
        public async Task<ActionResult<CampaignDetails>> GetNotification(string notificationIdStr)
        {
            // Parse parameters
            int notificationIdInt = await ParseNotificationId(notificationIdStr, checkForExistence: true);

            // Get the notification
            GetNotificationCampaignRequest request = new GetNotificationCampaignRequest(notificationIdInt);
            GetNotificationCampaignResponse response = await AskEntityAsync<GetNotificationCampaignResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);

            // Get totalPlayerCount from StatsCollector
            int totalPlayerCount = (await AskEntityAsync<StatsCollectorDatabaseEntityCountResponse>(StatsCollectorManager.EntityId, new StatsCollectorDatabaseEntityCountRequest(EntityKindCore.Player))).EntityCount;

            // Size estimate
            SegmentSizeEstimateResponse segmentSizeResponse = await AskEntityAsync<SegmentSizeEstimateResponse>(PlayerSegmentSizeEstimatorActor.EntityId, SegmentSizeEstimateRequest.Instance);

            long? audienceSizeEstimate = PlayerTargetingUtil.TryEstimateAudienceSize(totalPlayerCount, response.CampaignInfo.CampaignParams.PlayerFilter, segmentSizeResponse.SegmentEstimates);

            // Return results
            if (!response.Success)
                throw new MetaplayHttpException(400, "Failed to view notification.", response.Error ?? "Unknown.");
            return new CampaignDetails { CampaignInfo = response.CampaignInfo, AudienceSizeEstimate = audienceSizeEstimate };
        }


        /// <summary>
        /// API endpoint to update a previously sent notification
        /// Usage: PUT /api/notifications/{notificationId}
        /// </summary>
        [HttpPut("notifications/{notificationIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiNotificationsEdit)]
        public async Task<ActionResult> UpdateNotification(string notificationIdStr)
        {
            // Parse parameters
            int notificationIdInt = await ParseNotificationId(notificationIdStr, checkForExistence: true);
            NotificationCampaignParams campaignParams = await ParseBodyAsync<NotificationCampaignParams>();

            // Do the update
            UpdateNotificationCampaignRequest request = new UpdateNotificationCampaignRequest(notificationIdInt, campaignParams);
            UpdateNotificationCampaignResponse response = await AskEntityAsync<UpdateNotificationCampaignResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);
            if (!response.Success)
                throw new MetaplayHttpException(400, "Failed to update notification.", response.Error ?? "Unknown.");

            // Audit log event
            await WriteAuditLogEventAsync(new NotificationEventBuilder(new EventNotificationUpdated(notificationIdInt, campaignParams)));

            // Return results
            return NoContent();
        }


        /// <summary>
        /// API endpoint to begin the cancellation of a running notification campaign
        /// Usage: PUT /api/notifications/{notificationId}/cancel
        /// </summary>
        [HttpPut("notifications/{notificationIdStr}/cancel")]
        [RequirePermission(MetaplayPermissions.ApiNotificationsEdit)]
        public async Task<ActionResult> CancelNotification(string notificationIdStr)
        {
            // Parse parameters
            int notificationIdInt = await ParseNotificationId(notificationIdStr, checkForExistence: true);

            // Do the cancelation
            BeginCancelNotificationCampaignRequest request = new BeginCancelNotificationCampaignRequest(notificationIdInt);
            BeginCancelNotificationCampaignResponse response = await AskEntityAsync<BeginCancelNotificationCampaignResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);
            if (!response.Success)
                throw new MetaplayHttpException(400, "Failed to cancel notification.", response.Error ?? "Unknown.");

            // Audit log event
            await WriteAuditLogEventAsync(new NotificationEventBuilder(new EventNotificationCancelled(notificationIdInt)));

            // Return results
            return NoContent();
        }


        /// <summary>
        /// API endpoint to delete a previously sent notification
        /// Usage: DELETE /api/notifications/{notificationId}
        /// </summary>
        [HttpDelete("notifications/{notificationIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiNotificationsEdit)]
        public async Task<ActionResult> DeleteNotification(string notificationIdStr)
        {
            // Parse parameters
            int notificationIdInt = await ParseNotificationId(notificationIdStr, checkForExistence: true);

            // Do the deletion
            DeleteNotificationCampaignRequest request = new DeleteNotificationCampaignRequest(notificationIdInt);
            DeleteNotificationCampaignResponse response = await AskEntityAsync<DeleteNotificationCampaignResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);
            if (!response.Success)
                throw new MetaplayHttpException(400, "Failed to delete notification.", response.Error ?? "Unknown.");

            // Audit log event
            await WriteAuditLogEventAsync(new NotificationEventBuilder(new EventNotificationDeleted(notificationIdInt)));

            // Return results
            return NoContent();
        }


        /// <summary>
        /// Utility function to parse a notification ID and optionally check for the existence of
        /// that notification
        /// Throws MetaplayHttpException on error
        /// </summary>
        /// <param name="notificationIdStr">String representation of notification Id</param>
        /// <returns>Integer value of notification Id</returns>
        protected async Task<int> ParseNotificationId(string notificationIdStr, bool checkForExistence = false)
        {
            int notificationIdInt;
            try
            {
                notificationIdInt = Convert.ToInt32(notificationIdStr, CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new MetaplayHttpException(404, "Notification not found.", $"Notification ID {notificationIdStr} is not valid: {ex.Message}");
            }

            if (checkForExistence)
            {
                GetNotificationCampaignRequest request = new GetNotificationCampaignRequest(notificationIdInt);
                GetNotificationCampaignResponse response = await AskEntityAsync<GetNotificationCampaignResponse>(DatabaseScan.DatabaseScanCoordinatorActor.EntityId, request);
                if (!response.Success)
                {
                    throw new MetaplayHttpException(404, "Notification not found.", $"Cannot find notification with ID {notificationIdStr}.");
                }
            }

            return notificationIdInt;
        }
    }
}
