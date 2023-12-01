// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.MultiplayerEntity
{
    partial class MultiplayerEntityClientContext<TModel>
    {
        /// <summary>
        /// Bounded-size vector of TimeSpan. If the capacity is reached, adding a Timespan
        /// removes the oldest entry.
        /// </summary>
        class TimeSpanHistory
        {
            TimeSpan[] _entries;
            int _numEntriesAdded = 0;

            public TimeSpanHistory(int historySize)
            {
                _entries = new TimeSpan[historySize];
                _numEntriesAdded = 0;
            }

            /// <summary>
            /// Adds a new entry. If the capacity is reached, the oldest entry is removed.
            /// </summary>
            public void Add(TimeSpan ts)
            {
                _entries[_numEntriesAdded % _entries.Length] = ts;
                _numEntriesAdded++;
            }

            public TimeSpan MinOrDefault(TimeSpan defaultValue)
            {
                if (_numEntriesAdded == 0)
                    return defaultValue;

                TimeSpan min = _entries[0];
                int count = System.Math.Min(_numEntriesAdded, _entries.Length);
                for (int ndx = 1; ndx < count; ++ndx)
                    min = TimeSpan.FromTicks(System.Math.Min(min.Ticks, _entries[ndx].Ticks));

                return min;
            }
        }
    }
}
