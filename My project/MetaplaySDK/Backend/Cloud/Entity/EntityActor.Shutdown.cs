// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity.EntityStatusMessages;
using System;

namespace Metaplay.Cloud.Entity
{
    /// <summary>
    /// State of the auto shutdown logic.
    /// </summary>
    internal struct AutoShutdownState
    {
        public enum Modes
        {
            Never,          //! \note: this must be first (== 0). mode is only set after we transition to Running state. Inspecting state before that should be consistent.
            NoSubscribers,
        }
        public enum WaitType
        {
            InitialWait,
            SubscribersLost,
        }
        public readonly Modes       Mode;
        public readonly TimeSpan    ShutdownDuration;

        public ICancelable          activeTimer;
        public bool                 shouldShutdownOnSuspend;

        public TimeSpan             currentBaseWaitDuration;
        public TimeSpan             currentSmoothingDuration; // random additional duration to "smoothen" out the shutdown time distribution after an instant-like unsub flood.
        public WaitType             currentWaitType;

        internal AutoShutdownState(Modes mode, TimeSpan shutdownDuration)
        {
            Mode = mode;
            ShutdownDuration = shutdownDuration;

            activeTimer = null;
            shouldShutdownOnSuspend = true;
            currentBaseWaitDuration = TimeSpan.Zero;
            currentSmoothingDuration = TimeSpan.Zero;
            currentWaitType = WaitType.InitialWait;
        }

        public TimeSpan CreateSmoothingPeriod()
        {
            // To smoothen out the load if large amounts disconnect at the same time, add a random 50% to the timers.
            Random      rnd                 = new Random();
            TimeSpan    smoothingDuration   = TimeSpan.FromSeconds(ShutdownDuration.TotalSeconds * 0.5 * rnd.NextDouble());
            return smoothingDuration;
        }
    }

    /// <summary>
    /// Specifies whether and in which conditions the entity should be automatically shut down.
    /// See <see cref="ShutdownNever"/> and <see cref="ShutdownAfterSubscribersGone(TimeSpan)"/> for possible values.
    /// </summary>
    public struct AutoShutdownPolicy
    {
        internal AutoShutdownState.Modes    mode;
        internal TimeSpan                   spawnWaitDuration;
        internal TimeSpan                   lingerDuration;

        /// <summary>
        /// Automatic shutdown is never triggered. Entity must manage its own lifecycle.
        /// </summary>
        public static AutoShutdownPolicy ShutdownNever()
        {
            AutoShutdownPolicy policy = new AutoShutdownPolicy();
            policy.mode = AutoShutdownState.Modes.Never;
            return policy;
        }

        /// <summary>
        /// Automatic shutdown may be triggered after the entity has lost its subscribers, and the <paramref name="lingerDuration"/> in this
        /// state has passed. If no subscriber did subscribe, entity will stay alive for 30 seconds.
        /// </summary>
        public static AutoShutdownPolicy ShutdownAfterSubscribersGone(TimeSpan lingerDuration)
        {
            return ShutdownAfterSubscribersGone(lingerDuration, initialWaitDuration: TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Automatic shutdown may be triggered after the entity has lost its subscribers, and the <paramref name="lingerDuration"/> in this
        /// state has passed. If no subscriber did subscribe, entity will stay alive for <paramref name="initialWaitDuration"/>.
        /// </summary>
        public static AutoShutdownPolicy ShutdownAfterSubscribersGone(TimeSpan lingerDuration, TimeSpan initialWaitDuration)
        {
            // Entity spawns without any subscriber. For an entity not immediately die, it must have non-zero timeout.
            if (initialWaitDuration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(initialWaitDuration));

            AutoShutdownPolicy policy = new AutoShutdownPolicy();
            policy.mode = AutoShutdownState.Modes.NoSubscribers;
            policy.spawnWaitDuration = initialWaitDuration;
            policy.lingerDuration = lingerDuration;
            return policy;
        }

        internal AutoShutdownState BuildState()
        {
            AutoShutdownState state  = new AutoShutdownState(mode, lingerDuration);
            return state;
        }
    }

    public partial class EntityActor
    {
        internal class AutoShutdownTimerDeadline { public static readonly AutoShutdownTimerDeadline Instance = new AutoShutdownTimerDeadline(); }

        /// <inheritdoc cref="AutoShutdownPolicy"/>
        protected abstract AutoShutdownPolicy           ShutdownPolicy { get; }
        AutoShutdownState                               _autoShutdown;
        
        void RegisterShutdownHandlers()
        {
            Receive<EntitySuspendEvent>(ReceiveEntitySuspendEvent);
            Receive<AutoShutdownTimerDeadline>(ReceiveAutoShutdownTimerDeadline);
        }

        void InitializeShutdown()
        {
            var shutdownPolicy = ShutdownPolicy;
            _autoShutdown = shutdownPolicy.BuildState();
            switch (_autoShutdown.Mode)
            {
                case AutoShutdownState.Modes.Never:
                {
                    // nada
                    break;
                }

                case AutoShutdownState.Modes.NoSubscribers:
                {
                    // start wait for the first first subscriber
                    TimeSpan    smoothingDuration   = _autoShutdown.CreateSmoothingPeriod();
                    TimeSpan    totalDuration       = shutdownPolicy.spawnWaitDuration + smoothingDuration;

                    _autoShutdown.currentSmoothingDuration = smoothingDuration;
                    _autoShutdown.currentBaseWaitDuration = shutdownPolicy.spawnWaitDuration;
                    _autoShutdown.currentWaitType = AutoShutdownState.WaitType.InitialWait;
                    _autoShutdown.activeTimer = Context.System.Scheduler.ScheduleTellOnceCancelable(totalDuration, _self, AutoShutdownTimerDeadline.Instance, _self);
                    break;
                }
            }
        }

        void ReceiveEntitySuspendEvent(EntitySuspendEvent suspendEvent)
        {
            if (_autoShutdown.shouldShutdownOnSuspend)
            {
                switch (_autoShutdown.currentWaitType)
                {
                    case AutoShutdownState.WaitType.InitialWait:
                        _log.Debug("Entity has been idle since wakeup for ({ShutdownDuration} + smoothing {SmoothingDuration}s). Requesting shutdown.", _autoShutdown.currentBaseWaitDuration, _autoShutdown.currentSmoothingDuration);
                        break;

                    case AutoShutdownState.WaitType.SubscribersLost:
                        _log.Debug("No subscribers for ({ShutdownDuration} + smoothing {SmoothingDuration}s). Requesting shutdown.", _autoShutdown.currentBaseWaitDuration, _autoShutdown.currentSmoothingDuration);
                        break;
                }
                RequestShutdown();
            }
            else
            {
                // shutdown cancelled, wake up
                Tell(_shard, EntityResumeRequest.Instance);
            }
        }

        void ReceiveAutoShutdownTimerDeadline(AutoShutdownTimerDeadline timerDeadline)
        {
            // Suspend entity to flush the rest of the enqueued work. Continues in HandleEntitySuspendEvent.
            _autoShutdown.activeTimer = null;
            _autoShutdown.shouldShutdownOnSuspend = true;
            Tell(_shard, EntitySuspendRequest.Instance);
        }

        void TryScheduleShutdownAfterSubscriberLoss()
        {
            // auto-shutdown logic
            if (_autoShutdown.Mode == AutoShutdownState.Modes.NoSubscribers)
            {
                if (_subscribers.Count == 0)
                {
                    // We lost the last subscriber, start a timer for killing.
                    TimeSpan    smoothingDuration   = _autoShutdown.CreateSmoothingPeriod();
                    TimeSpan    totalDuration       = _autoShutdown.ShutdownDuration + smoothingDuration;
                    //_log.Debug("Initiating shutdown countdown ({ShutdownDuration} + smoothing {SmoothingDuration}s)..", _autoShutdown.ShutdownDuration, smoothingDuration);

                    _autoShutdown.currentSmoothingDuration = smoothingDuration;
                    _autoShutdown.currentBaseWaitDuration = _autoShutdown.ShutdownDuration;
                    _autoShutdown.currentWaitType = AutoShutdownState.WaitType.SubscribersLost;

                    _autoShutdown.activeTimer?.Cancel();
                    _autoShutdown.activeTimer = Context.System.Scheduler.ScheduleTellOnceCancelable(totalDuration, _self, AutoShutdownTimerDeadline.Instance, _self);
                }
            }
        }

        void TryCancelShutdownAfterNewSubscriber()
        {
            // auto-shutdown logic
            if (_autoShutdown.Mode == AutoShutdownState.Modes.NoSubscribers)
            {
                //if (_autoShutdown.activeTimer != null)
                //{
                //    _log.Debug("Cancelled shutdown countdown due to new subscribers.");
                //}

                // cancel shutdown in any case
                _autoShutdown.activeTimer?.Cancel();
                _autoShutdown.activeTimer = null;
                _autoShutdown.shouldShutdownOnSuspend = false;
            }
        }
    }
}
