// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for inspecting Entity sizes
    /// </summary>
    public class EntityDatabaseInfoController : GameAdminApiController
    {
        public EntityDatabaseInfoController(ILogger<EntityDatabaseInfoController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        [MetaSerializableDerived(MetaplayAuditLogEventCodes.DatabaseEntityInspected)]
        public class DatabaseEntityInspected : DatabaseEntityEventPayloadBase
        {
            public DatabaseEntityInspected() { }
            override public string EventTitle => "Inspected";
            override public string EventDescription => "Database entity inspected.";
        }

        public class EntityDetails
        {
            public object   Structure               { get; set; }
            public DateTime PersistedAt             { get; set; }
            public int      SchemaVersion           { get; set; }
            public bool     IsFinal                 { get; set; }
            public int      CurrentSchemaVersion    { get; set; }
            public int      CompressedSize          { get; set; }
            public int      UncompressedSize        { get; set; }
        }

        /// <summary>
        /// API endpoint to return size information of an Entity in the Database
        /// Usage:  GET /api/entities/{ENTITYID}/dbinfo
        /// Test:   curl http://localhost:5550/api/entities/{ENTITYID}/dbinfo
        /// </summary>
        [HttpGet("entities/{entityIdStr}/dbinfo")]
        [RequirePermission(MetaplayPermissions.ApiDatabaseInspectEntity)]
        public async Task<ActionResult<EntityDetails>> GetDbInfo(string entityIdStr)
        {
            // Fetch persisted
            EntityId entityId;
            try
            {
                entityId = EntityId.ParseFromString(entityIdStr);
            }
            catch (FormatException ex)
            {
                throw new MetaplayHttpException(400, "Entity not found.", $"Entity ID {entityIdStr} is not valid: {ex.Message}");
            }

            if (!EntityConfigRegistry.Instance.TryGetPersistedConfig(entityId.Kind, out PersistedEntityConfig entityConfig))
                throw new MetaplayHttpException(400, "Invalid Entity kind.", $"Entity kind {entityId.Kind} does not refer to a PersistedEntity");

            IPersistedEntity persistedEntity = await MetaDatabase.Get().TryGetAsync<IPersistedEntity>(entityConfig.PersistedType, entityId.ToString()).ConfigureAwait(false);
            if (persistedEntity == null)
                throw new MetaplayHttpException(404, "Entity not found.", $"Cannot find entity with ID {entityIdStr}.");

            // Log inspection into audit logs
            await WriteAuditLogEventAsync(new DatabaseEntityEventBuilder(entityId, new DatabaseEntityInspected()));

            if (persistedEntity.Payload != null)
            {
                // Inspect object tree and convert it into JSONified tree
                TaggedSerializedInspector.ObjectInfo objectTree;
                int uncompressedSize;
                if (BlobCompress.IsCompressed(persistedEntity.Payload))
                {
                    using FlatIOBuffer buffer = BlobCompress.DecompressBlob(persistedEntity.Payload);
                    uncompressedSize = buffer.Count;
                    using IOReader reader = new IOReader(buffer);
                    objectTree = TaggedSerializedInspector.Inspect(reader, entityConfig.PersistedPayloadType, checkReaderWasCompletelyConsumed: true);
                }
                else
                {
                    uncompressedSize = persistedEntity.Payload.Length;

                    using IOReader reader = new IOReader(persistedEntity.Payload);
                    objectTree = TaggedSerializedInspector.Inspect(reader, entityConfig.PersistedPayloadType, checkReaderWasCompletelyConsumed: true);
                }

                static JObject ConvertTree(TaggedSerializedInspector.ObjectInfo objectTree)
                {
                    JObject structure = new JObject();
                    structure["WireType"] = objectTree.WireType.ToString();
                    structure["TypeName"] = objectTree.SerializableType?.Name ?? objectTree.ProjectedPrimitiveType?.ToGenericTypeString() ?? "unknown";
                    structure["EnvelopeStartOffset"] = objectTree.EnvelopeStartOffset;
                    structure["EnvelopeEndOffset"] = objectTree.EnvelopeEndOffset;
                    structure["PayloadStartOffset"] = objectTree.PayloadStartOffset;
                    structure["PayloadEndOffset"] = objectTree.PayloadEndOffset;

                    if (objectTree.IsPrimitive)
                    {
                        object primitive = objectTree.ProjectedPrimitiveValue ?? objectTree.PrimitiveValue;

                        // \note: JS cannot represent all primitive values of C#, such as (U)Int64:s. To prevent a very confusing data loss (integers get converted
                        // into doubles which rounds off a few digits), we need to stringify these values. For consistency, stringify all other types too.
                        if (primitive != null)
                            structure["PrimitiveValue"] = Util.ObjectToStringInvariant(primitive);
                        else
                            structure["PrimitiveValue"] = null;
                    }
                    else if (objectTree.Members != null)
                    {
                        List<JObject> members = new List<JObject>();
                        foreach (TaggedSerializedInspector.ObjectInfo.MemberInfo member in objectTree.Members)
                        {
                            JObject memberStructure = ConvertTree(member.ObjectInfo);
                            memberStructure["TagId"] = member.TagId;
                            if (member.Name != null)
                                memberStructure["FieldName"] = member.Name;
                            members.Add(memberStructure);
                        }
                        structure["Members"] = new JArray(members.ToArray());
                    }
                    else if (objectTree.ValueCollection != null)
                    {
                        List<JObject> elements = new List<JObject>();
                        foreach (TaggedSerializedInspector.ObjectInfo element in objectTree.ValueCollection)
                        {
                            JObject elementStructure = ConvertTree(element);
                            elements.Add(elementStructure);
                        }
                        structure["Elements"] = new JArray(elements.ToArray());
                    }
                    else if (objectTree.KeyValueCollection != null)
                    {
                        List<JObject> kvPairs = new List<JObject>();
                        foreach ((TaggedSerializedInspector.ObjectInfo key, TaggedSerializedInspector.ObjectInfo value) in objectTree.KeyValueCollection)
                        {
                            JObject pair = new JObject();
                            pair["Key"] = ConvertTree(key);
                            pair["Value"] = ConvertTree(value);
                            kvPairs.Add(pair);
                        }
                        structure["Dictionary"] = new JArray(kvPairs.ToArray());
                    }

                    return structure;
                }

                // Respond to browser
                return new EntityDetails
                {
                    Structure = ConvertTree(objectTree),
                    PersistedAt = persistedEntity.PersistedAt,
                    SchemaVersion = persistedEntity.SchemaVersion,
                    IsFinal = persistedEntity.IsFinal,
                    CurrentSchemaVersion = entityConfig.CurrentSchemaVersion,
                    CompressedSize = persistedEntity.Payload.Length,
                    UncompressedSize = uncompressedSize,
                };
            }
            else
            {
                // If entity state has not yet been initialized, return limited details
                return new EntityDetails
                {
                    Structure = null,
                    PersistedAt = persistedEntity.PersistedAt,
                    SchemaVersion = persistedEntity.SchemaVersion,
                    IsFinal = persistedEntity.IsFinal,
                    CurrentSchemaVersion = entityConfig.CurrentSchemaVersion,
                    CompressedSize = 0,
                    UncompressedSize = 0,
                };
            }
        }
    }
}
