// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Metaplay.Core.Activables
{
    /// <summary>
    /// Parameters for an "extendable event": an in-game event whose activation can
    /// be optionally extended (i.e. re-activated) for a specific duration after the
    /// event has ended and is still in the review phase.
    /// </summary>
    [MetaSerializable]
    public class ExtendableEventParams
    {
        /// <summary>
        /// How many times an activation can be extended.
        /// </summary>
        [MetaMember(1)] public int MaxExtensionsPerActivation = 0;
        /// <summary>
        /// How long a single extension lasts, starting from the moment the
        /// extension is done.
        /// </summary>
        [MetaMember(2)] public MetaDuration ExtensionDuration = MetaDuration.Zero;
        /// <summary>
        /// How long the review phase is after an extension of an activation ends.
        /// </summary>
        [MetaMember(3)] public MetaDuration ExtensionReviewDuration = MetaDuration.Zero;

        public ExtendableEventParams() { }
        public ExtendableEventParams(
            int maxExtensionsPerActivation,
            MetaDuration extensionDuration,
            MetaDuration extensionReviewDuration)
        {
            MaxExtensionsPerActivation = maxExtensionsPerActivation;
            ExtensionDuration = extensionDuration;
            ExtensionReviewDuration = extensionReviewDuration;
        }
    }

    /// <summary>
    /// Augments <see cref="MetaActivableState{TId, TInfo}"/> with functionality for
    /// "extendable events": events whose activation can be optionally extended while
    /// the event is still in review phase.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(200, 300)]
    public abstract class ExtendableEventState<TId, TInfo> : MetaActivableState<TId, TInfo>
        where TInfo : class, IGameConfigData<TId>, IMetaActivableInfo<TId>
    {
        #region Per-activation state. Reset when a new activation starts.

        /// <summary>
        /// Time when the latest extension (if any) of the latest activation was started.
        /// </summary>
        [MetaMember(200)] public MetaTime?  LastExtensionStartedAt;
        /// <summary>
        /// How many times the latest activation has been extended.
        /// </summary>
        [MetaMember(201)] public int        LatestActivationNumExtended;
        /// <summary>
        /// How many times the latest activation has been soft-finalized.
        /// </summary>
        [MetaMember(202)] public int        NumSoftFinalizedInLatestActivation;

        #endregion

        /// <summary>
        /// Subclass shall provide access to the "extendable event" parameters.
        /// For example via a game config info reference.
        /// </summary>
        [IgnoreDataMember] public abstract ExtendableEventParams ExtendableEventParams { get; }

        protected ExtendableEventState() { }
        protected ExtendableEventState(TInfo info)
            : base(info)
        {
        }

        /// <summary>
        /// When a new activation starts, reset per-activation state.
        /// </summary>
        protected override void OnStartedActivation(IPlayerModelBase player)
        {
            base.OnStartedActivation(player);

            LastExtensionStartedAt = null;
            LatestActivationNumExtended = 0;
            NumSoftFinalizedInLatestActivation = 0;
        }

        /// <summary>
        /// Modifies the basic behavior of activables: if the latest activation
        /// was extended, then there's an "extension review" after the activation.
        /// That review is not schedule-based, but just a fixed duration after the
        /// activation has ended.
        /// If the latest activation hasn't been extended, then the review behavior
        /// is that of basic activables.
        /// </summary>
        public override bool IsInReview(MetaTime currentTime)
        {
            if (LatestActivationNumExtended == 0)
                return base.IsInReview(currentTime);
            else
            {
                if (HasOngoingActivation(currentTime))
                    return false;

                if (!LatestActivation.HasValue)
                    return false;

                Activation activation = LatestActivation.Value;

                MetaTime endedAt = activation.EndAt.Value;
                MetaTime visibilityEndAt = endedAt + ExtendableEventParams.ExtensionReviewDuration;
                return currentTime < visibilityEndAt;
            }
        }

        /// <summary>
        /// "Soft-finalization" happens after an activation has ended but
        /// might still get extended. This calls the virtual <see cref="SoftFinalize"/>
        /// if soft-finalization is possible in the current state.
        /// </summary>
        public bool TrySoftFinalize(IPlayerModelBase player)
        {
            if (!CanBeSoftFinalized(player))
                return false;

            SoftFinalize(player);
            NumSoftFinalizedInLatestActivation++;

            return true;
        }

        /// <summary>
        /// Checks if this event can be soft-finalized. Specifically, the latest activation
        /// must be still extendable, must not have already soft-finalized.
        /// </summary>
        public bool CanBeSoftFinalized(IPlayerModelBase player)
        {
            if (!LatestActivationCanBeExtended(player))
                return false;

            if (NumSoftFinalizedInLatestActivation >= LatestActivationNumExtended + 1)
                return false;

            return true;
        }

        /// <summary>
        /// Checks if the latest activation can be extended: must be in review, and
        /// must not have exceeded the configured extension count.
        /// </summary>
        public bool LatestActivationCanBeExtended(IPlayerModelBase player)
        {
            if (!IsInReview(player.CurrentTime))
                return false;

            if (LatestActivationNumExtended >= ExtendableEventParams.MaxExtensionsPerActivation)
                return false;

            return true;
        }

        /// <summary>
        /// Extend the latest activation, if possible in the current state.
        /// Extending an in-review activation bumps the end time of the
        /// activation, making it active again. The extension lasts for
        /// <see cref="ExtendableEventParams.ExtensionDuration"/> starting
        /// from the current time. After the extended activation ends again,
        /// there's a new review phase which lasts for
        /// <see cref="ExtendableEventParams.ExtensionReviewDuration"/>.
        /// </summary>
        public bool TryExtendLatestActivation(IPlayerModelBase player)
        {
            if (!LatestActivationCanBeExtended(player))
                return false;

            ForceExtendLatestActivation(player);
            return true;
        }

        public void ForceExtendLatestActivation(IPlayerModelBase player)
        {
            if (!LatestActivation.HasValue)
                return;

            LastExtensionStartedAt = player.CurrentTime;
            LatestActivationNumExtended++;

            MutateLatestActivation((ref Activation act) =>
            {
                MetaTime endAt = player.CurrentTime + ExtendableEventParams.ExtensionDuration;
                MetaTime cooldownEndAt = ActivableParams.Cooldown.GetCooldownEndTime(act.LocalStartedAt, endAt, ActivableParams.Schedule);

                act.EndAt = endAt;
                act.CooldownEndAt = cooldownEndAt;
            });

            OnExtendedActivation(player);
        }

        /// <summary>
        /// This is used to re-evaluate the end time of the ongoing activation,
        /// used by <see cref="MetaActivableState.TryAdjustActivation"/> to potentially
        /// adjust the end time when configs have changed.
        /// For extended activations, the end time calculation differs from that of
        /// basic activables.
        /// </summary>
        public override MetaTime? GetAdjustedActivationEndTime(IPlayerModelBase player)
        {
            if (LastExtensionStartedAt.HasValue)
                return LastExtensionStartedAt.Value + ExtendableEventParams.ExtensionDuration;
            else
                return base.GetAdjustedActivationEndTime(player);
        }

        /// <summary>
        /// Modifies the basic behavior of activables: the latest activation cannot
        /// be finalized while it is still extendable. Once the review ends, or the
        /// extension count limit is reached, it can be finalized.
        /// An ended activation can however be "soft-finalized" (see <see cref="TrySoftFinalize"/>)
        /// while it is still extendable.
        /// </summary>
        public override bool CanBeFinalized(IPlayerModelBase player)
        {
            if (LatestActivationCanBeExtended(player))
                return false;

            return base.CanBeFinalized(player);
        }

        /// <summary>
        /// Modifies the basic behavior of activables: an extended activation does
        /// not have the 'ending soon' period.
        /// \todo [nuutti] Implement if needed. A separate 'ending soon' duration
        ///                is probably needed in <see cref="ExtendableEventParams"/>.
        /// </summary>
        public override MetaTime? GetActivationEndingSoonStartsAtTime(IPlayerModelBase player)
        {
            if (LastExtensionStartedAt.HasValue)
                return null;
            else
                return base.GetActivationEndingSoonStartsAtTime(player);
        }

        /// <summary>
        /// Modifies the basic behavior of activables: an extended activation's
        /// review phase is calculated differently.
        /// </summary>
        public override MetaTime? GetActivationVisibilityEndsAtTime(IPlayerModelBase player)
        {
            if (LatestActivation.HasValue && LastExtensionStartedAt.HasValue)
                return LatestActivation.Value.EndAt + ExtendableEventParams.ExtensionReviewDuration;
            else
                return base.GetActivationVisibilityEndsAtTime(player);
        }

        /// <summary>
        /// Similar to <see cref="MetaActivableState.OnStartedActivation"/>,
        /// but instead of being called when a new activation starts, is called
        /// when the previous activation gets extended.
        /// The event was in review phase, but is now active again.
        /// </summary>
        protected virtual void OnExtendedActivation(IPlayerModelBase player){ }

        /// <summary>
        /// Similar to <see cref="MetaActivableState.Finalize"/>, but is called
        /// upon soft-finalization instead of the basic finalization.
        ///
        /// Note that this is only called if <see cref="TrySoftFinalize(IPlayerModelBase)"/>
        /// is explicitly called for this activable from the game code,
        /// for example via <see cref="ExtendableEventSet{TId, TInfo, TEventState}.TrySoftFinalizeEach"/>
        /// </summary>
        protected virtual void SoftFinalize(IPlayerModelBase player) { }
    }

    /// <summary>
    /// Augments <see cref="MetaActivableSet{TId, TInfo, TActivableState}"/> with functionality for
    /// "extendable events". See <see cref="ExtendableEventState{TId, TInfo}"/>.
    /// </summary>
    [MetaReservedMembers(200, 300)]
    public abstract class ExtendableEventSet<TId, TInfo, TEventState> : MetaActivableSet<TId, TInfo, TEventState>
        where TEventState : ExtendableEventState<TId, TInfo>
        where TInfo : class, IGameConfigData<TId>, IMetaActivableInfo<TId>
    {
        public bool TryExtendLatestActivation(TId id, IPlayerModelBase player)
        {
            if (_activableStates.TryGetValue(id, out TEventState existingEventState))
            {
                bool extended = existingEventState.TryExtendLatestActivation(player);
                if (extended)
                    player.Log.Debug("Extended latest activation for {ActivableId}", id);
                return extended;
            }
            else
                return false;
        }

        public bool CanExtendLatestActivation(TId id, IPlayerModelBase player)
        {
            if (_activableStates.TryGetValue(id, out TEventState existingEventState))
                return existingEventState.LatestActivationCanBeExtended(player);
            else
                return false;
        }

        public bool TrySoftFinalize(TId id, IPlayerModelBase player)
        {
            if (_activableStates.TryGetValue(id, out TEventState existingEventState))
            {
                bool softFinalized = existingEventState.TrySoftFinalize(player);
                if (softFinalized)
                    player.Log.Debug("Soft-finalized {ActivableId}", id);
                return softFinalized;
            }
            else
                return false;
        }

        public void TrySoftFinalizeEach(IEnumerable<TId> ids, IPlayerModelBase player)
        {
            foreach (TId id in ids)
                TrySoftFinalize(id, player);
        }

        public void TrySoftFinalizeEach(IEnumerable<TInfo> infos, IPlayerModelBase player)
        {
            foreach (TInfo info in infos)
                TrySoftFinalize(info.ActivableId, player);
        }
    }
}
