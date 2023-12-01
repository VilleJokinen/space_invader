// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using System;
using System.Threading.Tasks;

namespace Metaplay.Server.EntityArchive
{

    /// <summary>
    /// Base definition for an import handler
    /// Each importable type will need to derive a handler from this.
    /// </summary>
    [Obsolete("Please inherit SimpleImportEntityHandler")]
    public abstract class ImportEntityHandler
    {
        private EntityId Id;
        private enum Operation
        {
            NotSet,
            Ignore,
            Create,
            Overwrite
        }
        private Operation SelectedOperation = Operation.NotSet;

        public (bool, string) ValidateId(EntityId entityId)
        {
            if (!entityId.IsOfKind(EntityKind))
                return (false, $"Entity ID must be of kind {EntityKind}, got {entityId.Kind}");

            return DoValidateId(entityId);
        }

        public async Task<EntityId> GenerateNewId(IEntityAsker asker)
        {
            EntityId newId;
            do
            {
                newId = await DoGenerateNewId(asker);
            }
            while (await DoDoesExist(newId) == true);
            return newId;
        }

        public void Construct(EntityId id, ExportedEntity data)
        {
            Id = id;
            DoConstruct(data.Payload, data.SchemaVersion == 0 ? null : data.SchemaVersion);
        }

        public async Task<bool> DoesExist()
        {
            return await DoDoesExist(Id);
        }

        public EntityId GetId()
        {
            return Id;
        }

        public async Task Remap(EntityImportEntityIdRemapper remapper)
        {
            await DoRemap(remapper);
            Id = await remapper.Get(Id);
        }

        public void PrepareForIgnore()
        {
            if (SelectedOperation != Operation.NotSet)
                throw new ImportException("Internal error.", "Operation set twice");
            SelectedOperation = Operation.Ignore;
        }

        public void PrepareForCreate()
        {
            if (SelectedOperation != Operation.NotSet)
                throw new ImportException("Internal error.", "Operation set twice");
            SelectedOperation = Operation.Create;
        }

        public void PrepareForOverwrite()
        {
            if (SelectedOperation != Operation.NotSet)
                throw new ImportException("Internal error.", "Operation set twice");
            SelectedOperation = Operation.Overwrite;
        }

        public async Task Write(IEntityAsker asker)
        {
            switch (SelectedOperation)
            {
                case Operation.NotSet:
                    throw new ImportException("Internal error.", "Operation not set before write");

                case Operation.Ignore:
                    break;

                case Operation.Create:
                    await DoWrite(Id, true, asker);
                    break;

                case Operation.Overwrite:
                    await DoWrite(Id, false, asker);
                    break;
            }
        }

        virtual public async Task<string> Diff(EntityId otherId, IEntityAsker asker)
        {
            return await DoDiff(otherId, asker);
        }

        // --- Derived handlers need to implement functions from here onwards ---

        /// <summary>
        /// The kind of entities this importer handles.
        /// </summary>
        public abstract EntityKind EntityKind { get; }

        /// <summary>
        /// Called before any other method.
        /// </summary>
        public virtual void Initialize() { }

        /// <summary>
        /// Validate an ID to see if it is valid for this type of handler
        /// </summary>
        /// <param name="id">ID to validate</param>
        /// <returns>(bool, string) meaning (isValid, reason-if-not-valid)</returns>
        protected virtual (bool, string) DoValidateId(EntityId id)
        {
            return (true, "");
        }

        /// <summary>
        /// Generate a new random ID that is valid for this type of entity
        /// </summary>
        /// <returns>New ID for this type of entity</returns>
        protected virtual Task<EntityId> DoGenerateNewId(IEntityAsker asker)
        {
            return Task.FromResult<EntityId>(EntityId.CreateRandom(EntityKind));
        }

        /// <summary>
        /// Construct an internal representation of the entity from the supplied data
        /// </summary>
        /// <param name="data">String containing entity handler specific data, in whatever format this entities handler used</param>
        /// <param name="schemaVersion">Version of the handler specific data, if available</param>
        protected abstract void DoConstruct(string data, int? schemaVersion);

        /// <summary>
        /// Check to see if the given id already exists for this entity type
        /// </summary>
        /// <param name="id">ID to check. Guaranteed to be a valid ID for this entity type</param>
        /// <returns>True if the id exists</returns>
        protected abstract Task<bool> DoDoesExist(EntityId id);

        /// <summary>
        /// Do anything required to remap the entities internal data to the new id
        /// </summary>
        /// <param name="remapper">Remapper table which maps payload's entityIds to desired entityIds</param>
        protected abstract Task DoRemap(EntityImportEntityIdRemapper remapper);

        /// <summary>
        /// Persist the entity
        /// </summary>
        /// <param name="id">ID to persist the entity with. Guaranteed to be a valid ID for this entity type</param>
        /// <param name="createFirst">True if the entity doesn't already exist, false if it does and we are overwriting</param>
        /// <param name="asker">Helper object to send asks to entities</param>
        /// <returns></returns>
        protected abstract Task DoWrite(EntityId id, bool createFirst, IEntityAsker asker);

        /// <summary>
        /// Return a string repesentation of the difference between this entity and another
        /// </summary>
        /// <param name="otherId">ID of the other entity. Guaranteed to be a valid ID for this entity type</param>
        /// <param name="asker">Helper object to send asks to entities</param>
        /// <returns>String representing the difference</returns>
        protected abstract Task<string> DoDiff(EntityId otherId, IEntityAsker asker);
    }

    /// <summary>
    /// Simplified import handler for simple entities.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete #LegacyEntityHandlerCompat
    public abstract class SimpleImportEntityHandler : ImportEntityHandler
#pragma warning restore CS0618 // Type or member is obsolete
    {
        protected byte[]                Payload             { get; private set; }
        protected int?                  SchemaVersion       { get; private set; }
        protected PersistedEntityConfig EntityConfig        { get; private set; }
        protected ActiveGameConfig      ActiveGameConfig    { get; private set; }
        protected int                   LogicVersion        { get; private set; }

        EntityKind                      _entityKind;
        public override EntityKind EntityKind => _entityKind;

        protected SimpleImportEntityHandler(EntityKind entityKind)
        {
            _entityKind = entityKind;
        }

        public override void Initialize()
        {
            EntityConfig        = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKind);
            ActiveGameConfig    = GlobalStateProxyActor.ActiveGameConfig.Get();
            LogicVersion        = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;
        }

        /// <summary>
        /// Remaps model EntityIds.
        /// </summary>
        protected abstract Task DoRemapModel(IModel model, EntityImportEntityIdRemapper remapper);

        /// <summary>
        /// Sets neccessary runtime properties after model deserialization. Usually this includes LogicVersion and SharedConfig.
        /// </summary>
        protected abstract void AssignBasicRuntimePropertiesToModel(IModel model);

        protected override sealed void DoConstruct(string data, int? schemaVersion)
        {
            // Convert payload and remember it
            try
            {
                Payload = Convert.FromBase64String(data);
            }
            catch (FormatException ex)
            {
                throw new ImportException("Error reading base64 payload.", ex.Message);
            }
            SchemaVersion = schemaVersion;

            if (schemaVersion.HasValue)
            {
                SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator(EntityConfig.PersistedPayloadType);

                if (SchemaVersion < migrator.SupportedSchemaVersions.MinVersion)
                    throw new ImportException("Unsupported schema version (too old).", $"Migratable type is in a schema v{SchemaVersion} when oldest supported is v{migrator.SupportedSchemaVersions.MinVersion}");
                else if (SchemaVersion > migrator.SupportedSchemaVersions.MaxVersion)
                    throw new ImportException("Unsupported schema version (too new).", $"Migratable type is in a future schema v{SchemaVersion} when maximum supported is v{migrator.SupportedSchemaVersions.MaxVersion}");
            }

            // Validate data
            _ = MaterializeModel(runMigrations: true);
        }

        protected override sealed async Task<bool> DoDoesExist(EntityId id)
        {
            return await MetaDatabase.Get().TryGetAsync<IPersistedEntity>(EntityConfig.PersistedType, id.ToString()) != null;
        }

        protected override sealed async Task DoRemap(EntityImportEntityIdRemapper remapper)
        {
            // Materialize, remap, and reserialise model
            IModel model = MaterializeModel(runMigrations: false);
            await DoRemapModel(model, remapper);
            Payload = EntityConfig.SerializeDatabasePayload(model, LogicVersion);
        }

        protected IModel MaterializeModel(bool runMigrations)
        {
            IGameConfigDataResolver resolver = ActiveGameConfig.BaselineGameConfig.SharedConfig;
            IModel model;

            try
            {
                model = EntityConfig.DeserializeDatabasePayload<IModel>(Payload, resolver, LogicVersion);
            }
            catch (Exception ex)
            {
                throw new ImportException("Error deserializing data.", ex.ToString());
            }

            AssignBasicRuntimePropertiesToModel(model);

            if (runMigrations)
            {
                if (SchemaVersion.HasValue)
                {
                    SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator(EntityConfig.PersistedPayloadType);
                    int toVersion = migrator.SupportedSchemaVersions.MaxVersion;
                    if (SchemaVersion < toVersion)
                    {
                        try
                        {
                            migrator.RunMigrations(model, SchemaVersion.Value);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Failed running model migration from version {SchemaVersion} to {toVersion}", ex);
                        }
                    }
                }
            }

            return model;
        }

        protected override sealed async Task<string> DoDiff(EntityId otherId, IEntityAsker asker)
        {
            byte[] proposedPayload = Payload;

            // Dry-run migrations on the imported model for diff purposes
            if (SchemaVersion.HasValue)
            {
                SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator(EntityConfig.PersistedPayloadType);
                int toVersion = migrator.SupportedSchemaVersions.MaxVersion;
                if (SchemaVersion < toVersion)
                {
                    IModel model = MaterializeModel(runMigrations: true);
                    proposedPayload = EntityConfig.SerializeDatabasePayload(model, LogicVersion);
                }
            }

            // Ensure that the other entity's data is persisted to database
            _ = await asker.EntityAskAsync<PersistStateRequestResponse>(otherId, PersistStateRequestRequest.Instance);

            // Deserialise other entity
            IPersistedEntity otherPersisted = await MetaDatabase.Get().TryGetAsync<IPersistedEntity>(EntityConfig.PersistedType, otherId.ToString());

            // Decompress payload (if compressed)
            using FlatIOBuffer proposedPersisted =
                    BlobCompress.IsCompressed(proposedPayload) ? BlobCompress.DecompressBlob(proposedPayload) : FlatIOBuffer.CopyFromSpan(proposedPayload);
            using FlatIOBuffer existingPersisted =
                    BlobCompress.IsCompressed(otherPersisted.Payload) ? BlobCompress.DecompressBlob(otherPersisted.Payload) : FlatIOBuffer.CopyFromSpan(otherPersisted.Payload);

            SerializedObjectComparer comparer = new SerializedObjectComparer();
            comparer.FirstName = "Imported";
            comparer.SecondName = "Existing";
            comparer.Type = EntityConfig.PersistedPayloadType;
            SerializedObjectComparer.Report diffReport = comparer.Compare(proposedPersisted, existingPersisted);

            return diffReport.Description;
        }
    }
}
