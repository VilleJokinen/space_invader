// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class AuditLogController : MetaplayAdminApiController
    {
        public AuditLogController(ILogger<AuditLogController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// HTTP response for audit log event searches
        /// </summary>
        public class AuditLogResult
        {
            public List<AuditLogEvent>  Entries     { get; set; }       // List of search results
            public bool                 HasMore     { get; set; }       // If true then there are more results available - use paging to retrieve them
        }


        /// <summary>
        /// API Endpoint to view a specific audit log entry
        /// Usage:  GET /api/auditLog/{EVENTID}
        /// </summary>
        /// <param name="eventIdStr">ID of the event to return</param>
        /// <returns></returns>
        [HttpGet("auditLog/{eventIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiAuditLogsView)]
        public async Task<ActionResult<AuditLogEvent>> Get(string eventIdStr)
        {
            PersistedAuditLogEvent persistedEvent = await MetaDatabase.Get().GetAuditLogEventAsync(eventIdStr);
            if (persistedEvent != null)
                return Ok(new AuditLogEvent(persistedEvent));
            else
                throw new MetaplayHttpException(404, "Event not found.", $"Cannot find event with Event ID {eventIdStr}.");
        }


        /// <summary>
        /// API endpoint to performa simple search for audit log event. Use advancedSearch for more detailed searches and paging.
        /// Usage:  GET /api/auditLog/search
        /// </summary>
        /// <param name="targetType">Optional: Type of events to search for, eg: "Player" or "$GameServer"</param>
        /// <param name="targetId">Optional: Type-specifc ID to search for, eg: "1234567890" for a Player type</param>
        /// <param name="limit">Optional: Number of events to return, default value is 10</param>
        /// <returns></returns>
        [HttpGet("auditLog/search")]
        [RequirePermission(MetaplayPermissions.ApiAuditLogsView)]
        public async Task<ActionResult<AuditLogResult>> Search([FromQuery] string targetType, [FromQuery] string targetId, [FromQuery] int limit = 10)
        {
            if (limit < 1 || limit > 100)
            {
                throw new MetaplayHttpException(400, "Limit out of range.", "Limit must be between 1 and 100.");
            }

            return await PerformSearch(
                targetType: targetType,
                targetId: targetId,
                source: null,
                sourceIpAddress: null,
                sourceCountryIsoCode: null,
                eventIdLessThan: null,
                limit: limit
            );
        }


        /// <summary>
        /// API endpoint to search for audit log events. Supports paging.
        /// Usage:  GET /api/auditLog/advancedSearch
        /// </summary>
        /// <param name="targetType">Optional: Type of events to search for, eg: "Player" or "$GameServer"</param>
        /// <param name="targetId">Optional: Type-specifc ID to search for, eg: "1234567890" for a Player type</param>
        /// <param name="source">Optional: Source to search for, eg: "$AdminApi:paul.grenfell@metaplay.io"</param>
        /// <param name="sourceIpAddress">Optional: Source IP address to search for</param>
        /// <param name="sourceCountryIsoCode">Optional: Source ISO country code to search for, eg "FI"</param>
        /// <param name="eventIdLessThan">Optional: For paging, pass the ID of the final event from the last page.</param>
        /// <param name="limit">Optional: Number of events to return, default value is 10</param>
        /// <returns></returns>
        [HttpGet("auditLog/advancedSearch")]
        [RequirePermission(MetaplayPermissions.ApiAuditLogsSearch)]
        public async Task<ActionResult<AuditLogResult>> AdvancedSearch([FromQuery] string targetType, [FromQuery] string targetId, [FromQuery] string source, [FromQuery] string sourceIpAddress, [FromQuery] string sourceCountryIsoCode, [FromQuery] string eventIdLessThan, [FromQuery] int limit = 10)
        {
            if (limit < 1 || limit > 500)
            {
                throw new MetaplayHttpException(400, "Limit out of range.", "Limit must be between 1 and 500.");
            }

            return await PerformSearch(
                targetType: targetType,
                targetId: targetId,
                source: source,
                sourceIpAddress: sourceIpAddress,
                sourceCountryIsoCode: sourceCountryIsoCode,
                eventIdLessThan: eventIdLessThan,
                limit: limit
            );
        }


        /// <summary>
        /// Utility funciton to perform a search from one of the searh API endpoints
        /// </summary>
        /// <param name="targetType"></param>
        /// <param name="targetId"></param>
        /// <param name="source"></param>
        /// <param name="sourceIpAddress"></param>
        /// <param name="sourceCountryIsoCode"></param>
        /// <param name="eventIdLessThan"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        private async Task<ActionResult<AuditLogResult>> PerformSearch(string targetType, string targetId, string source, string sourceIpAddress, string sourceCountryIsoCode, string eventIdLessThan, int limit)
        {
            // It doesn't make sense to have a targetId without a targetType
            if (string.IsNullOrEmpty(targetType) && !string.IsNullOrEmpty(targetId))
            {
                throw new MetaplayHttpException(400, "TargetId supplied without TargetType.", "When specifiying a TargetId, you must also specifiy the TargetType.");
            }

            // Search for the results. Note that we ask for one more that "limit" so that we can caluclate the value of the HasMore flag
            List<AuditLogEvent> events = (await MetaDatabase.Get(QueryPriority.Low)
                .QueryAuditLogEventsAsync(
                    eventIdLessThan,
                    targetType,
                    targetId,
                    source,
                    sourceIpAddress,
                    sourceCountryIsoCode,
                    pageSize: limit + 1)
                )
                .ConvertAll(x => new AuditLogEvent(x));
            return Ok(new AuditLogResult()
            {
                Entries = events.Take(limit).ToList(),
                HasMore = events.Count > limit
            });
        }
    }
}
