// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.Model;
using Metaplay.Server.Database;
using Metaplay.Server.Guild;
using Metaplay.Server.Guild.InternalMessages;
using System;
using System.Threading.Tasks;

namespace Metaplay.Server.EntityArchive
{
    /// <summary>
    /// Custom export handler for Guild entities.
    /// </summary>
    public class DefaultExportGuildHandler : SimpleExportEntityHandler
    {
        public DefaultExportGuildHandler() : base(EntityKindCore.Guild)
        {
        }
    }

    /// <summary>
    /// Custom import handler for Guild entities.
    /// </summary>
    public class DefaultImportGuildHandler : SimpleImportEntityHandler
    {
        public DefaultImportGuildHandler() : base(EntityKindCore.Guild)
        {
        }

        protected override async Task DoRemapModel(IModel model, EntityImportEntityIdRemapper remapper)
        {
            IGuildModelBase guildModel = (IGuildModelBase)model;
            await guildModel.RemapEntityIdsAsync(remapper);
        }

        protected override async Task DoWrite(EntityId entityId, bool createFirst, IEntityAsker asker)
        {
            if (createFirst)
            {
                // Create an empty PersistedGuild state in the database to allocate the EntityId.
                // This could throw theoretically in a race condition when a genuine new guild gets the
                // same id, but it's extremely unlikely.
                await DatabaseEntityUtil.PersistEmptyGuildAsync(entityId);
            }

            InternalGuildImportModelDataRequest request = new InternalGuildImportModelDataRequest(Payload, SchemaVersion);
            InternalGuildImportModelDataResponse response = await asker.EntityAskAsync<InternalGuildImportModelDataResponse>(entityId, request);
            if (!response.Success)
            {
                throw new ImportException($"Overwrite of guild {entityId} failed", response.FailureInfo);
            }
        }

        protected override void AssignBasicRuntimePropertiesToModel(IModel model)
        {
            IGuildModelBase guildModel = (IGuildModelBase)model;
            guildModel.GameConfig = ActiveGameConfig.BaselineGameConfig.SharedConfig;
            guildModel.LogicVersion = LogicVersion;
        }
    }
}

#endif
