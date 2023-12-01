// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.EntityArchive.ImportResponse;

namespace Metaplay.Server.EntityArchive
{
    /// <summary>
    /// Determines what happens when trying to import over an entity that already exists
    /// </summary>
    public enum OverwritePolicy
    {
        Ignore,             // Don't do the import - leave the old entity alone and don't import the new entity
        Overwrite,          // Do the import, overwriting the old entity with the new one
        CreateNew,          // Create a new entity
    }


    /// <summary>
    /// Response from the import funtion
    /// </summary>
    public class ImportResponse
    {
        public enum EntityResultStatus
        {
            Success,
            Error,
            Ignored
        }
        public class EntityError
        {
            public string Message { get; private set; }
            public string Details { get; private set; }

            public EntityError(string message, string details)
            {
                Message = message;
                Details = details;
            }
        }
        public class EntityResult
        {
            public EntityId             SourceId;
            public EntityId             DestinationId;
            public EntityResultStatus   Status;
            public string               OverwriteDiff;
            public EntityError          Error;

            EntityResult(EntityId sourceId, EntityId destinationId, EntityResultStatus status, string overwriteDiff, EntityError error)
            {
                SourceId = sourceId;
                DestinationId = destinationId;
                Status = status;
                OverwriteDiff = overwriteDiff;
                Error = error;
            }

            public static EntityResult ForSuccessNewEntity(EntityId sourceId, EntityId destinationId)
            {
                return new EntityResult(sourceId, destinationId, EntityResultStatus.Success, null, null);
            }
            public static EntityResult ForSuccessOverwrite(EntityId sourceId, EntityId destinationId, string diff)
            {
                return new EntityResult(sourceId, destinationId, EntityResultStatus.Success, diff, null);
            }
            public static EntityResult ForError(EntityId sourceId, string message, string details)
            {
                return new EntityResult(sourceId, sourceId, EntityResultStatus.Error, null, new EntityError(message, details));
            }
            public static EntityResult ForDontOverwrite(EntityId sourceId)
            {
                return new EntityResult(sourceId, sourceId, EntityResultStatus.Ignored, null, null);
            }
        }

        public List<EntityResult> Entities;
        public ImportResponse(List<EntityResult> entities)
        {
            Entities = entities;
        }
    }

    /// <summary>
    /// Internal exception format
    /// </summary>
    public class ImportException : Exception
    {
        public string ImportMessage;
        public string ImportDetails;
        public ImportException(string message, string details) : base(message)
        {
            ImportMessage = message;
            ImportDetails = details;
        }
    }

    /// <summary>
    /// Provides mapping from source entityId to imported ids
    /// </summary>
    public class EntityImportEntityIdRemapper : IModelEntityIdRemapper
    {
        OverwritePolicy                                     _overwritePolicy;
        OrderedDictionary<EntityId, EntityId>               _memo               = new OrderedDictionary<EntityId, EntityId>();
        OrderedDictionary<EntityId, EntityId>               _fixedMap           = new OrderedDictionary<EntityId, EntityId>();
        OrderedDictionary<EntityKind, Func<Task<EntityId>>> _asyncIdGenerators  = new OrderedDictionary<EntityKind, Func<Task<EntityId>>>();

        public EntityImportEntityIdRemapper(OverwritePolicy overwritePolicy)
        {
            _overwritePolicy = overwritePolicy;
        }

        public void SetEntityKindGenerateIdFunc(EntityKind kind, Func<Task<EntityId>> generateNewIdAsyncFunc)
        {
            _asyncIdGenerators.Add(kind, generateNewIdAsyncFunc);
        }

        public void SetForcedMapping(EntityId sourceId, EntityId destinationId)
        {
            _fixedMap.Add(sourceId, destinationId);
        }

        public bool HasForcedMapping(EntityId sourceId) => _fixedMap.ContainsKey(sourceId);

        /// <summary>
        /// Gets the remapped entitity id for source id
        /// </summary>
        public async Task<EntityId> Get(EntityId sourceId)
        {
            if (_fixedMap.TryGetValue(sourceId, out EntityId fixedValue))
                return fixedValue;

            if (!_memo.ContainsKey(sourceId))
            {
                // Not in forced remap. In create mode, we create new ids as we go. Otherwise, Ids stay
                EntityId destinationId;
                if (_overwritePolicy == OverwritePolicy.CreateNew)
                {
                    Func<Task<EntityId>> getNewIdAsyncFunc = _asyncIdGenerators[sourceId.Kind];
                    destinationId = await getNewIdAsyncFunc();
                }
                else
                    destinationId = sourceId;

                _memo[sourceId] = destinationId;
            }

            return _memo[sourceId];
        }

        Task<EntityId> IModelEntityIdRemapper.RemapEntityIdAsync(EntityId originalId) => Get(originalId);
    }

    public static partial class EntityArchiveUtils
    {
        /// <summary>
        /// Import a series of entities. The overwrite policy determines what happens if an entity already exits. An optional
        /// 'remaps' list allows the user to specify that an entity in the import list should overwrite a specific entity. This
        /// is used in the use of the Dashboard's 'player overwrite' feature - but it's use is not limited to just this
        /// </summary>
        /// <param name="importEntities">List of entites to be imported. Format is a `{entityType:{id:data}}`</param>
        /// <param name="userRemaps">List of entity remaps. Format is `{entityType:{fromId:toId}}`</param>
        /// <param name="overwritePolicy">How to deal with overwriting entities that laready exist</param>
        /// <param name="verifyOnly">If true then only a dry-run of the import is performed</param>
        /// <param name="asker">External helper object to send Asks to entities</param>
        /// <returns></returns>
        public static Task<ImportResponse> Import(
            int formatVersion,
            OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> importEntities,
            OrderedDictionary<string, OrderedDictionary<string, string>> userRemaps,
            OverwritePolicy overwritePolicy,
            bool verifyOnly,
            IEntityAsker asker)
        {
            if (formatVersion < 1 || formatVersion > EntityArchiveFormatVersion)
                throw new InvalidOperationException($"Unsupported entity archive version {formatVersion}");

            OrderedDictionary<string, OrderedDictionary<EntityId, ExportedEntity>>  convertedImportEntities = new OrderedDictionary<string, OrderedDictionary<EntityId, ExportedEntity>>();
            OrderedDictionary<string, OrderedDictionary<EntityId, EntityId>> convertedUserRemaps = new OrderedDictionary<string, OrderedDictionary<EntityId, EntityId>>();

            foreach (string entityType in importEntities.Keys)
            {
                if (!convertedImportEntities.ContainsKey(entityType))
                    convertedImportEntities.Add(entityType, new OrderedDictionary<EntityId, ExportedEntity>());

                foreach ((string id, ExportedEntity data) in importEntities[entityType])
                {
                    EntityId entityId;
                    try
                    {
                        entityId = EntityId.ParseFromString(id);
                    }
                    catch (FormatException ex)
                    {
                        throw new ImportException($"Invalid source ID '{id}'.", ex.Message);
                    }
                    // Validate schemaVersion
                    if ((formatVersion > 1) && (data.SchemaVersion == 0))
                        throw new ImportException("Exported entity doesn't contain valid SchemaVersion", null);

                    convertedImportEntities[entityType].Add(entityId, data);
                }
            }

            foreach (string entityType in userRemaps.Keys)
            {
                if (!convertedUserRemaps.ContainsKey(entityType))
                    convertedUserRemaps.Add(entityType, new OrderedDictionary<EntityId, EntityId>());

                foreach((string sourceIdString, string destinationIdString) in userRemaps[entityType])
                {
                    EntityId sourceId;
                    EntityId destinationId;
                    try
                    {
                        sourceId = EntityId.ParseFromString(sourceIdString);
                    }
                    catch (FormatException ex)
                    {
                        throw new ImportException($"Invalid remap source ID '{sourceIdString}'.", ex.Message);
                    }
                    try
                    {
                        destinationId = EntityId.ParseFromString(destinationIdString);
                    }
                    catch (FormatException ex)
                    {
                        throw new ImportException("Invalid remap destination Id.", ex.Message);
                    }

                    convertedUserRemaps[entityType].Add(sourceId, destinationId);
                }
            }

            return Import(convertedImportEntities, convertedUserRemaps, overwritePolicy, verifyOnly, asker);
        }

#pragma warning disable CS0618 // Type or member is obsolete #LegacyEntityHandlerCompat
        static async Task<ImportResponse> Import(
            OrderedDictionary<string, OrderedDictionary<EntityId, ExportedEntity>> importEntities,
            OrderedDictionary<string, OrderedDictionary<EntityId, EntityId>> userRemaps,
            OverwritePolicy overwritePolicy,
            bool verifyOnly,
            IEntityAsker asker)
        {
            // Prepare remapping
            EntityImportEntityIdRemapper remapper = new EntityImportEntityIdRemapper(overwritePolicy);
            foreach ((string entityType, ArchivingSpec archivingSpec) in s_archiveHandlers)
            {
                ImportEntityHandler handler = archivingSpec.CreateImportHandler();

                remapper.SetEntityKindGenerateIdFunc(handler.EntityKind, async () => await handler.GenerateNewId(asker));

                if (userRemaps.TryGetValue(entityType, out OrderedDictionary<EntityId, EntityId> typeRemaps))
                {
                    foreach((EntityId sourceId, EntityId destinationId) in typeRemaps)
                    {
                        bool isValidId;
                        string validationMessage;

                        (isValidId, validationMessage) = handler.ValidateId(sourceId);
                        if (!isValidId)
                            throw new ImportException("Invalid remap source Id.", validationMessage);

                        (isValidId, validationMessage) = handler.ValidateId(destinationId);
                        if (!isValidId)
                            throw new ImportException("Invalid remap destination Id.", validationMessage);

                        remapper.SetForcedMapping(sourceId, destinationId);
                    }
                }
            }

            // Go through each entity type that we have a handler for and verify any available entities
            List<(ImportEntityHandler, EntityResult)> verifyResults = new List<(ImportEntityHandler, EntityResult)>();
            foreach ((string entityType, ArchivingSpec archivingSpec) in s_archiveHandlers)
            {
                // Extract a list of entities for this type from the input data
                OrderedDictionary<EntityId, ExportedEntity> entities = importEntities.GetValueOrDefault(entityType);

                // If there were any entities of this type then try to import them now
                if (entities == null)
                    continue;

                // Verify each entity of this type
                foreach ((EntityId id, ExportedEntity data) in entities)
                {
                    ImportEntityHandler handler = archivingSpec.CreateImportHandler();
                    EntityResult verifyResult = await PrepareHandlerAsync(remapper, overwritePolicy, handler, id, data, asker);

                    verifyResults.Add((handler, verifyResult));
                }
            }

            if (verifyOnly)
            {
                List<EntityResult> resultsOnly = verifyResults.Select(pair => pair.Item2).ToList();
                return new ImportResponse(resultsOnly);
            }

            // If we're not just verifying then now we can do the actual import work
            List<EntityResult> importResults = new List<EntityResult>();
            foreach ((ImportEntityHandler handler, EntityResult verifyResult) in verifyResults)
            {
                EntityResult importResult;

                if (verifyResult.Status != EntityResultStatus.Success)
                {
                    // Don't bother trying to write if validation already failed
                    importResult = verifyResult;
                }
                else
                {
                    try
                    {
                        await handler.Write(asker);
                        importResult = verifyResult;
                    }
                    catch (ImportException ex)
                    {
                        importResult = EntityResult.ForError(verifyResult.SourceId, ex.ImportMessage, ex.ImportDetails);
                    }
                }
                importResults.Add(importResult);
            }
            return new ImportResponse(importResults);
        }

        static async Task<EntityResult> PrepareHandlerAsync(EntityImportEntityIdRemapper remapper, OverwritePolicy overwritePolicy, ImportEntityHandler handler, EntityId sourceId, ExportedEntity data, IEntityAsker asker)
        {
            (bool isValidId, string validationMessage) = handler.ValidateId(sourceId);
            if (!isValidId)
            {
                return EntityResult.ForError(sourceId, "Invalid source ID", validationMessage);
            }

            try
            {
                handler.Construct(sourceId, data);
            }
            catch (ImportException ex)
            {
                return EntityResult.ForError(sourceId, ex.ImportMessage, ex.ImportDetails);
            }

            // Remap always. Even if this entity didn't get remapped, this entity might have references to other entities.
            try
            {
                await handler.Remap(remapper);
            }
            catch (Exception ex)
            {
                return EntityResult.ForError(sourceId, "EntityID remapping failed", ex.ToString());
            }

            bool destinationEntityExists = await handler.DoesExist();
            OverwritePolicy effectivePolicy;

            if (!destinationEntityExists)
            {
                // Any import on a new entity is a create new.
                effectivePolicy = OverwritePolicy.CreateNew;
            }
            else if (remapper.HasForcedMapping(sourceId))
            {
                // Any remap on existing is an overwrite.
                effectivePolicy = OverwritePolicy.Overwrite;
            }
            else
            {
                MetaDebug.Assert(overwritePolicy != OverwritePolicy.CreateNew, "remapping did not generate a new id for new entity");
                // Specified mode chooses between Overwrite and Ignore for an existing entity.
                effectivePolicy = overwritePolicy;
            }

            if (effectivePolicy == OverwritePolicy.CreateNew)
            {
                handler.PrepareForCreate();
                return EntityResult.ForSuccessNewEntity(sourceId, handler.GetId());
            }
            else if (effectivePolicy == OverwritePolicy.Overwrite)
            {
                // Overwrite existing entity
                handler.PrepareForOverwrite();
                string diff;
                try
                {
                    diff = await handler.Diff(handler.GetId(), asker);
                }
                catch (Exception ex)
                {
                    return EntityResult.ForError(sourceId, "Failed to generate difference against existing entity", ex.ToString());
                }
                return EntityResult.ForSuccessOverwrite(sourceId, handler.GetId(), diff);
            }
            else if (effectivePolicy == OverwritePolicy.Ignore)
            {
                // Entity already exists and we aren't allowed to overwrite
                handler.PrepareForIgnore();
                return EntityResult.ForDontOverwrite(sourceId);
            }
            else
            {
                throw new InvalidOperationException("unreachable");
            }
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
