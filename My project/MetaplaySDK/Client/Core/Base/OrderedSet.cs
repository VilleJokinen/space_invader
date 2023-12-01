// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Metaplay.Core
{
    /// <summary>
    /// Set that maintains the item insertion order.
    ///
    /// <para>
    /// Set iteration order is guaranteed to match the item insertion order. If
    /// an item A is Added into the set before an item B, then the item A will be
    /// visited before item B during set iteration.
    /// </para>
    /// </summary>
    [System.Diagnostics.DebuggerTypeProxy(typeof(OrderedSet<>.DebugView))]
    [System.Diagnostics.DebuggerDisplay("Count = {Count}")]
    [System.Serializable]
    public class OrderedSet<TKey>
        : ICollection<TKey>
        , ICollection
        , IReadOnlyCollection<TKey>
        , ISerializable
        , IDeserializationCallback
    {
        private struct BucketChainIndex
        {
            private int v;

            public static BucketChainIndex FromEntryIndex(int entryIndex)
            {
                BucketChainIndex ndx;
                ndx.v = entryIndex;
                return ndx;
            }
            public static BucketChainIndex FromBucketIndex(int bucketIndex)
            {
                BucketChainIndex ndx;
                ndx.v = -bucketIndex - 1;
                return ndx;
            }

            public bool IsBucket => v < 0;
            public bool IsEntry => v >= 0;
            public int ToBucketIndex()
            {
                if (!IsBucket)
                    throw new InvalidOperationException("internal logic error");
                return -v - 1;
            }
            public int ToEntryIndex()
            {
                if (!IsEntry)
                    throw new InvalidOperationException("internal logic error");
                return v;
            }

        }
        private struct Bucket
        {
            public int headNdx;
        }
        private struct ChainEntry
        {
            /// <summary>
            /// Index of the next entry in bucket chain, or -1 if last entry.
            /// Applies both to freelist and bucket chains.
            /// </summary>
            public int bucketNext;
            /// <summary>
            /// Index of the previous entry in bucket chain. Either an index to
            /// entry or to the bucket head;
            /// Applies to bucket chains.
            /// </summary>
            public BucketChainIndex bucketPrev;
            /// <summary>
            /// Index of the next entry in iteration order, or -1 if last entry.
            /// Applies to bucket chains.
            /// </summary>
            public int iterationNext;
            /// <summary>
            /// Index of the previous entry in iteration order, or -1 if first entry.
            /// Applies to bucket chains.
            /// </summary>
            public int iterationPrev;
            /// <summary>
            /// HashCode of the value.
            /// Applies to bucket chains.
            /// </summary>
            public int hashcode;
            /// <summary>
            /// Stored element value.
            /// Applies to bucket chains.
            /// </summary>
            public TKey key;
        }

        private const int MinNonZeroCapacity = 4;

        private Bucket[] _buckets = null;
        private ChainEntry[] _entries = null;
        private int _count = 0;
        private int _nextFreeEntry = -1;
        private int _iterationFirst = -1;
        private int _iterationLast = -1;
        private uint _version = 0;

        /// <summary>
        /// Creates a new empty set.
        /// </summary>
        public OrderedSet()
        {
        }

        /// <summary>
        /// Creates a new empty set with a Capacity at least <paramref name="capacity"/>.
        /// </summary>
        public OrderedSet(int capacity)
        {
            Resize(NextValidCapacity(capacity));
        }

        /// <summary>
        /// Creates a new set with the elements of <paramref name="enumerable"/>.
        /// </summary>
        public OrderedSet(IEnumerable<TKey> enumerable)
        {
            ICollection<TKey> asCollection = enumerable as ICollection<TKey>;
            if (asCollection != null)
                Resize(NextValidCapacity(asCollection.Count));

            foreach (TKey key in enumerable)
                Add(key);
        }

        public int Count => _count;
        public int Capacity => _entries?.Length ?? 0;

        /// <summary>
        /// Appends item to the set if no equal element already exists in the set.
        ///
        /// Returns true if element was added to the set.
        /// </summary>
        public bool Add(TKey item) => AddLast(item);

        /// <summary>
        /// Appends item to the set if no equal element already exists in the set.
        ///
        /// Returns true if element was added to the set.
        /// </summary>
        public bool AddLast(TKey item)
        {
            ++_version; // Even attempting to modify a container invalidates Enumerators

            if (_buckets == null)
            {
                Expand();
                return AddLast(item);
            }

            int hashcode = GetElementHash(item);
            int bucketNdx = GetBucketIndex(hashcode, _buckets.Length);

            // Walk the chain and look for duplicates.
            int walkNdx = _buckets[bucketNdx].headNdx;
            while (true)
            {
                if (walkNdx == -1)
                    break;
                if (_entries[walkNdx].hashcode == hashcode && EqualityComparer<TKey>.Default.Equals(_entries[walkNdx].key, item))
                    return false;
                walkNdx = _entries[walkNdx].bucketNext;
            }

            // Push in the head of the bucket list. Chain to last in iteration order.

            int headIndex;
            if (!TryPopElementFromFreelist(out headIndex))
            {
                Expand();
                return AddLast(item);
            }

            _entries[headIndex].bucketNext = _buckets[bucketNdx].headNdx;
            _buckets[bucketNdx].headNdx = headIndex;
            _entries[headIndex].bucketPrev = BucketChainIndex.FromBucketIndex(bucketNdx);
            if (_entries[headIndex].bucketNext != -1)
                _entries[_entries[headIndex].bucketNext].bucketPrev = BucketChainIndex.FromEntryIndex(headIndex);

            _entries[headIndex].iterationPrev = _iterationLast;
            _entries[headIndex].iterationNext = -1;
            if (_iterationLast != -1)
                _entries[_iterationLast].iterationNext = headIndex;
            else
                _iterationFirst = headIndex;
            _iterationLast = headIndex;

            _entries[headIndex].hashcode = hashcode;
            _entries[headIndex].key = item;

            ++_count;
            return true;
        }

        /// <summary>
        /// Returns true if the set contains the item.
        /// </summary>
        public bool Contains(TKey item)
        {
            if (_buckets == null)
                return false;

            int hashcode = GetElementHash(item);
            int bucketNdx = GetBucketIndex(hashcode, Capacity);
            int walkNdx = _buckets[bucketNdx].headNdx;
            while (true)
            {
                if (walkNdx == -1)
                    break;
                if (_entries[walkNdx].hashcode == hashcode && EqualityComparer<TKey>.Default.Equals(_entries[walkNdx].key, item))
                    return true;
                walkNdx = _entries[walkNdx].bucketNext;
            }
            return false;
        }

        /// <summary>
        /// Removes item from the set. Returns true if set contained such item.
        /// </summary>
        public bool Remove(TKey item)
        {
            ++_version; // Even attempting to modify a container invalidates Enumerators

            if (_buckets == null)
                return false;

            int hashcode = GetElementHash(item);
            int bucketNdx = GetBucketIndex(hashcode, Capacity);
            int walkNdx = _buckets[bucketNdx].headNdx;
            while (true)
            {
                if (walkNdx == -1)
                    return false;
                if (_entries[walkNdx].hashcode == hashcode && EqualityComparer<TKey>.Default.Equals(_entries[walkNdx].key, item))
                    break;

                walkNdx = _entries[walkNdx].bucketNext;
            }

            // Drop from bucket chain

            if (_entries[walkNdx].bucketPrev.IsEntry)
                _entries[_entries[walkNdx].bucketPrev.ToEntryIndex()].bucketNext = _entries[walkNdx].bucketNext;
            else
                _buckets[bucketNdx].headNdx = _entries[walkNdx].bucketNext;

            if (_entries[walkNdx].bucketNext != -1)
                _entries[_entries[walkNdx].bucketNext].bucketPrev = _entries[walkNdx].bucketPrev;

            // Drop from iteration chain

            if (_entries[walkNdx].iterationPrev != -1)
                _entries[_entries[walkNdx].iterationPrev].iterationNext = _entries[walkNdx].iterationNext;
            else
                _iterationFirst = _entries[walkNdx].iterationNext;

            if (_entries[walkNdx].iterationNext != -1)
                _entries[_entries[walkNdx].iterationNext].iterationPrev = _entries[walkNdx].iterationPrev;
            else
                _iterationLast = _entries[walkNdx].iterationPrev;

            _entries[walkNdx].key = default;
            _entries[walkNdx].bucketNext = _nextFreeEntry;
            _nextFreeEntry = walkNdx;
            --_count;
            return true;
        }

        /// <summary>
        /// Calls predicate for each item in the set, and removes the items for which the predicate returns true.
        /// </summary>
        /// <returns>the number of items removed</returns>
        public int RemoveWhere(Predicate<TKey> predicate)
        {
            ++_version; // Even attempting to modify a container invalidates Enumerators

            // default state does not need clearing
            if (_buckets == null)
                return 0;

            int numRemoved = 0;
            int walkNdx = _iterationFirst;
            while (true)
            {
                if (walkNdx == -1)
                    break;

                if (!predicate(_entries[walkNdx].key))
                {
                    walkNdx = _entries[walkNdx].iterationNext;
                    continue;
                }

                // Drop from bucket chain

                if (_entries[walkNdx].bucketPrev.IsEntry)
                    _entries[_entries[walkNdx].bucketPrev.ToEntryIndex()].bucketNext = _entries[walkNdx].bucketNext;
                else
                    _buckets[_entries[walkNdx].bucketPrev.ToBucketIndex()].headNdx = _entries[walkNdx].bucketNext;

                if (_entries[walkNdx].bucketNext != -1)
                    _entries[_entries[walkNdx].bucketNext].bucketPrev = _entries[walkNdx].bucketPrev;

                // Drop from iteration chain

                if (_entries[walkNdx].iterationPrev != -1)
                    _entries[_entries[walkNdx].iterationPrev].iterationNext = _entries[walkNdx].iterationNext;
                else
                    _iterationFirst = _entries[walkNdx].iterationNext;

                if (_entries[walkNdx].iterationNext != -1)
                    _entries[_entries[walkNdx].iterationNext].iterationPrev = _entries[walkNdx].iterationPrev;
                else
                    _iterationLast = _entries[walkNdx].iterationPrev;

                _entries[walkNdx].key = default;
                _entries[walkNdx].bucketNext = _nextFreeEntry;
                _nextFreeEntry = walkNdx;
                walkNdx = _entries[walkNdx].iterationNext;
                --_count;
                ++numRemoved;
            }

            return numRemoved;
        }

        /// <summary>
        /// Removes all elements from the set. Does not modify Capacity.
        /// </summary>
        public void Clear()
        {
            ++_version; // Even attempting to modify a container invalidates Enumerators

            // default state does not need clearing
            if (_buckets == null)
                return;

            // clear buckets
            for (int ndx = 0; ndx < _buckets.Length; ++ndx)
                _buckets[ndx].headNdx = -1;

            // build up freelist and drop references to elements
            for (int ndx = 0; ndx < _entries.Length - 1; ++ndx)
            {
                _entries[ndx].bucketNext = ndx + 1;
                _entries[ndx].key = default;
            }
            _entries[_entries.Length - 1].bucketNext = -1;
            _entries[_entries.Length - 1].key = default;

            _count = 0;
            _nextFreeEntry = 0;
            _iterationFirst = -1;
            _iterationLast = -1;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }
        public struct Enumerator : IEnumerator<TKey>
        {
            int _ndx;
            TKey _current;
            uint _version;
            OrderedSet<TKey> _container;

            public Enumerator(OrderedSet<TKey> container)
            {
                _ndx = default;
                _current = default;
                _version = default;
                _container = container;
                Reset();
            }

            public TKey Current => _current;
            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _container._version)
                    throw new InvalidOperationException("Container was modified during iteration.");
                if (_ndx == -1)
                    return false;
                _current = _container._entries[_ndx].key;
                _ndx = _container._entries[_ndx].iterationNext;
                return true;
            }
            public void Reset()
            {
                _ndx = _container._iterationFirst;
                _version = _container._version;
                _current = default;
            }
            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Copies set elements to destination array in iteration order.
        /// Copying starts at arrayIndex.
        /// </summary>
        public void CopyTo(TKey[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (checked(arrayIndex + _count) > array.Length)
                throw new ArgumentException("arrayIndex + _count > array.Length");

            int index = arrayIndex;
            foreach (var v in this)
                array[index++] = v;
        }

        /// <summary>
        /// Returns the first element in iteration order. Throws <see cref="InvalidOperationException"/> if container is empty.
        /// </summary>
        public TKey First()
        {
            Enumerator e = GetEnumerator();
            if (e.MoveNext())
                return e.Current;
            throw new InvalidOperationException("container was empty");
        }

        /// <summary>
        /// Returns the element at certain position in iteration order. Throws <see cref="ArgumentOutOfRangeException"/> if index is too large.
        /// Warning: Looking up an element is a linear time operation.
        /// </summary>
        public TKey ElementAt(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            Enumerator e = GetEnumerator();
            for (int i = 0; i <= index; ++i)
            {
                if (!e.MoveNext())
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
            return e.Current;
        }

        private static int GetElementHash(TKey item) => item?.GetHashCode() ?? 0;
        private static int GetBucketIndex(int hashcode, int capacity) => hashcode & (capacity - 1); // capacity is POT
        private bool TryPopElementFromFreelist(out int elementIndex)
        {
            if (_nextFreeEntry == -1)
            {
                elementIndex = -1;
                return false;
            }
            else
            {
                elementIndex = _nextFreeEntry;
                _nextFreeEntry = _entries[_nextFreeEntry].bucketNext;
                return true;
            }
        }
        private void Expand()
        {
            int nextCapacity = NextValidCapacity(Capacity + 1);
            Resize(nextCapacity);
        }
        private bool IsValidCapacity(int proposedCapacity)
        {
            if (proposedCapacity == 0)
                return true;
            if (proposedCapacity < MinNonZeroCapacity)
                return false;
            return Util.IsPowerOfTwo(proposedCapacity);
        }
        private int NextValidCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= 0)
                return 0;
            else if (requiredCapacity <= MinNonZeroCapacity)
                return MinNonZeroCapacity;
            return Util.CeilToPowerOfTwo(requiredCapacity);
        }
        private void Resize(int newCapacity)
        {
            Bucket[] newBuckets;
            ChainEntry[] newEntries;
            int newNextFreeEntry;
            int newIterationFirst;
            int newIterationLast;

            if (newCapacity < _count)
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "Resize(): Capacity < Count");
            if (!IsValidCapacity(newCapacity))
                throw new ArgumentException("Resize(): Capacity is not valid");

            if (newCapacity == 0)
            {
                newBuckets = null;
                newEntries = null;
                newNextFreeEntry = -1;
                newIterationFirst = -1;
                newIterationLast = -1;
            }
            else
            {
                newBuckets = new Bucket[newCapacity];
                newEntries = new ChainEntry[newCapacity];

                // Setup bucket chains and insert elements to them in iteration order.

                for (int ndx = 0; ndx < newCapacity; ++ndx)
                    newBuckets[ndx].headNdx = -1;

                int nextFreeIndex = 0;
                int walkNdx = _iterationFirst;
                while (walkNdx != -1)
                {
                    int newBucketNdx = GetBucketIndex(hashcode: _entries[walkNdx].hashcode, capacity: newCapacity);

                    newEntries[nextFreeIndex].bucketNext = newBuckets[newBucketNdx].headNdx;
                    newEntries[nextFreeIndex].bucketPrev = BucketChainIndex.FromBucketIndex(newBucketNdx);
                    newEntries[nextFreeIndex].iterationNext = nextFreeIndex + 1; // will point oob on last iteration. Fixed later.
                    newEntries[nextFreeIndex].iterationPrev = nextFreeIndex - 1;
                    newEntries[nextFreeIndex].hashcode = _entries[walkNdx].hashcode;
                    newEntries[nextFreeIndex].key = _entries[walkNdx].key;

                    newBuckets[newBucketNdx].headNdx = nextFreeIndex;
                    if (newEntries[nextFreeIndex].bucketNext != -1)
                        newEntries[newEntries[nextFreeIndex].bucketNext].bucketPrev = BucketChainIndex.FromEntryIndex(nextFreeIndex);

                    nextFreeIndex++;
                    walkNdx = _entries[walkNdx].iterationNext;
                }

                if (_count == 0)
                {
                    newIterationFirst = -1;
                    newIterationLast = -1;
                }
                else
                {
                    newIterationFirst = 0;
                    newIterationLast = _count - 1;
                    newEntries[newIterationLast].iterationNext = -1;
                }

                // Setup freelist

                if (_count == newCapacity)
                {
                    newNextFreeEntry = -1;
                }
                else
                {
                    newNextFreeEntry = _count;
                    for (int ndx = _count; ndx < newCapacity - 1; ++ndx)
                        newEntries[ndx].bucketNext = ndx + 1;
                    newEntries[newCapacity - 1].bucketNext = -1;
                }
            }

            _buckets = newBuckets;
            _entries = newEntries;
            _nextFreeEntry = newNextFreeEntry;
            _iterationFirst = newIterationFirst;
            _iterationLast = newIterationLast;
        }

        #region ICollection<TKey>
        bool ICollection<TKey>.IsReadOnly => false;
        void ICollection<TKey>.Add(TKey item) => Add(item);
        #endregion

        #region ICollection
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (checked(arrayIndex + _count) > array.Length)
                throw new ArgumentException("arrayIndex + _count > array.Length");

            int index = arrayIndex;
            foreach (var v in this)
                array.SetValue(v, index++);
        }
        #endregion

        #region IEnumerable<TKey>
        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
        #endregion

        #region IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        #endregion

        #region ISerializable
        private SerializationInfo _pendingSerializetionInfo;
        protected OrderedSet(SerializationInfo info, StreamingContext context)
        {
            // delay deserialization until OnDeserialization() since elems might
            // not be deserialized yet.
            _pendingSerializetionInfo = info;
        }
        void IDeserializationCallback.OnDeserialization(object sender)
        {
            if (_pendingSerializetionInfo == null)
                return;

            TKey[] keys = (TKey[])_pendingSerializetionInfo.GetValue("K", typeof(TKey[]));

            if (keys != null)
            {
                Resize(NextValidCapacity(keys.Length));
                foreach (TKey key in keys)
                    Add(key);
            }

            _pendingSerializetionInfo = null;
        }
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            TKey[] keys = null;
            int count = Count;

            if (count > 0)
            {
                keys = new TKey[count];
                CopyTo(keys, 0);
            }

            info.AddValue("K", keys);
        }
        #endregion

        private class DebugView
        {
            private OrderedSet<TKey> _container;
            private DebugView(OrderedSet<TKey> container)
            {
                _container = container;
            }

            [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.RootHidden)]
            public TKey[] Items
            {
                get
                {
                    TKey[] elems = new TKey[_container?.Count ?? 0];
                    _container?.CopyTo(elems, 0);
                    return elems;
                }
            }
        }
    }
}
