// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core
{
    /// <summary>
    /// Counter of element that need to be refreshed sufficiently often in order to contribute into the Count.
    /// This is intended for tracking and counting liveness of components.
    /// </summary>
    public class RefreshKeyCounter<TKey>
    {
        // The counter is based on placing counted elements in generations or cycles which are based on time.
        // New elements are always placed on the current generation (cycle). When generation changes, all elements
        // are moved from current to the previous "draining" generation and previous draining is cleared. During
        // the element handle refresh, the handle checks if it is on a draining generation and attempts to move the
        // contribution on the new current generation counter. If the element was on a generation older than draining,
        // it means it wasn't refreshed for the duration of the generation time span.

        public readonly struct CycleClock
        {
            readonly MetaTime       _cycleStartAt;
            readonly MetaDuration   _refreshCycle;

            public CycleClock(MetaDuration refreshInterval)
            {
                _cycleStartAt = MetaTime.Now;
                _refreshCycle = refreshInterval;
            }

            public int GetCurrentCycle()
            {
                return (int)((MetaTime.Now - _cycleStartAt).Milliseconds / _refreshCycle.Milliseconds);
            }
        }

        /// <summary>
        /// A Handle into a counted element in the set. This must be kept alive by caling Refresh() periodically.
        /// </summary>
        public class RefreshHandle
        {
            readonly CycleClock _clock;
            int _cycle;
            readonly TKey _key;
            RefreshKeyCounter<TKey> _counter;

            internal RefreshHandle(RefreshKeyCounter<TKey> counter, int cycle, TKey key)
            {
                _clock = counter._clock;
                _cycle = cycle;
                _key = key;
                _counter = counter;
            }

            /// <summary>
            /// Refreshes the element. Failure to refresh the handle will cause liveness checks to
            /// fail and it to be removed from the counter.
            /// </summary>
            public void Refresh()
            {
                // Counter is dead. Do nothing.
                if (_counter == null)
                    return;

                int counterCycle = _clock.GetCurrentCycle();
                if (counterCycle == _cycle)
                {
                    // Element is still on a valid cycle and doesn't need to be migrated.
                }
                else if (counterCycle - 1 == _cycle)
                {
                    // We are on the dying cycle. The counter needs to be migrated to the current cycle.
                    if (_counter.TryMigrateHandle(_cycle, _key))
                    {
                        ++_cycle;
                    }
                    else
                    {
                        // Could not migrate.
                        _counter = null;
                    }
                }
                else
                {
                    // This handle has expired. Mark as dead.
                    _counter = null;
                }
            }

            /// <summary>
            /// Removes this element from the counter. If already removed,
            /// does nothing.
            /// </summary>
            public void Remove()
            {
                // Already disposed?
                if (_counter == null)
                    return;

                _counter.RemoveHandle(_cycle, _key);
                _counter = null;
            }
        }

        readonly object         _lock;
        readonly CycleClock     _clock;
        OrderedSet<TKey>        _drainingGeneration; // The number of elements in Draining cycle (generation)
        OrderedSet<TKey>        _fillingGeneration; // The number of elements in Current (filling) cycle (generation)
        int                     _updateCycle; // The cycle number of the current generation.

        /// <summary>
        /// Creates a new counter where elements must be refreshed at least once per <paramref name="refreshInterval"/> or
        /// risk being removed from the counted set. Not refreshing for longer than <c>2 x <paramref name="refreshInterval"/></c>
        /// guarantees element removal.
        /// </summary>
        public RefreshKeyCounter(MetaDuration refreshInterval)
        {
            _lock = new object();
            _clock = new CycleClock(refreshInterval);
            _drainingGeneration = new OrderedSet<TKey>();
            _fillingGeneration = new OrderedSet<TKey>();
            _updateCycle = -1;
        }

        /// <summary>
        /// Adds a new entry into this set counter. If key already exists, the size of the
        /// set does not increase but a new refresh handle is created.
        /// </summary>
        public RefreshHandle Add(TKey key)
        {
            lock (_lock)
            {
                int currentCycle = _clock.GetCurrentCycle();
                UpdateCyclesLocked(currentCycle);

                // Remove from draining just in case. We might have an earlier handle there.
                _drainingGeneration.Remove(key);

                // Add into filling generation. We might already have a handle there.
                _fillingGeneration.Add(key);

                return new RefreshHandle(this, currentCycle, key);
            }
        }

        /// <summary>
        /// Returns the number of alive elements in this set.
        /// </summary>
        public int GetNumAlive()
        {
            lock (_lock)
            {
                int currentCycle = _clock.GetCurrentCycle();
                UpdateCyclesLocked(currentCycle);
                return _fillingGeneration.Count + _drainingGeneration.Count;
            }
        }

        bool TryMigrateHandle(int counterCycle, TKey key)
        {
            lock (_lock)
            {
                int currentCycle = _clock.GetCurrentCycle();
                UpdateCyclesLocked(currentCycle);

                if (counterCycle == currentCycle - 1)
                {
                    // Normal migration
                    if (_drainingGeneration.Remove(key))
                    {
                        _fillingGeneration.Add(key);
                        return true;
                    }

                    // Migration already done by and alias handle?
                    if (_fillingGeneration.Contains(key))
                    {
                        return true;
                    }

                    // Removed by an alias handle
                    return false;
                }
                else
                {
                    // Any other case the counter was neither in filling or draining bucket.
                    return false;
                }
            }
        }

        void RemoveHandle(int handleCycle, TKey key)
        {
            lock (_lock)
            {
                int currentCycle = _clock.GetCurrentCycle();
                UpdateCyclesLocked(currentCycle);
                if (handleCycle == currentCycle)
                {
                    _fillingGeneration.Remove(key);
                }
                else if (handleCycle == currentCycle - 1)
                {
                    _drainingGeneration.Remove(key);
                }
            }
        }

        void UpdateCyclesLocked(int currentCycle)
        {
            if (currentCycle == _updateCycle + 1)
            {
                // move forward one step
                _updateCycle = currentCycle;
                OrderedSet<TKey> swap = _drainingGeneration;
                _drainingGeneration = _fillingGeneration;
                _fillingGeneration = swap;
                _fillingGeneration.Clear();
            }
            else if (currentCycle > _updateCycle + 1)
            {
                // move forward more than 1 steps
                _updateCycle = currentCycle;
                _drainingGeneration.Clear();
                _fillingGeneration.Clear();
            }
            else
            {
                // migration is already done
            }
        }
    }
}
