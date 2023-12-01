// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class BroadcastsController : GameAdminApiController
    {
        public BroadcastsController(ILogger<BroadcastsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// HTTP response for an individual broadcast message details
        /// </summary>
        public class BroadcastDetails
        {
            public BroadcastMessage     Message                 { get; set; }
            public long?                AudienceSizeEstimate    { get; set; }
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.BroadcastCreated)]
        public class BroadcastEventCreated : BroadcastEventPayloadBase
        {
            [MetaMember(1)] public BroadcastMessageParams Message { get; private set; }
            public BroadcastEventCreated() { }
            public BroadcastEventCreated(BroadcastMessageParams message) { Message = message; }
            override public int BroadcastId => Message.Id;
            override public string EventTitle => "Created";
            override public string EventDescription => Invariant($"Broadcast (ID: #{BroadcastId}) created.");
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.BroadcastUpdated)]
        public class BroadcastEventUpdated : BroadcastEventPayloadBase
        {
            [MetaMember(1)] public BroadcastMessageParams Message { get; private set; }
            public BroadcastEventUpdated() { }
            public BroadcastEventUpdated(BroadcastMessageParams message) { Message = message; }
            override public int BroadcastId => Message.Id;
            override public string EventTitle => "Updated";
            override public string EventDescription => Invariant($"Broadcast (ID: #{BroadcastId}) updated.");
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.BroadcastDeleted)]
        public class BroadcastEventDeleted : BroadcastEventPayloadBase
        {
            [MetaMember(1)] public int Id { get; private set; }
            public BroadcastEventDeleted() { }
            public BroadcastEventDeleted(int broadcastId) { Id = broadcastId; }
            override public int BroadcastId => Id;
            override public string EventTitle => "Deleted";
            override public string EventDescription => Invariant($"Broadcast (ID: #{BroadcastId}) deleted.");
        }

        /// <summary>
        /// API endpoint to get all broadcasts
        /// Usage: GET /api/broadcasts
        /// </summary>
        [HttpGet("broadcasts")]
        [RequirePermission(MetaplayPermissions.ApiBroadcastView)]
        public async Task<ActionResult<IEnumerable<BroadcastMessage>>> Get(string contentTypeString)
        {
            GlobalStateSnapshot snapshot = await AskEntityAsync<GlobalStateSnapshot>(GlobalStateManager.EntityId, GlobalStateRequest.Instance);
            GlobalState globalState = snapshot.GlobalState.Deserialize(resolver: null, logicVersion: null);
            IEnumerable<BroadcastMessage> messages = globalState.BroadcastMessages.Values;
            if (contentTypeString != null)
            {
                Type contentType = TypeScanner.TryGetTypeByName(contentTypeString);
                if (contentType == null)
                {
                    throw new ArgumentException("Broadcast content class not found for search string {}", contentTypeString);
                }
                messages = messages.Where(x => x.Params != null && x.Params.Contents != null && contentType.IsInstanceOfType(x.Params.Contents));
            }
            return messages.ToList();
        }

        /// <summary>
        /// API endpoint to send a new broadcast
        /// Usage: POST /api/broadcasts
        /// </summary>
        [HttpPost("broadcasts")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiBroadcastEdit)]
        public async Task<ActionResult<BroadcastMessageParams>> NewBroadcast()
        {
            BroadcastMessageParams broadcast = await ParseBodyAsync<BroadcastMessageParams>();

            AddBroadcastMessageResponse response = await AskEntityAsync<AddBroadcastMessageResponse>(GlobalStateManager.EntityId, new AddBroadcastMessage(broadcast));
            if (!response.Success)
                throw new MetaplayHttpException(400, "Failed to create broadcast.", response.Error ?? "Unknown.");

            broadcast.Id = response.BroadcastId;

            // Audit log event
            await WriteAuditLogEventAsync(new BroadcastEventBuilder(new BroadcastEventCreated(broadcast)));

            return Ok(broadcast);
        }

        /// <summary>
        /// API endpoint to get details for a previously sent broadcast
        /// Usage: GET /api/broadcasts/{broadcastId}
        /// </summary>
        [HttpGet("broadcasts/{broadcastId}")]
        [RequirePermission(MetaplayPermissions.ApiBroadcastView)]
        public async Task<ActionResult<BroadcastDetails>> Get(int broadcastId)
        {
            GlobalStateSnapshot snapshot = await AskEntityAsync<GlobalStateSnapshot>(GlobalStateManager.EntityId, GlobalStateRequest.Instance);
            GlobalState globalState = snapshot.GlobalState.Deserialize(resolver: null, logicVersion: null);

            // Get totalPlayerCount from StatsCollector
            int totalPlayerCount = (await AskEntityAsync<StatsCollectorDatabaseEntityCountResponse>(StatsCollectorManager.EntityId, new StatsCollectorDatabaseEntityCountRequest(EntityKindCore.Player))).EntityCount;

            SegmentSizeEstimateResponse segmentSizeResponse = await AskEntityAsync<SegmentSizeEstimateResponse>(PlayerSegmentSizeEstimatorActor.EntityId, SegmentSizeEstimateRequest.Instance);

            // \todo [teemu] Could only fetch one?
            if (globalState.BroadcastMessages.TryGetValue(broadcastId, out BroadcastMessage broadcast))
            {
                long? audienceSize = PlayerTargetingUtil.TryEstimateAudienceSize(totalPlayerCount, broadcast.Params.PlayerFilter, segmentSizeResponse.SegmentEstimates);
                return new BroadcastDetails { Message = broadcast, AudienceSizeEstimate = audienceSize };
            }
            else
            {
                throw new MetaplayHttpException(404, "Broadcast not found.", $"Cannot find broadcast with ID {broadcastId}.");
            }
        }

        /// <summary>
        /// API endpoint to update a previously sent broadcast
        /// Usage: PUT /api/broadcasts/{broadcastId}
        /// </summary>
        [HttpPut("broadcasts/{broadcastId}")]
        [RequirePermission(MetaplayPermissions.ApiBroadcastEdit)]
        public async Task UpdateBroadcast(int broadcastId)
        {
            BroadcastMessageParams broadcast = await ParseBodyAsync<BroadcastMessageParams>();
            if (broadcastId != broadcast.Id)
                throw new InvalidOperationException($"BroadcastMessageId mismatch");

            if (!broadcast.Validate(out string error))
                throw new MetaplayHttpException(400, "Failed to update broadcast.", error ?? "Unknown.");

            await TellEntityAsync(GlobalStateManager.EntityId, new UpdateBroadcastMessage(broadcast));

            // Audit log event
            await WriteAuditLogEventAsync(new BroadcastEventBuilder(new BroadcastEventUpdated(broadcast)));
        }

        /// <summary>
        /// API endpoint to delete a previously sent broadcast
        /// Usage: DELETE /api/broadcasts/{broadcastId}
        /// </summary>
        [HttpDelete("broadcasts/{broadcastId}")]
        [RequirePermission(MetaplayPermissions.ApiBroadcastEdit)]
        public async Task DeleteBroadcast(int broadcastId)
        {
            await TellEntityAsync(GlobalStateManager.EntityId, new DeleteBroadcastMessage(broadcastId));

            // Audit log event
            await WriteAuditLogEventAsync(new BroadcastEventBuilder(new BroadcastEventDeleted(broadcastId)));
        }
    }
}
