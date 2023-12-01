// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    public abstract class PersistedGameData : IPersistedItem
    {
        [Column(TypeName = "varchar(64)"), Key, Required, MaxLength(64)]
        public string Id { get; set; }

        [Column(TypeName = "varchar(256)"), Required, MaxLength(256)]
        public string Name { get; set; }

        [Column(TypeName = "varchar(512)"), MaxLength(512)]
        public string Description { get; set; }

        [Column(TypeName = "varchar(64)"), MaxLength(64)]
        public string VersionHash { get; set; }

        [Column(TypeName = "DateTime"), Required]
        public DateTime LastModifiedAt { get; set; }

        [Column(TypeName = "varchar(128)"), Required]
        public string Source { get; set; }

        [Column(TypeName = "DateTime")]
        public DateTime ArchiveBuiltAt { get; set; }

        [Column(TypeName = "tinyint"), Required]
        public bool IsArchived { get; set; } = false;

        [Column(TypeName = "varchar(64)"), MaxLength(64)]
        public string TaskId { get; set; }

        public string FailureInfo { get; set; }

        public byte[] ArchiveBytes { get; set; }
    }

    [MetaMessage(MessageCodesCore.CreateOrUpdateGameDataResponse, MessageDirection.ServerInternal)]
    public class CreateOrUpdateGameDataResponse : MetaMessage
    {
        public MetaGuid Id             { get; private set; }
        public string   OldName        { get; private set; }
        public string   OldDescription { get; private set; }
        public bool     OldIsArchived  { get; private set; }

        private CreateOrUpdateGameDataResponse() { }

        public CreateOrUpdateGameDataResponse(MetaGuid id, string name, string description, bool isArchived)
        {
            Id             = id;
            OldName        = name;
            OldDescription = description;
            OldIsArchived  = isArchived;
        }
    }

    public abstract class GameDataManager<TDataType> : EntityComponent where TDataType : PersistedGameData, new()
    {
        protected readonly IGlobalStateManager _owner;
        protected override EntityActor         OwnerActor => _owner.Actor;
        protected          IMetaLogger         _log       => _owner.Logger;
        protected          GlobalState         _state     => _owner.State;

        protected abstract string GameDataTypeDesc { get; }

        #region Request message types

        // Operations on game data are typically done via EntityAsk requests. While many of the operations are
        // generic enough to not necessarily require a concrete type specific implementation, the mapping to
        // EntityAsk receiver is currently done by message type only so each concrete GameData subclass must
        // re-declare all of the associated (and supported) request message types.

        [MetaImplicitMembersRange(100, 200)]
        public abstract class CreateOrUpdateRequest : MetaMessage
        {
            public MetaGuid Id          { get; set; }
            public string   Name        { get; set; }
            public string   Description { get; set; }
            public byte[]   Content     { get; set; }
            public string   Source      { get; set; }
            public bool?    IsArchived  { get; set; }
            public MetaGuid TaskId      { get; set; }
            public string   FailureInfo { get; set; }
        }

        [MetaImplicitMembersRange(100, 200)]
        public abstract class RemoveRequest : MetaMessage
        {
            public MetaGuid Id { get; set; }
        }

        #endregion

        protected GameDataManager(IGlobalStateManager owner)
        {
            _owner = owner;
        }

        protected virtual TDataType CreatePersisted()
        {
            return new TDataType();
        }

        protected abstract MetaGuid ActiveVersion { get; set; }
        protected abstract MetaGuid LatestAutoUpdateVersion { get; set; }

        protected abstract void PrepareContentsForPersisting(TDataType entry, byte[] contents);

        protected abstract (string Path, bool IsFolder) BuiltinArchivePath { get; }

        protected async Task<TData> GetOrUpdateInitialDataAsync<TData>(Func<MetaGuid, Task<TData>> databaseFetchFunc)
        {
            // Get the database ID for the built-in game data version. If it didn't exist in the DB already then persist it.
            (MetaGuid builtinId, bool builtinIsNew) = await FindOrStoreBuiltinGameDataAsync();

            // Auto-update to built-in version if it's new or if the archive has been previously stored but a previous
            // auto-update attempt has not been persisted.
            if (builtinId != MetaGuid.None && (builtinIsNew || builtinId.GetDateTime() > LatestAutoUpdateVersion.GetDateTime()))
            {
                _log.Info("Updating {GameDataTypeDesc} to version {NewStaticVersion} from built-in storage", GameDataTypeDesc, builtinId);

                ActiveVersion           = builtinId;
                LatestAutoUpdateVersion = builtinId;

                // Note: at this point we rely on the builtin data loading properly as it has been previously
                // validated. Exceptions from here are intentionally uncaught to cause the actor to crash.
                return await databaseFetchFunc(builtinId);
            }

            // No forced auto-update, try to get the current version from DB.
            if (ActiveVersion.IsValid)
            {
                try
                {
                    return await databaseFetchFunc(ActiveVersion);
                }
                catch (Exception ex)
                {
                    _log.Error("Active {GameDataTypeDesc} id {ActiveVersion} not found or can not be parsed. Deactivating this version. Error {cause}", GameDataTypeDesc, ActiveVersion, ex);
                    ActiveVersion = MetaGuid.None;
                }
            }

            // Current config from DB failed to load, revert to builtin config if available.
            if (builtinId != MetaGuid.None)
            {
                try
                {
                    TData builtinData = await databaseFetchFunc(builtinId);
                    ActiveVersion = builtinId;
                    _log.Warning("Reverting {GameDataTypeDesc} to built-in version {BuiltInConfigId}", GameDataTypeDesc, builtinId);
                    return builtinData;
                }
                catch (Exception ex)
                {
                    _log.Error("Builtin {GameDataTypeDesc} id {BuiltinId} can not be parsed, not able to revert to it. Error {cause}", GameDataTypeDesc, builtinId, ex);
                }
            }

            return default;
        }

        protected async Task<CreateOrUpdateGameDataResponse> CreateOrUpdate(CreateOrUpdateRequest request)
        {
            MetaDatabase db = MetaDatabase.Get();
            TDataType    persisted;
            MetaGuid     configId = request.Id;

            // Get existing persisted data from DB or create a new instance
            if (configId.IsValid)
            {
                _log.Info($"Update {GameDataTypeDesc} request with id {configId} received");
                persisted = await db.TryGetAsync<TDataType>(configId.ToString());
                if (persisted == null)
                    throw new InvalidEntityAsk($"Existing {GameDataTypeDesc} {configId} not found in database");

                persisted.LastModifiedAt = DateTime.UtcNow;
            }
            else
            {
                configId = MetaGuid.New();
                _log.Info($"Create {GameDataTypeDesc} request received, new id ${configId}");
                persisted                = CreatePersisted();
                persisted.Id             = configId.ToString();
                persisted.LastModifiedAt = configId.GetDateTime();
            }

            // Capture old values for response
            CreateOrUpdateGameDataResponse response = new CreateOrUpdateGameDataResponse(configId, persisted.Name, persisted.Description, persisted.IsArchived);

            // Update values from request
            if (request.Content != null)
            {
                if (persisted.Source == null && request.Source == null)
                    throw new InvalidEntityAsk($"persisted source information missing");
                if (persisted.ArchiveBytes != null)
                    throw new InvalidEntityAsk($"Updating {GameDataTypeDesc} contents not allowed ({persisted.Id})");

                try
                {
                    PrepareContentsForPersisting(persisted, request.Content);
                }
                catch (Exception e)
                {
                    throw new InvalidEntityAsk($"Malformed {GameDataTypeDesc}: {e}");
                }
            }
            else if (request.FailureInfo != null)
            {
                persisted.FailureInfo = request.FailureInfo;
            }

            if (persisted.ArchiveBytes != null || persisted.FailureInfo != null)
                persisted.TaskId = MetaGuid.None.ToString();
            else if (request.TaskId.IsValid)
                persisted.TaskId = request.TaskId.ToString();

            if (request.Source != null)
                persisted.Source = request.Source;

            if (request.Name != null)
                persisted.Name = request.Name;
            else if (persisted.Name == null)
                persisted.Name = "";

            if (request.Description != null)
                persisted.Description = request.Description;

            if (request.IsArchived.HasValue)
                persisted.IsArchived = request.IsArchived.Value;

            await db.InsertOrUpdateAsync(persisted);

            return response;
        }

        public async Task<EntityAskOk> Remove(RemoveRequest request)
        {
            MetaDatabase db = MetaDatabase.Get();
            await db.RemoveAsync<TDataType>(request.Id.ToString());
            return EntityAskOk.Instance;
        }

        protected static byte[] CompressArchiveForPersisting(ConfigArchive archive) => ConfigArchiveBuildUtility.ToBytes(archive);

        async Task<MetaGuid> PersistGameDataFromDiskAsync(byte[] contents)
        {
            // Populate new persisted entry
            MetaGuid  id    = MetaGuid.New();
            TDataType entry = CreatePersisted();

            entry.Id             = id.ToString();
            entry.Source         = "disk";
            entry.LastModifiedAt = id.GetDateTime();
            entry.Name           = $"{GameDataTypeDesc} from disk";

            // Add contents (specific to data type)
            PrepareContentsForPersisting(entry, contents);

            // Persist into database
            await MetaDatabase.Get().InsertAsync(entry);

            return id;
        }

        async Task<(MetaGuid id, bool wasStored)> FindOrStoreBuiltinGameDataAsync()
        {
            (string archivePath, bool isFolder) = BuiltinArchivePath;

            if (string.IsNullOrEmpty(archivePath))
                return (MetaGuid.None, false);

            byte[]      builtinArchiveBytes;
            ContentHash builtinArchiveVersion;
            try
            {
                if (!isFolder)
                {
                    // \todo: could optimize here to read only header bytes!
                    builtinArchiveBytes = await FileUtil.ReadAllBytesAsync(archivePath);
                    using (IOReader reader = new IOReader(builtinArchiveBytes))
                    {
                        (int, ContentHash version, MetaTime, int) header = ConfigArchiveBuildUtility.ReadArchiveHeader(reader);
                        if (!header.version.IsValid)
                            throw new InvalidOperationException($"Built-in {GameDataTypeDesc} doesn't contain valid content hash");

                        builtinArchiveVersion = header.version;
                    }
                }
                else
                {
                    ConfigArchive archive = await ConfigArchiveBuildUtility.FolderEncoding.FromDirectoryAsync(archivePath);
                    // \note: Don't compress since that might change the hash, i.e. the version.
                    builtinArchiveBytes = ConfigArchiveBuildUtility.ToBytes(archive, compression: CompressionAlgorithm.None, minimumSizeForCompression: 0);
                    builtinArchiveVersion = archive.Version;
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to load {GameDataTypeDesc} from built-in storage: {{Error}}", ex);
                return (MetaGuid.None, false);
            }

            // Find the database ID for the builtin archive.
            MetaGuid gameConfigId = await MetaDatabase.Get().SearchGameDataByVersionHashAndSource<TDataType>(builtinArchiveVersion.ToString(), "disk");

            if (gameConfigId != MetaGuid.None)
                return (gameConfigId, false);

            // If it wasn't found, store it now. New archives are more carefully validated before inserting.
            gameConfigId = await PersistGameDataFromDiskAsync(builtinArchiveBytes);
            return (gameConfigId, true);
        }
    }
}
