// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to refund Google Play purchases via their API.
    /// </summary>
    public class GooglePlayRefundController : GameAdminApiController
    {
        public GooglePlayRefundController(ILogger<GooglePlayRefundController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GameServerIAPRefundRequested)]
        public class PlayerEventIAPRefundRequested : PlayerEventPayloadBase
        {
            [MetaMember(1)] public string PurchaseId { get; private set; }
            [MetaMember(2)] public string OrderId { get; private set; }
            public PlayerEventIAPRefundRequested() { }
            public PlayerEventIAPRefundRequested(string purchaseId, string orderId) { PurchaseId = purchaseId; OrderId = orderId; }
            override public string EventTitle => "IAP refund requested";
            override public string EventDescription => $"IAP refund requested (IAP ID: {PurchaseId}, Order ID: {OrderId}).";
        }


        /// <summary>
        /// API endpoint to request a refund for a player's IAP
        /// Usage:  POST /api/players/{PLAYERID}/refund/{PURCHASEID}
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/refund/{PURCHASEID} -X POST
        /// </summary>
        [HttpPost("players/{playerIdStr}/refund/{purchaseId}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersIapRefund)]
        public async Task<ActionResult<int>> RefundPurchase(string playerIdStr, string purchaseId)
        {
            GooglePlayStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();
            if (!storeOpts.EnableGooglePlayInAppPurchaseRefunds)
                throw new MetaplayHttpException(500, "IAP refunds not enabled!", "You need to configure IAP refunds in the game server before it has the permissions to request refunds on your behalf.");

            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);

            // Early checks to avoid unnecessary calls to Google API
            InAppPurchaseEvent iap = details.Model.InAppPurchaseHistory.Find(ev => ev.TransactionId == purchaseId);
            if (iap == null)
                throw new MetaplayHttpException(500, "No such purchase", "Could not find the purchase with the given transaction id.");

            if (iap.Status == InAppPurchaseStatus.Refunded)
                throw new MetaplayHttpException(500, "Already refunded", "Cannot refund a purchase that already has been refunded.");
            else if (iap.Status != InAppPurchaseStatus.ValidReceipt)
                throw new MetaplayHttpException(500, "Purchase has not passed validation", "Cannot refund a purchase that has not yet passed the validation.");

            if (iap.OrderId == null)
                throw new MetaplayHttpException(500, "Order id is missing", "Refunding requires the purchase's order id, but no order id is found for this purchase.");

            try
            {
                await AndroidPublisherServiceSingleton.Instance.Orders.Refund(storeOpts.AndroidPackageName, iap.OrderId).ExecuteAsync();

                await AskEntityAsync<PlayerRefundPurchaseResponse>(details.PlayerId, new PlayerRefundPurchaseRequest(purchaseId)).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                throw new MetaplayHttpException(404, "Refund request failed.", ex.Message);
            }

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(details.PlayerId, new PlayerEventIAPRefundRequested(purchaseId, iap.OrderId)));

            return NoContent();
        }
    }
}
