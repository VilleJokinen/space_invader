// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Offers
{
    [MetaSerializable]
    public class MetaOfferId : StringId<MetaOfferId> { }

    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaOfferInfoBase : IGameConfigData<MetaOfferId>, IGameConfigPostLoad, IRefersToMetaOffers
    {
        [MetaMember(100)] public MetaOfferId                            OfferId                     { get; private set; }
        [MetaMember(101)] public string                                 DisplayName                 { get; private set; }
        [MetaMember(102)] public string                                 Description                 { get; private set; }
        [MetaMember(103)] public MetaRef<InAppProductInfoBase>          InAppProduct                { get; private set; }
        [MetaMember(104)] public List<MetaPlayerRewardBase>             Rewards                     { get; private set; }
        [MetaMember(110)] public int?                                   MaxActivationsPerPlayer     { get; private set; } // null means unlimited
        [MetaMember(107)] public int?                                   MaxPurchasesPerPlayer       { get; private set; } // null means unlimited
        [MetaMember(105)] public int?                                   MaxPurchasesPerOfferGroup   { get; private set; } // null means unlimited
        [MetaMember(106)] public int?                                   MaxPurchasesPerActivation   { get; private set; } // null means unlimited
        [MetaMember(108)] public List<MetaRef<PlayerSegmentInfoBase>>   Segments                    { get; private set; }
        [MetaMember(109)] public List<PlayerCondition>                  AdditionalConditions        { get; private set; }
        /// <summary>
        /// Whether the offer will remain available in the offer group if the offer's
        /// conditions become unfulfilled during the containing group's activation.
        /// That is: if IsSticky is false, a previously available offer will become
        /// unavailable as soon as the offer-specific conditions (segments and additional
        /// conditions) become unfulfilled.
        /// Note that the other direction is still "non-sticky" regardless of this flag:
        /// if a previously unavailable offer's conditions become fulfilled during the
        /// offer group's activation, the offer will become available.
        /// </summary>
        [MetaMember(111)] public bool                                   IsSticky                    { get; private set; } = true;

        /// <summary>
        /// Whether the constructor and PostLoad should check that
        /// <see cref="InAppProduct"/> is non-null. By default this is true, as
        /// SDK-side purchase functionality only exists for IAP-backed offers.
        /// If the game implements a custom soft-currency offer purchase
        /// mechanism, this cn be overridden to false in a game-specific subclass.
        /// </summary>
        public virtual bool RequireInAppProduct => true;

        public MetaOfferId ConfigKey => OfferId;

        protected MetaOfferInfoBase(){ }
        protected MetaOfferInfoBase(
            MetaOfferId offerId,
            string displayName,
            string description,
            MetaRef<InAppProductInfoBase> inAppProduct,
            List<MetaPlayerRewardBase> rewards,
            int? maxActivationsPerPlayer,
            int? maxPurchasesPerPlayer,
            int? maxPurchasesPerOfferGroup,
            int? maxPurchasesPerActivation,
            List<MetaRef<PlayerSegmentInfoBase>> segments,
            List<PlayerCondition> additionalConditions,
            bool isSticky)
        {
            OfferId = offerId ?? throw new ArgumentNullException(nameof(offerId));
            DisplayName = displayName;
            Description = description;
            InAppProduct = inAppProduct;
            Rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            MaxActivationsPerPlayer = maxActivationsPerPlayer;
            MaxPurchasesPerPlayer = maxPurchasesPerPlayer;
            MaxPurchasesPerOfferGroup = maxPurchasesPerOfferGroup;
            MaxPurchasesPerActivation = maxPurchasesPerActivation;
            Segments = segments;
            AdditionalConditions = additionalConditions;
            IsSticky = isSticky;
        }

        /// <summary> Helper constructor for reducing boilerplate in game-side customization. </summary>
        protected MetaOfferInfoBase(MetaOfferSourceConfigItemBase source)
            : this(source.OfferId, source.DisplayName, source.Description, source.InAppProduct, source.Rewards, source.MaxActivations,
                   source.MaxPurchasesPerPlayer, source.MaxPurchasesPerOfferGroup, source.MaxPurchasesPerActivation, source.Segments,
                   source.ConstructPrecursorConditions(), source.IsSticky)
        {
        }

        public bool PerPlayerActivationLimitReached(int numActivatedByPlayer)
        {
            return MaxActivationsPerPlayer.HasValue
                && numActivatedByPlayer >= MaxActivationsPerPlayer.Value;
        }

        public bool PerPlayerPurchaseLimitReached(int numPurchasedByPlayer)
        {
            return MaxPurchasesPerPlayer.HasValue
                && numPurchasedByPlayer >= MaxPurchasesPerPlayer.Value;
        }

        public bool PerGroupPurchaseLimitReached(int numPurchasedInGroup)
        {
            return MaxPurchasesPerOfferGroup.HasValue
                && numPurchasedInGroup >= MaxPurchasesPerOfferGroup.Value;
        }

        public bool PerActivationPurchaseLimitReached(int numPurchasedDuringLatestActivation)
        {
            return MaxPurchasesPerActivation.HasValue
                && numPurchasedDuringLatestActivation >= MaxPurchasesPerActivation.Value;
        }

        public bool PlayerConditionsAreFulfilled(IPlayerModelBase player)
        {
            return SegmentationIsFulfilled(player)
                && AdditionalConditionsAreFulfilled(player);
        }

        bool SegmentationIsFulfilled(IPlayerModelBase player)
        {
            if (Segments == null || Segments.Count == 0)
                return true;
            foreach (MetaRef<PlayerSegmentInfoBase> segment in Segments)
            {
                if (segment.Ref.MatchesPlayer(player))
                    return true;
            }
            return false;
        }

        bool AdditionalConditionsAreFulfilled(IPlayerModelBase player)
        {
            if (AdditionalConditions == null)
                return true;
            foreach (PlayerCondition condition in AdditionalConditions)
            {
                if (!condition.MatchesPlayer(player))
                    return false;
            }
            return true;
        }

        void IGameConfigPostLoad.PostLoad()
        {
            if (RequireInAppProduct && InAppProduct == null)
                throw new InvalidOperationException($"Configuration of MetaOffer {OfferId} has null {nameof(InAppProduct)}. If that is intentional, please use a custom subclass of {nameof(MetaOfferInfoBase)} with {nameof(RequireInAppProduct)} overridden to false.");
            if (Rewards == null)
                throw new InvalidOperationException($"Configuration of MetaOffer {OfferId} has null {nameof(Rewards)}");
            if (Rewards.Contains(null))
                throw new InvalidOperationException($"Configuration of MetaOffer {OfferId} contains a null reward in the {nameof(Rewards)} list");
            if (Segments != null && Segments.Contains(null))
                throw new InvalidOperationException($"Configuration of MetaOffer {OfferId} has a null segment in the {nameof(Segments)} list");
            if (AdditionalConditions != null && AdditionalConditions.Contains(null))
                throw new InvalidOperationException($"Configuration of MetaOffer {OfferId} has a null condition in the {nameof(AdditionalConditions)} list");
        }

        public IEnumerable<MetaOfferId> GetReferencedMetaOffers()
        {
            return (AdditionalConditions ?? Enumerable.Empty<PlayerCondition>())
                .OfType<IRefersToMetaOffers>()
                .SelectMany(refers => refers.GetReferencedMetaOffers());
        }
    }

    public abstract class MetaOfferSourceConfigItemBase
    {
        public MetaOfferId                          OfferId;
        public string                               DisplayName;
        public string                               Description;
        public MetaRef<InAppProductInfoBase>        InAppProduct;
        public List<MetaPlayerRewardBase>           Rewards;
        public int?                                 MaxActivations;
        public int?                                 MaxPurchasesPerPlayer;
        public int?                                 MaxPurchasesPerOfferGroup;
        public int?                                 MaxPurchasesPerActivation;
        public List<MetaRef<PlayerSegmentInfoBase>> Segments;
        public List<MetaOfferId>                    PrecursorId;
        public List<bool>                           PrecursorConsumed;
        public List<MetaDuration>                   PrecursorDelay;
        public bool                                 IsSticky = true;

        /// <summary>
        /// Just a helper to turn the parallel Precursor* lists into <see cref="PlayerCondition"/>s
        /// (specifically, <see cref="MetaOfferPrecursorCondition"/>s).
        /// </summary>
        public List<PlayerCondition> ConstructPrecursorConditions()
        {
            int numPrecursors = PrecursorId?.Count ?? 0;

            if ((PrecursorId?.Count ?? 0)       != numPrecursors
             || (PrecursorConsumed?.Count ?? 0) != numPrecursors
             || (PrecursorDelay?.Count ?? 0)    != numPrecursors)
                throw new InvalidOperationException($"{OfferId}: {nameof(PrecursorId)}, {nameof(PrecursorConsumed)} and {nameof(PrecursorDelay)} lists don't have equal lengths");

            List<PlayerCondition> precursorConditions = new List<PlayerCondition>();
            for (int i = 0; i < numPrecursors; i++)
                precursorConditions.Add(new MetaOfferPrecursorCondition(PrecursorId[i], PrecursorConsumed[i], PrecursorDelay[i]));

            return precursorConditions;
        }
    }

    public abstract class MetaOfferSourceConfigItemBase<TMetaOfferInfo> :
        MetaOfferSourceConfigItemBase,
        IMetaIntegrationConstructible<MetaOfferSourceConfigItemBase<TMetaOfferInfo>>,
        IGameConfigSourceItem<MetaOfferId, TMetaOfferInfo>
        where TMetaOfferInfo : MetaOfferInfoBase, IGameConfigData<MetaOfferId>
    {
        public MetaOfferId ConfigKey => OfferId;

        public abstract TMetaOfferInfo ToConfigData();
    }

    [MetaSerializableDerived(100)]
    public class DefaultMetaOfferInfo : MetaOfferInfoBase
    {
        public DefaultMetaOfferInfo(){ }
        public DefaultMetaOfferInfo(MetaOfferSourceConfigItemBase source) : base(source) { }
    }

    public class DefaultMetaOfferSourceConfigItem : MetaOfferSourceConfigItemBase<DefaultMetaOfferInfo>
    {
        public override DefaultMetaOfferInfo ToConfigData()
        {
            return new DefaultMetaOfferInfo(this);
        }
    }

    /// <summary>
    /// Declares that an entity refers to zero or more MetaOffers.
    /// Used for resolving MetaOffers' "referenced by" information for dashboard.
    /// </summary>
    public interface IRefersToMetaOffers
    {
        IEnumerable<MetaOfferId> GetReferencedMetaOffers();
    }
}
