// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Profiling
{
    public struct ProfilerScope : IDisposable
    {
        ProfilerScope(string name)
        {
            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample(name);
            #endif
        }

        void IDisposable.Dispose()
        {
            #if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
            #endif
        }

        public static ProfilerScope Create(string name) => new ProfilerScope(name);
    }
}
