// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Server.Database;
using System;
using System.Threading.Tasks;

namespace Metaplay.Server.EntityArchive
{
    /// <summary>
    /// Custom export handler for Player entities
    /// Returns the player's binary blob 'Payload' from their entry in the Players database
    /// </summary>
    public class DefaultExportPlayerHandler : SimpleExportEntityHandler
    {
        public DefaultExportPlayerHandler() : base(EntityKindCore.Player)
        {
        }
    }


    /// <summary>
    /// Default import handler for Player entities.
    /// Input data is the player's binary blob 'Payload' data.
    /// </summary>
    public class DefaultImportPlayerHandler : SimpleImportEntityHandler
    {
        public DefaultImportPlayerHandler() : base(EntityKindCore.Player)
        {
        }

        protected override async Task DoRemapModel(IModel model, EntityImportEntityIdRemapper remapper)
        {
            IPlayerModelBase playerModel = (IPlayerModelBase)model;
            await playerModel.RemapEntityIdsAsync(remapper);
        }

        protected override async Task DoWrite(EntityId entityId, bool createFirst, IEntityAsker asker)
        {
            if (createFirst)
            {
                // Create an empty PersistedPlayer state in the database to allocate the EntityId.
                // This could throw theoretically in a race condition when a genuine new player gets the
                // same id, but it's extremely unlikely.
                await DatabaseEntityUtil.PersistEmptyPlayerAsync(entityId);
            }

            PlayerImportModelDataRequest request = new PlayerImportModelDataRequest(Payload, SchemaVersion, isNewEntity: createFirst);
            PlayerImportModelDataResponse response = await asker.EntityAskAsync<PlayerImportModelDataResponse>(entityId, request);
            if (!response.Success)
            {
                throw new ImportException($"Overwrite of player {entityId} failed", response.FailureInfo);
            }
        }

        protected override void AssignBasicRuntimePropertiesToModel(IModel model)
        {
            IPlayerModelBase playerModel = (IPlayerModelBase)model;
            playerModel.GameConfig = ActiveGameConfig.BaselineGameConfig.SharedConfig;
            playerModel.LogicVersion = LogicVersion;
        }
    }
}
