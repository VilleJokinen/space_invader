// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for determining if an Entity exists
    /// </summary>
    public class EntityExistsController : GameAdminApiController
    {
        public EntityExistsController(ILogger<EntityExistsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// API endpoint to determine if a persisted entity exists in the database
        /// Usage:  GET /api/entities/{entityIdStr}/exists
        /// Test:   curl http://localhost:5550/api/entities/{entityIdStr}/exists
        /// </summary>
        /// <returns>True, if entity exists. False, if entity is not found. Throws an exception if Entity ID is invalid or does not refer to a persisted Entity kind.</returns>
        [HttpGet("entities/{entityIdStr}/exists")]
        [RequirePermission(MetaplayPermissions.ApiDatabaseInspectEntity)]
        public async Task<IActionResult> Get(string entityIdStr)
        {
            EntityId entityId;
            try
            {
                entityId = EntityId.ParseFromString(entityIdStr);
            }
            catch (FormatException ex)
            {
                throw new MetaplayHttpException(400, "Invalid Entity ID.", $"Entity ID {entityIdStr} is not valid: {ex.Message}");
            }

            if (!EntityConfigRegistry.Instance.TryGetPersistedConfig(entityId.Kind, out PersistedEntityConfig entityConfig))
                throw new MetaplayHttpException(400, "Invalid Entity kind.", $"Entity kind {entityId.Kind} does not refer to a PersistedEntity");

            IPersistedEntity persistedEntity = await MetaDatabase.Get().TryGetAsync<IPersistedEntity>(entityConfig.PersistedType, entityId.ToString()).ConfigureAwait(false);
            if (persistedEntity == null)
                return Ok(false);
            
            return Ok(true);
        }
    }
}
