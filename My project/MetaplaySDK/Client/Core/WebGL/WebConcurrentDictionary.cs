// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL

using System.Collections.Generic;

// Inject into same namespace as ConcurrentDictionary<> to reduce noise in code using the class.
namespace System.Collections.Concurrent
{
    /// <summary>
    /// Sham implementation of <see cref="ConcurrentDictionary{TKey, TValue}"/> that can be used for WebGL builds
    /// where System ConcurrentDictionary causes random crashes, at least according to forum discussions.
    /// Implemented by deriving from non-thread safe <see cref="Dictionary{TKey, TValue}"/> with the missing
    /// compatibility methods added.
    /// </summary>
    internal class WebConcurrentDictionary<TKey, TValue> : Dictionary<TKey, TValue>
        where TKey : notnull
    {
        public bool TryRemove(TKey key, out TValue value) =>
            Remove(key, out value);

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            if (TryGetValue(key, out TValue existing))
                return existing;

            TValue value = valueFactory(key);
            // \note: `valueFactory()` may mutate this dictionary. If that happens, return the pre-existing value,
            // just like System.ConcurrentDictionary does. This can happen subtly with StringIds and lazy static class constructors.
            if (!TryAdd(key, value))
                return this[key];
            return value;
        }
    }
}

#endif
