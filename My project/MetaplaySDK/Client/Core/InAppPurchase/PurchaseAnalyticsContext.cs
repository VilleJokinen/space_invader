// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.InAppPurchase
{
    /// <summary>
    /// The analytics context of a certain purchase. The context is game-defined opaque
    /// data that is associated with the specific purchase and it can be used for
    /// analytics purposes. For example, purchases from the shop tab of the game
    /// could be set with a context of <c>MyGameContext { Placement=ShopTab, Group=TopRow }</c>.
    /// </summary>
    /// <remarks>
    /// For custom purchase contents, see <see cref="DynamicPurchaseContent"/>.
    /// </remarks>
    [MetaSerializable]
    public abstract class PurchaseAnalyticsContext
    {
        /// <summary>
        /// Returns the display string that is used in event log in the Dashboard.
        /// </summary>
        public abstract string GetDisplayStringForEventLog();
    }

    /// <summary>
    /// A player's state corresponding a specific in-app product, indicating what analytics context
    /// should be given for the next purchase of that product in the case of a non-dynamic purchase.
    /// Dynamic purchases manage the analytics context separately.
    /// </summary>
    [MetaSerializable]
    public class PendingNonDynamicPurchaseContext
    {
        /// <summary>
        /// The analytics id for the next purchase of the product. May be null. As with <see cref="PendingDynamicPurchaseContent"/>
        /// </summary>
        [MetaMember(1)] public string                                               GameProductAnalyticsId;
        /// <summary>
        /// The analytics context for the next purchase of the product. May be null.
        /// </summary>
        [MetaMember(2)] public PurchaseAnalyticsContext                             GameAnalyticsContext;
        /// <summary>
        /// The device on which this context was made pending.
        /// Can be used for customer support purposes.
        /// </summary>
        [MetaMember(3)] public string                                               DeviceId;
        /// <summary>
        /// The status of this pending context. Indicates whether this pending context
        /// has been seen (and persisted) by the server, at which point the client may
        /// proceed with initiating the purchase in the store.
        /// </summary>
        [MetaMember(4), NoChecksum, Transient] public PendingPurchaseAnalyticsContextStatus Status = PendingPurchaseAnalyticsContextStatus.ConfirmedByServer;

        PendingNonDynamicPurchaseContext(){ }
        public PendingNonDynamicPurchaseContext(string gameProductAnalyticsId, PurchaseAnalyticsContext gameAnalyticsContext, string deviceId, PendingPurchaseAnalyticsContextStatus status)
        {
            GameProductAnalyticsId = gameProductAnalyticsId;
            GameAnalyticsContext = gameAnalyticsContext;
            DeviceId = deviceId;
            Status = status;
        }
    }

    [MetaSerializable]
    public enum PendingPurchaseAnalyticsContextStatus
    {
        RequestedByClient,
        ConfirmedByServer,
    }

    /// <summary>
    /// Player sets the context assignment to the given product.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerPreparePurchaseContext)]
    public class PlayerPreparePurchaseContext : PlayerActionCore<IPlayerModelBase>
    {
        public InAppProductId               ProductId               { get; private set; }
        public string                       GameProductAnalyticsId  { get; private set; }
        public PurchaseAnalyticsContext     GameAnalyticsContext    { get; private set; }

        PlayerPreparePurchaseContext() { }
        public PlayerPreparePurchaseContext(InAppProductId productId, string gameProductAnalyticsId, PurchaseAnalyticsContext gameAnalyticsContext)
        {
            ProductId = productId;
            GameProductAnalyticsId = gameProductAnalyticsId;
            GameAnalyticsContext = gameAnalyticsContext;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                player.PendingNonDynamicPurchaseContexts[ProductId] = new PendingNonDynamicPurchaseContext(GameProductAnalyticsId, GameAnalyticsContext, player.SessionDeviceGuid, PendingPurchaseAnalyticsContextStatus.RequestedByClient);
                player.EventStream.Event(new PlayerEventPendingStaticPurchaseContextAssigned(ProductId, player.SessionDeviceGuid, GameProductAnalyticsId, GameAnalyticsContext));
                player.ClientListenerCore.PendingStaticInAppPurchaseContextAssigned(ProductId);
                player.ServerListenerCore.StaticInAppPurchaseContextRequested(ProductId);
            }
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Server confirms that it has received the assignment of context to the given product.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext)]
    public class PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public InAppProductId ProductId { get; private set; }

        PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext(){ }
        public PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext(InAppProductId productId)
        {
            ProductId = productId;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.PendingNonDynamicPurchaseContexts.TryGetValue(ProductId, out PendingNonDynamicPurchaseContext pendingContext))
            {
                player.Log.Warning("Failed to execute {Action}: product {ProductId} not in player.PendingNonDynamicPurchaseContexts", nameof(PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext), ProductId);
                return MetaActionResult.InvalidInAppProductId;
            }

            if (pendingContext.Status != PendingPurchaseAnalyticsContextStatus.RequestedByClient)
            {
                player.Log.Warning("Failed to execute {Action}: product {ProductId} has purchase context status {Status} instead of RequestedByClient", nameof(PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext), ProductId, pendingContext.Status);
                return MetaActionResult.InvalidNonDynamicInAppPurchaseStatus;
            }

            if (commit)
            {
                pendingContext.Status = PendingPurchaseAnalyticsContextStatus.ConfirmedByServer;
            }

            return MetaActionResult.Success;
        }
    }
}
