// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using AOT;
using Metaplay.Core;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Metaplay.Unity
{
    #if UNITY_WEBGL && !UNITY_EDITOR
    public class WebGLTimerManager : IMetaTimerManager
    {
        public static WebGLTimerManager Instance { get; private set; }

        public static void Initialize()
        {
            Instance = new WebGLTimerManager();
            MetaTimer.RegisterManager(Instance);

            WebGlTimerManagerJs_SetJsCallback(CallJsTimerCallback);
        }

        Dictionary<int, (MetaTimer, Action)> _timers = new Dictionary<int, (MetaTimer, Action)>();

        int _nextIdx = 0;

        public int RegisterTimer(MetaTimer timer)
        {
            int registeredIdx = _nextIdx++;
            _timers.Add(registeredIdx, (timer, null));
            return registeredIdx;
        }

        public void SetTimerParams(int timerId, int dueTimeMs, int periodMs, Action callback)
        {
            if (!_timers.TryGetValue(timerId, out (MetaTimer timer, Action timerCallback) value))
            {
                DebugLog.Error($"Trying to set parameters for non-existent timer! {timerId}");
                return;
            }

            value.timerCallback = callback ?? throw new ArgumentNullException(nameof(callback));
            _timers[timerId]    = value;

            WebGlTimerManagerJs_SetJsTimer(timerId, dueTimeMs, periodMs);
        }

        public void RemoveTimer(int timerId)
        {
            if (_timers.Remove(timerId))
                WebGlTimerManagerJs_RemoveJsTimer(timerId);
        }

        [MonoPInvokeCallback(typeof(Action<int>))]
        static void CallJsTimerCallback(int timerId)
        {
            Instance?.JsTimerCallback(timerId);
        }

        [DllImport("__Internal")]
        private static extern void WebGlTimerManagerJs_SetJsCallback(Action<int> callback);

        [DllImport("__Internal")]
        private static extern void WebGlTimerManagerJs_SetJsTimer(int timerId, int dueTimeMs, int periodMs);

        [DllImport("__Internal")]
        private static extern void WebGlTimerManagerJs_RemoveJsTimer(int timerId);

        void JsTimerCallback(int timerId)
        {
            try
            {
                if (_timers.TryGetValue(timerId, out (MetaTimer timer, Action callback) value))
                    value.callback.Invoke();
                else
                    DebugLog.Warning($"Received callback for non-existent timer! {timerId}");
            }
            catch (Exception ex)
            {
                DebugLog.Error(ex.ToString());
            }
        }
    }
    #endif
}
