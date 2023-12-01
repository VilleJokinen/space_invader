// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [Table("StaticGameConfigs")]
    [NonPartitioned]
    public class PersistedStaticGameConfig : PersistedGameData
    {
        public byte[] MetaDataBytes { get; set; }
    }

    #region MetaMessages

    [MetaMessage(MessageCodesCore.CreateOrUpdateGameConfigRequest, MessageDirection.ServerInternal)]
    [MetaImplicitMembersRange(1, 100)]
    public class CreateOrUpdateGameConfigRequest : GameConfigManager.CreateOrUpdateRequest { }

    [MetaMessage(MessageCodesCore.RemoveStaticGameConfigRequest, MessageDirection.ServerInternal)]
    [MetaImplicitMembersRange(1, 100)]
    public class RemoveGameConfigRequest : GameConfigManager.RemoveRequest { }


    /// <summary>
    /// Request to set a GameConfig active.
    /// </summary>
    [MetaMessage(MessageCodesCore.PublishGameConfigRequest, MessageDirection.ServerInternal)]
    public class PublishGameConfigRequest : MetaMessage
    {
        public MetaGuid Id                    { get; private set; } // StaticFullGameConfig
        public bool     ParentMustMatchActive { get; private set; }
        // add dynamic version here

        PublishGameConfigRequest() { }

        public PublishGameConfigRequest(MetaGuid id, bool parentMustMatchActive)
        {
            Id                    = id;
            ParentMustMatchActive = parentMustMatchActive;
        }
    }

    [MetaMessage(MessageCodesCore.PublishGameConfigResponse, MessageDirection.ServerInternal)]
    public class PublishGameConfigResponse : MetaMessage
    {
        [MetaSerializable]
        public enum StatusCode
        {
            Success = 0,
            Refused = 1,
        }

        public StatusCode Status       { get; private set; }
        public string     ErrorMessage { get; private set; }
        public MetaGuid   PreviousId   { get; private set; }

        PublishGameConfigResponse() { }

        PublishGameConfigResponse(StatusCode status, MetaGuid previousId, string errorMessage)
        {
            Status       = status;
            PreviousId   = previousId;
            ErrorMessage = errorMessage;
        }

        public static PublishGameConfigResponse Success(MetaGuid previousId) => new PublishGameConfigResponse(StatusCode.Success, previousId, errorMessage: null);
        public static PublishGameConfigResponse Refused(string errorMessage) => new PublishGameConfigResponse(StatusCode.Refused, MetaGuid.None, errorMessage: errorMessage);
    }

    #endregion

    public class GameConfigManager : GameDataManager<PersistedStaticGameConfig>
    {
        protected override string GameDataTypeDesc => "GameConfig";
        protected override (string, bool) BuiltinArchivePath => (RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>().StaticGameConfigPath, false);

        protected override MetaGuid ActiveVersion
        {
            get => _state.StaticGameConfigId;
            set
            {
                _state.StaticGameConfigId     = value;
                _state.LatestGameConfigUpdate = MetaTime.Now;
            }
        }

        protected override MetaGuid LatestAutoUpdateVersion
        {
            get => _state.LatestGameConfigAutoUpdateId;
            set => _state.LatestGameConfigAutoUpdateId = value;
        }

        public GameConfigManager(IGlobalStateManager owner) : base(owner) { }

        /// <summary>
        /// Tries to parse the archive as a static game config archive. Throws on failure.
        /// </summary>
        static void TestStaticGameConfigArchive(ConfigArchive archive)
        {
            // Parse the content to make sure it's valid
            _ = FullGameConfig.CreateSoloUnpatched(archive);
        }

        protected override void PrepareContentsForPersisting(PersistedStaticGameConfig entry, byte[] contents)
        {
            ConfigArchive archive = ConfigArchive.FromBytes(contents);

            // Sanity check archive
            ConfigArchiveBuildUtility.TestArchiveVersion("StaticGameConfig", archive);
            TestStaticGameConfigArchive(archive);

            // Re-encode and compress the config
            entry.VersionHash    = archive.Version.ToString();
            entry.ArchiveBuiltAt = archive.CreatedAt.ToDateTime();
            entry.ArchiveBytes   = CompressArchiveForPersisting(archive);

            // Remove build report from the MetaData which is stored in the MetaDataBytes column to ensure *near* constant size (build params is user defined thus can vary), this is important for performance in the dashboard list page.
            byte[]             metadataBytes      = null;
            GameConfigMetaData gameConfigMetaData = GameConfigMetaData.FromArchive(archive);
            if (gameConfigMetaData != null)
                metadataBytes = gameConfigMetaData.StripBuildReportAndCloneForPersisting().ToBytes();

            entry.MetaDataBytes = metadataBytes;
        }

        [EntityAskHandler]
        public Task<CreateOrUpdateGameDataResponse> HandleCreateOrUpdateGameConfigRequest(CreateOrUpdateGameConfigRequest request) => CreateOrUpdate(request);


        [EntityAskHandler]
        public async Task<EntityAskOk> HandleRemoveGameConfigRequest(RemoveGameConfigRequest request)
        {
            if (_state.StaticGameConfigId == request.Id)
                throw new InvalidEntityAsk($"Can't remove current active game config {request.Id}");

            return await Remove(request);
        }

        static async Task<(ConfigArchive, FullGameConfig)> GetGameConfigFromDatabaseAsync(MetaGuid id)
        {
            FullGameConfigImportResources resources = await ServerGameConfigProvider.Instance.GetImportResourcesAsync(id, MetaGuid.None);
            FullGameConfig                config    = ServerGameConfigProvider.Instance.GetBaselineGameConfig(id, MetaGuid.None, resources);
            return (resources.FullArchive, config);
        }

        public async Task Initialize()
        {
            SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();

            // Get the current game config.
            (ConfigArchive archive, FullGameConfig config) = await GetOrUpdateInitialDataAsync(GetGameConfigFromDatabaseAsync);

            if (config == null)
            {
                // Require some game config.
                if (systemOpts.MustHaveGameConfigToStart)
                    throw new InvalidOperationException("A valid StaticGameConfig is needed. There was no valid game config in System:StaticGameConfigPath, nor is there a valid StaticGameConfig in the server state.");
            }
            else
            {
                ConfigArchiveDeliverables deliverables = await UploadSharedGameConfigToCdnAsync(archive);
                _state.SharedGameConfigDeliverables = deliverables;
                await _owner.OnActiveGameConfigChanged(config, isInitial: true);
            }
        }

        [EntityAskHandler]
        public async Task<PublishGameConfigResponse> HandlePublishGameConfigRequest(PublishGameConfigRequest request)
        {
            _log.Info($"Publish game config request with StaticGameConfig id {request.Id} received, ParentMustMatchActive {request.ParentMustMatchActive}");

            // Get StaticGameConfig from DB
            MetaDatabase              db           = MetaDatabase.Get(QueryPriority.Normal);
            PersistedStaticGameConfig staticConfig = await db.TryGetAsync<PersistedStaticGameConfig>(request.Id.ToString());
            if (staticConfig == null)
                throw new InvalidEntityAsk($"Static game config {request.Id} not found in database");
            if (staticConfig.ArchiveBytes == null)
                throw new InvalidEntityAsk($"Static game config {request.Id} was not successfully built");

            ConfigArchive staticConfigArchive;
            try
            {
                staticConfigArchive = ConfigArchive.FromBytes(staticConfig.ArchiveBytes);
            }
            catch (Exception e)
            {
                throw new InvalidEntityAsk($"Static game config {request.Id} loading failed: {e}");
            }

            GameConfigMetaData metaData = GameConfigMetaData.FromArchive(staticConfigArchive);
            if (metaData != null)
            {
                if (request.ParentMustMatchActive)
                {
                    // When requested, and the gameconfig being published is a partial build, only publish
                    // if the currently active gameconfig is the parent
                    if (metaData.ParentConfigId.IsValid)
                    {
                        if (metaData.ParentConfigId != _state.StaticGameConfigId)
                            throw new InvalidEntityAsk($"Active gameconfig {_state.StaticGameConfigId} changed from parent config {metaData.ParentConfigId}, publish not allowed");
                    }
                }

                if (ShouldBlockPublishDueToBuildLog(metaData))
                    return PublishGameConfigResponse.Refused("GameConfigs with warnings are not allowed to be published in this environment.");
            }

            MetaGuid previousId = _state.StaticGameConfigId;
            try
            {
                await PublishGameConfig(request.Id, staticConfigArchive);
            }
            catch (Exception e)
            {
                _log.Error("Failed to publish game config: {Error}", e);

                // \note: not really "InvalidEntityAsk" but good enough
                throw new InvalidEntityAsk($"Static game config {request.Id} publish failed: {e}");
            }

            return PublishGameConfigResponse.Success(previousId);
        }

        /// <summary>
        /// Upload the specified SharedGameConfig into CDN under GameConfig/SharedGameConfig/. Throws on failure.
        /// </summary>
        async Task<ConfigArchiveDeliverables> UploadSharedGameConfigToCdnAsync(ConfigArchive staticGameConfigArchive)
        {
            ContentDeliveryOptions deliveryOptions = RuntimeOptionsRegistry.Instance.GetCurrent<ContentDeliveryOptions>();

            // Extract shared archive for client
            (ContentHash version, byte[] compressedArchive) = GameConfigUtil.GetSharedArchiveFromFullArchiveForClient(staticGameConfigArchive,
                deliveryOptions.ArchiveCompressionAlgorithm, deliveryOptions.ArchiveMinimumSizeBeforeCompression);

            // Upload to CDN
            await ServerConfigDataProvider.Instance.PublicBlobStorage.PutAsync($"SharedGameConfig/{version}", compressedArchive);

            return new ConfigArchiveDeliverables(version);
        }

        /// <summary>
        /// Uploads the necessary CDN resources and makes the given GameConfig active. On failure, throws without making the game config active.
        /// </summary>
        async Task PublishGameConfig(MetaGuid staticConfigId, ConfigArchive staticConfig)
        {
            // Publish SharedGameConfig to CDN
            // \note: This may fail by throwing
            // \todo [petri] combine with DynamicGameConfig first
            ConfigArchiveDeliverables deliverables = await UploadSharedGameConfigToCdnAsync(staticConfig);

            // Store id & version and persist
            // \note: patch versions are automatically set in MakeSharedGameConfigPatchesActiveAsync
            _state.StaticGameConfigId           = staticConfigId;
            _state.LatestGameConfigUpdate       = MetaTime.Now;
            _state.SharedGameConfigDeliverables = deliverables;

            await _owner.OnActiveGameConfigChanged(FullGameConfig.CreateSoloUnpatched(staticConfig), isInitial: false);
        }

        static bool ShouldBlockPublishDueToBuildLog(GameConfigMetaData metaData)
        {
            if (metaData?.BuildReport == null)
                return false;

            GameConfigBuildOptions configBuildOptions = RuntimeOptionsRegistry.Instance.GetCurrent<GameConfigBuildOptions>();
            if (configBuildOptions.TreatWarningsAsErrors)
                return metaData.BuildSummary.HighestMessageLevel >= GameConfigLogLevel.Warning;

            return metaData.BuildSummary.HighestMessageLevel >= GameConfigLogLevel.Error;
        }
    }
}
