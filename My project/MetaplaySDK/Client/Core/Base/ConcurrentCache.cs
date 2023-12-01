// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    // \todo [petri] add way to remove entries?
    public class ConcurrentCache<TKey, TValue>
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebConcurrentDictionary<TKey, Task<TValue>> _cache = new WebConcurrentDictionary<TKey, Task<TValue>>();
#else
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is non-WebGL.
        ConcurrentDictionary<TKey, Task<TValue>> _cache = new ConcurrentDictionary<TKey, Task<TValue>>();
#pragma warning restore MP_WGL_00
#endif

        public ConcurrentCache()
        {
        }

        public async Task<TValue> GetAsync(TKey key, Func<TKey, Task<TValue>> valueFactory)
        {
            // Get or insert fetch task into cache
            Task<TValue> task = _cache.GetOrAdd(key, valueFactory);
            try
            {
                return await task;
            }
            catch (Exception)
            {
                // If task threw an exception, remove it (so it'll be tried again)
                // \todo [petri] race: use Update() and check that still failing?
                _cache.TryRemove(key, out task);
                throw;
            }
        }
    }
}
