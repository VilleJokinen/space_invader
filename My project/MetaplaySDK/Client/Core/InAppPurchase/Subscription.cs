// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;
using System;
using static System.FormattableString;

namespace Metaplay.Core.InAppPurchase
{
    /// <summary>
    /// Holds all of a player's IAP subscriptions.
    /// </summary>
    [MetaSerializable]
    public class PlayerSubscriptionsModel
    {
        [MetaMember(1)] public OrderedDictionary<InAppProductId, SubscriptionModel> Subscriptions { get; private set; } = new OrderedDictionary<InAppProductId, SubscriptionModel>();

        /// <summary>
        /// Add or update the state of the subscription instance identified by <paramref name="originalTransactionId"/>.
        /// </summary>
        /// <param name="clearReuseDisablement">
        /// If this is true, then if the subscription instance was previously disabled due to
        /// purchase reuse (i.e. <see cref="SubscriptionInstanceModel.DisabledDueToReuse"/> is true),
        /// then it is made enabled again.
        /// </param>
        public void SetSubscriptionInstanceState(InAppProductId productId, string originalTransactionId, MetaTime stateQueriedAt, SubscriptionInstanceState? state, bool clearReuseDisablement)
        {
            if (!Subscriptions.TryGetValue(productId, out SubscriptionModel subscription))
            {
                subscription = new SubscriptionModel();
                Subscriptions.Add(productId, subscription);
            }

            subscription.SetInstanceState(originalTransactionId, stateQueriedAt, state, clearReuseDisablement: clearReuseDisablement);
        }

        /// <summary>
        /// Add or update the state of a subscription instance as described by <paramref name="queryResult"/>.
        /// </summary>
        /// <param name="clearReuseDisablement">
        /// If this is true, then if the subscription instance was previously disabled due to
        /// purchase reuse (i.e. <see cref="SubscriptionInstanceModel.DisabledDueToReuse"/> is true),
        /// then it is made enabled again.
        /// </param>
        public void SetSubscriptionInstanceState(InAppProductId productId, string originalTransactionId, SubscriptionQueryResult queryResult, bool clearReuseDisablement)
        {
            SetSubscriptionInstanceState(productId, originalTransactionId, queryResult.StateQueriedAt, queryResult.State, clearReuseDisablement: clearReuseDisablement);
        }

        public SubscriptionInstanceModel TryFindSubscriptionInstance(string originalTransactionId)
        {
            foreach (SubscriptionModel subscription in Subscriptions.Values)
            {
                if (subscription.SubscriptionInstances.TryGetValue(originalTransactionId, out SubscriptionInstanceModel instance))
                    return instance;
            }

            return null;
        }

        public InAppProductId TryFindProductId(string originalTransactionId)
        {
            foreach ((InAppProductId productId, SubscriptionModel subscription) in Subscriptions)
            {
                if (subscription.SubscriptionInstances.ContainsKey(originalTransactionId))
                    return productId;
            }
            return null;
        }
    }

    /// <summary>
    /// Represents the player's subscription to a specific subscription product.
    /// </summary>
    [MetaSerializable]
    public class SubscriptionModel
    {
        /// <summary>
        /// All the instances of this subscription. From the game's perspective,
        /// the separate subscription instances are mostly uninteresting, as
        /// the product is mostly seen as a single subscription.
        /// The helpers such as <see cref="GetExpirationTime"/> aggregate the
        /// properties across the instances.
        ///
        /// The id (key) for an instance is the original transaction id of the
        /// subscription purchase.
        /// </summary>
        /// <remarks>
        /// Renewals do not create a new subscription instance, but just update
        /// the state.
        ///
        /// Having multiple subscription instances may be caused by things like
        /// re-purchasing the subscription after cancelling it, or purchasing the
        /// subscription on multiple platforms and/or multiple store accounts.
        /// </remarks>
        [MetaMember(1)] public OrderedDictionary<string, SubscriptionInstanceModel> SubscriptionInstances { get; private set; } = new OrderedDictionary<string, SubscriptionInstanceModel>();

        public MetaTime GetStartTime()
        {
            return GetStartAndExpirationTime().Start;
        }

        /// <summary>
        /// Get the expiration time of this subscription.
        ///
        /// A safety margin is added to the raw expiration time in order
        /// to mitigate the risk of there being a gap in the subscription
        /// when the subscription expires and is renewed while the player
        /// is online. Gaps may occur because there is some delay when the
        /// server checks for renewals.
        /// </summary>
        public MetaTime GetExpirationTime()
        {
            return GetStartAndExpirationTime().Expiration + ExpirationSafetyMargin;
        }

        public MetaDuration ExpirationSafetyMargin => MetaDuration.FromMinutes(2);

        /// <summary>
        /// Get the expiration time of this subscription, without applying
        /// a safety margin as in <see cref="GetExpirationTime"/>.
        /// </summary>
        public MetaTime GetRawExpirationTime()
        {
            return GetStartAndExpirationTime().Expiration;
        }

        public SubscriptionRenewalStatus GetRenewalStatus()
        {
            bool anyUnknownStatus = false;

            // If any instance is expected to renew, the subscription is expected to renew.
            // Otherwise, if any instance's renewal status is unknown, the subscription's status is unknown.
            // Otherwise, the subscription is not expected to renew.
            foreach (SubscriptionInstanceModel instance in SubscriptionInstances.Values)
            {
                if (instance.DisabledDueToReuse)
                    continue;
                if (!instance.LastKnownState.HasValue)
                    continue;
                if (!instance.StateWasAvailableAtLastQuery) // \note If StateWasAvailableAtLastQuery is false, assume the subscription has expired and not expected to renew.
                    continue;

                SubscriptionRenewalStatus instanceStatus = instance.LastKnownState.Value.RenewalStatus;
                if (instanceStatus == SubscriptionRenewalStatus.ExpectedToRenew)
                    return SubscriptionRenewalStatus.ExpectedToRenew;
                else if (instanceStatus == SubscriptionRenewalStatus.Unknown)
                    anyUnknownStatus = true;
            }

            return anyUnknownStatus
                   ? SubscriptionRenewalStatus.Unknown
                   : SubscriptionRenewalStatus.NotExpectedToRenew;
        }

        /// <summary>
        /// Check whether the latest (i.e. greatest expiration time) instance
        /// of this subscription has been disabled due to purchase reuse.
        /// </summary>
        public bool GetLatestInstanceIsDisabledDueToReuse()
        {
            SubscriptionInstanceModel instance = TryGetInstanceWithLatestExpirationTime(includeDisabled: true);
            if (instance == null)
                return false;

            return instance.DisabledDueToReuse;
        }

        SubscriptionInstanceModel TryGetInstanceWithLatestExpirationTime(bool includeDisabled)
        {
            SubscriptionInstanceModel latestInstance = null;

            foreach (SubscriptionInstanceModel instance in SubscriptionInstances.Values)
            {
                if (instance.DisabledDueToReuse && !includeDisabled)
                    continue;
                if (!instance.LastKnownState.HasValue)
                    continue;

                if (latestInstance == null
                    || instance.LastKnownState.Value.ExpirationTime > latestInstance.LastKnownState.Value.ExpirationTime)
                {
                    latestInstance = instance;
                }
            }

            return latestInstance;
        }

        (MetaTime Start, MetaTime Expiration) GetStartAndExpirationTime()
        {
            // \note This gets both start time and expiration time from the instance
            //       with the greatest expiration time, instead of getting the minimum
            //       start time and the maximum expiration time among the instances.
            //       This to avoid getting a very out-of-date start time in case there
            //       remains some very old subscription instance that hasn't actually
            //       been active in a long time.

            SubscriptionInstanceModel instance = TryGetInstanceWithLatestExpirationTime(includeDisabled: false);
            if (instance == null)
                return (Start: MetaTime.Epoch, Expiration: MetaTime.Epoch);

            SubscriptionInstanceState state = instance.LastKnownState.Value;
            return (Start: state.StartTime, Expiration: state.ExpirationTime);
        }

        public void SetInstanceState(string originalTransactionId, MetaTime stateQueriedAt, SubscriptionInstanceState? state, bool clearReuseDisablement)
        {
            if (SubscriptionInstances.TryGetValue(originalTransactionId, out SubscriptionInstanceModel instance))
                instance.UpdateState(stateQueriedAt, state, clearReuseDisablement: clearReuseDisablement);
            else
                SubscriptionInstances.Add(originalTransactionId, new SubscriptionInstanceModel(stateQueriedAt, state));
        }

        // Helper properties for dashboard. The public getters are methods, not properties, to emphasize its possibly nontrivial cost.
        MetaTime StartTime => GetStartTime();
        MetaTime ExpirationTime => GetExpirationTime();
        SubscriptionRenewalStatus RenewalStatus => GetRenewalStatus();
        bool LatestInstanceIsDisabledDueToReuse => GetLatestInstanceIsDisabledDueToReuse();
    }

    /// <summary>
    /// Describes the expected renewal behavior of the subscription,
    /// as reported by the IAP store. This should only be considered
    /// a hint, and no hard decisions should be based on it.
    /// </summary>
    [MetaSerializable]
    public enum SubscriptionRenewalStatus
    {
        /// <summary>
        /// Renewal status not known.
        /// </summary>
        Unknown,
        /// <summary>
        /// Subscription is expected to end at its next expiration, instead of being renewed.
        /// </summary>
        NotExpectedToRenew,
        /// <summary>
        /// Subscription is expected to renew upon its next expiration.
        /// </summary>
        ExpectedToRenew,
    }

    /// <summary>
    /// State of a subscription instance, as well as information about when
    /// its state was queried from the store's servers.
    /// See comment on <see cref="SubscriptionModel.SubscriptionInstances"/>
    /// for a description of subscription instances.
    /// See also <see cref="SubscriptionQueryResult"/>.
    /// </summary>
    [MetaSerializable]
    public class SubscriptionInstanceModel
    {
        /// <summary>
        /// When the state was last queried from the store.
        /// </summary>
        [MetaMember(1)] public MetaTime                     StateQueriedAt                  { get; private set; }
        /// <summary>
        /// Whether the subscription instance's state was available from the store
        /// when it was last queried (at <see cref="StateQueriedAt"/>).
        /// In other words, whether <see cref="LastKnownState"/> is the result
        /// of the query made at <see cref="StateQueriedAt"/>.
        /// </summary>
        [MetaMember(2)] public bool                         StateWasAvailableAtLastQuery    { get; private set; }
        /// <summary>
        /// When <see cref="LastKnownState"/> was queried from the store.
        /// Null if <see cref="LastKnownState"/> is null.
        /// </summary>
        [MetaMember(3)] public MetaTime?                    LastKnownStateQueriedAt         { get; private set; }
        /// <summary>
        /// The state reported by the store in the last query where the state
        /// was available. This is not necessarily the result of the last query
        /// we made, in case <see cref="StateWasAvailableAtLastQuery"/> is false.
        ///
        /// This is null if the state wasn't available even in the initial query.
        /// </summary>
        [MetaMember(4)] public SubscriptionInstanceState?   LastKnownState                  { get; private set; }
        /// <summary>
        /// Whether this subscription instance has been disabled because the
        /// same purchase was more recently reused on another player account.
        /// A disabled subscription instance should be considered inactive.
        /// A disabled subscription instance can be enabled by restoring it
        /// again on this player account.
        /// </summary>
        [MetaMember(5)] public bool                         DisabledDueToReuse              { get; private set; }
        /// <summary>
        /// When purchase reuse was last checked. Used for periodic updating
        /// of <see cref="DisabledDueToReuse"/>.
        /// </summary>
        [MetaMember(6)] public MetaTime                     ReuseCheckedAt                  { get; private set; }

        SubscriptionInstanceModel() { }
        public SubscriptionInstanceModel(MetaTime stateQueriedAt, SubscriptionInstanceState? state)
        {
            StateQueriedAt = stateQueriedAt;
            StateWasAvailableAtLastQuery = state.HasValue;
            LastKnownStateQueriedAt = state.HasValue ? StateQueriedAt : (MetaTime?)null;
            LastKnownState = state;
            DisabledDueToReuse = false;
            ReuseCheckedAt = stateQueriedAt; // \todo Not the ideal value for this but good enough, only used for periodic checks
        }

        public void UpdateState(MetaTime stateQueriedAt, SubscriptionInstanceState? newState, bool clearReuseDisablement)
        {
            StateQueriedAt = stateQueriedAt;
            StateWasAvailableAtLastQuery = newState.HasValue;
            if (newState.HasValue)
            {
                LastKnownStateQueriedAt = StateQueriedAt;
                LastKnownState = newState.Value;
            }

            if (clearReuseDisablement)
            {
                DisabledDueToReuse = false;
                ReuseCheckedAt = stateQueriedAt; // \todo Not the ideal value for this but good enough, only used for periodic checks
            }
        }

        public void SetDisablementDueToReuse(bool disabled, MetaTime checkedAt)
        {
            DisabledDueToReuse = disabled;
            ReuseCheckedAt = checkedAt;
        }
    }

    /// <summary>
    /// The state of a subscription as reported by an IAP store.
    /// </summary>
    [MetaSerializable]
    public struct SubscriptionInstanceState : IEquatable<SubscriptionInstanceState>
    {
        [MetaMember(5)] public bool                         IsAcquiredViaFamilySharing  { get; private set; }
        [MetaMember(1)] public MetaTime                     StartTime                   { get; private set; }
        [MetaMember(2)] public MetaTime                     ExpirationTime              { get; private set; }
        [MetaMember(3)] public SubscriptionRenewalStatus    RenewalStatus               { get; private set; }
        [MetaMember(4)] public int                          NumPeriods                  { get; private set; }

        public SubscriptionInstanceState(bool isAcquiredViaFamilySharing, MetaTime startTime, MetaTime expirationTime, SubscriptionRenewalStatus renewalStatus, int numPeriods)
        {
            IsAcquiredViaFamilySharing = isAcquiredViaFamilySharing;
            StartTime = startTime;
            ExpirationTime = expirationTime;
            RenewalStatus = renewalStatus;
            NumPeriods = numPeriods;
        }

        public override string ToString() => Invariant($"{{ IsAcquiredViaFamilySharing={IsAcquiredViaFamilySharing} StartTime={StartTime} ExpirationTime={ExpirationTime} RenewalStatus={RenewalStatus} NumPeriods={NumPeriods} }}");

        public bool Equals(SubscriptionInstanceState other)
        {
            return IsAcquiredViaFamilySharing == other.IsAcquiredViaFamilySharing
                && StartTime == other.StartTime
                && ExpirationTime == other.ExpirationTime
                && RenewalStatus == other.RenewalStatus
                && NumPeriods == other.NumPeriods;
        }

        public override bool Equals(object obj) => obj is SubscriptionInstanceState other && Equals(other);

        public override int GetHashCode() => Util.CombineHashCode(StartTime.GetHashCode(), ExpirationTime.GetHashCode(), RenewalStatus.GetHashCode());

        public static bool operator ==(SubscriptionInstanceState left, SubscriptionInstanceState right) => left.Equals(right);
        public static bool operator !=(SubscriptionInstanceState left, SubscriptionInstanceState right) => !(left == right);
    }

    [ModelAction(ActionCodesCore.PlayerUpdateSubscriptionInstanceState)]
    public class PlayerUpdateSubscriptionInstanceState : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        // \note Member order is relevant (and differs from constructor parameter order) for persistence compatibility.
        public SubscriptionQueryResult SubscriptionQueryResult { get; private set; }
        // \note Synchronized actions may be persisted. Therefore there's an extra hoop
        //       to deal with the fact that OriginalTransactionId was moved out of
        //       SubscriptionQueryResult and into top-level here. For existing persisted
        //       actions, we will still use the legacy member inside SubscriptionQueryResult,
        //       but for newly-created actions, we'll use this top-level field instead.
        public string OriginalTransactionIdDirect { get; private set; } = null;

        PlayerUpdateSubscriptionInstanceState() { }
        public PlayerUpdateSubscriptionInstanceState(string originalTransactionId, SubscriptionQueryResult subscriptionQueryResult)
        {
            SubscriptionQueryResult = subscriptionQueryResult;
            OriginalTransactionIdDirect = originalTransactionId;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            string originalTransactionId = OriginalTransactionIdDirect ?? SubscriptionQueryResult.LegacyOriginalTransactionId;

            SubscriptionInstanceModel subscriptionInstanceModel = player.IAPSubscriptions.TryFindSubscriptionInstance(originalTransactionId);
            if (subscriptionInstanceModel == null)
                return MetaActionResult.NoSuchSubscription;
            if (SubscriptionQueryResult.StateQueriedAt < subscriptionInstanceModel.StateQueriedAt)
                return MetaActionResult.ExistingSubscriptionStateIsNewer;

            if (commit)
            {
                InAppProductId productId = player.IAPSubscriptions.TryFindProductId(originalTransactionId);
                player.Log.Info("Updating IAP subscription state: product {ProductId}, transaction {OriginalTransactionId}, state {SubscriptionState}", productId, originalTransactionId, SubscriptionQueryResult.State);

                SubscriptionInstanceState? oldState = subscriptionInstanceModel.LastKnownState;
                SubscriptionInstanceState? newState = SubscriptionQueryResult.State;

                subscriptionInstanceModel.UpdateState(
                    SubscriptionQueryResult.StateQueriedAt,
                    newState,
                    clearReuseDisablement: false); // Don't clear disablement state. That is only cleared when a new purchase/restoration of this subscription is done.

                if (oldState != newState)
                {
                    SubscriptionModel subscription = player.IAPSubscriptions.Subscriptions[productId];

                    player.EventStream.Event(new PlayerEventIAPSubscriptionStateUpdated(
                        productId,
                        originalTransactionId,
                        SubscriptionQueryResult.State,
                        overallExpirationTime: subscription.GetExpirationTime(),
                        overallRenewalStatus: subscription.GetRenewalStatus()));
                }
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.PlayerSetSubscriptionInstanceDisablementDueToReuse)]
    public class PlayerSetSubscriptionInstanceDisablementDueToReuse : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public string   OriginalTransactionId   { get; private set; }
        public bool     Disable                 { get; private set; }
        public MetaTime CheckedAt               { get; private set; }

        PlayerSetSubscriptionInstanceDisablementDueToReuse() { }
        public PlayerSetSubscriptionInstanceDisablementDueToReuse(string originalTransactionId, bool disable, MetaTime checkedAt)
        {
            OriginalTransactionId = originalTransactionId;
            Disable = disable;
            CheckedAt = checkedAt;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            SubscriptionInstanceModel subscriptionInstanceModel = player.IAPSubscriptions.TryFindSubscriptionInstance(OriginalTransactionId);
            if (subscriptionInstanceModel == null)
                return MetaActionResult.NoSuchSubscription;

            if (commit)
            {
                InAppProductId productId = player.IAPSubscriptions.TryFindProductId(OriginalTransactionId);
                bool oldDisabled = subscriptionInstanceModel.DisabledDueToReuse;

                if (oldDisabled != Disable)
                    player.Log.Debug("Setting IAP disablement-due-to-reuse: product {ProductId}, transaction {OriginalTransactionId}, disable={Disable}", productId, OriginalTransactionId, Disable);

                subscriptionInstanceModel.SetDisablementDueToReuse(Disable, CheckedAt);

                if (!oldDisabled && Disable)
                    player.EventStream.Event(new PlayerEventIAPSubscriptionDisabledDueToReuse(productId, OriginalTransactionId));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The result of querying a subscription's state from an IAP store.
    /// </summary>
    [MetaSerializable]
    public class SubscriptionQueryResult
    {
        /// <summary>
        /// When the state query was performed.
        /// </summary>
        /// <remarks>
        /// This time is produced by the Metaplay backend, instead of by the store.
        /// Therefore, it should not be assumed to be precisely consistent with the times in
        /// <see cref="State"/>, such as <see cref="SubscriptionInstanceState.ExpirationTime"/>.
        /// </remarks>
        [MetaMember(2)] public MetaTime                     StateQueriedAt          { get; private set; }
        /// <summary>
        /// The state of the subscription reported by the store.
        /// This can be null if the store's response did not contain the subscription.
        /// This can happen if the subscription expired a long time ago and has been
        /// removed from the store's backend.
        /// </summary>
        [MetaMember(3)] public SubscriptionInstanceState?   State                   { get; private set; }

        /// <summary>
        /// Legacy member, do not use.
        /// To access the original transaction id of an <see cref="InAppPurchaseEvent"/>,
        /// use <see cref="InAppPurchaseEvent.OriginalTransactionId"/> instead.
        /// </summary>
        [MetaMember(1)] public string                       LegacyOriginalTransactionId   { get; set; }

        SubscriptionQueryResult() { }
        public SubscriptionQueryResult(MetaTime stateQueriedAt, SubscriptionInstanceState? state)
        {
            StateQueriedAt = stateQueriedAt;
            State = state;
        }
    }
}
