// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.InAppPurchase
{
    /// <summary>
    /// Dynamic content of an in-app product.
    /// Unlike normal (static-content) IAPs, dynamic-content IAPs
    /// have content that is decided at runtime, instead of being
    /// specified by the IAP config.
    /// </summary>
    [MetaSerializable]
    public abstract class DynamicPurchaseContent
    {
        /// <summary>
        /// Called when the purchase is claimed.
        /// This should "consume" the content from the player, if applicable.
        /// <para>
        /// For example, if this content corresponds to a one-time offer,
        /// that offer should be removed/disabled in this method.
        /// </para>
        /// </summary>
        /// <remarks>
        /// This method should not grant the reward/content to the player.
        /// That is instead done by the SDK using <see cref="PurchaseRewards"/>.
        /// </remarks>
        public abstract void OnPurchased(IPlayerModelBase player);

        /// <summary>
        /// The rewards in this dynamic content.
        /// These are given to the player when the purchase is claimed.
        /// </summary>
        public abstract List<MetaPlayerRewardBase> PurchaseRewards { get; }
    }

    /// <summary>
    /// A player's state corresponding a specific in-app product,
    /// indicating what content should be given for the next purchase of that product.
    /// </summary>
    [MetaSerializable]
    public class PendingDynamicPurchaseContent
    {
        /// <summary>
        /// The content that should be given for the next purchase of the product.
        /// </summary>
        [MetaMember(1)] public DynamicPurchaseContent                           Content;
        /// <summary>
        /// The device on which this content was made pending.
        /// Can be used for customer support purposes.
        /// </summary>
        [MetaMember(3)] public string                                           DeviceId;
        /// <summary>
        /// The status of this pending content. Indicates whether this pending content
        /// has been seen (and persisted) by the server, at which point the client may
        /// proceed with initiating the purchase in the store.
        /// </summary>
        [MetaMember(2), NoChecksum, Transient] public PendingDynamicPurchaseContentStatus Status = PendingDynamicPurchaseContentStatus.ConfirmedByServer;
        /// <summary>
        /// The analytics id for the next purchase of the product. This is different from InAppPurchaseId,
        /// as the former maps exactly to one platform store IAP. As Dynamic offers may reuse a single IAP
        /// price point for multiple different purchases, this Analytics Id allows game to assign separate
        /// IDs different (from user's perspective) offers. May be null.
        /// </summary>
        [MetaMember(4)] public string                                           GameProductAnalyticsId;
        /// <summary>
        /// The analytics context for the next purchase of the product. May be null.
        /// </summary>
        [MetaMember(5)] public PurchaseAnalyticsContext                         GameAnalyticsContext;

        public PendingDynamicPurchaseContent(){ }
        public PendingDynamicPurchaseContent(DynamicPurchaseContent content, string deviceId, string gameProductAnalyticsId, PurchaseAnalyticsContext gameAnalyticsContext, PendingDynamicPurchaseContentStatus status)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            DeviceId = deviceId;
            Status = status;
            GameProductAnalyticsId = gameProductAnalyticsId;
            GameAnalyticsContext = gameAnalyticsContext;
        }
    }

    [MetaSerializable]
    public enum PendingDynamicPurchaseContentStatus
    {
        RequestedByClient,
        ConfirmedByServer,
    }

    /// <summary>
    /// Server confirms that it has received the assignment of dynamic content to the given product.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerConfirmPendingDynamicPurchaseContent)]
    public class PlayerConfirmPendingDynamicPurchaseContent : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public InAppProductId ProductId { get; private set; }

        PlayerConfirmPendingDynamicPurchaseContent(){ }
        public PlayerConfirmPendingDynamicPurchaseContent(InAppProductId productId){ ProductId = productId; }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.PendingDynamicPurchaseContents.TryGetValue(ProductId, out PendingDynamicPurchaseContent pendingContent))
            {
                player.Log.Warning("Failed to execute {Action}: product {ProductId} not in player.PendingDynamicInAppPurchases", nameof(PlayerConfirmPendingDynamicPurchaseContent), ProductId);
                return MetaActionResult.InvalidInAppProductId;
            }

            if (pendingContent.Status != PendingDynamicPurchaseContentStatus.RequestedByClient)
            {
                player.Log.Warning("Failed to execute {Action}: product {ProductId} has dynamic purchase content status {Status} instead of RequestedByClient", nameof(PlayerConfirmPendingDynamicPurchaseContent), ProductId, pendingContent.Status);
                return MetaActionResult.InvalidDynamicInAppPurchaseStatus;
            }

            if (commit)
            {
                pendingContent.Status = PendingDynamicPurchaseContentStatus.ConfirmedByServer;
            }

            return MetaActionResult.Success;
        }
    }
}
