// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Json;
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
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK routes to work with game configs.
    /// </summary>
    public class SystemGameConfigController : GameAdminApiController
    {
        public class LibraryCountGameConfig
        {
            public IReadOnlyDictionary<string, int> SharedLibraries { get; init; }
            public IReadOnlyDictionary<string, int> ServerLibraries { get; init; }
            public GameConfigMetaData               MetaData        { get; init; }

            LibraryCountGameConfig(IReadOnlyDictionary<string, int> sharedLibraries, IReadOnlyDictionary<string, int> serverLibraries, GameConfigMetaData metaData)
            {
                SharedLibraries = sharedLibraries;
                ServerLibraries = serverLibraries;
                MetaData        = metaData;
            }

            public static LibraryCountGameConfig FromPartial(PartialGameConfig gameConfig)
            {
                return new LibraryCountGameConfig(
                    gameConfig.SharedLibraries.ToDictionary(x => x.Key, x => x.Value is IGameConfigLibraryEntry lib ? lib.Count : MetaSerializerTypeRegistry.GetTypeSpec(x.Value.GetType()).Members.Count),
                    gameConfig.ServerLibraries.ToDictionary(x => x.Key, x => x.Value is IGameConfigLibraryEntry lib ? lib.Count : MetaSerializerTypeRegistry.GetTypeSpec(x.Value.GetType()).Members.Count),
                    gameConfig.MetaData);
            }
        }

        public class GameConfigInfoBase
        {
            public GameDataControllerUtility.GameDataBuildStatus Status         { get; protected init; }
            public MetaTime                                      PersistedAt    { get; protected init; }
            public MetaTime                                      LastModifiedAt { get; protected init; }
            public MetaTime                                      ArchiveBuiltAt { get; protected init; }
            public string                                        Source         { get; protected init; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string FailureInfo { get;   protected set; }
            public bool     IsActive    { get; protected init; }
            public bool     IsArchived  { get; protected init; }
            public MetaGuid Id          { get; protected init; }
            public string   Name        { get; protected init; }
            public string   Description { get; protected init; }
            /// <summary>
            /// The version string for the full (server+shared) config archive.
            /// This is not the same hash as seen by the client; see <see cref="CdnVersion"/> for that instead.
            /// </summary>
            public string   FullConfigVersion { get; protected init; }
            /// <summary>
            /// The version string for the CDN deliverable part, and to identify gameconfigs on client.
            /// </summary>
            public string   CdnVersion  { get; protected init; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string ContentsParseError { get;          protected set; }

            public int BlockingGameConfigMessageCount { get; protected set; }

            protected static int CalculateBlockingGameConfigMessages(GameConfigMetaData metaData)
            {
                GameConfigBuildOptions configBuildOptions = RuntimeOptionsRegistry.Instance.GetCurrent<GameConfigBuildOptions>();
                int                    blockingMessages   = 0;
                blockingMessages += metaData.BuildSummary.BuildMessagesCount[GameConfigLogLevel.Error];
                blockingMessages += metaData.BuildSummary.ValidationMessagesCount[GameConfigLogLevel.Error];

                if (configBuildOptions.TreatWarningsAsErrors)
                {
                    blockingMessages += metaData.BuildSummary.BuildMessagesCount[GameConfigLogLevel.Warning];
                    blockingMessages += metaData.BuildSummary.ValidationMessagesCount[GameConfigLogLevel.Warning];
                }

                return blockingMessages;
            }
        }

        public class ExperimentData
        {
            public string       DisplayName      { get; private init; }
            public string       Id               { get; private init; }
            public List<string> PatchedLibraries { get; private init; }
            public List<string> Variants         { get; private init; }

            public static ExperimentData Create(PlayerExperimentInfo experimentInfo)
            {
                List<string> patchedLibraries = new List<string>();

                foreach ((ExperimentVariantId _, PlayerExperimentInfo.Variant value) in experimentInfo.Variants)
                {
                    if(value.ConfigPatch?.ServerConfigPatch != null)
                        foreach ((string libraryKey, GameConfigEntryPatch _) in value.ConfigPatch.ServerConfigPatch.EnumerateEntryPatches())
                            if(!patchedLibraries.Contains(libraryKey))
                                patchedLibraries.Add(libraryKey);

                    if(value.ConfigPatch?.SharedConfigPatch != null)
                        foreach ((string libraryKey, GameConfigEntryPatch _) in value.ConfigPatch.SharedConfigPatch.EnumerateEntryPatches())
                            if(!patchedLibraries.Contains(libraryKey))
                                patchedLibraries.Add(libraryKey);
                }

                return new ExperimentData()
                {
                    Id               = experimentInfo.ExperimentId.ToString(),
                    DisplayName      = experimentInfo.DisplayName,
                    PatchedLibraries = patchedLibraries,
                    Variants         = experimentInfo.Variants.Select(x => x.Key.Value).ToList()
                };
            }
        }

        public class GameConfigEntryImportError
        {
            public Type   ExceptionType { get; init; }
            public string Message       { get; init; }
            public string FullException { get; init; }

            public GameConfigEntryImportError(Exception ex)
            {
                ExceptionType = ex.GetType();
                Message       = ex.Message;
                FullException = ex.ToString();
            }
        }

        public class LibraryCountGameConfigInfo : GameConfigInfoBase
        {
            public LibraryCountGameConfig Contents              { get; private init; }
            public List<ExperimentData>   Experiments           { get; private init; }
            public string                 ConfigValidationError { get; private init; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, GameConfigEntryImportError> LibraryParsingErrors { get; private init; }

            public bool   IsPublishBlockedByErrors { get; private init; }
            public string PublishBlockingError     { get; private init; }

            public DashboardBuildReportSummary BuildReportSummary { get; private init; }


            public static LibraryCountGameConfigInfo FromPartialGameConfig(
                PersistedStaticGameConfig persisted,
                MetaGuid activeId,
                (GameDataControllerUtility.GameDataBuildStatus status, string error) statusAndError,
                string parseError,
                PartialGameConfig partialGameConfigOrNull,
                string configValidationError,
                string cdnVersion)
            {
                MetaGuid                id              = MetaGuid.Parse(persisted.Id);
                LibraryCountGameConfig  libraryCounts   = null;
                List<ExperimentData>    experiments     = null;

                DashboardBuildReportSummary buildReportSummary = null;

                if (partialGameConfigOrNull != null)
                {
                    libraryCounts = LibraryCountGameConfig.FromPartial(partialGameConfigOrNull);
                    experiments = partialGameConfigOrNull.ServerLibraries
                        .Where(x => x.Value is GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo>)
                        .Select(x => x.Value)
                        .OfType<GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo>>()
                        .SelectMany(x => x.Values)
                        .Select(ExperimentData.Create)
                        .ToList();
                    if (libraryCounts.MetaData?.BuildSummary != null)
                        buildReportSummary = DashboardBuildReportSummary.CreateFromBuildSummary(libraryCounts.MetaData.BuildSummary);
                }

                int blockingGameConfigMessageCount = libraryCounts?.MetaData?.BuildSummary == null ? 0 : CalculateBlockingGameConfigMessages(libraryCounts.MetaData);

                (bool isBlocked, string error) = GetIsPublishBlockedStatus(
                    parseError,
                    persisted.FailureInfo,
                    configValidationError,
                    statusAndError.status,
                    statusAndError.error,
                    blockingGameConfigMessageCount);
                return new LibraryCountGameConfigInfo()
                {
                    Id                             = id,
                    Name                           = persisted.Name,
                    Description                    = persisted.Description,
                    FullConfigVersion              = persisted.VersionHash,
                    CdnVersion                     = cdnVersion,
                    PersistedAt                    = MetaTime.FromDateTime(id.GetDateTime()),
                    ArchiveBuiltAt                 = MetaTime.FromDateTime(persisted.ArchiveBuiltAt),
                    LastModifiedAt                 = MetaTime.FromDateTime(persisted.LastModifiedAt),
                    Source                         = persisted.Source,
                    ContentsParseError             = parseError,
                    IsActive                       = id == activeId,
                    IsArchived                     = persisted.IsArchived,
                    FailureInfo                    = persisted.FailureInfo,
                    Status                         = statusAndError.status,
                    Contents                       = libraryCounts,
                    Experiments                    = experiments,
                    ConfigValidationError          = configValidationError,
                    BlockingGameConfigMessageCount = blockingGameConfigMessageCount,
                    LibraryParsingErrors           = partialGameConfigOrNull?.Exceptions.ToDictionary(x => x.Key, x => new GameConfigEntryImportError(x.Value)),
                    IsPublishBlockedByErrors       = isBlocked,
                    PublishBlockingError           = error,
                    BuildReportSummary             = buildReportSummary,
                };
            }
        }

        public class StaticGameConfigInfo : GameConfigInfoBase
        {
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore), JsonExcludeReadOnlyProperties]
            public FullGameConfig Contents { get; private init; }

            public static StaticGameConfigInfo FromPersisted(PersistedStaticGameConfig persisted, MetaGuid activeId, (GameDataControllerUtility.GameDataBuildStatus status, string error) statusAndError)
            {
                FullGameConfig contents   = null;
                string         parseError = null;
                try
                {
                    if (persisted.ArchiveBytes != null)
                        contents = FullGameConfig.CreateSoloUnpatched(ConfigArchive.FromBytes(persisted.ArchiveBytes));
                    else if (persisted.MetaDataBytes != null)
                        contents = FullGameConfig.MetaDataOnly(GameConfigMetaData.FromBytes(persisted.MetaDataBytes));
                }
                catch (Exception e)
                {
                    parseError = e.ToString();
                }

                MetaGuid id         = MetaGuid.Parse(persisted.Id);
                bool     isActive   = id == activeId;
                string   cdnVersion = "";
                if (isActive)
                {
                    ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
                    cdnVersion = activeGameConfig.ClientSharedGameConfigContentHash.ToString();
                }

                return new StaticGameConfigInfo()
                {
                    Id                 = id,
                    Name               = persisted.Name,
                    Description        = persisted.Description,
                    FullConfigVersion  = persisted.VersionHash,
                    CdnVersion         = cdnVersion,
                    PersistedAt        = MetaTime.FromDateTime(id.GetDateTime()),
                    ArchiveBuiltAt     = MetaTime.FromDateTime(persisted.ArchiveBuiltAt),
                    LastModifiedAt     = MetaTime.FromDateTime(persisted.LastModifiedAt),
                    Source             = persisted.Source,
                    Contents           = contents,
                    ContentsParseError = parseError,
                    IsActive           = isActive,
                    IsArchived         = persisted.IsArchived,
                    FailureInfo        = statusAndError.error,
                    Status             = statusAndError.status,
                    BlockingGameConfigMessageCount = contents?.MetaData?.BuildSummary == null ? 0 : CalculateBlockingGameConfigMessages(contents.MetaData),
                };
            }
        }

        [MetaSerializable]
        [MetaBlockedMembers(1)]
        public struct StaticGameConfigBuildInput
        {
            [MetaMember(2)] public bool                                                 SetAsActive    { get; private set; }
            [MetaMember(3)] public GameConfigBuildParameters                            BuildParams    { get; private set; }
            [MetaMember(4)] public MetaGuid                                             ParentConfigId { get; private set; }
            [MetaMember(5)] public GameDataControllerUtility.GameDataEditableProperties Properties     { get; private set; }
        }

        public SystemGameConfigController(ILogger<SystemGameConfigController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameConfigPublished)]
        public class GameConfigEventGameConfigPublished : GameConfigEventPayloadBase
        {
            public GameConfigEventGameConfigPublished() { }
            override public string EventTitle => "Published";
            override public string EventDescription => $"Game config published.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameConfigUnpublished)]
        public class GameConfigEventGameConfigUnpublished : GameConfigEventPayloadBase
        {
            public GameConfigEventGameConfigUnpublished() { }
            override public string EventTitle => "Unpublished";
            override public string EventDescription => $"Game config unpublished.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameConfigStaticGameConfigUploaded)]
        public class GameConfigEventStaticGameConfigUploaded : GameConfigEventPayloadBase
        {
            public GameConfigEventStaticGameConfigUploaded() { }
            override public string EventTitle => "Uploaded";
            override public string EventDescription => $"Static game config uploaded.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameConfigStaticGameConfigEdited)]
        public class GameConfigEventStaticGameConfigEdited : GameConfigEventPayloadBase
        {
            [MetaMember(2)] public GameDataControllerUtility.GameDataEditableProperties OldValues { get; private set; }
            [MetaMember(3)] public GameDataControllerUtility.GameDataEditableProperties NewValues { get; private set; }

            public GameConfigEventStaticGameConfigEdited() { }
            public GameConfigEventStaticGameConfigEdited(GameDataControllerUtility.GameDataEditableProperties oldValues, GameDataControllerUtility.GameDataEditableProperties newValues)
            {
                OldValues = oldValues;
                NewValues = newValues;
            }
            public override string EventTitle       => "Edited";
            public override string EventDescription => NewValues.GetSummary("Game Config", OldValues);
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameConfigStaticGameConfigBuildStarted)]
        public class GameConfigEventStaticGameBuildStarted : GameConfigEventPayloadBase
        {
            [MetaMember(1)] public StaticGameConfigBuildInput Input { get; private set; }

            public GameConfigEventStaticGameBuildStarted() { }
            public GameConfigEventStaticGameBuildStarted(StaticGameConfigBuildInput input)
            {
                Input = input;
            }
            override public string EventTitle => "Build started";
            override public string EventDescription => $"Static game config build from source '{Input.BuildParams.DefaultSource?.DisplayName ?? "unknown"}' started, setAsActive: '{Input.SetAsActive}'.";
        }

        async Task<IEnumerable<StaticGameConfigInfo>> QueryStaticGameConfigMetaData(bool showArchived)
        {
            GlobalStatusResponse                   status  = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            MetaDatabase                           db      = MetaDatabase.Get(QueryPriority.Normal);
            IEnumerable<PersistedStaticGameConfig> configs = await db.QueryAllStaticGameConfigs(showArchived);

            // Retrieve build task statuses if needed
            Dictionary<MetaGuid, BackgroundTaskStatus> taskStatuses = null;
            if (configs.Where(config => config.TaskId != null).Any())
            {
                BackgroundTaskStatusResponse taskStatusResponse = await AskEntityAsync<BackgroundTaskStatusResponse>(BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(BuildStaticGameConfigTask)));
                taskStatuses = taskStatusResponse.Tasks.ToDictionary(t => t.Id, t => t);
            }

            IEnumerable<StaticGameConfigInfo> persisted = configs.Select(x => StaticGameConfigInfo.FromPersisted(x, status.ActiveStaticGameConfigId, GameDataControllerUtility.GetConfigStatus(x, taskStatuses)));
            return persisted;
        }

        // <summary>
        // Get list of StaticGameConfig entries in the database, without contents and stripped of all unnecessary data.
        // Usage:  GET /api/gameConfig
        // Test:   curl http://localhost:5550/api/gameConfig
        // </summary>
        [HttpGet("gameConfig")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigView)]
        public async Task<ActionResult<IEnumerable<StaticGameConfigInfo>>> GetMinimalStaticGameConfigList([FromQuery] bool showArchived = false)
        {
            IEnumerable<StaticGameConfigInfo> persisted = await QueryStaticGameConfigMetaData(showArchived);
            return Ok(persisted.Select(x => new MinimalGameConfigInfo(x)));
        }

        public class DashboardBuildReportSummary
        {
            DashboardBuildReportSummary(
                IReadOnlyDictionary<GameConfigLogLevel, int> buildLogLogLevelCounts,
                IReadOnlyDictionary<GameConfigLogLevel, int> validationResultsLogLevelCounts,
                IReadOnlyDictionary<GameConfigLogLevel, int> totalLogLevelCounts,
                bool isBuildMessagesTrimmed,
                bool isValidationMessagesTrimmed)
            {
                BuildLogLogLevelCounts          = buildLogLogLevelCounts;
                ValidationResultsLogLevelCounts = validationResultsLogLevelCounts;
                TotalLogLevelCounts             = totalLogLevelCounts;
                IsValidationMessagesTrimmed     = isValidationMessagesTrimmed;
                IsBuildMessagesTrimmed          = isBuildMessagesTrimmed;
            }

            public IReadOnlyDictionary<GameConfigLogLevel, int> BuildLogLogLevelCounts          { get; private init; }
            public IReadOnlyDictionary<GameConfigLogLevel, int> ValidationResultsLogLevelCounts { get; private init; }
            public IReadOnlyDictionary<GameConfigLogLevel, int> TotalLogLevelCounts             { get; private init; }
            public bool                                         IsValidationMessagesTrimmed             { get; private set; }
            public bool                                         IsBuildMessagesTrimmed             { get; private set; }

            public static DashboardBuildReportSummary CreateFromBuildSummary(GameConfigBuildSummary summary)
            {
                Dictionary<GameConfigLogLevel, int> totalLogLevelToCountMapping = summary.BuildMessagesCount.ToDictionary(x => x.Key, x => summary.ValidationMessagesCount[x.Key] + x.Value);

                return new DashboardBuildReportSummary(
                    summary.BuildMessagesCount,
                    summary.ValidationMessagesCount,
                    totalLogLevelToCountMapping,
                    summary.IsBuildMessagesTrimmed,
                    summary.IsValidationMessagesTrimmed);
            }
        }

        public class MinimalGameConfigInfo
        {
            public MinimalGameConfigInfo(StaticGameConfigInfo info)
            {
                Status                         = info.Status;
                Name                           = info.Name;
                IsArchived                     = info.IsArchived;
                IsActive                       = info.IsActive;
                Id                             = info.Id;
                FailureInfo                    = info.FailureInfo;
                Description                    = info.Description;
                FullConfigVersion              = info.FullConfigVersion;
                PersistedAt                    = info.PersistedAt;
                BlockingGameConfigMessageCount = info.BlockingGameConfigMessageCount;
                ArchiveBuiltAt                 = info.ArchiveBuiltAt;
                Source                         = info.Source;
                if (info.Contents?.MetaData?.BuildSummary != null)
                    BuildReportSummary = DashboardBuildReportSummary.CreateFromBuildSummary(info.Contents.MetaData.BuildSummary);

                (bool isBlocked, string error) = GetIsPublishBlockedStatus(
                    null,
                    FailureInfo,
                    null,
                    Status,
                    FailureInfo,
                    BlockingGameConfigMessageCount);
                BestEffortIsPublishBlockedByErrors = isBlocked;
                PublishBlockingError               = error;
            }

            public string Source { get; init; }

            public MetaTime ArchiveBuiltAt { get; init; }

            public GameDataControllerUtility.GameDataBuildStatus Status { get; init; }

            public string Name { get; init; }

            public bool IsActive { get; init; }

            public bool IsArchived { get; init; }

            // TODO: this is currently a best effort guess as we can't load all gameconfigs to see if they still parse, we should keep a cache somewhere if it is still parseable to get correct information
            public bool BestEffortIsPublishBlockedByErrors { get; init; }

            public string PublishBlockingError { get; init; }

            public MetaGuid Id { get; init; }

            public string Description { get; init; }

            public string FullConfigVersion { get; init; }

            public string FailureInfo { get; init; }

            public MetaTime PersistedAt { get; init; }

            public int BlockingGameConfigMessageCount { get; init; }

            public DashboardBuildReportSummary BuildReportSummary { get; init; }
        }

        /// <summary>
        /// Utility to figure out whether this config can be published and a relevant error message, all parameters are optional but at least one is required.
        /// </summary>
        /// TODO: Eventually this should replace most of the error states that are communicated to the dashboard (except partially loaded libraries), ensuring that this is enough to replace the existing error handling is out of scope currently.
        static (bool isBlocked, string error) GetIsPublishBlockedStatus(
            string parseError = null,
            string failureInfo = null,
            string configValidationError = null,
            GameDataControllerUtility.GameDataBuildStatus status = GameDataControllerUtility.GameDataBuildStatus.Succeeded,
            string statusError = null,
            int blockingMessageCount = 0)
        {
            if (!string.IsNullOrWhiteSpace(parseError))
                return (isBlocked: true, error: parseError);
            else if (!string.IsNullOrWhiteSpace(failureInfo))
                return (isBlocked: true, error: failureInfo);
            else if (!string.IsNullOrWhiteSpace(configValidationError))
                return (isBlocked: true, error: configValidationError);
            else if (status != GameDataControllerUtility.GameDataBuildStatus.Succeeded)
                return (isBlocked: true, error: statusError);
            else if (blockingMessageCount > 0)
                return (isBlocked: true, error: Invariant($"Config build ran into {blockingMessageCount} blocking messages."));

            return (isBlocked: false, error: string.Empty);
        }

        // <summary>
        // API Endpoint to get a specific version of StaticGameConfig, including contents.
        // Usage:  GET /api/gameConfig/staticGameConfig/{configVersion}
        // Test:   curl http://localhost:5550/api/gameConfig/696EC7A86DAFE630-281F450A9D56CA1C
        // </summary>
        [HttpGet("gameConfig/{configIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigView)]
        public async Task<IActionResult> GetStaticGameConfig(string configIdStr, [FromQuery] bool binary = false)
        {
            GlobalStatusResponse status = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            PersistedStaticGameConfig persisted = await GameDataControllerUtility.GetPersistedGameDataByIdStringOr404Async<PersistedStaticGameConfig>(configIdStr, status.ActiveStaticGameConfigId);
            if (binary)
            {
                HttpContext.Response.Headers.Add("GameConfig-Id", persisted.Id);
                MemoryStream stream = new MemoryStream(persisted.ArchiveBytes);
                return new FileStreamResult(stream, "application/octet-stream");
            }
            else
            {
                Dictionary<MetaGuid, BackgroundTaskStatus> taskStatuses = null;
                if (persisted.TaskId != null)
                {
                    BackgroundTaskStatusResponse taskStatusResponse = await AskEntityAsync<BackgroundTaskStatusResponse>(
                        BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(BuildStaticGameConfigTask)));
                    taskStatuses = taskStatusResponse.Tasks.ToDictionary(t => t.Id, t => t);
                }
                return Ok(StaticGameConfigInfo.FromPersisted(persisted, status.ActiveStaticGameConfigId, GameDataControllerUtility.GetConfigStatus(persisted, taskStatuses)));
            }
        }

        // <summary>
        // API Endpoint to get library and experiment information of a config,
        // Usage:  GET /api/gameConfig/staticGameConfig/{configVersion}/count
        // Test:   curl http://localhost:5550/api/gameConfig/696EC7A86DAFE630-281F450A9D56CA1C
        // </summary>
        [HttpGet("gameConfig/{configIdStr}/count")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigView)]
        public async Task<IActionResult> GetGameConfigLibraryCounts(string configIdStr)
        {
            GlobalStatusResponse      status                  = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            PersistedStaticGameConfig persisted               = await GameDataControllerUtility.GetPersistedGameDataByIdStringOr404Async<PersistedStaticGameConfig>(configIdStr, status.ActiveStaticGameConfigId);
            PartialGameConfig         partialGameConfigOrNull = null;
            string                    parseError              = null;
            try
            {
                partialGameConfigOrNull = LoadPartialGameConfig(persisted, librariesToInclude: null, includeMetadata: true);
            }
            catch (Exception ex)
            {
                parseError = ex.ToString();
            }

            Dictionary<MetaGuid, BackgroundTaskStatus> taskStatuses = null;
            if (persisted.TaskId != null)
            {
                BackgroundTaskStatusResponse taskStatusResponse = await AskEntityAsync<BackgroundTaskStatusResponse>(BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(BuildStaticGameConfigTask)));
                taskStatuses = taskStatusResponse.Tasks.ToDictionary(t => t.Id, t => t);
            }

            (GameDataControllerUtility.GameDataBuildStatus, string) configBuildStatus    = GameDataControllerUtility.GetConfigStatus(persisted, taskStatuses);
            StaticGameConfigInfo          staticGameConfigInfo = StaticGameConfigInfo.FromPersisted(persisted, status.ActiveStaticGameConfigId, configBuildStatus);

            return Ok(LibraryCountGameConfigInfo.FromPartialGameConfig(persisted, status.ActiveStaticGameConfigId, configBuildStatus, parseError, partialGameConfigOrNull, staticGameConfigInfo.ContentsParseError, staticGameConfigInfo.CdnVersion));
        }

        // <summary>
        // API Endpoint to get a content of a set of libraries, including variant patches
        // Usage:  POST /api/gameConfig/staticGameConfig/{configVersion}/details
        // Body:   {"Libraries": ["HappyHours"], "Experiments": ["EarlyGameFunnel"]}
        // Test:   curl --request POST --url http: //localhost:5551/api/gameConfig/03bf8a7a2dd587c-0-10385eeff6b71ee0/details --header 'Accept-Encoding: gzip' --header 'Content-Type: application/json' --data '{"Libraries": ["HappyHours"], "Experiments": ["EarlyGameFunnel"]}'
        // </summary>
        [HttpPost("gameConfig/{configIdStr}/details")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigView)]
        public async Task<IActionResult> PostStaticGameConfigDetails(string configIdStr, [FromBody] ConfigRequestPostArgs requestArgs)
        {
            bool addedExperimentsLibrary = false;
            if (requestArgs.Experiments?.Count > 0 && (!requestArgs.Libraries?.Any(x => string.Equals(x, ServerGameConfigBase.PlayerExperimentsEntryName, StringComparison.InvariantCultureIgnoreCase)) ?? false))
            {
                requestArgs.Libraries.Add(ServerGameConfigBase.PlayerExperimentsEntryName);
                addedExperimentsLibrary = true;
            }
            PartialGameConfig partialGameConfig = await LoadPartialGameConfig(configIdStr, requestArgs.Libraries);

            // Error checking
            if (!CheckLibraryAndExperimentExists(requestArgs, partialGameConfig, out IActionResult actionResult))
                return actionResult;

            Dictionary<string, GameConfigLibraryJsonConversionUtility.ConfigKey> config = GameConfigLibraryJsonConversionUtility.ConvertPartialGameConfigToConfigKeys(partialGameConfig, requestArgs.Experiments, _logger);

            if (addedExperimentsLibrary)
                config.Remove(ServerGameConfigBase.PlayerExperimentsEntryName);

            return Ok(
                new
                {
                    config               = config,
                    libraryParsingErrors = partialGameConfig.Exceptions.ToDictionary(x => x.Key, x => new GameConfigEntryImportError(x.Value))
                });
        }

        bool CheckLibraryAndExperimentExists(ConfigRequestPostArgs requestArgs, PartialGameConfig partialGameConfig, out IActionResult actionResult)
        {
            if (requestArgs.Libraries != null)
            {
                foreach (string lib in requestArgs.Libraries)
                {
                    if (!partialGameConfig.ServerLibraries.ContainsKey(lib) && !partialGameConfig.SharedLibraries.ContainsKey(lib))
                    {
                        actionResult = BadRequest($"Library {lib} not found.");
                        return false;
                    }
                }
            }

            if (requestArgs.Experiments?.Count > 0 &&
                partialGameConfig.ServerLibraries.ContainsKey(ServerGameConfigBase.PlayerExperimentsEntryName) &&
                partialGameConfig.ServerLibraries[ServerGameConfigBase.PlayerExperimentsEntryName] is GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo> experiments)
            {
                foreach (string experiment in requestArgs.Experiments)
                {
                    if (!experiments.ContainsKey(PlayerExperimentId.FromString(experiment)))
                    {
                        actionResult = BadRequest($"Player experiment {experiment} not found.");
                        return false;
                    }
                }
            }

            actionResult = Ok();
            return true;
        }


        public class ConfigRequestPostArgs
        {
            public List<string> Libraries   { get; set; }
            public List<string> Experiments { get; set; }
        }

        // <summary>
        // API Endpoint to get a diff between 2 configs
        // Usage:  POST /api/gameConfig/diff/{baselineConfigId}/{newConfigId}
        // Body:   {"Libraries": [ "Producers" ]}
        // Test:   curl --request POST --url http://localhost:5551/api/gameConfig/diff/03bfbc45bb8c57d-0-8cbdb5ae78e38f29/03bfc6b9d7d929c-0-74f7f2a4ee00083e --header 'Accept: application/json' --header 'Content-Type: application/json' --data '{"Libraries": [ "Producers" ]}'
        // </summary>
        [HttpPost("gameConfig/diff/{baselineConfigId}/{newConfigId}")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigView)]
        public async Task<IActionResult> PostStaticGameConfigDiff(string baselineConfigId, string newConfigId, [FromBody] ConfigRequestPostArgs args)
        {
            PartialGameConfig baseline  = await LoadPartialGameConfig(baselineConfigId, args.Libraries);
            PartialGameConfig newConfig = await LoadPartialGameConfig(newConfigId, args.Libraries);

            // Error checking
            if (!CheckLibraryAndExperimentExists(args, baseline, out IActionResult actionResult))
                return actionResult;
            if (!CheckLibraryAndExperimentExists(args, newConfig, out actionResult))
                return actionResult;

            Dictionary<string, GameConfigLibraryJsonConversionUtility.ConfigKey> config = GameConfigLibraryJsonConversionUtility.DiffPartialGameConfig(baselineConfigId, newConfigId, baseline, newConfig, _logger);

            return Ok(new {
                config                        = config,
                baselineLibraryParsingErrors  = baseline.Exceptions.ToDictionary(x => x.Key, x => new GameConfigEntryImportError(x.Value)),
                newConfigLibraryParsingErrors = newConfig.Exceptions.ToDictionary(x => x.Key, x => new GameConfigEntryImportError(x.Value))
            });
        }

        PartialGameConfig LoadPartialGameConfig(
            PersistedStaticGameConfig persisted,
            List<string> librariesToInclude = null,
            bool includeMetadata = false,
            bool omitPatchesInServerConfigExperiments = false)
        {
            PartialGameConfig libraries;

            if (persisted.ArchiveBytes != null)
            {
                ConfigArchive configArchive = ConfigArchive.FromBytes(persisted.ArchiveBytes);

                libraries = PartialGameConfig.PartialFromArchive(
                    configArchive,
                    omitPatchesInServerConfigExperiments: omitPatchesInServerConfigExperiments,
                    filters: librariesToInclude,
                    includeMetadata: includeMetadata);
            }
            else
                throw new InvalidOperationException("Unable to parse libraries from PersistedStaticGameConfig, PersistedStaticGameConfig does not contain archive bytes.");

            return libraries;
        }

        async Task<PartialGameConfig> LoadPartialGameConfig(string configIdStr, List<string> libraries, bool includeMetadata = false)
        {
            GlobalStatusResponse      status    = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            PersistedStaticGameConfig persisted = await GameDataControllerUtility.GetPersistedGameDataByIdStringOr404Async<PersistedStaticGameConfig>(configIdStr, status.ActiveStaticGameConfigId);
            return LoadPartialGameConfig(persisted, libraries, includeMetadata);
        }

        /// <summary>
        /// API Endpoint to get active game config id
        /// Usage:  GET /api/activeGameConfigId
        /// Test:   curl http://localhost:5550/api/activeGameConfigId
        /// </summary>
        [HttpGet("activeGameConfigId")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigView)]
        public async Task<IActionResult> GetActiveGameConfigId()
        {
            // Fetch active config version from global state
            // \note: Integration tests depend on 404 for no-active-gameconfig.
            GlobalStatusResponse globalStatus = await AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
            if (!globalStatus.ActiveStaticGameConfigId.IsValid)
                return NotFound();

            return Ok(globalStatus.ActiveStaticGameConfigId.ToString());
        }

        // <summary>
        // API Endpoint to edit the properties of a StaticGameConfig.
        // Usage:  POST /api/gameConfig/{configId}
        // Test:   curl -X POST http://localhost:5550/api/gameConfig/696EC7A86DAFE630-281F450A9D56CA1C
        // </summary>
        [HttpPost("gameConfig/{configIdStr}")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigEdit)]
        public async Task<IActionResult> UpdateStaticGameConfig(string configIdStr)
        {
            MetaGuid                                             configId = ParseMetaGuidStr(configIdStr);
            GameDataControllerUtility.GameDataEditableProperties input    = await ParseBodyAsync<GameDataControllerUtility.GameDataEditableProperties>();

            MetaDatabase db = MetaDatabase.Get(QueryPriority.Normal);
            PersistedStaticGameConfig persisted = await db.TryGetAsync<PersistedStaticGameConfig>(configId.ToString());
            if (persisted == null)
                throw new MetaplayHttpException(404, "Static Game Config not found.", $"Cannot find static game config with id {configId}.");

            if (!string.IsNullOrEmpty(input.Name) || !string.IsNullOrEmpty(input.Description) || input.IsArchived.HasValue)
            {
                CreateOrUpdateGameDataResponse response = await AskEntityAsync<CreateOrUpdateGameDataResponse>(
                    GlobalStateManager.EntityId,
                    new CreateOrUpdateGameConfigRequest() {
                        Id = configId,
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
                await WriteAuditLogEventAsync(new GameConfigEventBuilder(configId, new GameConfigEventStaticGameConfigEdited(oldValues, newValues)));
                return Ok();
            }
            else
            {
                throw new MetaplayHttpException(400, "No valid input.", "Must pass an editable property in the body request.");
            }
        }

        [HttpPost("gameConfig")]
        [Consumes("application/octet-stream")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigEdit)]
        public async Task<IActionResult> UploadStaticGameConfig([FromQuery] bool setAsActive = true, [FromQuery] bool parentMustMatchActive = true)
        {
            // Parse ConfigArchive
            byte[] bytes = await ReadBodyBytesAsync();

            // Update via GlobalStateManager
            CreateOrUpdateGameDataResponse uploadResponse = await AskEntityAsync<CreateOrUpdateGameDataResponse>(
                GlobalStateManager.EntityId,
                new CreateOrUpdateGameConfigRequest()
                {
                    Content = bytes,
                    Source = GetUserId()
                });

            MetaGuid configId = uploadResponse.Id;

            List<EventBuilder> auditLogEvents = new List<EventBuilder>()
            {
                new GameConfigEventBuilder(configId, new GameConfigEventStaticGameConfigUploaded())
            };

            if (setAsActive)
            {
                try
                {
                    PublishGameConfigResponse response = await AskEntityAsync<PublishGameConfigResponse>(GlobalStateManager.EntityId, new PublishGameConfigRequest(configId, parentMustMatchActive));

                    if (response.Status == PublishGameConfigResponse.StatusCode.Refused)
                        throw new MetaplayHttpException(400, "Cannot publish config", response.ErrorMessage);

                    MetaGuid oldActiveGameConfig = response.PreviousId;
                    if (oldActiveGameConfig.IsValid)
                        auditLogEvents.Add(new GameConfigEventBuilder(oldActiveGameConfig, new GameConfigEventGameConfigUnpublished()));
                    auditLogEvents.Add(new GameConfigEventBuilder(configId, new GameConfigEventGameConfigPublished()));
                }
                catch (Exception)
                {
                    // if publish doesn't succeed, roll back
                    await AskEntityAsync<EntityAskOk>(GlobalStateManager.EntityId, new RemoveGameConfigRequest() { Id = configId });
                    throw;
                }
            }

            await WriteRelatedAuditLogEventsAsync(auditLogEvents);

            return Ok(new { uploadResponse.Id });
        }

        // <summary>
        // API Endpoint to publish a game config build
        // Usage:  POST /api/gameConfig/publish
        // Test:   curl -X POST http://localhost:5550/api/gameConfig/publish
        // </summary>
        [HttpPost("gameConfig/publish")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigEdit)]
        public async Task<IActionResult> PublishGameConfig([FromQuery] bool parentMustMatchActive = true)
        {
            GameDataControllerUtility.GameDataIdInput input    = await ParseBodyAsync<GameDataControllerUtility.GameDataIdInput>();
            PublishGameConfigResponse                 response = await AskEntityAsync<PublishGameConfigResponse>(GlobalStateManager.EntityId, new PublishGameConfigRequest(input.Id, parentMustMatchActive));

            if (response.Status == PublishGameConfigResponse.StatusCode.Refused)
                throw new MetaplayHttpException(400, "Cannot publish config", response.ErrorMessage);

            MetaGuid oldActiveGameConfig = response.PreviousId;

            await WriteRelatedAuditLogEventsAsync(new List<EventBuilder>
            {
                new GameConfigEventBuilder(oldActiveGameConfig, new GameConfigEventGameConfigUnpublished()),
                new GameConfigEventBuilder(input.Id, new GameConfigEventGameConfigPublished())
            });

            return Ok();
        }

        // <summary>
        // API Endpoint to start a new game config build
        // Usage:  POST /api/gameConfig/build
        // Test:   curl -X POST http://localhost:5550/api/gameConfig/build
        // </summary>
        [HttpPost("gameConfig/build")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGameConfigEdit)]
        public async Task<IActionResult> StartStaticGameConfigBuild()
        {
            StaticGameConfigBuildInput input = await ParseBodyAsync<StaticGameConfigBuildInput>();

            // Generate an id for build task
            MetaGuid taskId = MetaGuid.New();

            // Create an empty StaticGameConfig entry for this build
            MetaGuid configId = (await AskEntityAsync<CreateOrUpdateGameDataResponse>(
                GlobalStateManager.EntityId,
                new CreateOrUpdateGameConfigRequest() {
                    Source = GetUserId(),
                    Name = input.Properties.Name,
                    Description = input.Properties.Description,
                    IsArchived = input.Properties.IsArchived,
                    TaskId = taskId
                })).Id;

            // Start the build task
            BuildStaticGameConfigTask buildTask = new BuildStaticGameConfigTask(configId, input.ParentConfigId, input.BuildParams);
            _ = await AskEntityAsync<StartBackgroundTaskResponse>(BackgroundTaskActor.EntityId, new StartBackgroundTaskRequest(taskId, buildTask));

            await WriteAuditLogEventAsync(new GameConfigEventBuilder(configId, new GameConfigEventStaticGameBuildStarted(input)));

            return Ok(new { Id = configId });
        }
    }
}
