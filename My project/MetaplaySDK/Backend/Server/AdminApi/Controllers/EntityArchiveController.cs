// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.EntityArchive;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for entity import and export requests.
    /// </summary>
    public class EntityArchiveController : GameAdminApiController
    {
        public EntityArchiveController(ILogger<EntityArchiveController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerEntityArchiveExported)]
        public class GameServerEventEntityArchiveExported : GameServerEventPayloadBase
        {
            [JsonIgnore]
            [MetaMember(1)] public ExportRequest RequestDeprecated { get; private set; }
            [MetaMember(2)] public List<string> RequestedEntityIds { get; private set; }
            public GameServerEventEntityArchiveExported() { }
            public GameServerEventEntityArchiveExported(ExportRequest request)
            {
                RequestedEntityIds = ReturnEntitiesFromExportRequest(request);
            }
            [MetaOnDeserialized]
            private void OnDeserialized()
            {
                // Version 0 used RequestDeprecated, but this was removed in favour of storing the IDs in an unrolled
                // fashion directly in RequestedEntityIds.
                if (RequestDeprecated != null)
                {
                    RequestedEntityIds = ReturnEntitiesFromExportRequest(RequestDeprecated);
                    RequestDeprecated = null;
                }
            }
            override public string SubsystemName => "EntityArchive";
            override public string EventTitle => "Exported";
            override public string EventDescription
            {
                get
                {
                    int entityCount = RequestedEntityIds.Count;
                    return Invariant($"{entityCount} {(entityCount == 1 ? "entity" : "entities")} exported.");
                }
            }
            private List<string> ReturnEntitiesFromExportRequest(ExportRequest exportRequest)
            {
                return exportRequest.Entities.Values
                    .SelectMany(x => x)
                    .Order(StringComparer.InvariantCulture)
                    .ToList();
            }
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerEntityArchiveImported)]
        public class GameServerEventEntityArchiveImported : GameServerEventPayloadBase
        {
            [MetaMember(1)] public ImportRequest Request { get; private set; }
            public GameServerEventEntityArchiveImported() { }
            public GameServerEventEntityArchiveImported(ImportRequest request) { Request = request; }
            override public string SubsystemName => "EntityArchive";
            override public string EventTitle => "Imported";
            override public string EventDescription
            {
                get
                {
                    int entityCount = Request.Entities.Sum(x => x.Value.Count());
                    return Invariant($"{entityCount} {(entityCount == 1 ? "entity" : "entities")} imported.");
                }
            }
        }


        /// <summary>
        /// HTTP request and response format for export
        /// </summary>
        [MetaSerializable]
        public class ExportRequest
        {
            // List of entity types to export, with each entity type containing a list of IDs to export. Eg:
            // {"player":["Player:0000000003","Player:0000000009"]}
            [JsonProperty(Required = Required.Always)]
            [MetaMember(1)] public OrderedDictionary<string, List<string>> Entities = new OrderedDictionary<string, List<string>>();
            [MetaMember(2)] public bool AllowExportOnFailure;
        }
        public class ExportResponse
        {
            // Version of the output format
            public int FormatVersion = EntityArchiveUtils.EntityArchiveFormatVersion;

            // List of player entity types, with each entity type containing a list of ID/data pairs. Eg:
            // {"player":{{"player:0000000003":"{DATA}"},{"player:0000000009":"{DATA}"}}}
            // DATA elements contain the actual payload as base64-encoded string and the schema version of the data.
            public OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> Entities = new OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>>();

            public ExportResponse(OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> entities)
            {
                Entities = entities;
            }
        }


        /// <summary>
        /// HTTP request and response format for import
        /// </summary>
        [MetaSerializable]
        public class ImportRequest
        {
            // Version of the input format. Initial version 1 did not contain this field so value default to version 1.
            [MetaMember(4)] public int FormatVersion = 1;

            // List of entity types to import, with each entity type containing a list of ID/data pairs, Eg:
            // {"player":[{"player:0000000003":"{DATA}"},{"player:0000000009":"{DATA}"}]}
            // DATA elements contain the actual payload as base64-encoded string and the schema version of the data.
            [MetaMember(3)] public OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> Entities = new OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>>();
            // Legacy format, retained for audit log
            [MetaMember(1)] public OrderedDictionary<string, OrderedDictionary<string, string>> EntitiesV1;

            // [Optional] list of entity types, with each entity type containing a list of fromID/toID pairs. Eg:
            // {"player":{{"player:0000000007":"player:0000000002"}}}
            [MetaMember(2)] public OrderedDictionary<string, OrderedDictionary<string, string>> Remaps = new OrderedDictionary<string, OrderedDictionary<string, string>>();
        }
        class ImportTopLevelFailure
        {
            public class ErrorResponse
            {
                public string Message { get; private set; }
                public string Details { get; private set; }

                public ErrorResponse(string message, string details)
                {
                    Message = message;
                    Details = details;
                }
            }
            ErrorResponse Error;
            public ImportTopLevelFailure(ErrorResponse error)
            {
                Error = error;
            }
        }

        /// <summary>
        /// API endpoint to export an entity archive. Provide a list of entites that you want to export
        /// Usage:  POST /api/entityArchive/export
        /// Test:   curl --location --request POST 'http://localhost:5550/api/entityArchive/export' \
        ///             --header 'Content-Type: application/json' \
        ///             --data-raw '{
        ///                 "entities": {
        ///                     "player": ["Player:0000000009"]
        ///                 }
        ///             }
        /// </summary>
        [HttpPost("entityArchive/export")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiEntityArchiveExport)]
        [RequestFormLimits(KeyLengthLimit = 9000000)]
        public async Task<ActionResult> Export()
        {
            // Parse parameters
            ExportRequest bodyRequest = await ParseBodyAsync<ExportRequest>();

            // Do the export
            EntityArchive.ExportResponse exportResponse = await EntityArchiveUtils.Export(bodyRequest.Entities, bodyRequest.AllowExportOnFailure, this);
            if (!exportResponse.Success)
                throw new MetaplayHttpException(400, exportResponse.ErrorMessage, exportResponse.ErrorDetails);

            // Audit log event
            await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerEventEntityArchiveExported(bodyRequest)));

            // Return results
            return new JsonResult(
                new ExportResponse(exportResponse.Results),
                AdminApiJsonSerialization.UntypedSerializationSettings);
        }


        /// <summary>
        /// API endpoint to validate the import of an entity archive
        /// Usage:  GETPOST /api/entityArchive/validate
        /// Test:   curl --location --request POST 'http://localhost:5550/api/entityArchive/validate?overwritePolicy=ignore' \
        ///             --header 'Content-Type: application/json' \
        ///             --data-raw '{
        ///                     "entities": {
        ///                             "player": {
        ///                                     "Player:0000000009": "DwICCAAPFAIQAgICiICAgICAgIAEEQwECkFJICM4AgYCAghmAg4UEQ8WAgIC4fnY1tj4ir6oAREPGAIQAgIC+oO31YRdERAEAgLErsHVhF0RAgYGAggAEQwaBGVuEhwADBIgAA8CJAASJgAPEyIADA8SKAAOAioCEiwAAhMuAgwPYGhFV2o4RkQyY2tTcEp4UkZyc2lISmVJQmFvOHB2U2dtVTdrTzgzbm00aU1xWGh2MQIMAmBoRVdqOEZEMmNrU3BKeFJGcnNpSEplSUJhbzhwdlNnbVU3a084M25tNGlNcVhodjEMBAZib3QQBgICgoS31YRdERESMAQPAhACAgKChLfVhF0RDARgaEVXajhGRDJja1NwSnhSRnJzaUhKZUlCYW84cHZTZ21VN2tPODNubTRpTXFYaHYxDAYGYm90EQIQAgICxK7B1YRdEQwEYGhFV2o4RkQyY2tTcEp4UkZyc2lISmVJQmFvOHB2U2dtVTdrTzgzbm00aU1xWGh2MQwGBmJvdBETMgIPDwICAgAMBGBoRVdqOEZEMmNrU3BKeFJGcnNpSEplSUJhbzhwdlNnbVU3a084M25tNGlNcVhodjERAhACAgKChLfVhF0RERPIAQQMDxJXb3JkUHJlc3MCDAISV29yZFByZXNzAgRkEAYCAtq9w9WEXRERElBob3RvU2hvcAIMAhJQaG90b1Nob3ACBBwQBgIC4uTD1YRdERER"
        ///                             }
        ///                     },
        ///                     "remaps": {
        ///                             "player": {
        ///                                     "Player:0000000009": "Player:0000000000"
        ///                             }
        ///                     }
        ///             }'
        /// </summary>
        [HttpPost("entityArchive/validate")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiEntityArchiveImport)]
        [RequestFormLimits(KeyLengthLimit = 9000000)]
        public Task<ActionResult> Validate([FromQuery] OverwritePolicy overwritePolicy = OverwritePolicy.Overwrite)
        {
            return ImportOrValidate(overwritePolicy, true);
        }

        /// <summary>
        /// API endpoint to import an entity archive
        /// Usage:  POST /api/entityArchive/import
        /// Test:   curl --location --request POST 'http://localhost:5550/api/entityArchive/import?overwritePolicy=ignore' \
        ///             --header 'Content-Type: application/json' \
        ///             --data-raw '{
        ///                     "entities": {
        ///                             "player": {
        ///                                     "Player:0000000009": "DwICCAAPFAIQAgICiICAgICAgIAEEQwECkFJICM4AgYCAghmAg4UEQ8WAgIC4fnY1tj4ir6oAREPGAIQAgIC+oO31YRdERAEAgLErsHVhF0RAgYGAggAEQwaBGVuEhwADBIgAA8CJAASJgAPEyIADA8SKAAOAioCEiwAAhMuAgwPYGhFV2o4RkQyY2tTcEp4UkZyc2lISmVJQmFvOHB2U2dtVTdrTzgzbm00aU1xWGh2MQIMAmBoRVdqOEZEMmNrU3BKeFJGcnNpSEplSUJhbzhwdlNnbVU3a084M25tNGlNcVhodjEMBAZib3QQBgICgoS31YRdERESMAQPAhACAgKChLfVhF0RDARgaEVXajhGRDJja1NwSnhSRnJzaUhKZUlCYW84cHZTZ21VN2tPODNubTRpTXFYaHYxDAYGYm90EQIQAgICxK7B1YRdEQwEYGhFV2o4RkQyY2tTcEp4UkZyc2lISmVJQmFvOHB2U2dtVTdrTzgzbm00aU1xWGh2MQwGBmJvdBETMgIPDwICAgAMBGBoRVdqOEZEMmNrU3BKeFJGcnNpSEplSUJhbzhwdlNnbVU3a084M25tNGlNcVhodjERAhACAgKChLfVhF0RERPIAQQMDxJXb3JkUHJlc3MCDAISV29yZFByZXNzAgRkEAYCAtq9w9WEXRERElBob3RvU2hvcAIMAhJQaG90b1Nob3ACBBwQBgIC4uTD1YRdERER"
        ///                             }
        ///                     },
        ///                     "remaps": {
        ///                             "player": {
        ///                                     "Player:0000000009": "Player:0000000000"
        ///                             }
        ///                     }
        ///             }'
        /// </summary>
        [HttpPost("entityArchive/import")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiEntityArchiveImport)]
        [RequestFormLimits(KeyLengthLimit = 9000000)]
        public Task<ActionResult> Import([FromQuery] OverwritePolicy overwritePolicy = OverwritePolicy.Overwrite)
        {
            return ImportOrValidate(overwritePolicy, false);
        }

        async Task<ActionResult> ImportOrValidate(OverwritePolicy overwritePolicy, bool validateOnly)
        {
            // Parse parameters
            ImportRequest bodyRequest = await ParseBodyAsync<ImportRequest>();
            object response;

            try
            {
                EntityArchive.ImportResponse importResponse = await EntityArchiveUtils.Import(bodyRequest.FormatVersion, bodyRequest.Entities, bodyRequest.Remaps, overwritePolicy, validateOnly, asker: this);
                if (!validateOnly)
                    await WriteAuditLogEventAsync(new GameServerEventBuilder(new GameServerEventEntityArchiveImported(bodyRequest)));
                response = importResponse;
            }
            catch (ImportException ex)
            {
                response = new ImportTopLevelFailure(new ImportTopLevelFailure.ErrorResponse(ex.Message, ex.ImportDetails));
            }
            catch (Exception ex)
            {
                response = new ImportTopLevelFailure(new ImportTopLevelFailure.ErrorResponse("Unknown import validation error.", ex.ToString()));
            }

            return Ok(response);
        }
    }
}
