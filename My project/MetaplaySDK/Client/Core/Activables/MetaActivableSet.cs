// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// \todo [nuutti] Make activables not specific to Player?

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Activables
{
    public interface IMetaActivableSet
    {
        bool CanStartActivation(IMetaActivableInfo info, IPlayerModelBase player);
        void ForceStartActivation(IMetaActivableInfo info, IPlayerModelBase player);
        bool TryStartActivation(IMetaActivableInfo info, IPlayerModelBase player);
        bool IsInPreview(IMetaActivableInfo info, IPlayerModelBase player);
        bool IsActive(IMetaActivableInfo info, IPlayerModelBase player);
        bool IsInReview(IMetaActivableInfo info, IPlayerModelBase player);
        bool TryConsume(IMetaActivableInfo info, IPlayerModelBase player);
        void ForceEndActivation(IMetaActivableInfo info, IPlayerModelBase player);
        int TryAdjustEachActivation(IPlayerModelBase player);
        bool TryFinalize(IMetaActivableInfo info, IPlayerModelBase player);
        bool CanBeFinalized(IMetaActivableInfo info, IPlayerModelBase player);
        MetaActivableState TryGetState(IMetaActivableInfo info);
        bool TryGetVisibleStatus(IMetaActivableInfo info, IPlayerModelBase player, out MetaActivableVisibleStatus visibleStatus);

        void DebugForceSetPhase(IMetaActivableInfo info, IPlayerModelBase player, MetaActivableState.DebugPhase? phase);
        void ClearErroneousActivableStates();
    }

    public interface IMetaActivableSet<TId> : IMetaActivableSet
    {
        MetaActivableState TryGetState(TId id);
        bool IsActive(TId id, IPlayerModelBase player);
        bool CanBeFinalized(TId id, IPlayerModelBase player);
        void TryFinalizeEach(IEnumerable<TId> ids, IPlayerModelBase player);
    }

    public interface IMetaActivableSet<TId, TInfo, TActivableState> : IMetaActivableSet<TId>
        where TActivableState : MetaActivableState
        where TInfo : IMetaActivableInfo<TId>
    {
        new TActivableState TryGetState(TId id);
        IEnumerable<TActivableState> GetActiveStates(IPlayerModelBase player);
    }

    public static class ActivableSetUtil
    {
        public const int ActivableStatesMemberTagId             = 100;
        public const int ErroneousActivableStatesMemberTagId    = 101;
    }

    /// <summary>
    /// Base class for the state of all activables of a kind (for one player).
    /// Holds the states of individual activables, and provides methods for manipulating
    /// them and querying information about the activables.
    /// </summary>
    /// <typeparam name="TId">ID type for the activables in this set. Likely matches a game config id type.</typeparam>
    /// <typeparam name="TInfo">Info type for the activables in this set. Likely matches a game config info type.</typeparam>
    /// <typeparam name="TActivableState">The concrete <see cref="MetaActivableState"/>-derived class representing the state of a single activable.</typeparam>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaActivableSet<TId, TInfo, TActivableState> : IMetaActivableSet<TId, TInfo, TActivableState>
        where TActivableState : MetaActivableState
        where TInfo : IMetaActivableInfo<TId>
    {
        #region Internal state

        /// <summary>
        /// Contains the normal, non-erroneous activable states.
        /// This is what should be used when operating on this set of activables.
        /// </summary>
        /// <remarks>
        /// Temporarily during deserialization, this may contain also
        /// erroneous states. <see cref="OnDeserialized"/> resolves that.
        /// </remarks>
        [MetaMember(ActivableSetUtil.ActivableStatesMemberTagId)] protected OrderedDictionary<TId, TActivableState> _activableStates = new OrderedDictionary<TId, TActivableState>();

        /// <summary>
        /// Contains the erroneous activable states.
        /// In practice, that means activables whose corresponding config items no longer exist.
        /// This should not be used for any actual operations on this set.
        /// This is only retained so that it can be serialized again,
        /// so that those erroneous states are not lost, but can be corrected later,
        /// e.g. in case a config item was mistakenly removed.
        /// </summary>
        /// <remarks>
        /// Temporarily during deserialization, this may contain also
        /// valid states. <see cref="OnDeserialized"/> resolves that.
        /// </remarks>
        [MetaMember(ActivableSetUtil.ErroneousActivableStatesMemberTagId)] protected OrderedDictionary<TId, TActivableState> _erroneousActivableStates = new OrderedDictionary<TId, TActivableState>();

        /// <summary>
        /// On deserialization, ensure that valid states reside in <see cref="_activableStates"/>,
        /// and erroneous states reside in <see cref="_erroneousActivableStates"/>.
        /// Even if a state was valid when it was last serialized, it can be erroneous
        /// at deserialization, if configs were changed to not contain the activable anymore.
        /// Similarly, previously erroneous states can become valid.
        /// </summary>
        [MetaOnDeserialized]
        void OrganizeActivableStatesByValidityOnDeserialization()
        {
            // Move erroneous states from _activableStates to _erroneousActivableStates.
            foreach ((TId id, TActivableState state) in _activableStates)
            {
                if (!state.IsValidState)
                    _erroneousActivableStates.AddOrReplace(id, state); // \note Shouldn't clash, but be safe.
            }
            _activableStates.RemoveWhere(kv => !kv.Value.IsValidState);

            // Move valid states from _erroneousActivableStates to _activableStates.
            foreach ((TId id, TActivableState state) in _erroneousActivableStates)
            {
                if (state.IsValidState)
                    _activableStates.AddIfAbsent(id, state); // \note Shouldn't clash, but be safe. Note AddIfAbsent, different from AddOrReplace for the reverse case above.
            }
            _erroneousActivableStates.RemoveWhere(kv => kv.Value.IsValidState);
        }

        #endregion

        #region Public interface

        /// <summary>
        /// Whether a new activation for the given activable can be started.
        /// </summary>
        public bool CanStartActivation(TInfo info, IPlayerModelBase player)
        {
            if (!CustomCanStartActivation(info, player))
                return false;

            if (_activableStates.TryGetValue(info.ActivableId, out TActivableState existingActivableState))
                return existingActivableState.CanStartActivation(player);
            else
                return info.ActivableParams.ConditionsAreFulfilled(player);
        }

        /// <summary>
        /// Force-start a new activation for the given activable, even if not permitted by <see cref="CanStartActivation"/>.
        /// </summary>
        public void ForceStartActivation(TInfo info, IPlayerModelBase player)
        {
            TActivableState activableState = EnsureHasState(info, player);

            OnJustBeforeStartActivation(info, player);
            activableState.ForceStartActivation(player);
            OnStartedActivation(info, player);
            player.Log.Debug("Started activation for {ActivableId}", info.ActivableId);
        }

        /// <summary>
        /// Start a new activation for the given activable, if permitted by <see cref="CanStartActivation"/>.
        /// </summary>
        public bool TryStartActivation(TInfo info, IPlayerModelBase player)
        {
            if (CanStartActivation(info, player))
            {
                ForceStartActivation(info, player);
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// Helper that does <see cref="TryStartActivation"/> for each of the activables.
        /// </summary>
        public void TryStartActivationForEach(IEnumerable<TInfo> infos, IPlayerModelBase player)
        {
            foreach (TInfo info in infos)
                TryStartActivation(info, player);
        }

        /// <summary>
        /// Helper that does <see cref="TryStartActivation"/> for each of the activables.
        /// </summary>
        public void TryStartActivationForEach<T>(OrderedDictionary<T, TInfo>.ValueCollection infos, IPlayerModelBase player)
        {
            foreach (TInfo info in infos)
                TryStartActivation(info, player);
        }

        /// <summary>
        /// Whether the given activable is in pre-activation preview.
        /// I.e., time is within the preview period of the next start time,
        /// and the activable has neither an ongoing activation nor review.
        /// Additionally, the activable's conditions must be currently fulfilled for the player,
        /// and the activable's state, if any, must not otherwise prohibit activation at the start time.
        /// </summary>
        /// <remarks>
        /// Even if an activable is currently in preview for the player,
        /// that does not necessarily mean it'll become active for the player,
        /// in case the activable's conditions no longer hold for the player
        /// at the time the activable is supposed to start.
        /// In particular, a player might move out of a player segment
        /// during the preview period.
        /// </remarks>
        public bool IsInPreview(TInfo info, IPlayerModelBase player)
        {
            MetaScheduleBase schedule = info.ActivableParams.Schedule;

            if (schedule == null)
                return false;

            MetaScheduleOccasion? occasionMaybe = schedule.TryGetCurrentOrNextEnabledOccasion(player.GetCurrentLocalTime());
            if (!occasionMaybe.HasValue)
                return false;
            MetaScheduleOccasion occasion = occasionMaybe.Value;

            if (!occasion.IsPreviewedAt(player.CurrentTime))
                return false;

            MetaTime nextStartTime = occasion.EnabledRange.Start;

            if (_activableStates.TryGetValue(info.ActivableId, out TActivableState existingActivableState))
            {
                if (existingActivableState.HasOngoingActivation(player.CurrentTime)
                 || existingActivableState.IsInReview(player.CurrentTime))
                    return false;

                if (!existingActivableState.CanStartActivationAt(player, nextStartTime))
                    return false;
            }
            else
            {
                if (!info.ActivableParams.ConditionsAreFulfilledAt(player, nextStartTime))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the given activable is currently active.
        /// I.e., its activation is ongoing, and, if the activable is transient, its conditions are fulfilled.
        /// </summary>
        public bool IsActive(TId id, IPlayerModelBase player)
        {
            return _activableStates.TryGetValue(id, out TActivableState existingActivableState)
                && existingActivableState.IsActive(player);
        }
        /// <inheritdoc cref="IsActive(TId, IPlayerModelBase)"/>
        public bool IsActive(TInfo info, IPlayerModelBase player)
            => IsActive(info.ActivableId, player);

        /// <summary>
        /// Whether the given activable is in post-activation review.
        /// I.e., it had an activation that ended due to expiration, and time is
        /// still within the review period of the end time.
        /// </summary>
        public bool IsInReview(TId id, IPlayerModelBase player)
        {
            return _activableStates.TryGetValue(id, out TActivableState existingActivableState)
                && existingActivableState.IsInReview(player.CurrentTime);
        }
        /// <inheritdoc cref="IsInReview(TId, IPlayerModelBase)"/>
        public bool IsInReview(TInfo info, IPlayerModelBase player)
            => IsInReview(info.ActivableId, player);

        /// <summary>
        /// Consume the given activable, if it's active.
        /// </summary>
        public bool TryConsume(TId id, IPlayerModelBase player)
        {
            if (_activableStates.TryGetValue(id, out TActivableState existingActivableState))
            {
                bool consumed = existingActivableState.TryConsume(player);
                if (consumed)
                    player.Log.Debug("Consumed {ActivableId}", id);
                return consumed;
            }
            else
                return false;
        }
        /// <inheritdoc cref="TryConsume(TId, IPlayerModelBase)"/>
        public bool TryConsume(TInfo info, IPlayerModelBase player)
            => TryConsume(info.ActivableId, player);

        /// <summary>
        /// Force-end the current activation (if any) of the given activable, even if its expiration or consumption limits aren't reached.
        /// </summary>
        public void ForceEndActivation(TId id, IPlayerModelBase player)
        {
            if (_activableStates.TryGetValue(id, out TActivableState existingActivableState))
            {
                existingActivableState.ForceEndActivation(player);
                player.Log.Debug("Force-ended activation of {ActivableId}", id);
            }
        }
        /// <inheritdoc cref="ForceEndActivation(TId, IPlayerModelBase)"/>
        public void ForceEndActivation(TInfo info, IPlayerModelBase player)
            => ForceEndActivation(info.ActivableId, player);

        /// <summary>
        /// Call <see cref="MetaActivableState.TryAdjustActivation"/>
        /// on all existing activable states in this set.
        /// Returns the number of activable states adjusted.
        /// </summary>
        public int TryAdjustEachActivation(IPlayerModelBase player)
        {
            int numAdjusted = 0;

            foreach ((TId id, TActivableState state) in _activableStates)
            {
                bool didAdjust = state.TryAdjustActivation(player);
                if (didAdjust)
                {
                    player.Log.Debug("Adjusted activation of {ActivableId}", id);
                    numAdjusted++;
                }
            }

            return numAdjusted;
        }

        /// <summary>
        /// Finalize the given activable, if it can be finalized.
        /// See <see cref="MetaActivableState.TryFinalize(IPlayerModelBase)"/>.
        /// </summary>
        public bool TryFinalize(TId id, IPlayerModelBase player)
        {
            if (_activableStates.TryGetValue(id, out TActivableState existingActivableState))
            {
                bool finalized = existingActivableState.TryFinalize(player);
                if (finalized)
                {
                    player.Log.Debug("Finalized {ActivableId}", id);
                    OnFinalizedActivation(id, player);
                }
                return finalized;
            }
            else
                return false;
        }
        /// <inheritdoc cref="TryFinalize(TId, IPlayerModelBase)"/>
        public bool TryFinalize(TInfo info, IPlayerModelBase player)
            => TryFinalize(info.ActivableId, player);

        /// <summary>
        /// Helper that does <see cref="TryFinalize"/> for each of the activables.
        /// </summary>
        public void TryFinalizeEach(IEnumerable<TId> ids, IPlayerModelBase player)
        {
            foreach (TId id in ids)
                TryFinalize(id, player);
        }
        /// <inheritdoc cref="TryFinalizeEach(IEnumerable{TId}, IPlayerModelBase)"/>
        public void TryFinalizeEach(IEnumerable<TInfo> infos, IPlayerModelBase player)
        {
            foreach (TInfo info in infos)
                TryFinalize(info.ActivableId, player);
        }
        /// <inheritdoc cref="TryFinalizeEach(IEnumerable{TId}, IPlayerModelBase)"/>
        public void TryFinalizeEach<T>(OrderedDictionary<T, TInfo>.ValueCollection infos, IPlayerModelBase player)
        {
            foreach (TInfo info in infos)
                TryFinalize(info.ActivableId, player);
        }

        /// <summary>
        /// Whether the given activable can be finalized.
        /// See <see cref="MetaActivableState.CanBeFinalized(IPlayerModelBase)"/>.
        /// </summary>
        public bool CanBeFinalized(TId id, IPlayerModelBase player)
        {
            if (_activableStates.TryGetValue(id, out TActivableState existingActivableState))
                return existingActivableState.CanBeFinalized(player);
            else
                return false;
        }
        /// <inheritdoc cref="CanBeFinalized(TId, IPlayerModelBase)"/>
        public bool CanBeFinalized(TInfo info, IPlayerModelBase player)
            => CanBeFinalized(info.ActivableId, player);

        /// <summary>
        /// Get the state of the given activable, if it has any state,
        /// and null otherwise.
        /// </summary>
        public TActivableState TryGetState(TId id)
        {
            _activableStates.TryGetValue(id, out TActivableState activableState);
            return activableState;
        }
        /// <inheritdoc cref="TryGetState(TId)"/>
        public TActivableState TryGetState(TInfo info)
            => TryGetState(info.ActivableId);

        /// <summary>
        /// Get states of all currently-active activables in this set.
        /// </summary>
        public IEnumerable<TActivableState> GetActiveStates(IPlayerModelBase player)
        {
            return _activableStates.Values.Where(activableState => activableState.IsActive(player));
        }

        /// <summary>
        /// Helper for getting the <see cref="MetaActivableVisibleStatus"/>
        /// if the activable is currently in one of the visible phases
        /// (preview, active, or review), or null otherwise.
        /// </summary>
        /// <param name="visibleStatus">Will contain the current visible status, or null the activable isn't in a visible phase.</param>
        /// <returns>Whether a non-null value was assigned to <paramref name="visibleStatus"/>.</returns>
        public bool TryGetVisibleStatus(TInfo info, IPlayerModelBase player, out MetaActivableVisibleStatus visibleStatus)
        {
            // Debug-forced state overrides the normal behavior.
            // Normal behavior can depend on the actual configured schedule of the activable,
            // which can be bypassed in a debug-forced state.
            {
                if (_activableStates.TryGetValue(info.ActivableId, out TActivableState activableState)
                    && activableState.Debug != null)
                {
                    return TryGetDebugVisibleStatus(activableState, player, activableState.Debug.Phase, out visibleStatus);
                }
            }

            // Not in debug state, use normal behavior.

            if (IsActive(info, player))
            {
                TActivableState                 activableState      = _activableStates[info.ActivableId];
                MetaActivableState.Activation   activation          = activableState.LatestActivation.Value;
                PlayerLocalTime                 startTime           = activation.LocalStartedAt;
                MetaTime?                       endingSoonStartsAt  = activableState.GetActivationEndingSoonStartsAtTime(player);

                if (endingSoonStartsAt.HasValue && player.CurrentTime >= endingSoonStartsAt.Value)
                {
                    visibleStatus = new MetaActivableVisibleStatus.EndingSoon(
                        isDebugStatus:          false,
                        activationStartedAt:    activation.StartedAt,
                        endingSoonStartedAt:    endingSoonStartsAt.Value,
                        activationEndsAt:       activation.EndAt,
                        scheduleEnabledRange:   info.ActivableParams.Schedule?.TryGetCurrentOrNextEnabledOccasion(startTime)?.EnabledRange);
                    return true;
                }
                else
                {
                    visibleStatus = new MetaActivableVisibleStatus.Active(
                        isDebugStatus:          false,
                        activationStartedAt:    activation.StartedAt,
                        endingSoonStartsAt:     endingSoonStartsAt,
                        activationEndsAt:       activation.EndAt,
                        scheduleEnabledRange:   info.ActivableParams.Schedule?.TryGetCurrentOrNextEnabledOccasion(startTime)?.EnabledRange);
                    return true;
                }
            }
            else if (IsInPreview(info, player))
            {
                visibleStatus = new MetaActivableVisibleStatus.InPreview(
                    isDebugStatus:          false,
                    scheduleEnabledRange:   info.ActivableParams.Schedule.TryGetCurrentOrNextEnabledOccasion(player.GetCurrentLocalTime()).Value.EnabledRange);
                return true;
            }
            else if (IsInReview(info, player))
            {
                TActivableState                 activableState  = _activableStates[info.ActivableId];
                MetaActivableState.Activation   activation      = activableState.LatestActivation.Value;
                PlayerLocalTime                 startTime       = activation.LocalStartedAt;

                visibleStatus = new MetaActivableVisibleStatus.InReview(
                    isDebugStatus:          false,
                    activationStartedAt:    activation.StartedAt,
                    activationEndedAt:      activation.EndAt.Value,
                    visibilityEndsAt:       activableState.GetActivationVisibilityEndsAtTime(player),
                    scheduleEnabledRange:   info.ActivableParams.Schedule?.TryGetCurrentOrNextEnabledOccasion(startTime)?.EnabledRange);
                return true;
            }
            else if (CanStartActivation(info, player))
            {
                visibleStatus = new MetaActivableVisibleStatus.Tentative(
                    isDebugStatus:          false,
                    scheduleEnabledRange:   info.ActivableParams.Schedule?.TryGetCurrentOrNextEnabledOccasion(player.GetCurrentLocalTime()).Value.EnabledRange);
                return true;
            }
            else
            {
                visibleStatus = default;
                return false;
            }
        }

        bool TryGetDebugVisibleStatus(TActivableState activableState, IPlayerModelBase player, MetaActivableState.DebugPhase phase, out MetaActivableVisibleStatus visibleStatus)
        {
            switch (phase)
            {
                case MetaActivableState.DebugPhase.Preview:
                    visibleStatus = new MetaActivableVisibleStatus.InPreview(
                        isDebugStatus:          true,
                        // \note The debug-forced preview state might not correspond to any real
                        //       configured schedule, so just make up something fake here.
                        scheduleEnabledRange:   new MetaTimeRange(
                            start:  player.CurrentTime + MetaDuration.FromSeconds(1),
                            end:    player.CurrentTime + MetaDuration.FromSeconds(2)));
                    return true;

                case MetaActivableState.DebugPhase.Active:
                {
                    MetaActivableState.Activation activation = activableState.LatestActivation.Value;
                    visibleStatus = new MetaActivableVisibleStatus.Active(
                        isDebugStatus:          true,
                        activationStartedAt:    activation.StartedAt,
                        endingSoonStartsAt:     null,
                        activationEndsAt:       activation.EndAt,
                        scheduleEnabledRange:   null);
                    return true;
                }

                case MetaActivableState.DebugPhase.EndingSoon:
                {
                    MetaActivableState.Activation activation = activableState.LatestActivation.Value;
                    visibleStatus = new MetaActivableVisibleStatus.EndingSoon(
                        isDebugStatus:          true,
                        activationStartedAt:    activation.StartedAt,
                        endingSoonStartedAt:    activation.StartedAt,
                        activationEndsAt:       activation.EndAt,
                        scheduleEnabledRange:   null);
                    return true;
                }

                case MetaActivableState.DebugPhase.Review:
                {
                    MetaActivableState.Activation activation = activableState.LatestActivation.Value;
                    visibleStatus = new MetaActivableVisibleStatus.InReview(
                        isDebugStatus:          true,
                        activationStartedAt:    activation.StartedAt,
                        activationEndedAt:      activation.EndAt ?? activation.StartedAt,
                        visibilityEndsAt:       null,
                        scheduleEnabledRange:   null);
                    return true;
                }

                case MetaActivableState.DebugPhase.Inactive:
                default:
                    visibleStatus = null;
                    return false;
            }
        }

        #endregion

        #region Development/debugging interface

        /// <summary>
        /// Debug-force or un-force a phase for the given activable.
        /// See <see cref="PlayerDebugForceSetActivablePhase"/>.
        /// </summary>
        public void DebugForceSetPhase(TInfo info, IPlayerModelBase player, MetaActivableState.DebugPhase? phaseMaybe)
        {
            TActivableState activable = EnsureHasState(info, player);

            if (phaseMaybe.HasValue)
            {
                // Set a debug phase.
                // Adjust activable.LatestActivation according to the specific phase,
                // in order to mimic normal behavior in that phase as much as possible.

                MetaActivableState.DebugPhase phase = phaseMaybe.Value;

                player.Log.Debug("Setting debug phase {Phase} for {ActivableId}", phase, info.ActivableId);

                if (phase == MetaActivableState.DebugPhase.Preview)
                {
                    // Preview:
                    // All that's needed for LatestActivation is that it is not currently ongoing.
                    // Force-end current activation, if any.

                    activable.Debug = new MetaActivableState.DebugState(phase);
                    activable.ForceEndActivation(player, skipCooldown: true);
                }
                else if (phase == MetaActivableState.DebugPhase.Active || phase == MetaActivableState.DebugPhase.EndingSoon)
                {
                    // Active (or EndingSoon - the difference is cosmetic, affecting only TryGetVisibleStatus):
                    // We need an ongoing activation.
                    // Start a new activation, unless the current state is also a debug Active (or EndingSoon)
                    // activation, in which case keep using that same activation.

                    bool keepCurrentActivation = activable.LatestActivation.HasValue
                                                 && activable.Debug != null
                                                 && (activable.Debug.Phase != MetaActivableState.DebugPhase.Active
                                                     || activable.Debug.Phase != MetaActivableState.DebugPhase.EndingSoon);

                    if (keepCurrentActivation)
                        activable.Debug = new MetaActivableState.DebugState(phase);
                    else
                    {
                        // End and finalize previous activation, if possible.

                        if (activable.LatestActivation.HasValue)
                        {
                            activable.ForceEndActivation(player, skipCooldown: true);
                            TryFinalize(info.ActivableId, player);
                        }

                        // Start a new (debug-)activation, calling activation callbacks
                        // the same way as normally.

                        OnJustBeforeStartActivation(info, player);
                        activable.Debug = new MetaActivableState.DebugState(phase);
                        activable.ForceStartDebugEndlessActivation(player);
                        OnStartedActivation(info, player);
                    }
                }
                else
                {
                    // Review or Inactive:
                    // We want to end up in a state where the latest activation has ended.
                    // If there was no previous latest activation, start a fake one first;
                    // then, no matter what, end the ongoing activation (if any).

                    if (!activable.LatestActivation.HasValue)
                    {
                        activable.ForceStartDebugEndlessActivation(player);
                        OnStartedActivation(info, player);
                    }

                    activable.Debug = new MetaActivableState.DebugState(phase);

                    // \note If there is no ongoing activation, this won't do anything,
                    //       but that's ok - in that case there's an already-ended activation,
                    //       which is what we want.
                    activable.ForceEndActivation(player, skipCooldown: true);
                }
            }
            else
            {
                player.Log.Debug("Unsetting debug phase for {ActivableId}", info.ActivableId);

                if (activable.Debug != null)
                    activable.ForceEndActivation(player, skipCooldown: true);

                activable.Debug = null;
            }
        }

        #endregion

        #region Misc internals

        protected TActivableState EnsureHasState(TInfo info, IPlayerModelBase player)
        {
            if (!_activableStates.TryGetValue(info.ActivableId, out TActivableState activableState))
            {
                activableState = CreateActivableState(info, player);
                _activableStates.Add(info.ActivableId, activableState);
            }

            return activableState;
        }

        #endregion

        #region IMetaActivableSet

        bool IMetaActivableSet.CanStartActivation(IMetaActivableInfo info, IPlayerModelBase player) => CanStartActivation((TInfo)info, player);
        void IMetaActivableSet.ForceStartActivation(IMetaActivableInfo info, IPlayerModelBase player) => ForceStartActivation((TInfo)info, player);
        bool IMetaActivableSet.TryStartActivation(IMetaActivableInfo info, IPlayerModelBase player) => TryStartActivation((TInfo)info, player);
        bool IMetaActivableSet.IsInPreview(IMetaActivableInfo info, IPlayerModelBase player) => IsInPreview((TInfo)info, player);
        bool IMetaActivableSet.IsActive(IMetaActivableInfo info, IPlayerModelBase player) => IsActive((TInfo)info, player);
        bool IMetaActivableSet.IsInReview(IMetaActivableInfo info, IPlayerModelBase player) => IsInReview((TInfo)info, player);
        bool IMetaActivableSet.TryConsume(IMetaActivableInfo info, IPlayerModelBase player) => TryConsume((TInfo)info, player);
        void IMetaActivableSet.ForceEndActivation(IMetaActivableInfo info, IPlayerModelBase player) => ForceEndActivation((TInfo)info, player);
        bool IMetaActivableSet.TryFinalize(IMetaActivableInfo info, IPlayerModelBase player) => TryFinalize((TInfo)info, player);
        bool IMetaActivableSet.CanBeFinalized(IMetaActivableInfo info, IPlayerModelBase player) => CanBeFinalized((TInfo)info, player);
        MetaActivableState IMetaActivableSet.TryGetState(IMetaActivableInfo info) => TryGetState((TInfo)info);
        bool IMetaActivableSet.TryGetVisibleStatus(IMetaActivableInfo info, IPlayerModelBase player, out MetaActivableVisibleStatus visibleStatus) => TryGetVisibleStatus((TInfo)info, player, out visibleStatus);

        void IMetaActivableSet.DebugForceSetPhase(IMetaActivableInfo info, IPlayerModelBase player, MetaActivableState.DebugPhase? phase) => DebugForceSetPhase((TInfo)info, player, phase);

        MetaActivableState IMetaActivableSet<TId>.TryGetState(TId id)
            => TryGetState(id);

        #endregion

        #region Virtuals to override in subclass

        protected abstract TActivableState CreateActivableState(TInfo info, IPlayerModelBase player);

        /// <summary>
        /// Called to check whether a new activation of the given activable can be started.
        /// The given activable may or may not already have state in the player.
        /// This can be used by subclass to define additional custom activation conditions,
        /// in addition to the normal conditions of activables.
        /// </summary>
        /// <remarks>
        /// For checks that apply only when the activable already has state in the player,
        /// <see cref="MetaActivableState.CustomCanStartActivation"/> may be more appropriate.
        /// </remarks>
        protected virtual bool CustomCanStartActivation(TInfo info, IPlayerModelBase player) => true;

        protected virtual void OnJustBeforeStartActivation(TInfo info, IPlayerModelBase player) { }
        protected virtual void OnStartedActivation(TInfo info, IPlayerModelBase player) { }

        protected virtual void OnFinalizedActivation(TId id, IPlayerModelBase player){ }

        #endregion

        public void ClearErroneousActivableStates()
        {
            _erroneousActivableStates.Clear();
        }
    }

    /// <summary>
    /// Represents information about an activable that is
    /// in a player-facing status (preview, tentative, active, ending soon, or review).
    /// </summary>
    public abstract class MetaActivableVisibleStatus
    {
        public readonly bool IsDebugStatus;

        public MetaActivableVisibleStatus(bool isDebugStatus)
        {
            IsDebugStatus = isDebugStatus;
        }

        /// <summary>
        /// The activable is not active, but its conditions are fulfilled
        /// such that it could be activated.
        /// An activable might be in tentative status for any of various reasons, for example:
        /// the player might not be online to tick the game logic (like when the player is
        /// only being viewed in the dashboard, but isn't currently playing the game),
        /// or the activation attempts are only performed occasionally in the game logic.
        /// </summary>
        public class Tentative : MetaActivableVisibleStatus
        {
            public readonly MetaTimeRange?  ScheduleEnabledRange;

            public Tentative(bool isDebugStatus, MetaTimeRange? scheduleEnabledRange)
                : base(isDebugStatus: isDebugStatus)
            {
                ScheduleEnabledRange    = scheduleEnabledRange;
            }
        }
        /// <summary>
        /// The activable is currently active for the player,
        /// and isn't within the "ending soon" period of the end time.
        /// Note that this is not the only possible status for active
        /// activables; <see cref="EndingSoon"/> is another possible
        /// status for active activables.
        /// </summary>
        public class Active : MetaActivableVisibleStatus
        {
            public readonly MetaTime        ActivationStartedAt;
            public readonly MetaTime?       EndingSoonStartsAt;
            public readonly MetaTime?       ActivationEndsAt;
            public readonly MetaTimeRange?  ScheduleEnabledRange;

            public Active(bool isDebugStatus, MetaTime activationStartedAt, MetaTime? endingSoonStartsAt, MetaTime? activationEndsAt, MetaTimeRange? scheduleEnabledRange)
                : base(isDebugStatus: isDebugStatus)
            {
                ActivationStartedAt     = activationStartedAt;
                EndingSoonStartsAt      = endingSoonStartsAt;
                ActivationEndsAt        = activationEndsAt;
                ScheduleEnabledRange    = scheduleEnabledRange;
            }
        }
        /// <summary>
        /// The activable is currently active for the player,
        /// and is within the "ending soon" period of the end time.
        /// </summary>
        public class EndingSoon : MetaActivableVisibleStatus
        {
            public readonly MetaTime        ActivationStartedAt;
            public readonly MetaTime        EndingSoonStartedAt;
            public readonly MetaTime?       ActivationEndsAt;
            public readonly MetaTimeRange?  ScheduleEnabledRange;

            public EndingSoon(bool isDebugStatus, MetaTime activationStartedAt, MetaTime endingSoonStartedAt, MetaTime? activationEndsAt, MetaTimeRange? scheduleEnabledRange)
                : base(isDebugStatus: isDebugStatus)
            {
                ActivationStartedAt     = activationStartedAt;
                EndingSoonStartedAt     = endingSoonStartedAt;
                ActivationEndsAt        = activationEndsAt;
                ScheduleEnabledRange    = scheduleEnabledRange;
            }
        }
        /// <summary>
        /// The activable is within the preview period of the next activation.
        /// </summary>
        public class InPreview : MetaActivableVisibleStatus
        {
            public readonly MetaTimeRange   ScheduleEnabledRange;

            public InPreview(bool isDebugStatus, MetaTimeRange scheduleEnabledRange)
                : base(isDebugStatus: isDebugStatus)
            {
                ScheduleEnabledRange = scheduleEnabledRange;
            }
        }
        /// <summary>
        /// The activable is within the review period of the previous activation.
        /// </summary>
        public class InReview : MetaActivableVisibleStatus
        {
            public readonly MetaTime        ActivationStartedAt;
            public readonly MetaTime        ActivationEndedAt;
            public readonly MetaTime?       VisibilityEndsAt;
            public readonly MetaTimeRange?  ScheduleEnabledRange;

            public InReview(bool isDebugStatus, MetaTime activationStartedAt, MetaTime activationEndedAt, MetaTime? visibilityEndsAt, MetaTimeRange? scheduleEnabledRange)
                : base(isDebugStatus: isDebugStatus)
            {
                ActivationStartedAt     = activationStartedAt;
                ActivationEndedAt       = activationEndedAt;
                VisibilityEndsAt        = visibilityEndsAt;
                ScheduleEnabledRange    = scheduleEnabledRange;
            }
        }
    }

    /// <summary>
    /// Variant of <see cref="MetaActivableSet{TId, TInfo, TActivableState}"/> which
    /// is serialization-compatible with it, but does not provide any actual functionality.
    /// The purpose of this is to support the migration of the state of obsolete kinds of
    /// activables into new kinds.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class LegacyMetaActivableSet<TId, TActivableState>
    {
        [MetaMember(ActivableSetUtil.ActivableStatesMemberTagId)]           public OrderedDictionary<TId, TActivableState> ActivableStates          { get; set; }
        [MetaMember(ActivableSetUtil.ErroneousActivableStatesMemberTagId)]  public OrderedDictionary<TId, TActivableState> ErroneousActivableStates { get; set; }
    }
}
