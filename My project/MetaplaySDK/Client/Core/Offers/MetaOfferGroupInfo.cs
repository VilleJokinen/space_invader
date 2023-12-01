// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Core.Offers
{
    [MetaActivableCategoryMetadata(
        id:                         "OfferGroup",
        displayName:                "Offer Groups",
        shortSingularDisplayName:   "Offer Group",
        description:                "Offer groups, each containing a number of purchasable offers")]
    public static class ActivableCategoryMetadataMetaOfferGroup
    {
    }

    [MetaActivableKindMetadata(
        id:             "OfferGroup",
        displayName:    "Offer Group",
        description:    "Offer group, containing a number of purchasable offers",
        categoryId:     "OfferGroup")]
    public static class ActivableKindMetadataMetaOfferGroup
    {
    }

    [MetaSerializable]
    public class OfferPlacementId : StringId<OfferPlacementId> { }

    [MetaSerializable]
    public class MetaOfferGroupId : StringId<MetaOfferGroupId> { }

    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaOfferGroupInfoBase : IMetaActivableConfigData<MetaOfferGroupId>, IGameConfigPostLoad
    {
        [MetaMember(100)] public MetaOfferGroupId                   GroupId         { get; private set; }
        [MetaMember(101)] public string                             DisplayName     { get; private set; }
        [MetaMember(102)] public string                             Description     { get; private set; }
        /// <summary>
        /// Placement of the offer group. At most one offer group per placement
        /// can be active at a time. Beyond that, the meaning of placements is game-specific.
        /// </summary>
        [MetaMember(103)] public OfferPlacementId                   Placement       { get; private set; }
        /// <summary>
        /// Priority ordinal.
        /// *Lower* Priority number means "higher priority"/"higher importance".
        /// When multiple offer groups with the same <see cref="Placement"/> are available,
        /// the one with the *lowest* <see cref="Priority"/> number (i.e. highest importance)
        /// gets activated.
        /// </summary>
        [MetaMember(104)] public int                                Priority        { get; private set; }
        [MetaMember(105)] public List<MetaRef<MetaOfferInfoBase>>   Offers          { get; private set; }
        [MetaMember(106)] public MetaActivableParams                ActivableParams { get; private set; }
        /// <summary>
        /// Maximum number of offers in this group that can be activate for a player at the same time. In cases where there
        /// would be more than this number of offers available for activation to the player, the order of offers in the
        /// `Offers` list is used as priority. A value of null (the default) disables the limit, all available offers will be
        /// activated.
        /// </summary>
        [MetaMember(107)] public int?                               MaxOffersActive { get; private set; }

        protected MetaOfferGroupInfoBase(){ }
        protected MetaOfferGroupInfoBase(MetaOfferGroupId groupId, string displayName, string description, OfferPlacementId placement, int priority, List<MetaRef<MetaOfferInfoBase>> offers, MetaActivableParams activableParams, int? maxOffersActive)
        {
            GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Placement = placement ?? throw new ArgumentNullException(nameof(placement));
            Priority = priority;
            Offers = offers ?? throw new ArgumentNullException(nameof(offers));
            ActivableParams = activableParams ?? throw new ArgumentNullException(nameof(activableParams));
            MaxOffersActive = maxOffersActive;
        }

        /// <summary> Helper constructor for reducing boilerplate in game-side customization. </summary>
        protected MetaOfferGroupInfoBase(MetaOfferGroupSourceConfigItemBase source)
            : this(source.GroupId, source.DisplayName ?? "", source.Description ?? "", source.Placement, source.Priority, source.Offers, source.GetActivableParams(), source.MaxOffersActive)
        {
        }

        public MetaOfferGroupId ActivableId => GroupId;
        public MetaOfferGroupId ConfigKey   => GroupId;

        public string DisplayShortInfo
        {
            get
            {
                int numOffers = Offers?.Count ?? 0;
                return Invariant($"{numOffers} offer{(numOffers == 1 ? "" : "s")}");
            }
        }

        public bool MaxOffersActiveLimitReached(int count) => MaxOffersActive.HasValue && count >= MaxOffersActive.Value;

        public void PostLoad()
        {
            foreach (MetaRef<MetaOfferInfoBase> offer in Offers)
            {
                if (offer == null)
                    throw new InvalidOperationException($"Offer group {GroupId} contains a null offer reference");
            }
            foreach (MetaRef<MetaOfferInfoBase> offer in Offers)
            {
                if (Offers.Count(o => o.Ref.OfferId == offer.Ref.OfferId) > 1)
                    throw new InvalidOperationException($"{GroupId} lists offer {offer.Ref.OfferId} multiple times");
            }
        }
    }

    public abstract class MetaOfferGroupSourceConfigItemBase
    {
        public MetaOfferGroupId                     GroupId         { get; private set; }
        public string                               DisplayName     { get; private set; }
        public string                               Description     { get; private set; }
        public OfferPlacementId                     Placement       { get; private set; }
        public int                                  Priority        { get; private set; }
        public List<MetaRef<MetaOfferInfoBase>>     Offers          { get; private set; }
        public List<MetaRef<PlayerSegmentInfoBase>> Segments        { get; private set; }
        public MetaActivableLifetimeSpec            Lifetime        { get; private set; }
        public MetaActivableCooldownSpec            Cooldown        { get; private set; } = MetaActivableCooldownSpec.Fixed.Zero;
        public MetaScheduleBase                     Schedule        { get; private set; }
        public int?                                 MaxOffersActive { get; private set; }


        public virtual MetaActivableParams GetActivableParams()
        {
            return new MetaActivableParams(
                isEnabled:                  true,
                segments:                   Segments,
                additionalConditions:       null,
                lifetime:                   GetEffectiveLifetime(),
                isTransient:                false,
                schedule:                   Schedule,
                maxActivations:             null, // \note For offer groups, limits are per-offer.
                maxTotalConsumes:           null, // \note For offer groups, limits are per-offer.
                maxConsumesPerActivation:   null, // \note For offer groups, limits are per-offer.
                cooldown:                   Cooldown,
                allowActivationAdjustment:  true);
        }

        public virtual MetaActivableLifetimeSpec GetEffectiveLifetime()
        {
            if (Lifetime != null)
                return Lifetime;
            else if (Schedule != null)
                return MetaActivableLifetimeSpec.ScheduleBased.Instance;
            else
                return MetaActivableLifetimeSpec.Forever.Instance;
        }
    }

    public abstract class MetaOfferGroupSourceConfigItemBase<TMetaOfferGroupInfo> :
        MetaOfferGroupSourceConfigItemBase,
        IGameConfigSourceItem<MetaOfferGroupId, TMetaOfferGroupInfo>,
        IMetaIntegrationConstructible<MetaOfferGroupSourceConfigItemBase<TMetaOfferGroupInfo>>
        where TMetaOfferGroupInfo : MetaOfferGroupInfoBase, IGameConfigData<MetaOfferGroupId>
    {
        public MetaOfferGroupId ConfigKey => GroupId;

        public abstract TMetaOfferGroupInfo ToConfigData();
    }


    [MetaSerializableDerived(100)]
    [MetaActivableConfigData("OfferGroup", fallback: true, warnAboutMissingConfigLibrary: false)]
    public class DefaultMetaOfferGroupInfo : MetaOfferGroupInfoBase
    {
        public DefaultMetaOfferGroupInfo() { }
        public DefaultMetaOfferGroupInfo(MetaOfferGroupSourceConfigItemBase source) : base(source) { }
    }

    public class DefaultMetaOfferGroupSourceConfigItem : MetaOfferGroupSourceConfigItemBase<DefaultMetaOfferGroupInfo>
    {
        public override DefaultMetaOfferGroupInfo ToConfigData()
        {
            return new DefaultMetaOfferGroupInfo(this);
        }
    }
}
