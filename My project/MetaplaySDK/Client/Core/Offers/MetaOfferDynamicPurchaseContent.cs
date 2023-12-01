// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Offers
{
    [MetaSerializableDerived(100)]
    public class MetaOfferGroupOfferDynamicPurchaseContent : DynamicPurchaseContent
    {
        [MetaMember(1)] public MetaOfferGroupId             GroupId { get; private set; }
        [MetaMember(2)] public MetaOfferId                  OfferId { get; private set; }
        [MetaMember(3)] public List<MetaPlayerRewardBase>   Rewards { get; private set; }

        MetaOfferGroupOfferDynamicPurchaseContent(){ }
        public MetaOfferGroupOfferDynamicPurchaseContent(MetaOfferGroupId groupId, MetaOfferId offerId, IEnumerable<MetaPlayerRewardBase> rewards)
        {
            GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
            OfferId = offerId ?? throw new ArgumentNullException(nameof(offerId));
            Rewards = (rewards ?? throw new ArgumentNullException(nameof(rewards))).ToList();
        }

        public override List<MetaPlayerRewardBase> PurchaseRewards => Rewards;

        public override void OnPurchased(IPlayerModelBase player)
        {
            if (!player.GameConfig.MetaOfferGroups.TryGetValue(GroupId, out MetaOfferGroupInfoBase groupInfo))
            {
                player.Log.Warning("Offer {OfferId} purchased, but {GroupId} has no config", OfferId, GroupId);
                return;
            }

            if (!player.GameConfig.MetaOffers.TryGetValue(OfferId, out MetaOfferInfoBase offerInfo))
            {
                player.Log.Warning("Offer {OfferId} (in {GroupId}) purchased, but it has no config", OfferId, GroupId);
                return;
            }

            player.MetaOfferGroups.OnPurchasedOffer(groupInfo, offerInfo, player);
        }
    }
}
