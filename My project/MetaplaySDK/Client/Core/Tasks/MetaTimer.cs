// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL" regarding Timer usage. MetaTimer is the assumed-safe wrapper.

namespace Metaplay.Core.Tasks
{
    /// <summary>
    /// A manager interface for <see cref="MetaTimer"/>s.
    /// One of these need to be registered when the timers are not using <see cref="System.Threading.Timer"/>s internally.
    /// Register a manager using <see cref="MetaTimer.RegisterManager"/> and call
    /// <see cref="MetaTimer.RemoveManager"/> if the manager becomes unavailable.
    /// </summary>
    public interface IMetaTimerManager
    {
        int RegisterTimer(MetaTimer timer);
        void SetTimerParams(int timerId, int dueTimeMs, int periodMs, Action callback);
        void RemoveTimer(int timerId);
    }

    /// <summary>
    /// A replacement for <see cref="System.Threading.Timer"/> class for WebGL.
    /// Uses <see cref="System.Threading.Timer"/> internally when not in WebGL.
    /// </summary>
    public class MetaTimer : System.IDisposable
    {
        static IMetaTimerManager _manager;

        // This indirection has to be used because WebGLTimerManager
        // is in a different assembly.
        public static void RegisterManager(IMetaTimerManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        #if UNITY_WEBGL && !UNITY_EDITOR

        static readonly TimeSpan _infinite = TimeSpan.FromMilliseconds(-1);

        TimerCallback _callback;
        object        _state;
        TimeSpan      _dueTime;
        TimeSpan      _period;
        bool          _disposed;
        int           _timerId;

        public MetaTimer(System.Threading.TimerCallback callback, object state, System.TimeSpan dueTime, System.TimeSpan period)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _callback      = callback;
            _state         = state;
            _dueTime       = dueTime;
            _period        = period;
            _disposed      = false;

            if (_manager == null)
                throw new InvalidOperationException("No IMetaTimerManager registered yet!");

            _timerId = _manager.RegisterTimer(this);
            _manager.SetTimerParams(_timerId,
                (int)_dueTime.TotalMilliseconds,
                (int)_period.TotalMilliseconds,
                OnTick);
        }

        public MetaTimer(System.Threading.TimerCallback callback)
            : this(callback, null, _infinite, _infinite) {}
        public MetaTimer(System.Threading.TimerCallback callback, object state, int dueTime, int period)
            : this(callback, state, TimeSpan.FromMilliseconds(dueTime), TimeSpan.FromMilliseconds(period)) { }
        public MetaTimer(System.Threading.TimerCallback callback, object state, long dueTime, long period)
            : this(callback, state, TimeSpan.FromMilliseconds(dueTime), TimeSpan.FromMilliseconds(period)) { }

        public MetaTimer(System.Threading.TimerCallback callback, object state, uint dueTime, uint period)
            : this(callback, state, TimeSpan.FromMilliseconds(dueTime), TimeSpan.FromMilliseconds(period)) { }

        void OnTick()
        {
            _callback?.Invoke(_state);
        }

        public bool Change(System.TimeSpan dueTime, System.TimeSpan period)
        {
            if (_disposed)
                return false;

            _dueTime       = dueTime;
            _period        = period;

            _manager.SetTimerParams(_timerId,
                (int)_dueTime.TotalMilliseconds,
                (int)_period.TotalMilliseconds,
                OnTick);

            return true;
        }

        public bool Change(int dueTime, int period)
            => Change(TimeSpan.FromMilliseconds(dueTime), TimeSpan.FromMilliseconds(period));
        public bool Change(uint dueTime, uint period)
            => Change(TimeSpan.FromMilliseconds(dueTime), TimeSpan.FromMilliseconds(period));
        public bool Change(long dueTime, long period)
            => Change(TimeSpan.FromMilliseconds(dueTime), TimeSpan.FromMilliseconds(period));

        public void Dispose()
        {
            if (_disposed)
                return;

            _manager.RemoveTimer(_timerId);
            _disposed = true;
            _timerId  = 0;
        }

        public System.Threading.Tasks.ValueTask DisposeAsync()
        {
            return default;
        }

        #else
        System.Threading.Timer _timer;

        public MetaTimer(System.Threading.TimerCallback callback)
        {
            _timer = new System.Threading.Timer(callback);
        }
        public MetaTimer(System.Threading.TimerCallback callback, object state, int dueTime, int period)
        {
            _timer = new System.Threading.Timer(callback, state, dueTime, period);
        }
        public MetaTimer(System.Threading.TimerCallback callback, object state, long dueTime, long period)
        {
            _timer = new System.Threading.Timer(callback, state, dueTime, period);
        }
        public MetaTimer(System.Threading.TimerCallback callback, object state, System.TimeSpan dueTime, System.TimeSpan period)
        {
            _timer = new System.Threading.Timer(callback, state, dueTime, period);
        }
        public MetaTimer(System.Threading.TimerCallback callback, object state, uint dueTime, uint period)
        {
            _timer = new System.Threading.Timer(callback, state, dueTime, period);
        }
        public bool Change(int dueTime, int period)
        {
            return _timer.Change(dueTime, period);
        }
        public bool Change(long dueTime, long period)
        {
            return _timer.Change(dueTime, period);
        }
        public bool Change(System.TimeSpan dueTime, System.TimeSpan period)
        {
            return _timer.Change(dueTime, period);
        }

        public bool Change(uint dueTime, uint period)
        {
            return _timer.Change(dueTime, period);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
        #endif
    }
}

#pragma warning restore MP_WGL_00
