// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Base Controller for basic routes Multiplayer Entities should implement.
    /// </summary>
    public abstract class MultiplayerEntityControllerBase : GameAdminApiController
    {
        protected abstract EntityKind EntityKind { get; }

        public MultiplayerEntityControllerBase(ILogger<MultiplayerEntityControllerBase> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// HTTP response for an individual entitity details This may be extended by inheriting this class
        /// and populating the new fields in <see cref="GetEntityDetails"/>.
        /// </summary>
        public class EntityDetailsItem
        {
            public string                       Id              { get; set; }
            public IModel                       Model           { get; set; }
            public int                          PersistedSize   { get; set; }

            public EntityDetailsItem(string id, IModel model, int persistedSize)
            {
                Id = id;
                Model = model;
                PersistedSize = persistedSize;
            }
        }

        /// <summary>
        /// API endpoint to return detailed information of a certian entity. Default implementation should call <see cref="DefaultGetEntityDetailsAsync"/>.
        /// <para>
        /// Implementation MUST set [HttpGet] and [RequirePermission] attributes, like:
        /// <code>
        ///  [HttpGet("myentities/{entityIdString}")]
        ///  [RequirePermission(MetaplayPermissions.ApiMyEntityView)]
        ///  public override Task GetEntityDetails(...)
        /// </code>
        /// This exposes the route on `/api/myentities/{entityId}`.
        /// </para>
        /// <code>
        /// <!--
        /// Usage:  GET /api/myentities
        /// Test:   curl http://localhost:5550/api/myentities/MyEntity:00002345
        /// -->
        /// </code>
        /// </summary>
        public abstract Task<ActionResult<EntityDetailsItem>> GetEntityDetails(string entityIdString);

        protected async Task<ActionResult<EntityDetailsItem>> DefaultGetEntityDetailsAsync(string entityIdString)
        {
            EntityId entityId = ParseEntityIdStr(entityIdString, EntityKind);
            IPersistedEntity persisted = await GetPersistedEntityAsync(entityId);
            IModel model = await GetEntityStateAsync(entityId);

            if (model == null)
                throw new MetaplayHttpException(404, $"{EntityKind} not yet initialized.", $"Entity {entityId} has not yet been set up but the ID has been allocated for the new entity.");

            // \todo: viewed-audit log entry.

            // Respond to browser
            return new EntityDetailsItem(
                id:             entityId.ToString(),
                model:          model,
                persistedSize:  persisted.Payload.Length
            );
        }

        /// <summary>
        /// HTTP response for listing entities. This may be extended by inheriting this class
        /// and populating the new fields in <see cref="ListEntities"/> or in its converter function.
        /// </summary>
        public class EntityListItem
        {
            public string       EntityId                    { get; set; }
            public string       DisplayName                 { get; set; }
            public DateTime     CreatedAt                   { get; set; }

            /// <summary>
            /// Only set when creating the item fails.
            /// </summary>
            // \note Specifically stringifying and *not* using Exception as member the type, since that appears to break json serialization. \todo Investigate?
            public string       Error                       { get; set; }
        }

        /// <summary>
        /// API endpoint to return basic information about all entities in the system, with an optional query string that
        /// works to filter entities based on entity id. Default implementation should call <see cref="DefaultListEntitiesAsync"/>.
        /// <para>
        /// Implementation MUST set [HttpGet] and [RequirePermission] attributes, like:
        /// <code>
        ///  [HttpGet("myentities")]
        ///  [RequirePermission(GamePermissions.ApiMyEntityView)]
        ///  public override Task ListEntities(...)
        /// </code>
        /// This exposes the route on `/api/myentities`
        /// </para>
        /// <code>
        /// <!--
        /// Usage:  GET /api/myentities
        /// Test:   curl http://localhost:5550/api/myentities?query=QUERY&count=LIMIT
        /// -->
        /// </code>
        /// </summary>
        public abstract Task<ActionResult<IEnumerable<EntityListItem>>> ListEntities([FromQuery] string query = "", [FromQuery] int count = 10);

        /// <summary>
        /// Default implementation for <see cref="ListEntities"/>.
        /// </summary>
        /// <param name="convertPersistedToItemAsync">Convert PersistedEntity to EntityListItem. Defaults to <see cref="DefaultConvertPersistedToItemAsync"/>.</param>
        protected async Task<ActionResult<IEnumerable<EntityListItem>>> DefaultListEntitiesAsync(string query, int count, Func<IPersistedEntity, Task<EntityListItem>> convertPersistedToItemAsync = null)
        {
            if (convertPersistedToItemAsync == null)
                convertPersistedToItemAsync = DefaultConvertPersistedToItemAsync;

            if (!EntityConfigRegistry.Instance.TryGetPersistedConfig(EntityKind, out PersistedEntityConfig entityConfig))
                throw new MetaplayHttpException(400, "Invalid Entity kind.", $"Entity kind {EntityKind} does not refer to a PersistedEntity");

            // Search by id and convert to items
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Low);
            List<IPersistedEntity> persistedEntities = await db.SearchEntitiesByIdAsync(entityConfig.PersistedType, EntityKind, count, query);
            EntityListItem[] items = await Task.WhenAll(persistedEntities.Select(convertPersistedToItemAsync));
            return new ActionResult<IEnumerable<EntityListItem>>(items);
        }

        protected Task<EntityListItem> DefaultConvertPersistedToItemAsync(IPersistedEntity persisted)
        {
            if (persisted.Payload == null)
            {
                return Task.FromResult(new EntityListItem
                {
                    EntityId        = persisted.EntityId.ToString(),
                    DisplayName     = "<Uninitialized>",
                });
            }

            PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKind);

            try
            {
                // \note[jarkko]: should not use global resolver. Each Model should be deserialized with the appropriate resolver. In practice
                //                this is a bit involved (should wake actor to run migrations et al.), so let's just hope for the best :)
                ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
                IGameConfigDataResolver resolver = activeGameConfig.BaselineGameConfig.SharedConfig;
                int activeLogicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;
                IMultiplayerModel model = entityConfig.DeserializeDatabasePayload<IMultiplayerModel>(persisted.Payload, resolver, activeLogicVersion);
                return Task.FromResult(new EntityListItem
                {
                    EntityId        = persisted.EntityId.ToString(),
                    DisplayName     = model.GetDisplayNameForDashboard(),
                    CreatedAt       = model.CreatedAt.ToDateTime(),
                });
            }
            catch (MetaSerializationException exception)
            {
                return Task.FromResult(new EntityListItem
                {
                    EntityId    = persisted.EntityId.ToString(),
                    Error       = exception.ToString(),
                });
            }
        }

        /// <summary>
        /// API Endpoint to return information about active entities - these are the entities which are running right now.
        /// Default implementation should call <see cref="DefaultGetActiveEntitiesAsync"/>.
        /// <para>
        /// Implementation MUST set [HttpGet] and [RequirePermission] attributes, like:
        /// <code>
        ///  [HttpGet("myentities/active")]
        ///  [RequirePermission(GamePermissions.ApiMyEntityView)]
        ///  public override Task GetActiveEntities(...)
        /// </code>
        /// This exposes the route on `/api/myentities/active`
        /// </para>
        /// Usage:  GET /api/myentities/active
        /// Test:   curl http://localhost:5550/api/myentities/active
        /// </summary>
        public abstract Task<ActionResult<IEnumerable<IActiveEntityInfo>>> GetActiveEntities();

        protected async Task<ActionResult<IEnumerable<IActiveEntityInfo>>> DefaultGetActiveEntitiesAsync()
        {
            ActiveEntitiesResponse response = await AskEntityAsync<ActiveEntitiesResponse>(StatsCollectorManager.EntityId, new ActiveEntitiesRequest(EntityKind));
            return new ActionResult<IEnumerable<IActiveEntityInfo>>(response.ActiveEntities);
        }
    }
}
