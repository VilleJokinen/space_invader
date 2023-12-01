// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to manage a player's IAP subscriptions.
    /// </summary>
    public class PlayerIAPSubscriptionsController : GameAdminApiController
    {
        public PlayerIAPSubscriptionsController(ILogger<PlayerIAPSubscriptionsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerIAPSubscriptionRemoved)]
        public class PlayerIAPSubscriptionRemoved : PlayerEventPayloadBase
        {
            [MetaMember(1)] public InAppProductId ProductId { get; private set; }
            PlayerIAPSubscriptionRemoved() { }
            public PlayerIAPSubscriptionRemoved(InAppProductId productId)
            {
                ProductId = productId;
            }
            public override string EventTitle => "Subscription removed";
            public override string EventDescription => $"IAP subscription {ProductId} removed.";
        }

        /// <summary>
        /// API endpoint to remove an IAP subscription from a player. Development-only action.
        /// Usage:  POST /api/players/{PLAYERID}/removeIAPSubscription/{PRODUCTID}
        /// </summary>
        [HttpPost("players/{playerIdStr}/removeIAPSubscription/{productIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersRemoveIAPSubscription)]
        public async Task RemoveIAPSubscription(string playerIdStr, string productIdStr)
        {
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);
            bool isDeveloper = GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(playerId);
            if (!envOpts.EnableDevelopmentFeatures && !isDeveloper)
                throw new MetaplayHttpException(400, "Development features not enabled.", $"Removing an IAP subscription is a development feature, and {nameof(EnvironmentOptions)}.{nameof(EnvironmentOptions.EnableDevelopmentFeatures)} is false, and the player isn't marked as a developer.");

            _logger.LogInformation("Removing IAP subscription {ProductId} from player {PlayerId}", productIdStr, playerIdStr);

            InAppProductId productId = InAppProductId.FromString(productIdStr);

            // Enqueue action
            await EnqueuePlayerServerActionAsync(playerId, new PlayerDebugRemoveSubscription(productId));

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerIAPSubscriptionRemoved(productId)));
        }
    }
}
