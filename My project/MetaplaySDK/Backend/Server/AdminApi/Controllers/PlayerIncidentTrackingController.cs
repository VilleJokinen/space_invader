// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for inspecting incident reports.
    /// </summary>
    public class PlayerIncidentTrackingController : GameAdminApiController
    {
        public PlayerIncidentTrackingController(ILogger<PlayerIncidentTrackingController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        [HttpGet("players/{playerIdStr}/incidentReport/{incidentId}")]
        [RequirePermission(MetaplayPermissions.ApiIncidentReportsView)]
        public async Task<ActionResult<PlayerIncidentReport>> GetPlayerIncidentReport(string playerIdStr, string incidentId)
        {
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);
            PersistedPlayerIncident persisted = await MetaDatabase.Get().TryGetIncidentReportAsync(playerId, incidentId).ConfigureAwait(false);
            if (persisted != null)
            {
                byte[]                  uncompressed    = CompressUtil.Decompress(persisted.Payload, persisted.Compression);
                PlayerIncidentReport    report          = MetaSerialization.DeserializeTagged<PlayerIncidentReport>(uncompressed, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                // Fill in UploadedAt time (same as report's PersistedAt)
                report.UploadedAt = persisted.PersistedAt;

                SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
                report.DeletionDateTime = persisted.PersistedAt + systemOpts.IncidentReportRetentionPeriod;

                return report;
            }
            else
                throw new MetaplayHttpException(404, "Incident not found.", $"Cannot find incident with ID {incidentId} in player with ID {playerId}.");
        }

        [HttpGet("incidentReports/latest/{count}")]
        [RequirePermission(MetaplayPermissions.ApiIncidentReportsView)]
        public async Task<ActionResult<List<PlayerIncidentHeader>>> GetLatestPlayerIncidentReports(int count)
        {
            return await MetaDatabase.Get(QueryPriority.Low).QueryGlobalPlayerIncidentHeadersAsync(fingerprint: null, count: count).ConfigureAwait(false);
        }

        [HttpGet("incidentReports/{fingerprint}/{count}")]
        [RequirePermission(MetaplayPermissions.ApiIncidentReportsView)]
        public async Task<ActionResult<List<PlayerIncidentHeader>>> GetLatestPlayerIncidentsByType(string fingerprint, int count)
        {
            return await MetaDatabase.Get(QueryPriority.Low).QueryGlobalPlayerIncidentHeadersAsync(fingerprint: fingerprint, count: count).ConfigureAwait(false);
        }

        [HttpGet("incidentReports/statistics")]
        [RequirePermission(MetaplayPermissions.ApiIncidentReportsView)]
        public async Task<ActionResult<List<PlayerIncidentStatistics>>> GetPlayerIncidentReportStatistics()
        {
            MetaTime since = MetaTime.Now - MetaDuration.FromDays(1);
            const int QueryLimit = 5000; // limit statistic query to prevent too long queries
            return await MetaDatabase.Get(QueryPriority.Low).QueryGlobalPlayerIncidentsStatisticsAsync(since, queryLimitPerShard: QueryLimit).ConfigureAwait(false);
        }
    }
}
