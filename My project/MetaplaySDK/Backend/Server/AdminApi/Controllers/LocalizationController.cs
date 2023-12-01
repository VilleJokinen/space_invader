// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Json;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK routes to work with localizations.
    /// </summary>
    [LocalizationsEnabledCondition]
    public class LocalizationController : GameAdminApiController
    {
        public LocalizationController(ILogger<LocalizationController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        public class MinimalLocalizationInfo
        {
            public MetaGuid Id { get; private set; }

            public string Name { get; private set; }

            public string Description { get; private set; }

            public string VersionHash { get; private set; }

            public DateTime LastModifiedAt { get; private set; }

            public string Source { get; private set; }

            public DateTime ArchiveBuiltAt { get; private set; }

            public bool IsArchived { get; private set; } = false;

            public string TaskId { get; private set; }

            public string FailureInfo { get; private set; }

            public GameDataControllerUtility.GameDataBuildStatus Status { get; set; }

            public bool IsActive { get; set; }

            public MetaTime PersistedAt { get; set; }

            public MinimalLocalizationInfo()
            {
            }

            public static MinimalLocalizationInfo FromPersisted(PersistedLocalizations locs, MetaGuid activeId, (GameDataControllerUtility.GameDataBuildStatus status, string error) statusAndError)
            {
                MetaGuid id = MetaGuid.Parse(locs.Id);
                return new MinimalLocalizationInfo()
                {
                    Id             = id,
                    Name           = locs.Name,
                    Description    = locs.Description,
                    VersionHash    = locs.VersionHash,
                    PersistedAt    = MetaTime.FromDateTime(id.GetDateTime()),
                    LastModifiedAt = locs.LastModifiedAt,
                    Source         = locs.Source,
                    ArchiveBuiltAt = locs.ArchiveBuiltAt,
                    IsArchived     = locs.IsArchived,
                    IsActive       = id == activeId,
                    TaskId         = locs.TaskId,
                    FailureInfo    = statusAndError.error,
                    Status         = statusAndError.status,
                };
            }
        }

        // <summary>
        // Get list of PersistedLocalizations entries in the database, without contents.
        // Usage:  GET /api/localizations
        // Test:   curl http://localhost:5550/api/localizations
        // </summary>
        [HttpGet("localizations")]
        [RequirePermission(MetaplayPermissions.ApiLocalizationView)]
        public async Task<ActionResult<IEnumerable<MinimalLocalizationInfo>>> GetLocalizationList([FromQuery] bool showArchived = false)
        {
            GlobalStatusResponse                status  = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            MetaDatabase                        db      = MetaDatabase.Get(QueryPriority.Normal);
            IEnumerable<PersistedLocalizations> locs = await db.QueryAllLocalizations(showArchived);

            // Retrieve build task statuses if needed
            Dictionary<MetaGuid, BackgroundTaskStatus> taskStatuses = null;
            if (locs.Any(config => config.TaskId != null))
            {
                //TODO: Fix this once we get loc building from dash
                // BackgroundTaskStatusResponse taskStatusResponse = await AskEntityAsync<BackgroundTaskStatusResponse>(BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(BuildStaticGameConfigTask)));
                // taskStatuses = taskStatusResponse.Tasks.ToDictionary(t => t.Id, t => t);
            }

            IEnumerable<MinimalLocalizationInfo> persisted = locs.Select(x => MinimalLocalizationInfo.FromPersisted(x, status.ActiveLocalizationsId, GameDataControllerUtility.GetConfigStatus(x, taskStatuses)));
            return Ok(persisted);
        }

        // <summary>
        // Get a PersistedLocalizations entry.
        // Usage:  GET /api/localization/03c44d2ee8f24bd-0-ef35666bc8a69033
        // Test:   curl http://localhost:5550/api/localization/03c44d2ee8f24bd-0-ef35666bc8a69033
        // </summary>
        [HttpGet("localization/{localizationId}")]
        [RequirePermission(MetaplayPermissions.ApiLocalizationView)]
        public async Task<ActionResult<Dictionary<LanguageId, LocalizationLanguage>>> GetLocalization(string localizationId)
        {
            GlobalStatusResponse                status = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);

            PersistedLocalizations persisted = await GameDataControllerUtility.GetPersistedGameDataByIdStringOr404Async<PersistedLocalizations>(localizationId, status.ActiveLocalizationsId);
            // Retrieve build task statuses if needed
            Dictionary<MetaGuid, BackgroundTaskStatus> taskStatuses = null;
            if (persisted.TaskId != null)
            {
                //TODO: Fix this once we get loc building from dash
                // BackgroundTaskStatusResponse taskStatusResponse = await AskEntityAsync<BackgroundTaskStatusResponse>(
                //     BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(BuildStaticGameConfigTask)));
                // taskStatuses = taskStatusResponse.Tasks.ToDictionary(t => t.Id, t => t);
            }

            Dictionary<LanguageId, LocalizationLanguage> locs    = new Dictionary<LanguageId, LocalizationLanguage>();
            ConfigArchive                                archive = ConfigArchive.FromBytes(persisted.ArchiveBytes);
            foreach (ConfigArchiveEntry entry in archive.Entries)
            {
                LanguageId           languageId    = LanguageId.FromString(Path.GetFileNameWithoutExtension(entry.Name));
                byte[]               languageBytes = entry.Bytes.ToArray();
                LocalizationLanguage loc           = LocalizationLanguage.FromBytes(languageId, entry.Hash, languageBytes);
                locs.Add(languageId, loc);
            }

            return Ok(new {
                info  =  MinimalLocalizationInfo.FromPersisted(persisted, status.ActiveLocalizationsId, GameDataControllerUtility.GetConfigStatus(persisted, taskStatuses)),
                locs = locs,
            });
        }

        // <summary>
        // Get a PersistedLocalizations entry, transposed to a table format with languages as columns.
        // Usage:  GET /api/localization/03c44d2ee8f24bd-0-ef35666bc8a69033
        // Test:   curl http://localhost:5550/api/localization/03c44d2ee8f24bd-0-ef35666bc8a69033
        // </summary>
        [HttpGet("localization/{localizationId}/table")]
        [RequirePermission(MetaplayPermissions.ApiLocalizationView)]
        public async Task<ActionResult<IEnumerable<MinimalLocalizationInfo>>> GetLocalizationTable(string localizationId)
        {
            GlobalStatusResponse                status = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);

            PersistedLocalizations persisted = await GameDataControllerUtility.GetPersistedGameDataByIdStringOr404Async<PersistedLocalizations>(localizationId, status.ActiveLocalizationsId);
            // Retrieve build task statuses if needed
            Dictionary<MetaGuid, BackgroundTaskStatus> taskStatuses = null;
            if (persisted.TaskId != null)
            {
                //TODO: Fix this once we get loc building from dash
                // BackgroundTaskStatusResponse taskStatusResponse = await AskEntityAsync<BackgroundTaskStatusResponse>(
                //     BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(BuildStaticGameConfigTask)));
                // taskStatuses = taskStatusResponse.Tasks.ToDictionary(t => t.Id, t => t);
            }

            Dictionary<LanguageId, LocalizationLanguage> locs        = new Dictionary<LanguageId, LocalizationLanguage>();
            ConfigArchive                                archive     = ConfigArchive.FromBytes(persisted.ArchiveBytes);
            List<string>                                 languageIds = new List<string>(locs.Count);
            foreach (ConfigArchiveEntry entry in archive.Entries)
            {
                LanguageId           languageId    = LanguageId.FromString(Path.GetFileNameWithoutExtension(entry.Name));
                byte[]               languageBytes = entry.Bytes.ToArray();
                LocalizationLanguage loc           = LocalizationLanguage.FromBytes(languageId, entry.Hash, languageBytes);
                languageIds.Add(languageId.Value);
                locs.Add(languageId, loc);
            }

            // Order english to front since it's the most important language for our users.
            int englishIndex = languageIds.FindIndex(
                x => x.Equals("en", StringComparison.InvariantCultureIgnoreCase) ||
                    x.Equals("english", StringComparison.InvariantCultureIgnoreCase) ||
                    x.Equals("eng", StringComparison.InvariantCultureIgnoreCase));
            if (englishIndex != -1)
            {
                var englishKey = languageIds[englishIndex];
                languageIds.RemoveAt(englishIndex);
                languageIds.Insert(0, englishKey);
            }

            List<GameConfigLibraryJsonConversionUtility.ConfigKey> table = new List<GameConfigLibraryJsonConversionUtility.ConfigKey>();
            foreach ((TranslationId key, string _) in locs.FirstOrDefault().Value.Translations)
            {
                Dictionary<string, object> values = new Dictionary<string, object>(locs.Count);
                foreach ((LanguageId languageId, LocalizationLanguage localizationLanguage) in locs)
                {
                    if (localizationLanguage.Translations.TryGetValue(key, out string translation))
                        values.Add(languageId.Value, translation);
                    else
                        values.Add(languageId.Value, null);
                }
                GameConfigLibraryJsonConversionUtility.ConfigKey configKey = new GameConfigLibraryJsonConversionUtility.ConfigKey(key.Value, null, values, "string");
                table.Add(configKey);
            }

            return Ok(new {
                info =  MinimalLocalizationInfo.FromPersisted(persisted, status.ActiveLocalizationsId, GameDataControllerUtility.GetConfigStatus(persisted, taskStatuses)),
                table = table,
                languageIds,
            });
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LocalizationPublished)]
        public class LocalizationEventLocalizationPublished : LocalizationEventPayloadBase
        {
            public LocalizationEventLocalizationPublished() { }
            public override string EventTitle       => "Published";
            public override string EventDescription => $"Localizations published.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LocalizationUnpublished)]
        public class LocalizationEventLocalizationUnpublished : LocalizationEventPayloadBase
        {
            public LocalizationEventLocalizationUnpublished() { }
            public override string EventTitle       => "Unpublished";
            public override string EventDescription => $"Localizations unpublished.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LocalizationUploaded)]
        public class LocalizationEventLocalizationUploaded : LocalizationEventPayloadBase
        {
            public LocalizationEventLocalizationUploaded() { }
            public override string EventTitle       => "Uploaded";
            public override string EventDescription => $"Localizations uploaded.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.LocalizationEdited)]
        public class LocalizationEventLocalizationEdited : LocalizationEventPayloadBase
        {
            [MetaMember(2)] public GameDataControllerUtility.GameDataEditableProperties OldValues { get; private set; }
            [MetaMember(3)] public GameDataControllerUtility.GameDataEditableProperties NewValues { get; private set; }

            public LocalizationEventLocalizationEdited() { }
            public LocalizationEventLocalizationEdited(GameDataControllerUtility.GameDataEditableProperties oldValues, GameDataControllerUtility.GameDataEditableProperties newValues)
            {
                OldValues = oldValues;
                NewValues = newValues;
            }
            public override string EventTitle       => "Edited";
            public override string EventDescription => NewValues.GetSummary("Localization", OldValues);
        }

        // <summary>
        // API Endpoint to publish a game config build
        // Usage:  POST /api/localizations/publish
        // Test:   curl -X POST http://localhost:5550/api/localizations/publish
        // </summary>
        [HttpPost("localization/publish")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiLocalizationEdit)]
        public async Task<IActionResult> PublishLocalization()
        {
            GameDataControllerUtility.GameDataIdInput input                = await ParseBodyAsync<GameDataControllerUtility.GameDataIdInput>();
            List<EventBuilder>                         auditLogs = await PublishLocalizations(input.Id);

            await WriteRelatedAuditLogEventsAsync(auditLogs);

            return Ok();
        }

        async Task<List<EventBuilder>> PublishLocalizations(MetaGuid id)
        {
            PublishLocalizationResponse response =
                await AskEntityAsync<PublishLocalizationResponse>(GlobalStateManager.EntityId,
                    new PublishLocalizationRequest(id));

            if (response.Status == PublishLocalizationResponse.StatusCode.Refused)
                throw new MetaplayHttpException(400, "Cannot publish localization", response.ErrorMessage);

            MetaGuid oldActiveLocalizationId = response.PreviousId;

            List<EventBuilder> auditLogs = new List<EventBuilder>();

            if (oldActiveLocalizationId.IsValid)
                auditLogs.Add(new LocalizationEventBuilder(oldActiveLocalizationId, new LocalizationEventLocalizationUnpublished()));
            auditLogs.Add(new LocalizationEventBuilder(id, new LocalizationEventLocalizationPublished()));

            return auditLogs;
        }

        /// <summary>
        /// API Endpoint to get active localization id
        /// Usage:  GET /api/activeLocalizationId
        /// Test:   curl http://localhost:5550/api/activeLocalizationId
        /// </summary>
        [HttpGet("activeLocalizationId")]
        [RequirePermission(MetaplayPermissions.ApiLocalizationView)]
        public async Task<IActionResult> GetActiveLocalizationId()
        {
            // Fetch active localization version from global state
            GlobalStatusResponse globalStatus = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            if (!globalStatus.ActiveLocalizationsId.IsValid)
                return NotFound();

            return Ok(globalStatus.ActiveLocalizationsId.ToString());
        }

        // <summary>
        // API Endpoint to edit the properties of a Localization.
        // Usage:  POST /api/localization/{configId}
        // Test:   curl -X POST http://localhost:5550/api/localization/696EC7A86DAFE630-281F450A9D56CA1C
        // </summary>
        [HttpPost("localization/{localizationIdStr}")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiLocalizationEdit)]
        public async Task<IActionResult> UpdateLocalization(string localizationIdStr)
        {
            MetaGuid                                                      localizationId = ParseMetaGuidStr(localizationIdStr);
            GameDataControllerUtility.GameDataEditableProperties input    = await ParseBodyAsync<GameDataControllerUtility.GameDataEditableProperties>();

            MetaDatabase           db        = MetaDatabase.Get(QueryPriority.Normal);
            PersistedLocalizations persisted = await db.TryGetAsync<PersistedLocalizations>(localizationId.ToString());
            if (persisted == null)
                throw new MetaplayHttpException(404, "Localization not found.", $"Cannot find localization with id {localizationId}.");

            if (!string.IsNullOrEmpty(input.Name) || !string.IsNullOrEmpty(input.Description) || input.IsArchived.HasValue)
            {
                CreateOrUpdateGameDataResponse response = await AskEntityAsync<CreateOrUpdateGameDataResponse>(
                    GlobalStateManager.EntityId,
                    new CreateOrUpdateLocalizationsRequest() {
                        Id = localizationId,
                        Name = input.Name,
                        Description = input.Description,
                        IsArchived = input.IsArchived
                    });

                GameDataControllerUtility.GameDataEditableProperties oldValues = new GameDataControllerUtility.GameDataEditableProperties()
                {
                    Name = response.OldName,
                    Description = response.OldDescription,
                    IsArchived = response.OldIsArchived
                };
                GameDataControllerUtility.GameDataEditableProperties newValues = input.FillEmpty(oldValues);
                await WriteAuditLogEventAsync(new LocalizationEventBuilder(localizationId, new LocalizationEventLocalizationEdited(oldValues, newValues)));
                return Ok();
            }
            else
            {
                throw new MetaplayHttpException(400, "No valid input.", "Must pass an editable property in the body request.");
            }
        }

        /// <summary>
        /// API endpoint to update localizations
        /// Usage:  POST /api/localizations
        /// Test: curl -H "Content-Type:application/octet-stream" --data-binary {FILENAME} http://localhost:5550/api/localizations
        /// </summary>
        [HttpPost("localizations")]
        [Consumes("application/octet-stream")]
        [RequirePermission(MetaplayPermissions.ApiLocalizationEdit)]
        public async Task<IActionResult> UploadLocalizationsArchive([FromQuery] bool setAsActive = true)
        {
            // Publish the update via GlobalStateManager
            byte[] bytes = await ReadBodyBytesAsync();

            // Update via GlobalStateManager
            CreateOrUpdateGameDataResponse uploadResponse = await AskEntityAsync<CreateOrUpdateGameDataResponse>(
                GlobalStateManager.EntityId,
                new CreateOrUpdateLocalizationsRequest()
                {
                    Content = bytes,
                    Source  = GetUserId()
                });

            MetaGuid configId = uploadResponse.Id;

            List<EventBuilder> auditLogs = new List<EventBuilder>()
            {
                new LocalizationEventBuilder(configId, new LocalizationEventLocalizationUploaded())
            };

            if (setAsActive)
            {
                try
                {
                    List<EventBuilder> publishLogs = await PublishLocalizations(configId);
                    auditLogs.AddRange(publishLogs);
                }
                catch
                {
                    // if publish doesn't succeed, roll back
                    await AskEntityAsync<EntityAskOk>(GlobalStateManager.EntityId, new RemoveLocalizationsRequest() { Id = configId });
                    throw;
                }
            }

            await WriteRelatedAuditLogEventsAsync(auditLogs);

            return Ok(configId);
        }
    }
}
