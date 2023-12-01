// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [Table("Localizations")]
    [NonPartitioned]
    [LocalizationsEnabledCondition]
    public class PersistedLocalizations : PersistedGameData
    {
        //public byte[] MetaDataBytes { get; set; }
    }

    #region MetaMessages

    [MetaMessage(MessageCodesCore.CreateOrUpdateLocalizationsRequest, MessageDirection.ServerInternal)]
    [MetaImplicitMembersRange(1, 100)]
    [LocalizationsEnabledCondition]
    public class CreateOrUpdateLocalizationsRequest : LocalizationsDataManager.CreateOrUpdateRequest { }

    [MetaMessage(MessageCodesCore.RemoveLocalizationsResponse, MessageDirection.ServerInternal)]
    [MetaImplicitMembersRange(1, 100)]
    [LocalizationsEnabledCondition]
    public class RemoveLocalizationsRequest : LocalizationsDataManager.RemoveRequest { }

    /// <summary>
    /// Request to set a GameConfig active.
    /// </summary>
    [MetaMessage(MessageCodesCore.PublishLocalizationsRequest, MessageDirection.ServerInternal)]
    [LocalizationsEnabledCondition]
    public class PublishLocalizationRequest : MetaMessage
    {
        public MetaGuid Id                    { get; private set; }

        PublishLocalizationRequest() { }

        public PublishLocalizationRequest(MetaGuid id)
        {
            Id                    = id;
        }
    }

    [MetaMessage(MessageCodesCore.PublishLocalizationsResponse, MessageDirection.ServerInternal)]
    [LocalizationsEnabledCondition]
    public class PublishLocalizationResponse : MetaMessage
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

        PublishLocalizationResponse() { }

        PublishLocalizationResponse(StatusCode status, MetaGuid previousId, string errorMessage)
        {
            Status       = status;
            PreviousId   = previousId;
            ErrorMessage = errorMessage;
        }

        public static PublishLocalizationResponse Success(MetaGuid previousId) => new PublishLocalizationResponse(StatusCode.Success, previousId, errorMessage: null);
        public static PublishLocalizationResponse Refused(string errorMessage) => new PublishLocalizationResponse(StatusCode.Refused, MetaGuid.None, errorMessage: errorMessage);
    }

    #endregion

    public class LocalizationsDataManager : GameDataManager<PersistedLocalizations>
    {
        protected override string GameDataTypeDesc => "Localizations";

        protected override MetaGuid ActiveVersion
        {
            get => _state.ActiveLocalizationsId;
            set
            {
                _state.ActiveLocalizationsId  = value;
                _state.LatestLocalizationsUpdate = MetaTime.Now;
            }
        }

        protected override MetaGuid LatestAutoUpdateVersion
        {
            get => _state.LatestLocalizationsAutoUpdateId;
            set => _state.LatestLocalizationsAutoUpdateId = value;
        }

        protected override (string Path, bool IsFolder) BuiltinArchivePath
        {
            get
            {
                SystemOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
                return (opts.StaticLocalizationsPath, opts.StaticLocalizationsIsFolder);
            }
        }

        public LocalizationsDataManager(IGlobalStateManager owner) : base(owner) { }

        static async Task<ConfigArchive> GetLocalizationsFromDatabase(MetaGuid id)
        {
            PersistedLocalizations persisted = await MetaDatabaseBase.Get().TryGetAsync<PersistedLocalizations>(id.ToString());
            return persisted == null ? null : ConfigArchive.FromBytes(persisted.ArchiveBytes);
        }

        public async Task Initialize()
        {
            // If localizations not enabled there's nothing to do
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                return;

            // Get the current game config.
            ConfigArchive archive = await GetOrUpdateInitialDataAsync(GetLocalizationsFromDatabase);

            if (archive == null)
            {
                // Require some localization.
                if (RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>().MustHaveLocalizationToStart)
                {
                    if (BuiltinArchivePath.Path != null)
                        throw new InvalidOperationException($"A valid Localizations version is needed. There was no valid builtin localizations archive in {BuiltinArchivePath.Path}, and there is no valid localizations entry in the server state.");
                    throw new InvalidOperationException($"A valid Localizations version is needed. No builtin localizations archive path has been configured in System:StaticLocalizationsPath, and there is no valid localizations entry in the server state.");
                }
            }
            else
            {
                _state.LocalizationsDeliverables = await UploadPerLanguageLocalizationFilesToCdnAsync(archive);
            }
        }

        /// <summary>
        /// Tries to parse the archive as a localization archive. Throws on failure.
        /// </summary>
        static void TestLocalizationArchive(ConfigArchive archive)
        {
            // Parse the content to make sure it's valid
            foreach (ConfigArchiveEntry entry in archive.Entries)
            {
                LanguageId  languageId    = LanguageId.FromString(Path.GetFileNameWithoutExtension(entry.Name));
                byte[]      languageBytes = entry.Bytes.ToArray();
                _ = LocalizationLanguage.FromBytes(languageId, entry.Hash, languageBytes);
            }
        }

        [EntityAskHandler]
        public async Task<PublishLocalizationResponse> HandlePublishLocalizationRequest(PublishLocalizationRequest request)
        {
            _log.Info($"Publish localization request with id {request.Id} received");

            // Get StaticGameConfig from DB
            MetaDatabase           db           = MetaDatabase.Get(QueryPriority.Normal);
            PersistedLocalizations locs = await db.TryGetAsync<PersistedLocalizations>(request.Id.ToString());
            if (locs == null)
                throw new InvalidEntityAsk($"Localizations {request.Id} not found in database");
            if (locs.ArchiveBytes == null)
                throw new InvalidEntityAsk($"Localizations {request.Id} was not successfully built");

            ConfigArchive localizationArchive;
            try
            {
                localizationArchive = ConfigArchive.FromBytes(locs.ArchiveBytes);
            }
            catch (Exception e)
            {
                throw new InvalidEntityAsk($"Localization {request.Id} loading failed: {e}");
            }

            // TODO: Handle metadata?

            MetaGuid previousId = _state.ActiveLocalizationsId;
            try
            {
                await PublishLocalization(request.Id, localizationArchive);
            }
            catch (Exception e)
            {
                _log.Error("Failed to publish localization: {Error}", e);

                // \note: not really "InvalidEntityAsk" but good enough
                throw new InvalidEntityAsk($"Localization {request.Id} publish failed: {e}");
            }

            return PublishLocalizationResponse.Success(previousId);
        }

        /// <summary>
        /// Uploads the necessary CDN resources and makes the given Localization active. On failure, throws without making the game config active.
        /// </summary>
        async Task PublishLocalization(MetaGuid configId, ConfigArchive archive)
        {
            // Publish locs to CDN
            // \note: This may fail by throwing
            LocalizationsDeliverables deliverables = await UploadPerLanguageLocalizationFilesToCdnAsync(archive);

            // Store id & version and persist
            _state.ActiveLocalizationsId     = configId;
            _state.LatestLocalizationsUpdate = MetaTime.Now;
            _state.LocalizationsDeliverables = deliverables;

            await _owner.OnActiveLocalizationsChanged();
        }

        /// <summary>
        /// Throws on failure.
        /// </summary>
        static async Task<LocalizationsDeliverables> UploadPerLanguageLocalizationFilesToCdnAsync(ConfigArchive archive)
        {
            var results = await Task.WhenAll(archive.Entries.Select(async entry =>
            {
                // Parse the content to make sure it's valid
                LanguageId              languageId      = LanguageId.FromString(Path.GetFileNameWithoutExtension(entry.Name));
                byte[]                  languageBytes   = entry.Bytes.ToArray();
                LocalizationLanguage    language        = LocalizationLanguage.FromBytes(languageId, entry.Hash, languageBytes);

                await ServerConfigDataProvider.Instance.PublicBlobStorage.PutAsync($"Localizations/{entry.Name}/{entry.Hash}", language.ExportCompressedBinary());
                return (languageId, entry.Hash);
            }));
            return new LocalizationsDeliverables(results);
        }

        [EntityAskHandler]
        public Task<CreateOrUpdateGameDataResponse> HandleCreateOrUpdateLocalizationsRequest(CreateOrUpdateLocalizationsRequest request) => CreateOrUpdate(request);

        protected override void PrepareContentsForPersisting(PersistedLocalizations entry, byte[] contents)
        {
            // Parse archive to make sure it's valid
            ConfigArchive archive = ConfigArchive.FromBytes(contents);
            ConfigArchiveBuildUtility.TestArchiveVersion("Localizations", archive);
            TestLocalizationArchive(archive);

            // Re-encode and compress the config
            entry.VersionHash    = archive.Version.ToString();
            entry.ArchiveBuiltAt = archive.CreatedAt.ToDateTime();
            entry.ArchiveBytes   = CompressArchiveForPersisting(archive);
            // do we want MetaData?
            //entry.MetaDataBytes  = archive.ContainsEntryWithName("_metadata") ? archive.GetEntryByName("_metadata").Bytes.ToArray() : null;
        }
    }
}
