// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// \todo [nuutti] Make activables not specific to Player?

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using Metaplay.Core.Serialization;
using System;
using System.Runtime.Serialization;

namespace Metaplay.Core.Activables
{
    /// <summary>
    /// Defines the persistent base state for a single activable.
    /// </summary>
    /// <remarks>
    /// This is separate from <see cref="MetaActivableState"/> (which
    /// defines the methods) because in some atypical use cases we
    /// want just the storage and no additional fluff. In practice,
    /// this can happen when a kind of activable is obsoleted, but
    /// we still want to be able to deserialize its state so that we
    /// can migrate it to a new state; in that case it is convenient
    /// to change the obsolete activable model type to inherit from this
    /// class instead of from <see cref="MetaActivableState"/>, so that
    /// it doesn't need to implement unnecessary parts like
    /// <see cref="MetaActivableState.ActivableParams"/>, allowing the
    /// config types for the obsolete activable to be removed.
    /// </remarks>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaActivableStateStorage
    {
        [MetaMember(100)] public int                            NumActivated        { get; protected set; } = 0;
        [MetaMember(101)] public int                            TotalNumConsumed    { get; protected set; } = 0;
        [MetaMember(103)] public int                            NumFinalized        { get; protected set; } = 0;
        /// <summary> If active, this is the current activation; otherwise, this is the last activation if any. </summary>
        [MetaMember(102)] public MetaActivableState.Activation? LatestActivation    { get; protected set; } = null;
        /// <summary> Development/debugging forced state. Normally null, i.e. normal state, no debug-forcing. </summary>
        [MetaMember(104)] public MetaActivableState.DebugState  Debug               { get; set; } = null; // \note Public setter for access from MetaActivableSet
    }

    /// <summary>
    /// Base class for the state of a single activable.
    ///
    /// See <see cref="MetaActivableState{TId, TInfo}"/> for an intermediate helper
    /// class implementing some further commonly helpful functionality.
    /// </summary>
    [MetaSerializable]
    public abstract class MetaActivableState : MetaActivableStateStorage
    {
        [MetaSerializable]
        [MetaBlockedMembers(4)]
        public struct Activation
        {
            /// <summary>
            /// UTC offset used for this activation.
            /// If using a local-time schedule, this is the player's local UTC offset
            /// when the activation started; otherwise, this is zero.
            /// </summary>
            [MetaMember(1)] public MetaDuration UtcOffset;
            /// <summary>
            /// Time when this activation started.
            /// Note that even if the activable has a schedule,
            /// this is not necessarily equal to the start of the
            /// schedule occasion, because (since activations are
            /// started explicitly by game logic code) this activation
            /// might not have been started at exactly that time.
            /// </summary>
            [MetaMember(2)] public MetaTime     StartedAt;
            /// <summary>
            /// Time when this activation did or will end.
            /// Initially set based on lifetime and schedule, then may be updated to
            /// current time upon consumption or force-ending.
            /// </summary>
            [MetaMember(3)] public MetaTime?    EndAt;
            /// <summary>
            /// Whether the activation was ended explicitly (due to consumption
            /// or force-ending), as opposed to expiration.
            /// Initially set to false, then may be updated to true
            /// upon consumption or force-ending.
            /// </summary>
            [MetaMember(8)] public bool         EndedExplicitly;
            /// <summary>
            /// Time when the cooldown of this activation did or will end.
            /// Initially set based on cooldown parameter and schedule, then may be updated
            /// upon consumption.
            /// </summary>
            [MetaMember(6)] public MetaTime?    CooldownEndAt;
            /// <summary>
            /// Number of consumptions during this activation.
            /// </summary>
            [MetaMember(5)] public int          NumConsumed;
            /// <summary>
            /// Whether this activation has been "finalized".
            /// Finalization can be explicitly done after the activation has ended.
            /// The effects of finalization are feature-specific.
            /// It can be used for e.g. cleaning up something from player's state
            /// after the activation has ended.
            /// </summary>
            [MetaMember(7)] public bool         IsFinalized;

            [IgnoreDataMember]
            public PlayerLocalTime LocalStartedAt => new PlayerLocalTime(StartedAt, UtcOffset);

            public Activation(MetaDuration utcOffset, MetaTime startedAt, MetaTime? endAt, bool endedExplicitly, MetaTime? cooldownEndAt, int numConsumed, bool isFinalized)
            {
                UtcOffset       = utcOffset;
                StartedAt       = startedAt;
                EndAt           = endAt;
                EndedExplicitly = endedExplicitly;
                CooldownEndAt   = cooldownEndAt;
                NumConsumed     = numConsumed;
                IsFinalized     = isFinalized;
            }
        }

        [MetaSerializable]
        public class DebugState
        {
            [MetaMember(1)] public DebugPhase Phase { get; private set; }

            DebugState() { }
            public DebugState(DebugPhase phase)
            {
                Phase = phase;
            }
        }

        [MetaSerializable]
        public enum DebugPhase
        {
            Preview = 0,
            Active = 1,
            EndingSoon = 2,
            Review = 3,
            Inactive = 4,
        }

        /// <summary>
        /// Subclass shall provide access to the parameters of this activable.
        /// For example via a game config info reference.
        /// </summary>
        [IgnoreDataMember] public abstract MetaActivableParams ActivableParams { get; }

        /// <summary>
        /// Whether this activable has a valid state such that it can be
        /// used by game code. In practice, this is false for <see cref="MetaActivableState{TId, TInfo}"/>
        /// when the config item for the activable no longer exists.
        /// See implementation of <see cref="MetaActivableSet{TId, TInfo, TActivableState}.OnDeserialized"/>
        /// for the purpose.
        /// </summary>
        public virtual bool IsValidState => true;

        /// <summary>
        /// Whether a new activation can be started.
        /// I.e., the activable's state doesn't prohibit activation,
        /// and the activable's conditions are fulfilled.
        /// </summary>
        public bool CanStartActivation(IPlayerModelBase player)
        {
            return CanStartActivationAt(player, player.CurrentTime);
        }

        /// <summary>
        /// Like <see cref="CanStartActivation"/>, but evaluates schedule, cooldown and
        /// similar time-based criteria at the specified time instead of at player.CurrentTime.
        /// </summary>
        public bool CanStartActivationAt(IPlayerModelBase player, MetaTime time)
        {
            // A debug-forced state shouldn't get disturbed by normal behavior.
            // The debug-forced state should be removed first.
            if (Debug != null)
                return false;

            if (TotalLimitsAreReached()
             || IsInCooldown(time)
             || HasOngoingActivation(time)
             || IsScheduleOffsetBlocked(new PlayerLocalTime(time, player.TimeZoneInfo.CurrentUtcOffset)))
                return false;

            if (!CustomCanStartActivation(player, time))
                return false;

            return ActivableParams.ConditionsAreFulfilledAt(player, time);
        }

        /// <summary>
        /// Start a new activation, if permitted by <see cref="CanStartActivation"/>.
        /// </summary>
        public bool TryStartActivation(IPlayerModelBase player)
        {
            if (!CanStartActivation(player))
                return false;

            ForceStartActivation(player);
            return true;
        }

        /// <summary>
        /// Force-start a new activation, even if not permitted by <see cref="CanStartActivation"/>.
        /// Used by MetaActivable internally, but can also be called from outside
        /// to bypass normal activation behavior of activables.
        /// </summary>
        public void ForceStartActivation(IPlayerModelBase player)
        {
            PlayerLocalTime currentTime = player.GetCurrentLocalTime();

            MetaTime? endAt = ActivableParams.Lifetime.GetExpiresAtTime(currentTime, ActivableParams.Schedule);
            MetaTime? cooldownEndAt;
            if (endAt.HasValue)
                cooldownEndAt = ActivableParams.Cooldown.GetCooldownEndTime(currentTime, endAt.Value, ActivableParams.Schedule);
            else
                cooldownEndAt = null;

            NumActivated++;
            LatestActivation = new Activation(
                utcOffset:          GetUtcOffsetForActivation(currentTime.UtcOffset),
                startedAt:          currentTime.Time,
                endAt:              endAt,
                endedExplicitly:    false,
                cooldownEndAt:      cooldownEndAt,
                numConsumed:        0,
                isFinalized:        false);

            OnStartedActivation(player);

            TryInvokeOnActivationStartedListener(player);
        }

        /// <summary>
        /// Start a new activation which never expires, and ignores the configured schedule.
        /// This is used when debug-forcing an activable into a specific phase.
        /// </summary>
        public void ForceStartDebugEndlessActivation(IPlayerModelBase player)
        {
            NumActivated++;
            LatestActivation = new Activation(
                utcOffset:          GetUtcOffsetForActivation(player.TimeZoneInfo.CurrentUtcOffset),
                startedAt:          player.CurrentTime,
                endAt:              null,
                endedExplicitly:    false,
                cooldownEndAt:      null,
                numConsumed:        0,
                isFinalized:        false);

            OnStartedActivation(player);

            TryInvokeOnActivationStartedListener(player);
        }

        /// <summary>
        /// Whether an activation is ongoing; i.e. it's been started,
        /// and it hasn't yet ended (either due to expiration or consumption limit).
        /// </summary>
        public bool HasOngoingActivation(MetaTime currentTime)
        {
            if (!LatestActivation.HasValue)
                return false;

            MetaTime? activationEndAt = LatestActivation.Value.EndAt;
            return !activationEndAt.HasValue || currentTime < activationEndAt.Value;
        }

        /// <summary>
        /// Whether this activable is currently active.
        /// I.e., its activation is ongoing, and, if the activable is transient, its conditions are fulfilled.
        /// </summary>
        public bool IsActive(IPlayerModelBase player)
        {
            if (!HasOngoingActivation(player.CurrentTime))
                return false;

            if (ActivableParams.IsTransient
                && !ActivableParams.ConditionsAreFulfilled(player))
                return false;

            return true;
        }

        protected virtual bool AllowConsume(IPlayerModelBase player)
        {
            return IsActive(player);
        }

        public virtual MetaTime? GetActivationEndingSoonStartsAtTime(IPlayerModelBase player)
        {
            if (!HasOngoingActivation(player.CurrentTime))
                return null;

            return ActivableParams.Lifetime.GetEndingSoonStartsAtTime(LatestActivation.Value.LocalStartedAt, ActivableParams.Schedule);
        }

        public virtual MetaTime? GetActivationVisibilityEndsAtTime(IPlayerModelBase player)
        {
            if (!LatestActivation.HasValue)
                return null;

            return ActivableParams.Lifetime.GetVisibilityEndsAtTime(LatestActivation.Value.LocalStartedAt, ActivableParams.Schedule);
        }

        /// <summary>
        /// Whether the activable is in post-activation review.
        /// I.e., there was an activation that ended due to expiration, and time is
        /// still within the review period of the end time.
        /// </summary>
        public virtual bool IsInReview(MetaTime currentTime)
        {
            if (HasOngoingActivation(currentTime))
                return false;

            if (!LatestActivation.HasValue)
                return false;

            // Debug-forced phase needs special treatment for review,
            // because review is normally based on the configured schedule,
            // and debug-forcing specifically ignores the configured schedule.
            if (Debug != null)
                return Debug.Phase == DebugPhase.Review;

            if (ActivableParams.Schedule == null)
                return false;

            Activation activation = LatestActivation.Value;

            if (activation.EndedExplicitly)
                return false;

            if (!ActivableParams.Schedule.TryGetCurrentOrNextEnabledOccasion(activation.LocalStartedAt).HasValue) // \note Can happen if schedule config has changed.
                return false;

            MetaTime? visibilityEndAt = ActivableParams.Lifetime.GetVisibilityEndsAtTime(activation.LocalStartedAt, ActivableParams.Schedule);
            return !visibilityEndAt.HasValue || currentTime < visibilityEndAt.Value;
        }

        /// <summary>
        /// Consume this activable, if it's allowed.
        /// If a consumption limit is reached (either total limit, or per-activation limit),
        /// the activation ends and cooldown starts.
        /// </summary>
        public bool TryConsume(IPlayerModelBase player)
        {
            if (!AllowConsume(player))
                return false;

            ForceConsume(player);
            return true;
        }

        /// <summary>
        /// Consume this activable, even if there's no ongoing activation.
        /// If an activation is ongoing and a consumption limit is reached
        /// (either total limit, or per-activation limit), the activation
        /// ends and cooldown starts.
        /// If there is no ongoing activation, this just increments
        /// the total consumption counter.
        /// </summary>
        public void ForceConsume(IPlayerModelBase player)
        {
            // Bump total consumption count no matter what.
            TotalNumConsumed++;

            // If an activation is ongoing, bump also per-activation consumption count,
            // and then check limits.
            if (HasOngoingActivation(player.CurrentTime))
            {
                MutateLatestActivation((ref Activation act) => act.NumConsumed++);

                // If either activation limit (total or per-activation) was reached, deactivate now.

                int? maxTotalConsumes           = ActivableParams.MaxTotalConsumes;
                int? maxConsumesPerActivation   = ActivableParams.MaxConsumesPerActivation;

                bool limitsReached = (maxTotalConsumes.HasValue && TotalNumConsumed >= maxTotalConsumes.Value)
                                  || (maxConsumesPerActivation.HasValue && LatestActivation.Value.NumConsumed >= maxConsumesPerActivation.Value);

                if (limitsReached)
                    ForceEndActivation(player);
            }

            TryInvokeOnConsumedListener(player);
        }

        /// <summary>
        /// Force-end current activation (if any), even if expiration or consumption limits aren't reached.
        /// Used by MetaActivable internally, but can also be called from outside
        /// to bypass normal deactivation behavior of activables.
        /// </summary>
        public void ForceEndActivation(IPlayerModelBase player, bool skipCooldown = false)
        {
            if (!HasOngoingActivation(player.CurrentTime))
                return;

            MutateLatestActivation((ref Activation act) =>
            {
                act.EndAt           = player.CurrentTime;
                act.EndedExplicitly = true;

                if (skipCooldown)
                    act.CooldownEndAt = player.CurrentTime;
                else
                    act.CooldownEndAt = ActivableParams.Cooldown.GetCooldownEndTime(act.LocalStartedAt, player.CurrentTime, ActivableParams.Schedule);
            });
        }

        /// <summary>
        /// If <see cref="MetaActivableParams.AllowActivationAdjustment"/>
        /// in <see cref="ActivableParams"/> is true, this method attempts
        /// to adjust activation-related state according to changes that
        /// have happened in the params. For example, if the activable has
        /// been disabled, any ongoing activation will be terminated; if
        /// lifetime and/or cooldown have been changed, the end time of an
        /// ongoing activation or cooldown will be adjusted accordingly.
        /// </summary>
        /// <returns>
        /// Whether the state was changed in any way.
        /// </returns>
        public bool TryAdjustActivation(IPlayerModelBase player)
        {
            // Debug-forced state is specifically allowed to have an activation that
            // disagrees with the configuration.
            if (Debug != null)
                return false;

            // Only adjust if so configured.
            if (!ActivableParams.AllowActivationAdjustment)
                return false;

            // If there has been no activation, there's nothing to adjust.
            if (!LatestActivation.HasValue)
                return false;

            Activation oldActivation = LatestActivation.Value;

            if (IsInCooldown(player.CurrentTime))
            {
                // In cooldown: only thing that can be adjusted is cooldown end time.
                // Can happen due to cooldown parameter changing.

                MetaTime newCooldownEndAt = ActivableParams.Cooldown.GetCooldownEndTime(oldActivation.LocalStartedAt, oldActivation.EndAt.Value, ActivableParams.Schedule);

                if (oldActivation.CooldownEndAt != newCooldownEndAt)
                {
                    MutateLatestActivation((ref Activation act) =>
                    {
                        act.CooldownEndAt = newCooldownEndAt;
                    });

                    return true;
                }
                else
                    return false;
            }
            else if (HasOngoingActivation(player.CurrentTime))
            {
                // Ongoing activation: various things may cause termination of the activation.

                // Disabling causes termination.
                if (!ActivableParams.IsEnabled)
                {
                    ForceEndActivation(player);
                    return true;
                }

                // If schedule was changed such that it shouldn't have been enabled at the time
                // this activation was started, then this activation is terminated.
                // Cooldown is also skipped.
                if (ActivableParams.Schedule != null
                    && !ActivableParams.Schedule.IsEnabledAt(oldActivation.LocalStartedAt))
                {
                    ForceEndActivation(player, skipCooldown: true);
                    return true;
                }

                // Activation limit being exceeded causes termination.
                // \note Termination only happens if NumActivated is strictly *greater* than maxActivations.
                //       NumActivated includes the current ongoing activation, which is allowed to continue
                //       if it is the last (or before the last) activation allowed according to maxActivations,
                //       but not if it is beyond the last activation allowed.
                int? maxActivations = ActivableParams.MaxActivations;
                if (maxActivations.HasValue && NumActivated > maxActivations.Value)
                {
                    ForceEndActivation(player);
                    return true;
                }

                // Total consumption limit being reached causes termination.
                int? maxTotalConsumes = ActivableParams.MaxTotalConsumes;
                if (maxTotalConsumes.HasValue && TotalNumConsumed >= maxTotalConsumes.Value)
                {
                    ForceEndActivation(player);
                    return true;
                }

                // Per-activation consumption limit being reached causes termination.
                int? maxConsumesPerActivation = ActivableParams.MaxConsumesPerActivation;
                if (maxConsumesPerActivation.HasValue && oldActivation.NumConsumed >= maxConsumesPerActivation.Value)
                {
                    ForceEndActivation(player);
                    return true;
                }

                // Lifetime and cooldown changes do not generally cause termination, but they can
                // change the end time of the current activation as well as the cooldown end time.
                MetaTime? endAt = GetAdjustedActivationEndTime(player);
                MetaTime? cooldownEndAt;
                if (endAt.HasValue)
                    cooldownEndAt = ActivableParams.Cooldown.GetCooldownEndTime(oldActivation.LocalStartedAt, endAt.Value, ActivableParams.Schedule);
                else
                    cooldownEndAt = null;

                if (endAt != oldActivation.EndAt
                 || cooldownEndAt != oldActivation.CooldownEndAt)
                {
                    MutateLatestActivation((ref Activation act) =>
                    {
                        act.EndAt = endAt;
                        act.CooldownEndAt = cooldownEndAt;
                    });
                    return true;
                }
                else
                    return false;
            }
            else
                return false;
        }

        public virtual MetaTime? GetAdjustedActivationEndTime(IPlayerModelBase player)
        {
            return ActivableParams.Lifetime.GetExpiresAtTime(LatestActivation.Value.LocalStartedAt, ActivableParams.Schedule);
        }

        /// <summary>
        /// Finalize the activable, if it can be finalized.
        /// See <see cref="Finalize(IPlayerModelBase)"/>.
        /// </summary>
        public bool TryFinalize(IPlayerModelBase player)
        {
            if (!CanBeFinalized(player))
                return false;

            MutateLatestActivation((ref Activation act) =>
            {
               act.IsFinalized = true;
            });

            Finalize(player);
            NumFinalized++;

            TryInvokeOnFinalizedListener(player);

            return true;
        }

        /// <summary>
        /// Whether the activable can be finalized: i.e., the latest activation
        /// has ended, and hasn't yet been finalized.
        /// </summary>
        public virtual bool CanBeFinalized(IPlayerModelBase player)
        {
            if (HasOngoingActivation(player.CurrentTime))
                return false;

            if (!LatestActivation.HasValue)
                return false;

            return !LatestActivation.Value.IsFinalized;
        }

        /// <summary>
        /// Whether cooldown is ongoing; i.e. there was an activation that
        /// ended, and time is still within cooldown duration of the end of the activation.
        /// </summary>
        public bool IsInCooldown(MetaTime currentTime)
        {
            if (!LatestActivation.HasValue)
                return false;

            MetaTime? activationEndAt   = LatestActivation.Value.EndAt;
            MetaTime? cooldownEndAt     = LatestActivation.Value.CooldownEndAt;

            return activationEndAt.HasValue
                && currentTime >= activationEndAt.Value
                && (!cooldownEndAt.HasValue || currentTime < cooldownEndAt.Value);
        }

        /// <summary>
        /// Whether total activation or consumption limits have been reached.
        /// </summary>
        public bool TotalLimitsAreReached()
        {
            return (ActivableParams.MaxActivations.HasValue && NumActivated >= ActivableParams.MaxActivations.Value)
                || (ActivableParams.MaxTotalConsumes.HasValue && TotalNumConsumed >= ActivableParams.MaxTotalConsumes.Value);
        }

        /// <summary>
        /// Whether activable is schedule-offset-blocked, i.e.,
        /// new activation's local time at the start of the new activation would be earlier than
        /// the last activation's local time at the end of the last activation.
        ///
        /// This is used to guard against re-activating an old schedule period by turning the clock backwards.
        /// </summary>
        public bool IsScheduleOffsetBlocked(PlayerLocalTime currentTime)
        {
            if (!LatestActivation.HasValue)
                return false;

            Activation latestActivation = LatestActivation.Value;

            if (!latestActivation.EndAt.HasValue)
                return false;

            MetaDuration    lastActivationUtcOffset = latestActivation.UtcOffset;
            MetaTime        lastDeactivationAt      = latestActivation.EndAt.Value;
            MetaDuration    newActivationUtcOffset  = GetUtcOffsetForActivation(currentTime.UtcOffset);

            return currentTime.Time + newActivationUtcOffset < lastDeactivationAt + lastActivationUtcOffset;
        }

        MetaDuration GetUtcOffsetForActivation(MetaDuration playerLocalUtcOffset)
        {
            return ActivableParams.Schedule?.TimeMode == MetaScheduleTimeMode.Local
                   ? playerLocalUtcOffset
                   : MetaDuration.Zero;
        }

        protected void MutateLatestActivation(ActionRef<Activation> mutate)
        {
            Activation act = LatestActivation.Value;
            mutate(ref act);
            LatestActivation = act;
        }

        protected delegate void ActionRef<T>(ref T value);

        void TryInvokeOnActivationStartedListener(IPlayerModelBase player)
        {
            MetaActivableKey? activableKey = TryGetActivableKey();
            if (activableKey != null)
                player.ServerListenerCore.ActivableActivationStarted(activableKey.Value);
        }

        void TryInvokeOnConsumedListener(IPlayerModelBase player)
        {
            MetaActivableKey? activableKey = TryGetActivableKey();
            if (activableKey != null)
                player.ServerListenerCore.ActivableConsumed(activableKey.Value);
        }

        void TryInvokeOnFinalizedListener(IPlayerModelBase player)
        {
            MetaActivableKey? activableKey = TryGetActivableKey();
            if (activableKey != null)
                player.ServerListenerCore.ActivableFinalized(activableKey.Value);
        }

        MetaActivableKey? TryGetActivableKey()
        {
            MetaActivableKindId kindId = MetaActivableRepository.Instance.TryGetKindIdForConcreteActivableStateType(GetType());
            if (kindId == null)
                return null;

            object activableId = TryGetActivableId();
            if (activableId == null)
                return null;

            return new MetaActivableKey(kindId, activableId);
        }

        protected abstract object TryGetActivableId();

        /// <summary>
        /// Called to check whether a new activation of this activable can be started.
        /// This can be used by subclass to define additional custom activation conditions,
        /// in addition to the normal conditions of activables.
        /// </summary>
        /// <remarks>
        /// For checks that apply also when the activable doesn't yet have state in the player,
        /// <see cref="MetaActivableSet{TId, TInfo, TActivableState}.CustomCanStartActivation"/>
        /// is likely more appropriate.
        /// </remarks>
        protected virtual bool CustomCanStartActivation(IPlayerModelBase player, MetaTime time) => true;

        /// <summary>
        /// Called when a new activation has been started.
        /// Can be used by subclass to set custom per-activation state.
        /// </summary>
        protected virtual void OnStartedActivation(IPlayerModelBase player){ }

        /// <summary>
        /// May be called to finalize the activable after an activation has ended.
        ///
        /// Note that this is only called if <see cref="TryFinalize(IPlayerModelBase)"/>
        /// is explicitly called for this activable from the game code.
        ///
        /// Can be used by subclass to implement feature-specific finalization.
        /// It can be used for e.g. cleaning up something from player's state
        /// after the activation has ended.
        /// </summary>
        protected virtual void Finalize(IPlayerModelBase player){ }
    }

    /// <summary>
    /// Intermediate helper class for <see cref="MetaActivableState"/>,
    /// for minor static type convenience for activable id.
    /// </summary>
    public abstract class MetaActivableState<TId> : MetaActivableState
    {
        /// <summary>
        /// Subclass shall provide access to the id of this activable.
        /// </summary>
        [IgnoreDataMember] public abstract TId ActivableId { get; }

        protected override object TryGetActivableId() => ActivableId;
    }

    /// <summary>
    /// Intermediate <see cref="IGameConfigData{TKey}"/>-aware helper class
    /// for <see cref="MetaActivableState"/>. The subclass should implement
    /// the <see cref="ActivableId"/> property as a MetaMember.
    ///
    /// This is intended for activables whose <see cref="MetaActivableState.ActivableParams"/>
    /// comes from a game config item.
    ///
    /// This base class implements custom resolving of <see cref="ActivableInfo"/>
    /// (which is itself not a MetaMember), so that deserializing an activable
    /// whose corresponding game config item has been removed does not throw an
    /// error; instead, <see cref="ActivableInfo"/> is simply set to null in that
    /// case. Together with special (de)serialization logic in
    /// <see cref="MetaActivableSet{TId, TInfo, TActivableState}"/>, this allows
    /// safely removing activable config items without breaking existing
    /// persisted states.
    /// </summary>
    public abstract class MetaActivableState<TId, TInfo> : MetaActivableState
        where TInfo : class, IGameConfigData<TId>, IMetaActivableInfo<TId>
    {
        /// <summary>
        /// Id of this activable.
        /// The subclass should implement this with a [MetaMember]
        /// so this gets persisted.
        /// </summary>
        [IgnoreDataMember] public abstract TId  ActivableId     { get; protected set; }
        /// <summary>
        /// The config info of this activable, or null if the info reference
        /// could not be resolved. This is used by the MetaActivable utilities
        /// to access the <see cref="MetaActivableParams"/> of this activable,
        /// and can also be used from custom code.
        /// </summary>
        [IgnoreDataMember] public TInfo         ActivableInfo   { get; protected set; }

        protected override object TryGetActivableId() => ActivableId;
        [IgnoreDataMember] public override MetaActivableParams ActivableParams => ActivableInfo.ActivableParams;
        public override bool IsValidState => ActivableInfo != null && base.IsValidState;

        protected MetaActivableState(){ }
        protected MetaActivableState(TInfo activableInfo)
        {
            ActivableId = activableInfo.ConfigKey;
            ActivableInfo = activableInfo;
        }

        /// <summary>
        /// On-deserialized handler: attempt to resolve <see cref="ActivableInfo"/>
        /// based on <see cref="ActivableId"/>, or leave it null if cannot resolve.
        /// </summary>
        [MetaOnDeserialized]
        void TryResolveInfo(MetaOnDeserializedParams par)
        {
            ActivableInfo = ActivableId != null
                            ? (TInfo)par.Resolver.TryResolveReference(typeof(TInfo), ActivableId)
                            : null;
        }
    }
}
