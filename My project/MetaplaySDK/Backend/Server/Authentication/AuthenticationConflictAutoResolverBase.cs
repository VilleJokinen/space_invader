// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Player;
using Metaplay.Server.GameConfig;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication
{
    /// <summary>
    /// Provides logic to perform automatic resolutions in cases where user's authentication methods ambiguously map to multiple player Ids (i.e. player accounts).
    /// This state of ambigous mappings is called a conflict.
    /// </summary>
    public class AuthenticationConflictAutoResolverBase : IMetaIntegrationSingleton<AuthenticationConflictAutoResolverBase>
    {
        /// <summary>
        /// Fetches the current model of a player.
        /// </summary>
        public static async Task<IPlayerModelBase> GetPlayerModelAsync(IEntityAsker asker, EntityId playerId)
        {
            InternalEntityStateResponse response            = await asker.EntityAskAsync<InternalEntityStateResponse>(playerId, InternalEntityStateRequest.Instance);
            FullGameConfig              specializedConfig   = await ServerGameConfigProvider.Instance.GetSpecializedGameConfigAsync(response.StaticGameConfigId, response.DynamicGameConfigId, response.SpecializationKey);
            IGameConfigDataResolver     resolver            = specializedConfig.SharedConfig;
            IPlayerModelBase            state               = (IPlayerModelBase)response.Model.Deserialize(resolver: resolver, logicVersion: response.LogicVersion);

            state.GameConfig = specializedConfig.SharedConfig;
            state.LogicVersion = response.LogicVersion;

            return state;
        }

        /// <summary>
        /// Chooses the best Player from <paramref name="possiblePlayers"/>.
        /// <para>
        /// This is called when a single Social authentication resolves to multiple players. Single social authentication can resolve to multiple keys for example in cases
        /// where platform provides some kind of a legacy and current account ids. This method is called if these account ids then resolve to different players. This can
        /// happen if Social platform or or its integration has failed to provide either legacy or new ids at some point in time.
        /// </para>
        /// </summary>
        /// <param name="possiblePlayers">Possible player ids for the keys. Always 2 or more elements.</param>
        public virtual Task<EntityId> ResolveInternallyConflictingSocialPlatformAsync(IEntityAsker asker, AuthenticatedSocialClaimKeys keys, EntityId[] possiblePlayers)
        {
            // Default behavior is to pick the first.
            return Task.FromResult(possiblePlayers[0]);
        }
    }
}
