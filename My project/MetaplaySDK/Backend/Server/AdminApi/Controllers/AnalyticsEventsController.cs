// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Analytics;
using Metaplay.Core.Analytics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using ActionResult = Microsoft.AspNetCore.Mvc.ActionResult;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class AnalyticsEventsController : GameAdminApiController
    {
        public AnalyticsEventsController(ILogger<AnalyticsEventsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// API endpoint to get information about all known analytics events
        /// Usage: GET /api/analyticsEvents
        /// </summary>
        [HttpGet("analyticsEvents")]
        [RequirePermission(MetaplayPermissions.ApiAnalyticsEventsView)]
        public ActionResult GetAnalyticsEvents()
        {
            return Ok(AnalyticsEventRegistry.AllEventSpecs);
        }

        /// <summary>
        /// API endpoint to get bigquery example of an analytics event
        /// Usage: GET /api/analyticsEvents/{EVENTCODE}/bigQueryExample
        /// </summary>
        [HttpGet("analyticsEvents/{eventCodeStr}/bigQueryExample")]
        [RequirePermission(MetaplayPermissions.ApiAnalyticsEventsView)]
        public ActionResult GetBigQueryExample(string eventCodeStr)
        {
            int eventTypeCode = Convert.ToInt32(eventCodeStr, CultureInfo.InvariantCulture);
            AnalyticsEventSpec eventSpec = AnalyticsEventRegistry.EventSpecs.Values.FirstOrDefault(eventSpec => eventSpec.TypeCode == eventTypeCode);
            if (eventSpec == null)
                return NotFound();

            return new JsonResult(
                BigQueryFormatter.Instance.GetExampleResultObject(eventSpec),
                AdminApiJsonSerialization.UntypedSerializationSettings);
        }
    }
}
