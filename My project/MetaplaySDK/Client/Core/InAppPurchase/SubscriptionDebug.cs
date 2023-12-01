// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.InAppPurchase
{
    /// <summary>
    /// Development action:
    /// Remove the IAP subscription of the given product from the player,
    /// as well as its related entries in the player's IAP history.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerDebugRemoveSubscription)]
    [DevelopmentOnlyAction]
    public class PlayerDebugRemoveSubscription : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public InAppProductId ProductId { get; private set; }

        PlayerDebugRemoveSubscription() { }
        public PlayerDebugRemoveSubscription(InAppProductId productId)
        {
            ProductId = productId;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.IAPSubscriptions.Subscriptions.ContainsKey(ProductId))
                return MetaActionResult.NoSuchSubscription;

            if (commit)
            {
                player.IAPSubscriptions.Subscriptions.Remove(ProductId);

                player.InAppPurchaseHistory.RemoveAll(p => p.ProductId == ProductId);
                player.FailedInAppPurchaseHistory.RemoveAll(p => p.ProductId == ProductId);
            }

            return MetaActionResult.Success;
        }
    }
}
