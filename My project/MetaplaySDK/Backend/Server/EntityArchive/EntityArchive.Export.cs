// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Server.EntityArchive
{
    /// <summary>
    /// Response from the export function
    /// </summary>
    [MetaSerializable]
    public class ExportResponse
    {
        [MetaMember(1)] public bool Success;                // If true then Results are valid, otherwise the Error members are valid

        [MetaMember(2)] public OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> Results;

        [MetaMember(3)] public string ErrorMessage;         // Human readable error message overview
        [MetaMember(4)] public string ErrorDetails;         // Detailed error message

        public ExportResponse() { }
        public ExportResponse(OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> results)
        {
            Success = true;
            Results = results;
        }
        public ExportResponse(string errorMessage, string errorDetails)
        {
            Success = false;
            ErrorMessage = errorMessage;
            ErrorDetails = errorDetails;
        }
    }


    /// <summary>
    /// Internal exception format
    /// </summary>
    public class ExportException : Exception
    {
        public string ExportMessage;
        public string ExportDetails;
        public ExportException() { }
        public ExportException(string message, string details) : base(message)
        {
            ExportMessage = message;
            ExportDetails = details;
        }
    }


    public static partial class EntityArchiveUtils
    {
        /// <summary>
        /// Export a series of entities
        /// </summary>
        /// <param name="exportEntities">Details about which entities to request. Format is `{entityType:[id]}`</param>
        /// <param name="allowExportOnLoadFailure">Export raw entity payloads even if loading the associated entity fails.</param>
        /// <param name="asker">External helper object to send Asks to entities</param>
        /// <returns></returns>
        public static async Task<ExportResponse> Export(OrderedDictionary<string, List<string>> exportEntities, bool allowExportOnLoadFailure, IEntityAsker asker)
        {
            // We'll store the export results in here
            OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> results = new OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>>();

            // Go through each entity type that we have a handler for and try to export any available entities
            foreach ((string entityType, ArchivingSpec archivingSpec) in s_archiveHandlers)
            {
                // Extract a list of entities for this type from the input data
                List<string> entityRequests = exportEntities.GetValueOrDefault(entityType);

                // If there were any entities of this type then try to export them now
                if (entityRequests != null)
                {
                    // Export all requested entities, storing the results in a dictionary
                    OrderedDictionary<string, ExportedEntity> entities = new OrderedDictionary<string, ExportedEntity>();
                    foreach (string id in entityRequests)
                    {
                        try
                        {
#pragma warning disable CS0618 // Type or member is obsolete #LegacyEntityHandlerCompat
                            ExportEntityHandler handler = archivingSpec.CreateExportHandler();
                            handler.Construct(id, allowExportOnLoadFailure);
                            ExportedEntity exportData = await handler.Export(asker);
                            entities.Add(id, exportData);
#pragma warning restore CS0618 // Type or member is obsolete
                        }
                        catch (ExportException ex)
                        {
                            return new ExportResponse(ex.ExportMessage, ex.ExportDetails);
                        }
                        catch (Exception ex)
                        {
                            return new ExportResponse("Unknown error.", ex.ToString());
                        }
                    }

                    // Add the results from this entity type into the overall results
                    results.Add(entityType, entities);
                }
            }

            return new ExportResponse(results);
        }
    }
}
