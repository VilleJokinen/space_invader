// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Server.Database;
using System;
using System.Threading.Tasks;

namespace Metaplay.Server.EntityArchive
{
    /// <summary>
    /// Base definition for an export handler
    /// Each exportable type will need to derive a handler from this
    /// </summary>
    [Obsolete("Please inherit SimpleExportEntityHandler")]
    public abstract class ExportEntityHandler
    {
        protected string Id;
        protected bool AllowExportOnLoadFailure;

        public void Construct(string id, bool allowExportOnLoadFailure)
        {
            Id = id;
            AllowExportOnLoadFailure = allowExportOnLoadFailure;
        }

        public async Task<ExportedEntity> Export(IEntityAsker asker)
        {
            return await DoExport(Id, asker);
        }

        // --- Derived handlers need to implement functions from here onwards ---

        /// <summary>
        /// Return a representation of the data of the given entity as base64-encoded string.
        /// </summary>
        /// <param name="id">ID of the entity to export</param>
        /// <param name="asker">Helper object to send asks to entities</param>
        /// <returns>String representation of the entity</returns>
        protected abstract Task<ExportedEntity> DoExport(string id, IEntityAsker asker);
    }

    /// <summary>
    /// Simplified export handler for simple entities.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete #LegacyEntityHandlerCompat
    public abstract class SimpleExportEntityHandler : ExportEntityHandler
#pragma warning restore CS0618 // Type or member is obsolete
    {
        /// <summary>
        /// The kind of entities this exporter handles.
        /// </summary>
        public EntityKind EntityKind { get; init; }

        protected SimpleExportEntityHandler(EntityKind entityKind)
        {
            EntityKind = entityKind;
        }

        protected override sealed async Task<ExportedEntity> DoExport(string id, IEntityAsker asker)
        {
            PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKind);

            // Look up the entity id
            EntityId entityId;
            try
            {
                entityId = EntityId.ParseFromString(Id);
            }
            catch (FormatException ex)
            {
                throw new ExportException("Entity ID error.", ex.ToString());
            }

            // Check that the entity exists
            if (await MetaDatabase.Get().TryGetAsync<IPersistedEntity>(entityConfig.PersistedType, entityId.ToString()) == null)
                throw new ExportException("Entity ID error.", $"Entity {entityId} does not exist");

            try
            {
                // Ensure that the entity data is persisted to database
                MetaMessage _ = await asker.EntityAskAsync<PersistStateRequestResponse>(entityId, PersistStateRequestRequest.Instance);
            }
            catch (Exception)
            {
                // If called with "AllowExportOnLoadFailure" we ignore any exceptions in the PersistStateRequest (including any errors
                // in deserializing or running schema migrations) and export the raw payload anyway.
                if (!AllowExportOnLoadFailure)
                    throw;
            }

            // Retrieve entity from db. This shouldn't ever fail (ie: no null check required) because we already
            // checked that the entity exists
            IPersistedEntity persistedEntity = await MetaDatabase.Get().TryGetAsync<IPersistedEntity>(entityConfig.PersistedType, entityId.ToString());

            // Return export data
            return ExportedEntity.Create(persistedEntity.SchemaVersion, persistedEntity.Payload);
        }
    }
}
