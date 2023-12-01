// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Offers
{
    /// <summary>
    /// Refresh offers and offer groups; that is, activate new offer groups
    /// if possible, and also activate new offers in already-active offer groups
    /// if possible.
    /// This can be invoked by the game, for example when the player opens the shop UI.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerRefreshMetaOffers)]
    public class PlayerRefreshMetaOffers : PlayerActionCore<IPlayerModelBase>
    {
        MetaOfferGroupsRefreshInfo? _partialRefreshInfo;

        PlayerRefreshMetaOffers() { }

        /// <param name="partialRefreshInfo">
        /// Info produced by <see cref="IPlayerModelBase.GetMetaOfferGroupsRefreshInfo"/>,
        /// or null to force the action to consider all configured offer groups.
        /// </param>
        public PlayerRefreshMetaOffers(MetaOfferGroupsRefreshInfo? partialRefreshInfo)
        {
            _partialRefreshInfo = partialRefreshInfo;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                player.RefreshMetaOffers(_partialRefreshInfo);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Parameters for <see cref="PlayerRefreshMetaOffers"/>,
    /// describing which offer groups it should consider refreshing.
    /// See <see cref="IPlayerModelBase.GetMetaOfferGroupsRefreshInfo"/>.
    /// </summary>
    [MetaSerializable]
    public struct MetaOfferGroupsRefreshInfo
    {
        [MetaMember(1)] public List<MetaOfferGroupInfoBase> GroupsToFinalize;
        [MetaMember(2)] public List<MetaOfferGroupInfoBase> GroupsWithOffersToRefresh;
        [MetaMember(3)] public List<MetaOfferGroupInfoBase> GroupsToActivate;

        public MetaOfferGroupsRefreshInfo(List<MetaOfferGroupInfoBase> groupsToFinalize, List<MetaOfferGroupInfoBase> groupsWithOffersToRefresh, List<MetaOfferGroupInfoBase> groupsToActivate)
        {
            GroupsToFinalize = groupsToFinalize;
            GroupsWithOffersToRefresh = groupsWithOffersToRefresh;
            GroupsToActivate = groupsToActivate;
        }

        public bool HasAny() => (GroupsToFinalize?.Count ?? 0) != 0
                             || (GroupsWithOffersToRefresh?.Count ?? 0) != 0
                             || (GroupsToActivate?.Count ?? 0) != 0;
    }

    /// <summary>
    /// Prepare the purchase of the given offer IAP in the given offer group.
    ///
    /// This uses the dynamic-content IAP flow:
    /// The offer is assigned as the pending dynamic content for the IAP product.
    /// The client should wait for the server's confirmation of the content assignment
    /// by tracking player.PendingDynamicPurchaseContents[].Status, and then initiate
    /// an IAP purchase.
    /// The dynamic-content purchase flow is documented in the "Advanced Usage of
    /// In-App Purchases" document and has a reference implementation in the Idler
    /// reference project.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerPreparePurchaseMetaOffer)]
    public class PlayerPreparePurchaseMetaOffer : PlayerActionCore<IPlayerModelBase>
    {
        public MetaOfferGroupInfoBase   OfferGroupInfo      { get; private set; }
        public MetaOfferInfoBase        OfferInfo           { get; private set; }
        public PurchaseAnalyticsContext AnalyticsContext    { get; private set; }

        PlayerPreparePurchaseMetaOffer(){ }
        public PlayerPreparePurchaseMetaOffer(MetaOfferGroupInfoBase offerGroupInfo, MetaOfferInfoBase offerInfo, PurchaseAnalyticsContext analyticsContext)
        {
            OfferGroupInfo = offerGroupInfo ?? throw new ArgumentNullException(nameof(offerGroupInfo));
            OfferInfo = offerInfo ?? throw new ArgumentNullException(nameof(offerInfo));
            AnalyticsContext = analyticsContext;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            // Offer group must have state (i.e. has been activated previously)
            if (player.MetaOfferGroups.TryGetState(OfferGroupInfo.ActivableId) == null)
                return MetaActionResult.MetaOfferGroupHasNoState;

            IEnumerable<MetaOfferStatus> offersInGroup = player.MetaOfferGroups.GetOffersInGroup(OfferGroupInfo, player);

            // Offer must exist in group
            if (!offersInGroup.Any(offer => offer.Info.OfferId == OfferInfo.OfferId))
                return MetaActionResult.MetaOfferNotInGroup;

            MetaOfferStatus offerStatus = offersInGroup.FirstOrDefault(offer => offer.Info.OfferId == OfferInfo.OfferId);

            // Offer must be purchasable
            if (!player.MetaOfferGroups.OfferIsPurchasable(offerStatus))
            {
                player.Log.Warning("{Action}: Offer {OfferId} in group {GroupId} is not currently purchasable (isActive={IsActive}, limitReached={LimitReached})", nameof(PlayerPreparePurchaseMetaOffer), OfferInfo.OfferId, OfferGroupInfo.GroupId, offerStatus.IsActive, offerStatus.AnyPurchaseLimitReached);
                return MetaActionResult.MetaOfferNotPurchasable;
            }

            // Must be an IAP offer.
            if (OfferInfo.InAppProduct == null)
                return MetaActionResult.MetaOfferDoesNotHaveInAppProduct;

            DynamicPurchaseContent purchaseContent = new MetaOfferGroupOfferDynamicPurchaseContent(OfferGroupInfo.GroupId, OfferInfo.OfferId, OfferInfo.Rewards);

            if (!player.CanSetPendingDynamicInAppPurchase(OfferInfo.InAppProduct.Ref, purchaseContent, out string errorMessage))
            {
                player.Log.Warning("{Action}: {Error}", nameof(PlayerPreparePurchaseMetaOffer), errorMessage);
                return MetaActionResult.CannotSetPendingDynamicPurchase;
            }

            if (commit)
            {
                player.SetPendingDynamicInAppPurchase(OfferInfo.InAppProduct.Ref, purchaseContent, gameProductAnalyticsId: OfferInfo.OfferId.ToString(), AnalyticsContext);
            }

            return MetaActionResult.Success;
        }
    }
}
