// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.DatabaseScan;
using Metaplay.Server.MaintenanceJob;
using Metaplay.Server.MaintenanceJob.EntityRefresher;
using Metaplay.Server.MaintenanceJob.EntitySchemaMigrator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for endpoints related to maintenance jobs
    /// </summary>
    public class MaintenanceJobController : GameAdminApiController
    {
        public MaintenanceJobController(ILogger<MaintenanceJobController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerMaintenanceJobCreated)]
        public class GameServerMaintenanceJobCreated : GameServerEventPayloadBase
        {
            [MetaMember(1)] public int                  MaintenanceJobId    { get; private set; }
            [MetaMember(2)] public MaintenanceJobSpec   Spec                { get; private set; }

            GameServerMaintenanceJobCreated() { }
            public GameServerMaintenanceJobCreated(int maintenanceJobId, MaintenanceJobSpec spec)
            {
                MaintenanceJobId = maintenanceJobId;
                Spec = spec;
            }

            public override string SubsystemName => "MaintenanceJob";
            public override string EventTitle => "Created";
            public override string EventDescription => Invariant($"Maintenance job '{Spec.JobTitle}' (id {MaintenanceJobId}) created.");
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerMaintenanceJobDeleted)]
        public class GameServerMaintenanceJobDeleted : GameServerEventPayloadBase
        {
            [MetaMember(1)] public int                  MaintenanceJobId    { get; private set; }
            [MetaMember(2)] public MaintenanceJobSpec   Spec                { get; private set; }

            GameServerMaintenanceJobDeleted() { }
            public GameServerMaintenanceJobDeleted(int maintenanceJobId, MaintenanceJobSpec spec)
            {
                MaintenanceJobId = maintenanceJobId;
                Spec = spec;
            }

            public override string SubsystemName => "MaintenanceJob";
            public override string EventTitle => "Deleted";
            public override string EventDescription => Invariant($"Maintenance job '{Spec.JobTitle}' (id {MaintenanceJobId}) deleted.");
        }

        /// <summary>
        /// Get maintenance jobs
        /// Usage:  GET /api/maintenanceJobs
        /// Test:   curl http://localhost:5550/api/maintenanceJobs
        /// </summary>
        [HttpGet("maintenanceJobs")]
        [RequirePermission(MetaplayPermissions.ApiSystemMaintenanceJobs)]
        public ActionResult GetJobs()
        {
            // Return results
            return Ok(new
            {
                SupportedJobKinds = GetSupportedJobKinds().Select(kv => new { Id = kv.Key, Spec = kv.Value }).ToList(),
            });
        }

        /// <summary>
        /// Delete an enqueued maintenance job
        /// Usage: DELETE /api/maintenanceJobs/{maintenanceJobId}
        /// </summary>
        [HttpDelete("maintenanceJobs/{maintenanceJobIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiSystemMaintenanceJobs)]
        public async Task<ActionResult> DeleteEnqueuedJob(string maintenanceJobIdStr)
        {
            int maintenanceJobId = int.Parse(maintenanceJobIdStr, NumberStyles.None, CultureInfo.InvariantCulture);

            RemoveMaintenanceJobRequest request = new RemoveMaintenanceJobRequest(maintenanceJobId);
            RemoveMaintenanceJobResponse response = await AskEntityAsync<RemoveMaintenanceJobResponse>(DatabaseScanCoordinatorActor.EntityId, request);
            // \todo [nuutti] Figure out proper statusCode
            if (!response.IsSuccess)
                throw new MetaplayHttpException(400, "Failed to delete job.", response.Error);

            await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerMaintenanceJobDeleted(maintenanceJobId, response.JobSpec)));

            return Ok();
        }

        public class EnqueueRequestBody
        {
            public string JobKindId;
        }

        /// <summary>
        /// Enqueue a new maintenance job
        /// Usage: POST /api/maintenanceJob
        ///        with body { jobKindId: JOB_KIND_ID }, where JOB_KIND_ID is one of the ids in supportedJobKinds returned by GET /api/maintenanceJobs
        /// </summary>
        [HttpPost("maintenanceJobs")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiSystemMaintenanceJobs)]
        public async Task<ActionResult> EnqueueJob()
        {
            EnqueueRequestBody  requestBody = await ParseBodyAsync<EnqueueRequestBody>();
            MaintenanceJobSpec  jobSpec     = GetSupportedJobKinds()[requestBody.JobKindId];

            AddMaintenanceJobRequest request = new AddMaintenanceJobRequest(jobSpec);
            AddMaintenanceJobResponse response = await AskEntityAsync<AddMaintenanceJobResponse>(DatabaseScanCoordinatorActor.EntityId, request);
            // \todo [nuutti] Figure out proper statusCode
            if (!response.IsSuccess)
                throw new MetaplayHttpException(400, "Failed to enqueue job.", response.Error);

            await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerMaintenanceJobCreated(response.MaintenanceJobId, jobSpec)));

            return Ok(new
            {
                MaintenanceJobId = response.MaintenanceJobId,
                Spec = jobSpec,
            });
        }

        // Note: This used to be static list but that meant that ScheduledPlayerDeletionJobSpecParams() only got initialized with
        // MetaTime.Now once, at start-up. This was incorrect behaviour - the time should be set to 'now' of whenever the job is run.
        // The kludge fix here is to make this a non-static property so that the params get constructed (and thus the correct time is
        // set) each time the list is accessed.
        OrderedDictionary<string, MaintenanceJobSpec> GetSupportedJobKinds()
        {
            OrderedDictionary<string, MaintenanceJobSpec> specs = new OrderedDictionary<string, MaintenanceJobSpec>();

            foreach ((EntityKind kind, Func<EntitySchemaMigratorJobSpec> factory) in EntityMaintenanceJobRegistry.SchemaMigrationJobSpecs)
                specs.Add($"Migrate_{kind}", factory());

            foreach ((EntityKind kind, Func<EntityRefresherJobSpec> factory) in EntityMaintenanceJobRegistry.RefreshJobSpecs)
                specs.Add($"Refresh_{kind}", factory());

            foreach (((EntityKind kind, string jobId), EntityMaintenanceJobAttribute attrib) in EntityMaintenanceJobRegistry.GenericJobs)
                specs.Add($"{jobId}_{kind}", attrib.JobSpecFactory());

            return specs;
        }
    }
}
