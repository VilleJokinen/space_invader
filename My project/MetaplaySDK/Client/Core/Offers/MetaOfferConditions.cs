// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Offers
{
    /// <summary>
    /// Condition used for defining dependency relations between MetaOffers.
    ///
    /// Specifies a condition that <see cref="OfferId"/> was previously
    /// active for the player, at least <see cref="Delay"/> has elapsed since
    /// the activation ended, and the offer either was or wasn't purchased
    /// (according to <see cref="Purchased"/>).
    /// </summary>
    [MetaSerializableDerived(1100)]
    public class MetaOfferPrecursorCondition : PlayerCondition, IRefersToMetaOffers
    {
        [MetaMember(1)] public MetaOfferId  OfferId     { get; private set; }
        [MetaMember(2)] public bool         Purchased   { get; private set; }
        [MetaMember(3)] public MetaDuration Delay       { get; private set; }

        MetaOfferPrecursorCondition() { }
        public MetaOfferPrecursorCondition(MetaOfferId offerId, bool purchased, MetaDuration delay)
        {
            OfferId = offerId;
            Purchased = purchased;
            Delay = delay;
        }

        public override bool MatchesPlayer(IPlayerModelBase player)
        {
            MetaOfferPerPlayerStateBase offer = player.MetaOfferGroups.TryGetOfferPerPlayerState(OfferId);
            if (offer == null)
                return false;
            if (!offer.LatestActivation.HasValue)
                return false;

            MetaOfferPerPlayerActivation activation = offer.LatestActivation.Value;

            if (!activation.EndedAt.HasValue || player.CurrentTime < activation.EndedAt.Value + Delay)
                return false;

            bool offerWasPurchased = activation.NumPurchased > 0;
            return offerWasPurchased == Purchased;
        }

        public override IEnumerable<PlayerSegmentId> GetSegmentReferences()
        {
            return Enumerable.Empty<PlayerSegmentId>();
        }

        IEnumerable<MetaOfferId> IRefersToMetaOffers.GetReferencedMetaOffers()
            => new MetaOfferId[]{ OfferId };
    }

    /// <summary>
    /// Similar to <see cref="MetaOfferPrecursorCondition"/> but the condition concerns
    /// an offer group instead of an individual offer.
    /// </summary>
    [MetaSerializableDerived(1101)]
    public class MetaOfferGroupPrecursorCondition : MetaActivablePrecursorCondition<MetaOfferGroupId>
    {
        protected override IMetaActivableSet<MetaOfferGroupId> GetActivableSet(IPlayerModelBase player)
            => player.MetaOfferGroups;
    }
}
