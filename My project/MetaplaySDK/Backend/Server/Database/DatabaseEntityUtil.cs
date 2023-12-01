// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Server.Authentication;
#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Server.Guild;
#endif
using System;
using System.Threading.Tasks;

namespace Metaplay.Server.Database
{
    public class DatabaseEntityUtil
    {
        #region Player

        public static Task PersistEmptyPlayerAsync(EntityId entityId, bool replace = false)
        {
            // Persist an empty PersistedPlayerBase in the database
            // \note We create the PersistedPlayer with the default constructor, which doesn't allow it to do any of its own initialization
            PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKindCore.Player);
            PersistedPlayerBase persistedPlayer = (PersistedPlayerBase)Activator.CreateInstance(entityConfig.PersistedType);
            persistedPlayer.EntityId         = entityId.ToString();
            persistedPlayer.PersistedAt      = DateTime.UtcNow;
            persistedPlayer.Payload          = null;
            persistedPlayer.SchemaVersion    = 0;
            persistedPlayer.IsFinal          = true;
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Normal);
            return replace ? db.UpdateAsync(persistedPlayer) : db.InsertAsync(persistedPlayer);
        }

        public static async Task<EntityId> CreateNewPlayerAsync(IMetaLogger log)
        {
            return await TaskUtil.RetryUntilSuccessAsync(maxNumRetries: 5, async () =>
            {
                // Randomize id for the player (such that it doesn't overlap with botIds)
                EntityId playerId = GetRandomEntityId(kind: EntityKindCore.Player, useReservedBotIdRange: false);
                log.Verbose("Trying to register new account {PlayerId}", playerId);

                // Insert an empty PersistedPlayer for the player being registered. If the given player already exists, this will fail.
                try
                {
                    await PersistEmptyPlayerAsync(playerId).ConfigureAwait(false);

                    return playerId; // on successful write, unique playerId is found & allocated
                }
                catch (Exception ex)
                {
                    // On failure, print
                    log.Warning("Failed to register account, {PlayerId} already exists: {Exception}", playerId, ex);

                    // and keep trying. RetryUntilSuccessAsync handles. Up to a point.
                    throw;
                }
            });
        }

        #endregion // Player

#if !METAPLAY_DISABLE_GUILDS
        #region Guild

        public static Task PersistEmptyGuildAsync(EntityId entityId)
        {
            // Persist an empty PersistedGuildBase in the database
            // \note We create the PersistedGuild with the default constructor, which doesn't allow it to do any of its own initialization
            PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKindCore.Guild);
            PersistedGuildBase persistedGuild = (PersistedGuildBase)Activator.CreateInstance(entityConfig.PersistedType);
            persistedGuild.EntityId         = entityId.ToString();
            persistedGuild.PersistedAt      = DateTime.UtcNow;
            persistedGuild.Payload          = null;
            persistedGuild.SchemaVersion    = 0;
            persistedGuild.IsFinal          = true;
            return MetaDatabase.Get(QueryPriority.Normal).InsertAsync(persistedGuild);
        }

        public static async Task<EntityId> CreateNewGuildAsync(IMetaLogger log)
        {
            return await TaskUtil.RetryUntilSuccessAsync(maxNumRetries: 5, async () =>
            {
                // Randomize id for the enetity (such that it doesn't overlap with botIds)
                EntityId entityId = GetRandomEntityId(kind: EntityKindCore.Guild, useReservedBotIdRange: false);
                log.Verbose("Trying to register new guild {EntityId}", entityId);

                // Insert an empty TPersisted for the guild being registered. If the given guild already exists, this will fail.
                try
                {
                    await PersistEmptyGuildAsync(entityId).ConfigureAwait(false);

                    return entityId; // on successful write, unique guildId is found & allocated
                }
                catch (Exception ex)
                {
                    // On failure, print
                    log.Warning("Failed to register guild, {GuildId} already exists: {Exception}", entityId, ex);

                    // and keep trying. RetryUntilSuccessAsync handles. Up to a point.
                    throw;
                }
            });
        }

        #endregion // Guild
#endif // !METAPLAY_DISABLE_GUILDS

        // Generic

        static EntityId GetRandomEntityId(EntityKind kind, bool useReservedBotIdRange)
        {
            // Keep randomizing until we find something suitable
            ulong numReservedIds;
            if (kind == EntityKindCore.Player)
                numReservedIds = Authenticator.NumReservedBotIds;
            else
                numReservedIds = 1UL << 32; // reserve the same amount as player does for bot testing (by default)

            while (true)
            {
                EntityId entityId = EntityId.CreateRandom(kind);
                bool isOnReservedRange = entityId.Value < numReservedIds;
                if (isOnReservedRange == useReservedBotIdRange)
                    return entityId;
            }
        }

        /// <summary>
        /// Creates a new entity and returns the EntityId. This method inserts the intitial persisted state into the database.
        /// </summary>
        /// <typeparam name="TPersisted">The concrete type of the persisted data.</typeparam>
        /// <param name="kind">The EntityKind of the entity.</param>
        /// <param name="useReservedBotIdRange">If true, the generated ID will be on the reserved low range (starting with 000..). Useful for creating visually distinct IDs for debugging.</param>
        /// <param name="defaultValues">The default values for Persisted.</param>
        public static async Task<EntityId> CreateNewEntityAsync<TPersisted>(EntityKind kind, bool useReservedBotIdRange = false, TPersisted defaultValues = null)
            where TPersisted : class, IPersistedEntity, new()
        {
            for (int retryNum = 0; retryNum < 5; ++retryNum)
            {
                // Randomize id for the entity (such that it doesn't overlap with botIds)
                EntityId entityId = GetRandomEntityId(kind, useReservedBotIdRange);
                if (await CreateNewEntityAsync<TPersisted>(entityId, defaultValues))
                    return entityId;
            }

            throw new Exception($"CreateNewEntity<{typeof(TPersisted).ToGenericTypeString()}> failed. Could not generate unused id.");
        }

        /// <summary>
        /// Creates a new entity with a predetermined EntityId. This method inserts the intitial persisted state into the database.
        /// If the Entity already exists in the database, this method returns false.
        /// </summary>
        /// <typeparam name="TPersisted">The concrete type of the persisted data.</typeparam>
        /// <param name="entityId">The EntityId of the entity.</param>
        /// <param name="defaultValues">The default values for Persisted.</param>
        public static async Task<bool> CreateNewEntityAsync<TPersisted>(EntityId entityId, TPersisted defaultValues = null)
            where TPersisted : class, IPersistedEntity, new()
        {
            TPersisted persisted;
            if (defaultValues != null)
                persisted = defaultValues;
            else
            {
                persisted               = new TPersisted();
                persisted.Payload       = null;
                persisted.SchemaVersion = 0;
                persisted.IsFinal       = true;
            }

            persisted.EntityId    = entityId.ToString();
            persisted.PersistedAt = DateTime.UtcNow;

            return await MetaDatabase.Get().InsertOrIgnoreAsync<TPersisted>(persisted);
        }
    }
}
