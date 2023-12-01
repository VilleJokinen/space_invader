// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// \todo [nuutti] Make activables not specific to Player?

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.Activables
{
    /// <summary>
    /// Parameters defining the behavior of an activable.
    /// </summary>
    [MetaSerializable]
    public class MetaActivableParams
    {
        /// <summary>
        /// Whether new activations of this activable can be started.
        /// Whether this affects already-ongoing activations (when this
        /// is changed from false to true in a config update) is controlled
        /// by <see cref="AllowActivationAdjustment"/>.
        /// </summary>
        [MetaMember(1)]
        public bool                         IsEnabled                   = true;
        /// <summary>
        /// List of player segments for which this activable is enabled.
        /// If empty or null, then there are no segmentation requirements.
        /// Otherwise, player must belong to at least one of the segments.
        /// </summary>
        [MetaMember(2)]
        public List<MetaRef<PlayerSegmentInfoBase>> Segments            = null;
        /// <summary>
        /// Conditions in addition to the player segments.
        /// All of these additional conditions must be fulfilled.
        /// </summary>
        [MetaMember(3)]
        public List<PlayerCondition>        AdditionalConditions        = null;
        /// <summary>
        /// Specifies how long an activation lasts after it has been started.
        /// </summary>
        [MetaMember(4)]
        public MetaActivableLifetimeSpec    Lifetime                    = MetaActivableLifetimeSpec.Forever.Instance;
        /// <summary>
        /// Specifies whether the activable's conditions (i.e. segmentation,
        /// additional conditions, and schedule) should be checked dynamically
        /// even when an activation is ongoing.
        /// <para>
        /// If IsTransient is false, then once an activation has been
        /// started, the activable will be considered active for as long as
        /// the activation lasts, even if the conditions become false during
        /// the activation.
        /// </para>
        /// <para>
        /// If IsTransient is true, then once an activation has been
        /// started, the activable will be considered active during the
        /// activation when the conditions are also true. E.g. the conditions
        /// going false -> true -> false during an activation means the
        /// activable will go active -> not-active -> active.
        /// </para>
        /// </summary>
        [MetaMember(5)]
        public bool                         IsTransient                 = false;
        /// <summary>
        /// The schedule for the activations.
        /// If null, there are no time restrictions on the activable.
        /// Otherwise, this limits when the activations can be started and,
        /// if <see cref="Lifetime"/> is <see cref="MetaActivableLifetimeSpec.ScheduleBased"/>,
        /// then this also affects how long the activations last.
        /// </summary>
        [MetaMember(6)]
        public MetaScheduleBase             Schedule                    = null;
        /// <summary>
        /// How many times the activable can be activated for a player
        /// until it becomes unavailable forever.
        /// </summary>
        [MetaMember(7)]
        public int?                         MaxActivations              = null;
        /// <summary>
        /// How many times the activable can be consumed by a player
        /// until it becomes unavailable forever.
        /// </summary>
        [MetaMember(8)]
        public int?                         MaxTotalConsumes            = null;
        /// <summary>
        /// How many times the activable can be consumed by a player
        /// during one activation, until ending its current activation.
        /// </summary>
        [MetaMember(9)]
        public int?                         MaxConsumesPerActivation    = null;
        /// <summary>
        /// Duration of cooldown that starts after an activation ends.
        /// While the cooldown is ongoing, a new activation cannot be started.
        /// </summary>
        [MetaMember(10)]
        public MetaActivableCooldownSpec    Cooldown                    = MetaActivableCooldownSpec.Fixed.Zero;
        /// <summary>
        /// If true, the activable adjusts its existing activation-related state
        /// according to changes in its MetaActivableParams when
        /// <see cref="MetaActivableState.TryAdjustActivation"/> is called
        /// (normally, this is called automatically by the SDK if the activable
        /// kind has been registered to <see cref="MetaActivableRepository"/>).
        /// For example: if a config change happens during an ongoing activation,
        /// and that config change decreases the <see cref="Lifetime"/> parameter,
        /// then the activation's end time is adjusted accordingly.
        ///
        /// If false, each activation's lifetime and cooldown state is fixed when
        /// the activation starts, and won't be affected by config changes.
        /// Subsequent activations will use the new config in any case.
        /// </summary>
        [MetaMember(11)]
        public bool                         AllowActivationAdjustment   = true;

        public MetaActivableParams(){ }
        public MetaActivableParams(
            bool                                    isEnabled,
            List<MetaRef<PlayerSegmentInfoBase>>    segments,
            List<PlayerCondition>                   additionalConditions,
            MetaActivableLifetimeSpec               lifetime,
            bool                                    isTransient,
            MetaScheduleBase                        schedule,
            int?                                    maxActivations,
            int?                                    maxTotalConsumes,
            int?                                    maxConsumesPerActivation,
            MetaActivableCooldownSpec               cooldown,
            bool                                    allowActivationAdjustment)
        {
            IsEnabled                   = isEnabled;
            Segments                    = segments;
            AdditionalConditions        = additionalConditions;
            Lifetime                    = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            IsTransient                 = isTransient;
            Schedule                    = schedule;
            MaxActivations              = maxActivations;
            MaxTotalConsumes            = maxTotalConsumes;
            MaxConsumesPerActivation    = maxConsumesPerActivation;
            Cooldown                    = cooldown ?? throw new ArgumentNullException(nameof(cooldown));
            AllowActivationAdjustment   = allowActivationAdjustment;
        }

        public bool ConditionsAreFulfilled(IPlayerModelBase player)
        {
            return ConditionsAreFulfilledAt(player, player.CurrentTime);
        }

        public bool ConditionsAreFulfilledAt(IPlayerModelBase player, MetaTime time)
        {
            if (!IsEnabled)
                return false;

            if (Schedule != null
                && !Schedule.IsEnabledAt(new PlayerLocalTime(time, player.TimeZoneInfo.CurrentUtcOffset)))
                return false;

            if (!PlayerConditionsAreFulfilled(player))
                return false;

            return true;
        }

        public bool PlayerConditionsAreFulfilled(IPlayerModelBase player)
        {
            return SegmentationIsFulfilled(player)
                && AdditionalConditionsAreFulfilled(player);
        }

        bool SegmentationIsFulfilled(IPlayerModelBase player)
        {
            // If segments are defined, fulfilling any of them is sufficient.
            if (Segments == null)
                return true;
            if (Segments.Count == 0)
                return true;
            foreach (MetaRef<PlayerSegmentInfoBase> segInfo in Segments)
            {
                if (segInfo.Ref.MatchesPlayer(player))
                    return true;
            }
            return false;
        }

        bool AdditionalConditionsAreFulfilled(IPlayerModelBase player)
        {
            // If additional conditions are defined, all of them need to be fulfilled.
            if (AdditionalConditions == null)
                return true;
            foreach (PlayerCondition cond in AdditionalConditions)
            {
                if (!cond.MatchesPlayer(player))
                    return false;
            }
            return true;
        }
    }
}
