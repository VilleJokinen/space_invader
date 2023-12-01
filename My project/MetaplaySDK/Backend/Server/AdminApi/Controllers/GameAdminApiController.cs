// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// AdminApi controller that supports game-related features (eg, players, action execution, etc.).
    /// </summary>
    public abstract class GameAdminApiController : MetaplayAdminApiController
    {
        protected GameAdminApiController(ILogger logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Utility function to parse and validate a player Id from a string
        /// Throws MetaplayHttpException on error
        /// NB: This function does not guarantee that the player exists, only that the id
        /// look like a valid one. Use ParsePlayerIdStrAndCheckForExistenceAsync instead
        /// if you intend to, eg, send messages to the player
        /// </summary>
        /// <param name="playerIdStr">String representation of the player's Id</param>
        /// <returns>EntityId of the player</returns>
        protected EntityId ParsePlayerIdStr(string playerIdStr) => ParseEntityIdStr(playerIdStr, EntityKindCore.Player);

        /// <summary>
        /// Utility function to parse, validate and check for the existence of a player based
        /// on a string of their player Id
        /// Throws MetaplayHttpException on error
        /// </summary>
        /// <param name="playerIdStr">String representation of the player's Id</param>
        /// <returns>EntityId of the player</returns>
        protected async Task<EntityId> ParsePlayerIdStrAndCheckForExistenceAsync(string playerIdStr)
        {
            EntityId playerId = ParsePlayerIdStr(playerIdStr);
            await GetPersistedEntityAsync(playerId);
            return playerId;
        }

        protected readonly struct PlayerDetails
        {
            public readonly EntityId PlayerId;
            public readonly IPlayerModelBase Model;
            public readonly PersistedPlayerBase Persisted;
            public readonly IGameConfigDataResolver GameConfigResolver;

            public int LogicVersion => Model.LogicVersion;

            public PlayerDetails(EntityId playerId, IPlayerModelBase model, PersistedPlayerBase persisted, IGameConfigDataResolver gameConfigResolver)
            {
                PlayerId = playerId;
                Model = model;
                Persisted = persisted;
                GameConfigResolver = gameConfigResolver;
            }
        }

        /// <summary>
        /// Utility function to fetch and deserialize a player based on their Id.
        /// If the entity does not exist, throws 404.
        /// </summary>
        /// <param name="playerIdStr">String representation of the player's Id</param>
        /// <returns></returns>
        protected async Task<PlayerDetails> GetPlayerDetailsAsync(string playerIdStr)
        {
            EntityId playerId = ParsePlayerIdStr(playerIdStr);
            PersistedPlayerBase player = (PersistedPlayerBase)await GetPersistedEntityAsync(playerId);
            InternalEntityStateResponse response = await AskEntityAsync<InternalEntityStateResponse>(playerId, InternalEntityStateRequest.Instance);
            FullGameConfig specializedConfig = await ServerGameConfigProvider.Instance.GetSpecializedGameConfigAsync(response.StaticGameConfigId, response.DynamicGameConfigId, response.SpecializationKey);

            try
            {
                IPlayerModelBase model = (IPlayerModelBase)response.Model.Deserialize(resolver: specializedConfig.SharedConfig, response.LogicVersion);
                model.GameConfig = specializedConfig.SharedConfig;
                model.LogicVersion = response.LogicVersion;
                model.IsDeveloper = GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(model.PlayerId);
                return new PlayerDetails(playerId, model, player, specializedConfig.ServerConfig);
            }
            catch
            {
                throw new MetaplayHttpException(500, "Player deserialization error.", $"Cannot deserialize player with ID {playerIdStr}.");
            }
        }

        /// <summary>
        /// Enqueues execution of the Action. The action should have <see cref="Metaplay.Core.Model.ModelActionExecuteFlags.FollowerUnsynchronized"/> flag set.
        /// </summary>
        protected async Task ExecutePlayerServerActionAsync(EntityId entityId, PlayerActionBase serverAction)
        {
            // \note PlayerActor deserializes with correct LogicVersion
            await AskEntityAsync<InternalPlayerExecuteServerActionResponse>(entityId, new InternalPlayerExecuteServerActionRequest(new MetaSerialized<PlayerActionBase>(serverAction, MetaSerializationFlags.IncludeAll, logicVersion: null)));
        }

        /// <summary>
        /// Enqueues execution of the Action. The action should have <see cref="Metaplay.Core.Model.ModelActionExecuteFlags.FollowerSynchronized"/> flag set.
        /// </summary>
        protected async Task EnqueuePlayerServerActionAsync(EntityId entityId, PlayerActionBase serverAction)
        {
            // \note PlayerActor deserializes with correct LogicVersion
            await AskEntityAsync<InternalPlayerEnqueueServerActionResponse>(entityId, new InternalPlayerEnqueueServerActionRequest(new MetaSerialized<PlayerActionBase>(serverAction, MetaSerializationFlags.IncludeAll, logicVersion: null)));
        }

        /// <summary>
        /// Returns the Model state of an entity. Currently only supported by actors based on <see cref="PersistedMultiplayerEntityActorBase"/>.
        /// Note that the state may be <c>null</c> if the entity is not set up yet. If the entity does not exist, throws 404.
        /// </summary>
        protected async Task<IModel> GetEntityStateAsync(EntityId entityId)
        {
            // Check entity exists in the DB
            if (!EntityConfigRegistry.Instance.TryGetPersistedConfig(entityId.Kind, out PersistedEntityConfig entityConfig))
                throw new MetaplayHttpException(400, "Invalid Entity kind.", $"Entity kind {entityId.Kind} does not refer to a PersistedEntity");

            bool exists = await MetaDatabase.Get().TestExistsAsync(entityConfig.PersistedType, entityId.ToString());
            if (!exists)
                throw new MetaplayHttpException(404, "Persisted entity not found.", $"Cannot find {entityId}.");

            // Request state from Actor
            InternalEntityStateResponse response = await AskEntityAsync<InternalEntityStateResponse>(entityId, InternalEntityStateRequest.Instance);
            if (response.Model.IsEmpty)
                return null;

            FullGameConfig specializedConfig = await ServerGameConfigProvider.Instance.GetSpecializedGameConfigAsync(response.StaticGameConfigId, response.DynamicGameConfigId, response.SpecializationKey);
            IGameConfigDataResolver resolver = specializedConfig.SharedConfig;

            try
            {
                IModel model = response.Model.Deserialize(resolver, response.LogicVersion);
                model.LogicVersion = response.LogicVersion;
                return model;
            }
            catch
            {
                throw new MetaplayHttpException(500, $"{entityId.Kind} deserialization error.", $"Cannot deserialize Entity {entityId}.");
            }
        }
    }
}
