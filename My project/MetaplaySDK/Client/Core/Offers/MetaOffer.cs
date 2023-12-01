// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Metaplay.Core.Offers
{
    /// <summary>
    /// An offer's per-offer-group state.
    /// This tracks the offer's per-offer-group purchase limits.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaOfferPerGroupStateBase
    {
        /// <summary>
        /// How many times the offer has been activated in the specific group.
        /// </summary>
        [MetaMember(100)] public int                    NumActivatedInGroup { get; protected set; } = 0;

        /// <summary>
        /// How many times the offer has been purchased via the specific group.
        /// </summary>
        [MetaMember(101)] public int                    NumPurchasedInGroup { get; protected set; } = 0;

        /// <summary>
        /// The current, or previous ongoing, activation of this offer.
        /// An activation of an offer occurs within the containing group's activation.
        ///
        /// Due to per-offer segmentation and other conditions, an offer might be inactive
        /// even though the group itself is active, and the offer might suddenly become active
        /// (and subsequently inactive again) while the group is already active.
        /// An offer's activation starts when the offer first becomes active during the
        /// group's activation.
        /// </summary>
        [MetaMember(102)] public MetaOfferPerGroupActivation? LatestActivation { get; protected set; } = null;

        /// <summary>
        /// Start an activation for this offer, for the given activation instance
        /// (identified by <paramref name="groupActivationIndex"/>) of the containing group.
        /// </summary>
        public virtual void StartActivation(IPlayerModelBase player, MetaOfferInfoBase offerInfo, int groupActivationIndex)
        {
            NumActivatedInGroup++;
            LatestActivation = new MetaOfferPerGroupActivation(
                groupActivationIndex:   groupActivationIndex,
                isActive:               true,
                numPurchased:           0,
                endedAt:                null);
        }

        public virtual void FinalizeActivation(IPlayerModelBase player, int groupActivationIndex, MetaTime endedAt)
        {
            if (IsIncludedInGroupActivation(groupActivationIndex))
                MutateLatestActivation((ref MetaOfferPerGroupActivation act) => act.EndedAt = endedAt);
        }

        /// <summary>
        /// Whether this offer has been active at any point the given activation of the containing offer group.
        /// </summary>
        public bool IsIncludedInGroupActivation(int groupActivationIndex)
        {
            return LatestActivation.HasValue
                && LatestActivation.Value.GroupActivationIndex == groupActivationIndex;
        }

        /// <summary>
        /// Whether this offer is currently active in the given activation of the containing offer group.
        /// </summary>
        public bool IsActiveInGroupActivation(int groupActivationIndex)
        {
            return IsIncludedInGroupActivation(groupActivationIndex)
                && LatestActivation.Value.IsActive;
        }

        /// <summary>
        /// Called when the offer has been purchased.
        /// This should bump the offer's purchase counts.
        /// </summary>
        /// <remarks>
        /// This method does not grant the offer's rewards.
        /// Those are granted separately, by the dynamic purchase mechanism
        /// (see <see cref="DynamicPurchaseContent.PurchaseRewards"/>).
        /// </remarks>
        public virtual void OnPurchased(IPlayerModelBase player, MetaOfferInfoBase offerInfo, int groupActivationIndex)
        {
            NumPurchasedInGroup++;

            if (IsIncludedInGroupActivation(groupActivationIndex))
                MutateLatestActivation((ref MetaOfferPerGroupActivation act) => act.NumPurchased++);
        }

        /// <summary>
        /// Sets the offer inactive when it is currently active in the given activation of the containing offer group.
        /// </summary>
        public virtual void SetInactiveDuringOngoingActivation(int groupActivationIndex)
        {
            if (IsIncludedInGroupActivation(groupActivationIndex))
                MutateLatestActivation((ref MetaOfferPerGroupActivation act) => act.IsActive = false);
        }

        /// <summary>
        /// Sets the offer active when it is currently inactive in the given activation of the containing offer group.
        /// </summary>
        public virtual void SetActiveDuringOngoingActivation(int groupActivationIndex)
        {
            if (IsIncludedInGroupActivation(groupActivationIndex))
                MutateLatestActivation((ref MetaOfferPerGroupActivation act) => act.IsActive = true);
        }

        protected void MutateLatestActivation(ActionRef<MetaOfferPerGroupActivation> mutate)
        {
            MetaOfferPerGroupActivation act = LatestActivation.Value;
            mutate(ref act);
            LatestActivation = act;
        }

        protected delegate void ActionRef<T>(ref T value);
    }

    [MetaSerializable]
    public struct MetaOfferPerGroupActivation
    {
        /// <summary>
        /// The index of the containing group's activation, i.e. the
        /// <see cref="MetaOfferGroupModelBase"/> <c>NumActivated-1</c>
        /// during the activation. This is used to determine whether
        /// the offer is included in the group's current activation.
        /// </summary>
        [MetaMember(1)] public int          GroupActivationIndex;
        /// <summary>
        /// Whether the offer is currently active. An offer can be inactive
        /// even if it has an activation, in case its segmentation or other
        /// conditions became unfulfilled during the activation.
        /// </summary>
        [MetaMember(2)] public bool         IsActive;
        /// <summary>
        /// How many times the offer has been purchased during this
        /// activation.
        /// </summary>
        [MetaMember(3)] public int          NumPurchased;
        /// <summary>
        /// When the activation ended.
        /// Assigned when the containing group's activation is finalized.
        /// </summary>
        [MetaMember(4)] public MetaTime?    EndedAt;

        public MetaOfferPerGroupActivation(int groupActivationIndex, bool isActive, int numPurchased, MetaTime? endedAt)
        {
            GroupActivationIndex = groupActivationIndex;
            IsActive = isActive;
            NumPurchased = numPurchased;
            EndedAt = endedAt;
        }
    }

    /// <summary>
    /// An offer's per-player state.
    /// This tracks the offer's per-player (as opposed to per-offer-group) purchase limits.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaOfferPerPlayerStateBase
    {
        [MetaMember(101)] public int NumActivatedByPlayer { get; set; } = 0;
        [MetaMember(100)] public int NumPurchasedByPlayer { get; set; } = 0;

        /// <summary>
        /// Similar to <see cref="MetaOfferPerGroupStateBase.LatestActivation"/>,
        /// but not for a specific group. I.e., the latest activation of this offer
        /// in any group.
        /// </summary>
        [MetaMember(102)] public MetaOfferPerPlayerActivation? LatestActivation { get; set; } = null;

        public virtual void OnActivated(IPlayerModelBase player, MetaOfferInfoBase offerInfo)
        {
            NumActivatedByPlayer++;
            LatestActivation = new MetaOfferPerPlayerActivation(
                numPurchased:   0,
                endedAt:        null);
        }

        public virtual void OnActivationFinalized(IPlayerModelBase player, MetaTime endedAt)
        {
            if (LatestActivation.HasValue)
                MutateLatestActivation((ref MetaOfferPerPlayerActivation act) => act.EndedAt = endedAt);
        }

        /// <summary>
        /// Called when the offer has been purchased.
        /// This should bump the offer's purchase counts.
        /// </summary>
        /// <remarks>
        /// This method does not grant the offer's rewards.
        /// Those are granted separately, by the dynamic purchase mechanism
        /// (see <see cref="DynamicPurchaseContent.PurchaseRewards"/>).
        /// </remarks>
        public virtual void OnPurchased(IPlayerModelBase player, MetaOfferInfoBase offerInfo)
        {
            NumPurchasedByPlayer++;
            if (LatestActivation.HasValue)
                MutateLatestActivation((ref MetaOfferPerPlayerActivation act) => act.NumPurchased++);
        }

        protected void MutateLatestActivation(ActionRef<MetaOfferPerPlayerActivation> mutate)
        {
            MetaOfferPerPlayerActivation act = LatestActivation.Value;
            mutate(ref act);
            LatestActivation = act;
        }

        protected delegate void ActionRef<T>(ref T value);
    }

    [MetaSerializable]
    public struct MetaOfferPerPlayerActivation
    {
        [MetaMember(3)] public int          NumPurchased;
        [MetaMember(4)] public MetaTime?    EndedAt;

        public MetaOfferPerPlayerActivation(int numPurchased, MetaTime? endedAt)
        {
            NumPurchased = numPurchased;
            EndedAt = endedAt;
        }
    }

    [MetaSerializableDerived(100)]
    public class DefaultMetaOfferPerGroupState : MetaOfferPerGroupStateBase
    {
    }

    [MetaSerializableDerived(100)]
    public class DefaultMetaOfferPerPlayerState : MetaOfferPerPlayerStateBase
    {
    }
}
