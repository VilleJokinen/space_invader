// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Like <see cref="WeakReferencingCache{TKey, TValue}"/>, except that after an
    /// entry has been queried, it will be retained at least for a minimum duration
    /// (which is specified in the constructor) even if other references let go of it
    /// before that.
    /// </summary>
    public class WeakReferencingCacheWithTimedRetention<TKey, TValue> : IDisposable
        where TKey : IEquatable<TKey>
        where TValue : class
    {
        readonly TimeSpan _minimumRetentionTime;

        /// <summary>
        /// Underlying weak-referencing cache.
        /// </summary>
        WeakReferencingCache<TKey, TValue> _weakCache = new WeakReferencingCache<TKey, TValue>();

        object _lock = new object();
        /// <summary>
        /// Holds strong references (for a limited time) to entries in <see cref="_weakCache"/>.
        /// </summary>
        Dictionary<TKey, Retainer> _retainers = new Dictionary<TKey, Retainer>();
        /// <summary>
        /// Has a corresponding entry for each entry in <see cref="_retainers"/>,
        /// in order of increasing expiration time.
        /// \todo The order isn't guaranteed to be strictly honored
        ///       by the current implementation. #expiration-order-inconsistency
        /// </summary>
        LinkedList<RetainerExpiration> _expirationList = new LinkedList<RetainerExpiration>();
        /// <summary>
        /// Set up to fire at the earliest upcoming expiration time among current
        /// retainers (if any), or possibly earlier in case a retention has been
        /// bumped further into the future in the meantime.
        /// </summary>
        Timer _expirationTimer;
        /// <summary>
        /// Whether <see cref="_expirationTimer"/> is currently scheduled to fire.
        /// </summary>
        bool _hasScheduledExpirationTimer = false;

        bool _isDisposed = false;

        readonly struct Retainer
        {
            public readonly LinkedListNode<RetainerExpiration> ExpirationNode;
            public readonly TValue StrongRef;

            public Retainer(LinkedListNode<RetainerExpiration> expirationNode, TValue strongRef)
            {
                ExpirationNode = expirationNode;
                StrongRef = strongRef;
            }
        }

        readonly struct RetainerExpiration
        {
            public readonly DateTime ExpiresAt;
            public readonly TKey Key;

            public RetainerExpiration(DateTime expiresAt, TKey key)
            {
                ExpiresAt = expiresAt;
                Key = key;
            }
        }

        public WeakReferencingCacheWithTimedRetention(TimeSpan minimumRetentionTime)
        {
            _minimumRetentionTime = minimumRetentionTime;
            _expirationTimer = new Timer(_ => ExpirationTimerCallback());
        }

        public async ValueTask<TValue> GetCachedOrCreateNewAsync(TKey key, Func<TKey, Task<TValue>> createNewAsync)
        {
            CheckNotDisposed();

            TValue value = await _weakCache.GetCachedOrCreateNewAsync(key, createNewAsync);
            RefreshRetention(key, value);
            return value;
        }

        public TValue GetCachedOrCreateNew(TKey key, Func<TKey, TValue> createNew)
        {
            CheckNotDisposed();

            // Re-use async version, but with actually sync factory,
            // so taking .Result is safe.
            return GetCachedOrCreateNewAsync(
                key,
                key => Task.FromResult(createNew(key))
                ).GetCompletedResult();
        }

        /// <summary>
        /// Whether the entry for <paramref name="key"/> currently has an active
        /// timed-based retention in this cache. This is intended for testing.
        /// </summary>
        public bool IsRetainedDebug(TKey key)
        {
            lock (_lock)
                return _retainers.ContainsKey(key);
        }

        void RefreshRetention(TKey key, TValue strongRef)
        {
            // \todo If called from multiple threads, there is no guarantee that
            //       the expiration times that end up in _expirationList will actually be
            //       in increasing order. But the only bad consequence of this is that
            //       some entries will be expired later than their intended expiration time,
            //       which is not fatal. And the magnitude of this error is likely
            //       to be small.
            //       #expiration-order-inconsistency
            DateTime currentTime = DateTime.UtcNow;
            DateTime expiresAt = currentTime + _minimumRetentionTime;

            lock (_lock)
            {
                // If there's an existing retainer for this key, remove that
                // (both the retainer entry and the expiration list entry).
                if (_retainers.Remove(key, out Retainer oldRetainer))
                    _expirationList.Remove(oldRetainer.ExpirationNode);

                // Add a new retainer, with its expiration at the end of the expiration list.
                LinkedListNode<RetainerExpiration> expirationNode = _expirationList.AddLast(new RetainerExpiration(expiresAt, key));
                _retainers.Add(key, new Retainer(expirationNode, strongRef));

                // Ensure there's a scheduled expiration timer.
                TryScheduleExpirationTimer(currentTime);
            }
        }

        void ExpirationTimerCallback()
        {
            // \note Code shouldn't throw, but let's be defensive with this catch-all.
            //       This method is used as a Timer callback, and exceptions from
            //       Timer callbacks go uncaught, so we shouldn't throw.
            try
            {
                DateTime currentTime = DateTime.UtcNow;

                // \note This removes only one expired retainer per callback call.
                //       The intention is to avoid spending a long time in the lock in one go.
                //       If more retainers are due after the first one, then the timer will
                //       be scheduled to fire right away.

                lock (_lock)
                {
                    _hasScheduledExpirationTimer = false;

                    if (_isDisposed)
                        return;

                    // Expire the earliest existing retainer, if any, if it's due.
                    TryExpireFirstRetainer(currentTime);
                    // Ensure there's a scheduled expiration timer, if it's needed.
                    TryScheduleExpirationTimer(currentTime);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error($"{nameof(WeakReferencingCacheWithTimedRetention<TKey, TValue>)}<{typeof(TKey).Name}, {typeof(TValue).Name}>: Exception from expiration timer callback: {{Exception}}", ex);
            }
        }

        void TryExpireFirstRetainer(DateTime currentTime)
        {
            LinkedListNode<RetainerExpiration> firstNode = _expirationList.First;
            if (firstNode == null) // Nothing to expire
                return;

            RetainerExpiration firstExpiration = firstNode.Value;
            if (currentTime < firstExpiration.ExpiresAt) // First (earliest) expiration isn't due yet
                return;

            _retainers.Remove(firstExpiration.Key);
            _expirationList.Remove(firstNode);
        }

        void TryScheduleExpirationTimer(DateTime currentTime)
        {
            if (_hasScheduledExpirationTimer) // Already scheduled (for an earlier expiration)
                return;

            LinkedListNode<RetainerExpiration> firstNode = _expirationList.First;
            if (firstNode == null) // No expiration to schedule for
                return;

            RetainerExpiration firstExpiration = firstNode.Value;
            TimeSpan timeUntilExpiration = firstExpiration.ExpiresAt - currentTime;

            // Timer's documented maximum dueTime
            const int timerMaxDueTime = int.MaxValue;

            _hasScheduledExpirationTimer = true;
            _expirationTimer.Change(
                dueTime: (int)Util.Clamp<long>((long)timeUntilExpiration.TotalMilliseconds, 0, timerMaxDueTime),
                period: Timeout.Infinite);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _expirationTimer.Dispose();
                _isDisposed = true;
            }
        }

        void CheckNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WeakReferencingCache<TKey, TValue>));
        }
    }

    public class WeakReferencingCache<TKey, TValue>
        where TKey : IEquatable<TKey>
        where TValue : class
    {
        OrderedDictionary<TKey, WeakReference<TValue>>  _cache  = new OrderedDictionary<TKey, WeakReference<TValue>>();
        object                                          _lock   = new object();

        public async ValueTask<TValue> GetCachedOrCreateNewAsync(TKey key, Func<TKey, Task<TValue>> createNewAsync)
        {
            // Try get from the cache.

            WeakReference<TValue> weakRef = null;
            lock(_lock)
            {
                weakRef = _cache.GetValueOrDefault(key);
            }

            TValue strongRef;
            if (weakRef != null && weakRef.TryGetTarget(out strongRef))
                return strongRef;

            // Not in the cache. Fetch and populate into cache.

            TValue value = await createNewAsync(key);
            lock(_lock)
            {
                weakRef = _cache.GetValueOrDefault(key);

                // already filled and it is valid?
                if (weakRef != null && weakRef.TryGetTarget(out strongRef))
                    return strongRef;

                // either does not exist or has decayed
                _cache[key] = new WeakReference<TValue>(value);
            }

            return value;
        }

        public TValue GetCachedOrCreateNew(TKey key, Func<TKey, TValue> createNew)
        {
            // Re-use async version, but with actually sync factory,
            // so taking .Result is safe.
            return GetCachedOrCreateNewAsync(
                key,
                key => Task.FromResult(createNew(key))
                ).GetCompletedResult();
        }
    }
}
