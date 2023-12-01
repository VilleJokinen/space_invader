// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Offers
{
    /// <summary>
    /// State for a single offer group.
    /// </summary>
    /// <remarks>
    /// Some offer group functionality is implemented in this class, and
    /// some (that needs also group-independent/per-player offer state)
    /// in <see cref="PlayerMetaOfferGroupsModelBase{TOfferGroupInfo}"/>.
    ///
    /// An offer group is itself an Activable, and furthermore contains
    /// a number of offers, each of which has some per-group state (and
    /// additionally per-player state, but that is stored in
    /// <see cref="PlayerMetaOfferGroupsModelBase{TOfferGroupInfo}"/>).
    /// The contained offers are not Activables, though they have some
    /// similar functionality (such as per-offer targeting and purchase
    /// limits).
    ///
    /// OFfers can be purchased via an active offer group.
    /// However, since offers can have individual conditions, an offer
    /// might be inactive (and thus un-purchasable) even if the containing
    /// group is active. Additionally, an offer might be sold out and thus
    /// un-purchasable.
    /// </remarks>
    [MetaSerializable]
    [MetaReservedMembers(200, 300)]
    public abstract class MetaOfferGroupModelBase : MetaActivableState<MetaOfferGroupId, MetaOfferGroupInfoBase>
    {
        [MetaMember(200)] public sealed override MetaOfferGroupId                           ActivableId { get; protected set; }
        /// <summary>
        /// State for offers included in this group. Note that an offer's state gets added
        /// here only when necessary (such as when an activation starts); this does not
        /// necessarily contain entries for all of the offers configured for this group.
        /// </summary>
        [MetaMember(201)] public OrderedDictionary<MetaOfferId, MetaOfferPerGroupStateBase> OfferStates { get; protected set; } = new OrderedDictionary<MetaOfferId, MetaOfferPerGroupStateBase>();

        /// <summary>
        /// Used to identify the activation instance of the group,
        /// to associate that with the offers' activations.
        /// </summary>
        public int CurrentActivationIndex => NumActivated-1;

        protected MetaOfferGroupModelBase(){ }
        public MetaOfferGroupModelBase(MetaOfferGroupInfoBase info)
            : base(info)
        {
        }

        /// <summary>
        /// Mark each contained offer's activation as ended.
        /// </summary>
        protected override void Finalize(IPlayerModelBase player)
        {
            foreach ((MetaOfferId offerId, MetaOfferPerGroupStateBase offer) in OfferStates)
            {
                if (offer.IsIncludedInGroupActivation(CurrentActivationIndex))
                    offer.FinalizeActivation(player, CurrentActivationIndex, LatestActivation.Value.EndAt.Value);
            }
        }

        public MetaOfferPerGroupStateBase EnsureContainsOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player)
        {
            if (!OfferStates.ContainsKey(offerInfo.OfferId))
                OfferStates.Add(offerInfo.OfferId, CreateOfferState(offerInfo, player));
            return OfferStates[offerInfo.OfferId];
        }

        public int GetOfferNumActivatedInGroup(MetaOfferId offerId)
        {
            if (OfferStates.TryGetValue(offerId, out MetaOfferPerGroupStateBase offer))
                return offer.NumActivatedInGroup;
            else
                return 0;
        }

        public int GetOfferNumPurchasedInGroup(MetaOfferId offerId)
        {
            if (OfferStates.TryGetValue(offerId, out MetaOfferPerGroupStateBase offer))
                return offer.NumPurchasedInGroup;
            else
                return 0;
        }

        public int GetOfferNumPurchasedDuringLatestActivation(MetaOfferId offerId, IPlayerModelBase player)
        {
            if (OfferIsIncludedInLatestActivation(offerId, player))
                return OfferStates[offerId].LatestActivation.Value.NumPurchased;
            else
                return 0;
        }

        /// <summary>
        /// Whether the given offer is currently active in this group.
        /// Requires the offer group to be active, and also the offer itself to be active.
        /// An offer might be inactive if its per-offer conditions are not fulfilled.
        /// </summary>
        public bool OfferIsActive(MetaOfferId offerId, IPlayerModelBase player)
        {
            return IsActive(player)
                && OfferStates.TryGetValue(offerId, out MetaOfferPerGroupStateBase offer)
                && offer.IsActiveInGroupActivation(CurrentActivationIndex);
        }

        /// <summary>
        /// Whether the given offer has been active at any point in the current/latest activation of this group.
        /// </summary>
        public bool OfferIsIncludedInLatestActivation(MetaOfferId offerId, IPlayerModelBase player)
        {
            return LatestActivation.HasValue
                && OfferStates.TryGetValue(offerId, out MetaOfferPerGroupStateBase offer)
                && offer.IsIncludedInGroupActivation(CurrentActivationIndex);
        }

        public abstract MetaOfferPerGroupStateBase CreateOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player);
    }

    /// <summary>
    /// Information about an offer's state (in a specific group), useful for determining what
    /// to show in game UI, and whether the offer can currently be purchased.
    /// </summary>
    /// <remarks>
    /// <see cref="PlayerMetaOfferGroupsModelBase{TOfferGroupInfo}.OfferIsPurchasable(MetaOfferStatus)"/>
    /// can be overridden by the game, and is used by the SDK to decide whether an offer
    /// can purchased. By overriding it, the game can define less or more strict purchasability rules.
    /// </remarks>
    public readonly struct MetaOfferStatus
    {
        public readonly MetaOfferInfoBase   Info;
        public readonly bool                IsActive;
        public readonly bool                IsIncludedInGroupActivation;
        public readonly int                 NumActivatedByPlayer;
        public readonly int                 NumActivatedInGroup;
        public readonly int                 NumPurchasedByPlayer;
        public readonly int                 NumPurchasedInGroup;
        public readonly int                 NumPurchasedDuringActivation;

        public bool AnyPurchaseLimitReached             => PerPlayerPurchaseLimitReached
                                                        || PerGroupPurchaseLimitReached
                                                        || PerActivationPurchaseLimitReached;

        public bool PerPlayerPurchaseLimitReached       => Info.PerPlayerPurchaseLimitReached(NumPurchasedByPlayer);
        public bool PerGroupPurchaseLimitReached        => Info.PerGroupPurchaseLimitReached(NumPurchasedInGroup);
        public bool PerActivationPurchaseLimitReached   => Info.PerActivationPurchaseLimitReached(NumPurchasedDuringActivation);

        public int? PerPlayerPurchasesRemaining     => Info.MaxPurchasesPerPlayer - NumPurchasedByPlayer;
        public int? PerGroupPurchasesRemaining      => Info.MaxPurchasesPerOfferGroup - NumPurchasedInGroup;
        public int? PerActivationPurchasesRemaining => Info.MaxPurchasesPerActivation - NumPurchasedDuringActivation;

        /// <summary>
        /// How many purchases are remaining in the current activation
        /// before *any* purchase limit is reached. This considers the
        /// various remaining purchase counts (per player, group, and activation)
        /// and returns the one that is lowest.
        /// If the offer has no purchase limits at all, null is returned.
        /// </summary>
        public int? PurchasesRemainingInThisActivation
        {
            get
            {
                int? remainingPerPlayer     = Info.MaxPurchasesPerPlayer - NumPurchasedByPlayer;
                int? remainingPerGroup      = Info.MaxPurchasesPerOfferGroup - NumPurchasedInGroup;
                int? remainingPerActivation = Info.MaxPurchasesPerActivation - NumPurchasedDuringActivation;
                return NullableUtil.Min(remainingPerPlayer, remainingPerGroup, remainingPerActivation);
            }
        }

        public MetaOfferStatus(MetaOfferInfoBase info, bool isActive, bool isIncludedInGroupActivation, int numActivatedByPlayer, int numActivatedInGroup, int numPurchasedByPlayer, int numPurchasedInGroup, int numPurchasedDuringActivation)
        {
            Info = info;
            IsActive = isActive;
            IsIncludedInGroupActivation = isIncludedInGroupActivation;
            NumActivatedByPlayer = numActivatedByPlayer;
            NumActivatedInGroup = numActivatedInGroup;
            NumPurchasedByPlayer = numPurchasedByPlayer;
            NumPurchasedInGroup = numPurchasedInGroup;
            NumPurchasedDuringActivation = numPurchasedDuringActivation;
        }
    }

    /// <summary>
    /// State for all of a player's offer groups, as well as offers' group-independent states.
    ///
    /// Note: This is somewhat a misnomer by being called "offer groups model" but also containing
    /// offers' non-group-specific states.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(200, 300)]
    public abstract class PlayerMetaOfferGroupsModelBase<TOfferGroupInfo>
        : MetaActivableSet<MetaOfferGroupId, TOfferGroupInfo, MetaOfferGroupModelBase>
        , IPlayerMetaOfferGroups
        where TOfferGroupInfo : MetaOfferGroupInfoBase
    {
        [MetaMember(200)] public OrderedDictionary<MetaOfferId, MetaOfferPerPlayerStateBase> OfferStates { get; protected set; } = new OrderedDictionary<MetaOfferId, MetaOfferPerPlayerStateBase>();

        public MetaOfferPerPlayerStateBase TryGetOfferPerPlayerState(MetaOfferId offerId)
            => OfferStates.TryGetValue(offerId, out MetaOfferPerPlayerStateBase state) ? state : null;

        /// <summary>
        /// Custom activation conditions for offer groups:
        /// There must be at least one offer in the group that can be purchased.
        /// </summary>
        /// <remarks>
        /// #offer-group-placement-condition
        ///
        /// Conceptually, placement availability (see <see cref="PlacementIsAvailable"/>) should also be
        /// considered a kind of activation condition. However, for performance reasons, it is not checked here.
        /// Determining placement availability potentially checks all the offer groups, and is therefore an O(N)
        /// operation where N is the number of offer groups. It would thus be prone to causing O(N^2) loops when
        /// checking the activability of all the offer groups.
        /// Instead, placement availability is considered at "outer loop" level, where placement availability can
        /// be evaluated just once and then used for all the offer groups. For example, see
        /// <see cref="PlayerModelBase{TPlayerModel, TPlayerStatistics, TPlayerMetaOfferGroups, TPlayerGuildState}.TryActivateMetaOfferGroups"/>.
        ///
        /// One should thus be careful to check the placement condition when activating or checking the activability
        /// of offer groups.
        ///
        /// \todo [nuutti] Ideally, there would be an optimized way of getting all the active offer groups, making
        ///                placement availability checking O(numberOfActiveOfferGroups) instead of O(numberOfAllOfferGroups).
        /// </remarks>
        protected override bool CustomCanStartActivation(TOfferGroupInfo groupInfo, IPlayerModelBase player)
        {
            return HasAnyOffersAvailableForNewActivation(player, groupInfo);
        }

        /// <summary>
        /// Just before a group's activation starts: decide which offers to include
        /// in the activation, and mark them as active.
        /// </summary>
        protected override void OnJustBeforeStartActivation(TOfferGroupInfo groupInfo, IPlayerModelBase player)
        {
            MetaOfferGroupModelBase group                   = TryGetState(groupInfo);
            int                     upcomingActivationIndex = group.CurrentActivationIndex + 1; // \note +1 because this is *just before* the activation starts.

            // \note Whether to include each offer in this activation of the group
            //       is evaluated before actually modifying the state, because
            //       the offers might have interdependencies via their conditions,
            //       and might thus need to observe the state before modifications.
            //       For example an offer might want to be included only if another
            //       offer was not included in the *previous* activation.
            IEnumerable<MetaOfferInfoBase> offersToInclude =
                groupInfo.Offers.MetaRefUnwrap()
                .Where(offerInfo =>
                {
                    bool includeInActivation = !offerInfo.PerPlayerActivationLimitReached(GetOfferNumActivatedByPlayer(offerInfo.OfferId))
                                            && offerInfo.PlayerConditionsAreFulfilled(player);

                    return includeInActivation;
                });

            if (groupInfo.MaxOffersActive.HasValue)
                offersToInclude = offersToInclude.Take(groupInfo.MaxOffersActive.Value);

            // \note Force evaluation of the 'offersToInclude' linq, before the state modifications.
            foreach (MetaOfferInfoBase offerInfo in offersToInclude.ToList())
            {
                MetaOfferPerGroupStateBase offerPerGroup = group.EnsureContainsOfferState(offerInfo, player);
                offerPerGroup.StartActivation(player, offerInfo, upcomingActivationIndex);

                MetaOfferPerPlayerStateBase offerPerPlayer = EnsureContainsOfferState(offerInfo, player);
                offerPerPlayer.OnActivated(player, offerInfo);
            }
        }

        /// <summary>
        /// An offer group was activated; collect activation statistics for each contained offer.
        /// </summary>
        protected override void OnStartedActivation(TOfferGroupInfo groupInfo, IPlayerModelBase player)
        {
            MetaOfferGroupModelBase group = TryGetState(groupInfo);

            foreach (MetaOfferInfoBase offerInfo in groupInfo.Offers.MetaRefUnwrap())
            {
                if (group.OfferIsIncludedInLatestActivation(offerInfo.OfferId, player))
                    player.ServerListenerCore.MetaOfferActivationStarted(groupInfo.GroupId, offerInfo.OfferId);
            }
        }

        /// <summary>
        /// Re-evaluate the conditions of the individual offers in the given offer group,
        /// and activate them if possible. Similarly, deactivate offers whose conditions
        /// are unfulfilled.
        ///
        /// For example, if an offer group contains an offer that targets players at
        /// player level 5 and above, if the offer group is already active when the
        /// player reaches level 5, then that offer will become active.
        /// </summary>
        public virtual void RefreshPerOfferActivations(MetaOfferGroupInfoBase groupInfo, IPlayerModelBase player)
        {
            RefreshPerOfferActivationsImpl(groupInfo, player, commit: true);
        }

        /// <summary>
        /// Check whether <see cref="RefreshPerOfferActivations"/> would have any effect.
        /// </summary>
        public virtual bool HasAnyPerOfferActivationsToRefresh(MetaOfferGroupInfoBase groupInfo, IPlayerModelBase player)
        {
            return RefreshPerOfferActivationsImpl(groupInfo, player, commit: false);
        }

        // Attempt to deactivate an already-active offer if its conditions have become unfulfilled
        // (unless the offer is "sticky").
        bool TryDeactivateOfferDuringOngoingActivation(MetaOfferGroupModelBase group, MetaOfferInfoBase offerInfo, IPlayerModelBase player, bool commit)
        {
            if (offerInfo.IsSticky ||
                !group.OfferIsActive(offerInfo.OfferId, player) ||
                offerInfo.PlayerConditionsAreFulfilled(player))
                return false;


            if (commit)
            {
                MetaOfferPerGroupStateBase offerPerGroup = group.OfferStates[offerInfo.OfferId];
                offerPerGroup.SetInactiveDuringOngoingActivation(group.CurrentActivationIndex);
            }

            return true;
        }

        // Attempt to activate an offer that is currently not activate in the group activation, either due to player conditions not being met
        // or the MaxOffersActivePerPlayer limit.
        bool TryActivateOfferDuringOngoingActivation(MetaOfferGroupModelBase group, MetaOfferInfoBase offerInfo, IPlayerModelBase player, bool commit)
        {
            if (group.OfferIsActive(offerInfo.OfferId, player))
                return false;
            if (!offerInfo.PlayerConditionsAreFulfilled(player))
                return false;

            if (group.OfferIsIncludedInLatestActivation(offerInfo.OfferId, player))
            {
                // Activate an offer that is currently inactive (but is included in the offer group's
                // current activation, i.e. has been active at some point during the activation)
                // if its conditions have become fulfilled.
                if (commit)
                {
                    MetaOfferPerGroupStateBase offerPerGroup = group.OfferStates[offerInfo.OfferId];
                    offerPerGroup.SetActiveDuringOngoingActivation(group.CurrentActivationIndex);
                }
            }
            else if (!offerInfo.PerPlayerActivationLimitReached(GetOfferNumActivatedByPlayer(offerInfo.OfferId)))
            {
                // Start a new activation for an offer that hasn't yet been included in the offer group's
                // current activation if its conditions have become fulfilled.

                if (commit)
                {
                    MetaOfferPerGroupStateBase offerPerGroup = group.EnsureContainsOfferState(offerInfo, player);
                    offerPerGroup.StartActivation(player, offerInfo, group.CurrentActivationIndex);

                    MetaOfferPerPlayerStateBase offerPerPlayer = EnsureContainsOfferState(offerInfo, player);
                    offerPerPlayer.OnActivated(player, offerInfo);

                    player.ServerListenerCore.MetaOfferActivationStarted(group.ActivableId, offerInfo.OfferId);
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Implementation for <see cref="RefreshPerOfferActivations"/> and <see cref="HasAnyOffersAvailableForNewActivation"/>.
        /// If <paramref name="commit"/> is false, this acts as <see cref="HasAnyOffersAvailableForNewActivation"/>; i.e.
        /// just checks whether any state would be modified by <see cref="RefreshPerOfferActivations"/>, but does not actually
        /// modify the state.
        /// </summary>
        /// <returns>
        /// If <paramref name="commit"/> is true: whether any state was modified.
        /// If <paramref name="commit"/> is false: whether any state would be modified if called with commit=true.
        /// </returns>
        protected virtual bool RefreshPerOfferActivationsImpl(MetaOfferGroupInfoBase groupInfo, IPlayerModelBase player, bool commit)
        {
            if (!IsActive(groupInfo.GroupId, player))
                return false;

            MetaOfferGroupModelBase group = TryGetState(groupInfo.GroupId);
            IEnumerable<MetaOfferInfoBase> offers = groupInfo.Offers.MetaRefUnwrap();
            int numOffersActive = offers.Count(x => group.OfferIsActive(x.OfferId, player));

            // First, attempt to deactivate currently active offers to get up-to-date active count for maximum active offer limit.
            bool modified = false;

            if (numOffersActive > 0)
            {
                foreach (MetaOfferInfoBase offer in offers)
                {
                    if (TryDeactivateOfferDuringOngoingActivation(group, offer, player, commit))
                    {
                        modified = true;
                        if (!commit)
                            return true;
                        if (--numOffersActive == 0)
                            break;
                    }
                }
            }

            // Attempt to activate new offers up to the active limit.
            if (!groupInfo.MaxOffersActiveLimitReached(numOffersActive))
            {
                foreach (MetaOfferInfoBase offer in offers)
                {
                    if (TryActivateOfferDuringOngoingActivation(group, offer, player, commit))
                    {
                        modified = true;
                        if (!commit)
                            return true;
                        if (groupInfo.MaxOffersActiveLimitReached(++numOffersActive))
                            break;
                    }
                }
            }

            return modified;
        }

        /// <summary>
        /// Called when an offer from a group has been purchased.
        /// This bumps the offer's purchase counts, and deactivates
        /// the offer group if no more offers remain.
        /// </summary>
        /// <remarks>
        /// This method does not grant the offer's rewards.
        /// Those are granted separately, by the dynamic purchase mechanism
        /// (see <see cref="DynamicPurchaseContent.PurchaseRewards"/>).
        /// </remarks>
        public virtual void OnPurchasedOffer(MetaOfferGroupInfoBase groupInfo, MetaOfferInfoBase offerInfo, IPlayerModelBase player)
        {
            // \note This method is called just after the purchase was made.
            //       Therefore the purchase validity checks here are soft (just log);
            //       the purchase counts are bumped even if the soft-checks fail.
            //       Why the soft-checks might fail in practice is because offers
            //       involve IAP purchases, which are asynchronous. The purchase
            //       validity hard-checks are done before the IAP purchase is started,
            //       but after the IAP has actually been purchased, the offer group
            //       might have expired.

            // Soft checks

            if (!_activableStates.TryGetValue(groupInfo.GroupId, out MetaOfferGroupModelBase offerGroup))
            {
                // \note This shouldn't happen. Offer group state existence is a prerequisite for starting the purchase,
                //       and the state shouldn't be removed after it's been created.
                player.Log.Error("Offer {OfferId} purchased, but {GroupId} has no state", offerInfo.OfferId, groupInfo.GroupId);
            }
            else if (!offerGroup.IsActive(player))
            {
                // This can happen under normal circumstances; most likely because
                // the purchase was started close to (but before) the end of the
                // activation, such that it finishes after the end of the activation.
                // Thus logged as info instead of warning.
                MetaTime?       endedAt     = offerGroup.LatestActivation?.EndAt;
                MetaDuration?   endedAgo    = player.CurrentTime - endedAt;
                player.Log.Info("{OfferId} was purchased but {GroupId} is not active (ended {EndedAgo} ago, at {EndedAt})", offerInfo.OfferId, groupInfo.GroupId, endedAgo?.ToString() ?? "<null>", endedAt?.ToString() ?? "<null>");
            }
            else if (!groupInfo.Offers.Any(o => o.Ref.OfferId == offerInfo.OfferId))
                player.Log.Warning("{OfferId} was purchased but {GroupId} is not configured to contain that offer", offerInfo.OfferId, groupInfo.GroupId);
            else
            {
                MetaOfferStatus offerStatus = GetOfferStatus(player, groupInfo, offerInfo);
                if (!OfferIsPurchasable(offerStatus))
                {
                    player.Log.Warning("{OfferId} in group {GroupId} was purchased but is not currently purchasable (isActive={IsActive}, limitReached={LimitReached})",
                        offerInfo.OfferId, groupInfo.GroupId, offerStatus.IsActive, offerStatus.AnyPurchaseLimitReached);
                }
            }

            // Act: bump purchase counts, and deactivate this offer group if nothing more remains.
            // \note This should not make strict assumptions about the purchase validity. The above checks were just soft checks.

            // Update the offer's per-group state
            if (offerGroup != null)
            {
                MetaOfferPerGroupStateBase offerPerGroup = offerGroup.EnsureContainsOfferState(offerInfo, player);
                offerPerGroup.OnPurchased(player, offerInfo, offerGroup.CurrentActivationIndex);
            }

            // Update the offer's per-player state
            MetaOfferPerPlayerStateBase offerPerPlayer = EnsureContainsOfferState(offerInfo, player);
            offerPerPlayer.OnPurchased(player, offerInfo);

            // Consume the offer group. Note that offer groups themselves
            // do not have consumption limits. This is done just to keep
            // track of the total number of purchases made in the group,
            // and make that number available via the general activables
            // utilities as the "consumption count" of the group.
            if (offerGroup != null)
                offerGroup.ForceConsume(player);

            // If offer group is currently active and no more purchasable offers remain, deactivate the offer group.
            if (IsActive(groupInfo.GroupId, player)
                && !GetOffersInGroup(groupInfo, player).Any(o => OfferIsPurchasable(o)))
            {
                player.Log.Debug("Offer {OfferId} in {GroupId} was purchased; it was the last remaining offer in this activation of the group", offerInfo.OfferId, groupInfo.GroupId);
                ForceEndActivation(groupInfo.GroupId, player);

                // If per-player purchase limit reached, consider deactivating each other offer group that contains the offer.
                if (offerInfo.PerPlayerPurchaseLimitReached(offerPerPlayer.NumPurchasedByPlayer))
                {
                    foreach (MetaOfferGroupInfoBase otherGroupInfo in player.GameConfig.MetaOfferContainingGroups[offerInfo.OfferId])
                    {
                        if (IsActive(otherGroupInfo.GroupId, player)
                         && !GetOffersInGroup(otherGroupInfo, player).Any(o => OfferIsPurchasable(o)))
                        {
                            player.Log.Debug("Per-player purchase limit of {OfferId} reached, deactivating also {OtherGroupId}", offerInfo.OfferId, otherGroupInfo.GroupId);
                            ForceEndActivation(otherGroupInfo.GroupId, player);
                        }
                    }
                }
            }
            else
                player.Log.Debug("Offer {OfferId} in {GroupId} was purchased", offerInfo.OfferId, groupInfo.GroupId);

            player.ServerListenerCore.MetaOfferPurchased(groupInfo.GroupId, offerInfo.OfferId);
        }

        protected override void OnFinalizedActivation(MetaOfferGroupId groupId, IPlayerModelBase player)
        {
            MetaOfferGroupModelBase group = TryGetState(groupId);

            foreach ((MetaOfferId offerId, MetaOfferPerGroupStateBase offerPerGroup) in group.OfferStates)
            {
                if (offerPerGroup.IsIncludedInGroupActivation(group.CurrentActivationIndex))
                {
                    if (OfferStates.TryGetValue(offerId, out MetaOfferPerPlayerStateBase offerPerPlayer))
                        offerPerPlayer.OnActivationFinalized(player, group.LatestActivation.Value.EndAt.Value);
                }
            }
        }

        public MetaOfferPerPlayerStateBase EnsureContainsOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player)
        {
            if (!OfferStates.ContainsKey(offerInfo.OfferId))
                OfferStates.Add(offerInfo.OfferId, CreateOfferState(offerInfo, player));
            return OfferStates[offerInfo.OfferId];
        }

        /// <summary>
        /// Whether the given placement is unoccupied, i.e. there are no offer groups
        /// currently active in that placement.
        /// </summary>
        public virtual bool PlacementIsAvailable(IPlayerModelBase player, OfferPlacementId placement)
        {
            return !GetActiveStates(player).Any(offerGroup => offerGroup.ActivableInfo.Placement == placement);
        }

        /// <summary>
        /// Whether any of the given group's offers are available for a new activation,
        /// i.e. the offer's conditions are fulfilled, and the offer is also not sold out.
        /// This is used to decide whether the offer group can be activated.
        /// </summary>
        protected bool HasAnyOffersAvailableForNewActivation(IPlayerModelBase player, TOfferGroupInfo offerGroupInfo)
        {
            MetaOfferGroupModelBase offerGroupMaybe = TryGetState(offerGroupInfo);
            foreach (MetaOfferInfoBase offerInfo in offerGroupInfo.Offers.MetaRefUnwrap())
            {
                if (OfferIsAvailableForNewActivation(player, offerGroupInfo, offerGroupMaybe, offerInfo))
                    return true;
            }
            return false;
        }

        protected bool OfferIsAvailableForNewActivation(IPlayerModelBase player, TOfferGroupInfo offerGroupInfo, MetaOfferGroupModelBase offerGroupMaybe, MetaOfferInfoBase offerInfo)
        {
            if (offerInfo.PerPlayerActivationLimitReached(GetOfferNumActivatedByPlayer(offerInfo.OfferId)))
                return false;

            if (offerInfo.PerPlayerPurchaseLimitReached(GetOfferNumPurchasedByPlayer(offerInfo.OfferId)))
                return false;

            if (offerInfo.PerGroupPurchaseLimitReached(offerGroupMaybe?.GetOfferNumPurchasedInGroup(offerInfo.OfferId) ?? 0))
                return false;

            if (!offerInfo.PlayerConditionsAreFulfilled(player))
                return false;

            return true;
        }

        public int GetOfferNumActivatedByPlayer(MetaOfferId offerId)
        {
            if (OfferStates.TryGetValue(offerId, out MetaOfferPerPlayerStateBase offer))
                return offer.NumActivatedByPlayer;
            else
                return 0;
        }

        public int GetOfferNumPurchasedByPlayer(MetaOfferId offerId)
        {
            if (OfferStates.TryGetValue(offerId, out MetaOfferPerPlayerStateBase offer))
                return offer.NumPurchasedByPlayer;
            else
                return 0;
        }

        /// <summary>
        /// Get the current <see cref="MetaOfferStatus"/>es of the offers contained in the given group.
        ///
        /// Note that this returns also offers that are currently un-purchasable, such as sold-out offers
        /// and offers whose conditions are not fulfilled. The caller can use properties such as
        /// <see cref="MetaOfferStatus.AnyPurchaseLimitReached"/> and <see cref="MetaOfferStatus.IsActive"/>
        /// to decide how to treat each offer (e.g. how and whether to show sold-out offers in the game UI).
        /// </summary>
        public IEnumerable<MetaOfferStatus> GetOffersInGroup(MetaOfferGroupInfoBase groupInfo, IPlayerModelBase player)
        {
            return groupInfo.Offers.MetaRefUnwrap()
                .Select(offerInfo => GetOfferStatus(player, groupInfo, offerInfo));
        }

        public MetaOfferStatus GetOfferStatus(IPlayerModelBase player, MetaOfferGroupInfoBase offerGroupInfo, MetaOfferInfoBase offerInfo)
        {
            MetaOfferGroupModelBase offerGroupMaybe = TryGetState(offerGroupInfo.ActivableId);

            return new MetaOfferStatus(
                offerInfo,
                isActive:                       offerGroupMaybe?.OfferIsActive(offerInfo.OfferId, player) ?? false,
                isIncludedInGroupActivation:    offerGroupMaybe?.OfferIsIncludedInLatestActivation(offerInfo.OfferId, player) ?? false,
                numActivatedByPlayer:           GetOfferNumActivatedByPlayer(offerInfo.OfferId),
                numActivatedInGroup:            offerGroupMaybe?.GetOfferNumActivatedInGroup(offerInfo.OfferId) ?? 0,
                numPurchasedByPlayer:           GetOfferNumPurchasedByPlayer(offerInfo.OfferId),
                numPurchasedInGroup:            offerGroupMaybe?.GetOfferNumPurchasedInGroup(offerInfo.OfferId) ?? 0,
                numPurchasedDuringActivation:   offerGroupMaybe?.GetOfferNumPurchasedDuringLatestActivation(offerInfo.OfferId, player) ?? 0);
        }

        /// <summary>
        /// Whether the given offer can be purchased. By default, this considers
        /// the offer's purchase limits and its activeness.
        ///
        /// A game-specific subclass can override this to use less or more strict
        /// purchasing rules. For example, a game can choose to allow players to
        /// still purchase an offer even if it expires while the client has its
        /// shop UI open, by keeping additional offer group state and taking that
        /// into account in this method.
        /// </summary>
        public virtual bool OfferIsPurchasable(MetaOfferStatus offerStatus)
        {
            return offerStatus.IsActive && !offerStatus.AnyPurchaseLimitReached;
        }

        MetaOfferGroupModelBase IMetaActivableSet<MetaOfferGroupId, MetaOfferGroupInfoBase, MetaOfferGroupModelBase>.TryGetState(MetaOfferGroupId groupId)
            => TryGetState(groupId);

        IEnumerable<MetaOfferGroupModelBase> IMetaActivableSet<MetaOfferGroupId, MetaOfferGroupInfoBase, MetaOfferGroupModelBase>.GetActiveStates(IPlayerModelBase player)
            => GetActiveStates(player);

        protected abstract MetaOfferPerPlayerStateBase CreateOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player);
    }

    /// <summary>
    /// Helper interface used by the SDK for accessing some offer group functionality without needing to pass the type parameters everywhere.
    /// </summary>
    public interface IPlayerMetaOfferGroups
        : IMetaActivableSet<
            MetaOfferGroupId,
            MetaOfferGroupInfoBase,
            MetaOfferGroupModelBase>
    {
        void RefreshPerOfferActivations(MetaOfferGroupInfoBase groupInfo, IPlayerModelBase player);
        bool HasAnyPerOfferActivationsToRefresh(MetaOfferGroupInfoBase groupInfo, IPlayerModelBase player);
        IEnumerable<MetaOfferStatus> GetOffersInGroup(MetaOfferGroupInfoBase groupInfo, IPlayerModelBase player);
        MetaOfferStatus GetOfferStatus(IPlayerModelBase player, MetaOfferGroupInfoBase offerGroupInfo, MetaOfferInfoBase offerInfo);
        bool OfferIsPurchasable(MetaOfferStatus offerStatus);
        void OnPurchasedOffer(MetaOfferGroupInfoBase groupInfo, MetaOfferInfoBase offerInfo, IPlayerModelBase player);
        bool PlacementIsAvailable(IPlayerModelBase player, OfferPlacementId placement);
        MetaOfferPerPlayerStateBase TryGetOfferPerPlayerState(MetaOfferId offerId);
    }

    [MetaSerializableDerived(100)]
    public class DefaultMetaOfferGroupModel : MetaOfferGroupModelBase
    {
        DefaultMetaOfferGroupModel(){ }
        public DefaultMetaOfferGroupModel(MetaOfferGroupInfoBase info)
            : base(info)
        {
        }

        public override MetaOfferPerGroupStateBase CreateOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player)
        {
            return new DefaultMetaOfferPerGroupState();
        }
    }

    [MetaSerializableDerived(100)]
    [MetaActivableSet("OfferGroup", fallback: true)]
    public class DefaultPlayerMetaOfferGroupsModel : PlayerMetaOfferGroupsModelBase<DefaultMetaOfferGroupInfo>
    {
        protected override MetaOfferGroupModelBase CreateActivableState(DefaultMetaOfferGroupInfo info, IPlayerModelBase player)
        {
            return new DefaultMetaOfferGroupModel(info);
        }

        protected override MetaOfferPerPlayerStateBase CreateOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player)
        {
            return new DefaultMetaOfferPerPlayerState();
        }
    }
}
