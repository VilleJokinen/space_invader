// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using static System.FormattableString;

namespace Metaplay.Core
{
    /// <summary>
    /// Dictionary that maintains the item insertion order.
    ///
    /// <para>
    /// Dictionary iteration order is guaranteed to match the item insertion order. For example if
    /// a mapping A->X is Added into the Dictionary before a mapping B->Y, then the key A will be
    /// visited before key B during iteration. Modifying an existing mapping does not alter the
    /// iteration order.
    /// </para>
    /// </summary>
    [System.Diagnostics.DebuggerTypeProxy(typeof(OrderedDictionary<,>.DebugView))]
    [System.Diagnostics.DebuggerDisplay("Count = {Count}")]
    [System.Serializable]
    public class OrderedDictionary<TKey, TValue>
        : IEnumerable<KeyValuePair<TKey, TValue>>
        , ICollection<KeyValuePair<TKey, TValue>>
        , IDictionary<TKey, TValue>
        , IDictionary
        , IReadOnlyCollection<KeyValuePair<TKey, TValue>>
        , IReadOnlyDictionary<TKey, TValue>
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
                    throw new InvalidOperationException("Internal logic error");
                return -v - 1;
            }
            public int ToEntryIndex()
            {
                if (!IsEntry)
                    throw new InvalidOperationException("Internal logic error");
                return v;
            }

        }
        private struct Bucket
        {
            public int headNdx;
        }
        public struct KeyValue
        {
            public readonly TKey Key;
            public TValue Value;
            public KeyValue(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
            public void Deconstruct(out TKey key, out TValue value)
            {
                key = Key;
                value = Value;
            }
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
            /// HashCode of the key.
            /// Applies to bucket chains.
            /// </summary>
            public int hashcode;
            /// <summary>
            /// Stored element key and value.
            /// Applies to bucket chains.
            /// </summary>
            public KeyValue kv;
        }
        private enum InternalAddConflictPolicy { KeepExisting, OverwriteExisting };

        private const int MinNonZeroCapacity = 4;

        private Bucket[] _buckets = null;
        private ChainEntry[] _entries = null;
        private int _count = 0;
        private int _nextFreeEntry = -1;
        private int _iterationFirst = -1;
        private int _iterationLast = -1;
        private uint _version = 0;

        /// <summary>
        /// Creates a new empty dictionary.
        /// </summary>
        public OrderedDictionary()
        {
        }

        /// <summary>
        /// Creates a new empty dictionary with a Capacity at least <paramref name="capacity"/>.
        /// </summary>
        public OrderedDictionary(int capacity)
        {
            Resize(NextValidCapacity(capacity));
        }

        /// <summary>
        /// Creates a new dictionary with the elements of <paramref name="enumerable"/>.
        /// If there are multiple mappings with the same key, an exception is thrown.
        /// </summary>
        public OrderedDictionary(IEnumerable<KeyValuePair<TKey, TValue>> enumerable)
        {
            ICollection<KeyValuePair<TKey, TValue>> asCollection = enumerable as ICollection<KeyValuePair<TKey, TValue>>;
            if (asCollection != null)
                Resize(NextValidCapacity(asCollection.Count));

            foreach (var item in enumerable)
            {
                bool added = AddIfAbsent(item.Key, item.Value);
                if (!added)
                    throw new ArgumentException(Invariant($"Multiple items (of type {typeof(TValue).ToGenericTypeString()}) in input with the same key '{item.Key}'"));
            }
        }

        public int Count => _count;
        public int Capacity => _entries?.Length ?? 0;
        /// <summary>
        /// Gets the the keys of the dictionary. Note that the returned instance DOES NOT COPY
        /// the underlying keys but just provides a view to them. Any modification to the
        /// dictionary invalidates any existing <c>KeyCollection</c>.
        /// </summary>
        public KeyCollection Keys => new KeyCollection(this);
        /// <summary>
        /// Gets the the values of the dictionary. Note that the returned instance DOES NOT COPY
        /// the underlying values but just provides a view to them. Any modification to the
        /// dictionary invalidates any existing <c>ValueCollection</c>.
        /// </summary>
        public ValueCollection Values => new ValueCollection(this);

        /// <summary>
        /// Adds a mapping from <paramref name="key" /> -> <paramref name="value" /> if no mappings
        /// already exist for <paramref name="key" />. If a mapping already exists for the key, it
        /// is not modified.
        ///
        /// <para>
        /// Returns true if the entry was added to the dictionary. Returns false if a
        /// mapping already existed in the dictionary.
        /// </para>
        /// </summary>
        public bool AddIfAbsent(TKey key, TValue value) => InternalAdd(key, value, InternalAddConflictPolicy.KeepExisting);

        /// <inheritdoc cref="AddIfAbsent(TKey, TValue)"/>
        public bool TryAdd(TKey key, TValue value) => AddIfAbsent(key, value);

        /// <summary>
        /// Sets a mapping from <paramref name="key" /> -> <paramref name="value" />. If a mapping
        /// already exists for the key, it is modified.
        ///
        /// <para>
        /// Returns true if a new entry was added to the dictionary. Returns false if an existing
        /// mapping was modified.
        /// </para>
        /// </summary>
        public bool AddOrReplace(TKey key, TValue value) => InternalAdd(key, value, InternalAddConflictPolicy.OverwriteExisting);

        /// <summary>
        /// Adds a mapping from new <paramref name="key" /> -> <paramref name="value" />. If a mapping
        /// already exists for <paramref name="key" />, <c>ArgumentException</c> is thrown and dictionary
        /// is not modified.
        /// </summary>
        /// <exception cref="ArgumentException">if a mapping already exists for key</exception>
        public void Add(TKey key, TValue value)
        {
            if (!AddIfAbsent(key, value))
                throw new ArgumentException(Invariant($"Key already exists: '{key}'"));
        }

        /// <summary>
        /// Returns true if a mapping exists for the key.
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            return TryGetValue(key, out _);
        }

        /// <summary>
        /// Looks up for a mapping for the supplied key. If a mapping exists for the key,
        /// <paramref name="value"/> is set to the mapped value and the invocation returns
        /// <c>true</c>.
        /// Otherwise, <paramref name="value"/> is set to the default value, and invocation
        /// returns <c>false</c>.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_buckets == null)
            {
                value = default;
                return false;
            }

            int hashcode = GetElementHash(key);
            int bucketNdx = GetBucketIndex(hashcode, Capacity);
            int walkNdx = IndexOfKeyOnChain(chainHead: _buckets[bucketNdx].headNdx, hashcode, key);

            if (walkNdx == -1)
            {
                value = default;
                return false;
            }

            value = _entries[walkNdx].kv.Value;
            return true;
        }

        public TValue this[TKey key]
        {
            get
            {
                if (!TryGetValue(key, out TValue value))
                    throw new KeyNotFoundException(Invariant($"Key '{key}' was not found"));
                return value;
            }
            set
            {
                AddOrReplace(key, value);
            }
        }

        /// <summary>
        /// Returns the mapping for the supplied key if such a mapping exists. Otherwise returns <paramref name="defaultValue"/>.
        /// </summary>
        public TValue GetValueOrDefault(TKey key, TValue defaultValue = default)
        {
            if (!TryGetValue(key, out TValue value))
                return defaultValue;
            return value;
        }

        /// <summary>
        /// Removes a mapping for the key, if such exists. Returns true if the mapping was removed, false if no such mapping exists.
        /// </summary>
        public bool Remove(TKey key)
        {
            ++_version; // Even attempting to modify a container invalidates Enumerators

            if (_buckets == null)
                return false;

            int hashcode = GetElementHash(key);
            int bucketNdx = GetBucketIndex(hashcode, Capacity);
            int walkNdx = IndexOfKeyOnChain(chainHead: _buckets[bucketNdx].headNdx, hashcode, key);

            if (walkNdx == -1)
                return false;

            DropChainElement(walkNdx);

            return true;
        }

        /// <summary>
        /// Removes a mapping for the key, if such exists and returns true and sets <paramref name="value"/> parameter to the removed value. If no mapping exists,
        /// returns false, and <paramref name="value"/> is set to the default value;
        /// </summary>
        public bool Remove(TKey key, out TValue value)
        {
            if (_buckets == null)
            {
                value = default;
                return false;
            }

            int hashcode = GetElementHash(key);
            int bucketNdx = GetBucketIndex(hashcode, Capacity);
            int walkNdx = IndexOfKeyOnChain(chainHead: _buckets[bucketNdx].headNdx, hashcode, key);

            if (walkNdx == -1)
            {
                value = default;
                return false;
            }

            value = _entries[walkNdx].kv.Value;
            DropChainElement(walkNdx);
            return true;
        }

        /// <summary>
        /// Calls predicate for each mapping in the dictionary, and removes the items for which the predicate returns true.
        /// </summary>
        /// <returns>the number of items removed</returns>
        public int RemoveWhere(Predicate<KeyValue> predicate)
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

                if (!predicate(_entries[walkNdx].kv))
                {
                    walkNdx = _entries[walkNdx].iterationNext;
                    continue;
                }

                int iterationNext = _entries[walkNdx].iterationNext;
                DropChainElement(walkNdx);
                walkNdx = iterationNext;
                ++numRemoved;
            }

            return numRemoved;
        }

        /// <summary>
        /// Removes all mappings from the dictionary. Does not modify Capacity.
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
                _entries[ndx].kv = default;
            }
            _entries[_entries.Length - 1].bucketNext = -1;
            _entries[_entries.Length - 1].kv = default;

            _count = 0;
            _nextFreeEntry = 0;
            _iterationFirst = -1;
            _iterationLast = -1;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <summary>
        /// Copies keys to destination array in iteration order.
        /// Copying starts at arrayIndex.
        /// </summary>
        public void CopyKeysTo(TKey[] array, int arrayIndex)
        {
            Keys.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Copies values to destination array in iteration order.
        /// Copying starts at arrayIndex.
        /// </summary>
        public void CopyValuesTo(TValue[] array, int arrayIndex)
        {
            Values.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Returns the first element in iteration order. Throws <see cref="InvalidOperationException"/> if container is empty.
        /// </summary>
        public KeyValue First()
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
        public KeyValue ElementAt(int index)
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

        private struct IterationWalker
        {
            OrderedDictionary<TKey, TValue> _container;
            int _ndx;
            uint _version;

            public IterationWalker(OrderedDictionary<TKey, TValue> container)
            {
                _ndx = default;
                _version = default;
                _container = container;
                Reset();
            }
            public bool MoveNext()
            {
                if (_version != _container._version)
                    throw new InvalidOperationException("Container was modified during iteration.");

                if (_ndx == -1)
                    return false;
                if (_ndx == -2)
                {
                    _ndx = _container._iterationFirst;
                    return (_ndx != -1);
                }
                _ndx = _container._entries[_ndx].iterationNext;
                if (_ndx == -1)
                    return false;
                return true;
            }
            public void Reset()
            {
                _ndx = -2;
                _version = _container._version;
            }
            public ref KeyValue GetCurrent() => ref _container._entries[_ndx].kv;
        }
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            IterationWalker _walker;

            public Enumerator(OrderedDictionary<TKey, TValue> container)
            {
                _walker = new IterationWalker(container);
                Reset();
            }

            public ref KeyValue Current => ref _walker.GetCurrent();
            object IEnumerator.Current => ((IEnumerator<KeyValuePair<TKey, TValue>>)this).Current;
            KeyValuePair<TKey, TValue> IEnumerator<KeyValuePair<TKey, TValue>>.Current => new KeyValuePair<TKey, TValue>(_walker.GetCurrent().Key, _walker.GetCurrent().Value);

            public bool MoveNext() => _walker.MoveNext();
            public void Reset() => _walker.Reset();
            public void Dispose()
            {
            }
        }
        public struct DictionaryEnumerator : IDictionaryEnumerator
        {
            IterationWalker _walker;

            public DictionaryEnumerator(OrderedDictionary<TKey, TValue> container)
            {
                _walker = new IterationWalker(container);
                Reset();
            }
            public DictionaryEntry Entry => new DictionaryEntry(_walker.GetCurrent().Key, _walker.GetCurrent().Value);
            public object Key => _walker.GetCurrent().Key;
            public object Value => _walker.GetCurrent().Value;
            public object Current => Entry;
            public bool MoveNext() => _walker.MoveNext();
            public void Reset() => _walker.Reset();
        }
        public struct KeyEnumerator : IEnumerator<TKey>
        {
            IterationWalker _walker;

            public KeyEnumerator(OrderedDictionary<TKey, TValue> container)
            {
                _walker = new IterationWalker(container);
                Reset();
            }

            public TKey Current => _walker.GetCurrent().Key;
            object IEnumerator.Current => Current;

            void IDisposable.Dispose()
            {
            }
            public bool MoveNext() => _walker.MoveNext();
            public void Reset() => _walker.Reset();
        }
        public struct ValueEnumerator : IEnumerator<TValue>
        {
            IterationWalker _walker;

            public ValueEnumerator(OrderedDictionary<TKey, TValue> container)
            {
                _walker = new IterationWalker(container);
                Reset();
            }

            public ref TValue Current => ref _walker.GetCurrent().Value;
            TValue IEnumerator<TValue>.Current => Current;
            object IEnumerator.Current => Current;

            void IDisposable.Dispose()
            {
            }
            public bool MoveNext() => _walker.MoveNext();
            public void Reset() => _walker.Reset();
        }
        public struct KeyCollection : IEnumerable<TKey>, ICollection<TKey>, ICollection
        {
            OrderedDictionary<TKey, TValue> _container;
            uint _version;
            public KeyCollection(OrderedDictionary<TKey, TValue> container)
            {
                _container = container;
                _version = _container._version;
            }
            public KeyEnumerator GetEnumerator()
            {
                CheckVersion();
                return new KeyEnumerator(_container);
            }
            public int Count
            {
                get
                {
                    CheckVersion();
                    return _container.Count;
                }
            }
            public bool Contains(TKey item)
            {
                CheckVersion();
                return _container.ContainsKey(item);
            }
            public void CopyTo(TKey[] array, int arrayIndex)
            {
                CheckVersion();
                if (array == null)
                    throw new ArgumentNullException(nameof(array));
                if (arrayIndex < 0)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex));
                if (checked(arrayIndex + Count) > array.Length)
                    throw new ArgumentException("arrayIndex + Count > array.Length");

                int index = arrayIndex;
                foreach (var v in this)
                    array[index++] = v;
            }
            public TKey First()
            {
                CheckVersion();
                return _container.First().Key;
            }
            public TKey ElementAt(int index)
            {
                CheckVersion();
                return _container.ElementAt(index).Key;
            }
            private void CheckVersion()
            {
                if (_version != _container._version)
                    throw new InvalidOperationException("Container was modified between Keys() and the key set access.");
            }
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            void ICollection<TKey>.Add(TKey item) => throw new NotSupportedException("Cannot modify Keys projection");
            void ICollection<TKey>.Clear() => throw new NotSupportedException("Cannot modify Keys projection");
            bool ICollection<TKey>.Remove(TKey item) => throw new NotSupportedException("Cannot modify Keys projection");
            bool ICollection<TKey>.IsReadOnly => true;
            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => ((ICollection)_container).SyncRoot;
            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                CheckVersion();
                OrderedDictionary<TKey, TValue>.CopyEnumerableTo<TKey, KeyCollection>(array, arrayIndex, this);
            }
        }
        public struct ValueCollection : IEnumerable<TValue>, ICollection<TValue>, ICollection
        {
            OrderedDictionary<TKey, TValue> _container;
            uint _version;
            public ValueCollection(OrderedDictionary<TKey, TValue> container)
            {
                _container = container;
                _version = _container._version;
            }
            public ValueEnumerator GetEnumerator()
            {
                if (_version != _container._version)
                    throw new InvalidOperationException("Container was modified between Values() and Values().GetEnumerator().");
                return new ValueEnumerator(_container);
            }
            public int Count
            {
                get
                {
                    CheckVersion();
                    return _container.Count;
                }
            }
            public bool Contains(TValue item)
            {
                CheckVersion();
                foreach(TValue value in this)
                {
                    if (item == null && value == null)
                        return true;
                    if (item != null && value != null && item.Equals(value))
                        return true;
                }
                return false;
            }
            public void CopyTo(TValue[] array, int arrayIndex)
            {
                CheckVersion();
                if (array == null)
                    throw new ArgumentNullException(nameof(array));
                if (arrayIndex < 0)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex));
                if (checked(arrayIndex + Count) > array.Length)
                    throw new ArgumentException("arrayIndex + Count > array.Length");

                int index = arrayIndex;
                foreach (var v in this)
                    array[index++] = v;
            }
            public TValue First()
            {
                CheckVersion();
                return _container.First().Value;
            }
            public TValue ElementAt(int index)
            {
                CheckVersion();
                return _container.ElementAt(index).Value;
            }
            private void CheckVersion()
            {
                if (_version != _container._version)
                    throw new InvalidOperationException("Container was modified between Values() and the value set access.");
            }
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            void ICollection<TValue>.Add(TValue item) => throw new NotSupportedException("Cannot modify Values projection");
            void ICollection<TValue>.Clear() => throw new NotSupportedException("Cannot modify Values projection");
            bool ICollection<TValue>.Remove(TValue item) => throw new NotSupportedException("Cannot modify Values projection");
            bool ICollection<TValue>.IsReadOnly => true;
            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => ((ICollection)_container).SyncRoot;
            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                CheckVersion();
                OrderedDictionary<TKey, TValue>.CopyEnumerableTo<TValue, ValueCollection>(array, arrayIndex, this);
            }
        }
        private static int GetElementHash(TKey key) => key?.GetHashCode() ?? 0;
        private static int GetBucketIndex(int hashcode, int capacity) => hashcode & (capacity - 1); // capacity is POT

        private int IndexOfKeyOnChain(int chainHead, int hashcode, TKey key)
        {
            int walkNdx = chainHead;
            while (true)
            {
                if (walkNdx == -1)
                    break;
                if (_entries[walkNdx].hashcode == hashcode && EqualityComparer<TKey>.Default.Equals(_entries[walkNdx].kv.Key, key))
                    break;
                walkNdx = _entries[walkNdx].bucketNext;
            }
            return walkNdx;
        }

        private void DropChainElement(int walkNdx)
        {
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

            _entries[walkNdx].kv = default;
            _entries[walkNdx].bucketNext = _nextFreeEntry;
            _nextFreeEntry = walkNdx;
            --_count;
        }

        /// <summary>
        /// Returns true if count was increased. (i.e. a new key was inserted).
        /// </summary>
        private bool InternalAdd(TKey key, TValue value, InternalAddConflictPolicy conflictPolicy)
        {
            ++_version; // Even attempting to modify a container invalidates Enumerators

            if (_buckets == null)
            {
                Expand();
                return InternalAdd(key, value, conflictPolicy);
            }

            int hashcode = GetElementHash(key);
            int bucketNdx = GetBucketIndex(hashcode, _buckets.Length);

            // Walk the chain and look for a duplicate.
            int walkNdx = IndexOfKeyOnChain(chainHead: _buckets[bucketNdx].headNdx, hashcode, key);

            // Updating existing mapping
            if (walkNdx != -1)
            {
                if (conflictPolicy == InternalAddConflictPolicy.OverwriteExisting)
                    _entries[walkNdx].kv = new KeyValue(key, value);
                return false;
            }

            // Push in the head of the bucket list. Chain to last in iteration order.

            int headIndex;
            if (!TryPopElementFromFreelist(out headIndex))
            {
                Expand();
                return InternalAdd(key, value, conflictPolicy);
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
            _entries[headIndex].kv = new KeyValue(key, value);

            ++_count;
            return true;
        }
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
                    newEntries[nextFreeIndex].kv = _entries[walkNdx].kv;

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
        private static void CopyEnumerableTo<TElem, TEnumerable>(TElem[] array, int arrayIndex, TEnumerable enumerable) where TEnumerable: ICollection<TElem>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (checked(arrayIndex + enumerable.Count) > array.Length)
                throw new ArgumentException("arrayIndex + enumerable.Count > array.Length");

            int index = arrayIndex;
            foreach (var v in enumerable)
                array[index++] = v;
        }
        private static void CopyEnumerableTo<TElem, TEnumerable>(Array array, int arrayIndex, TEnumerable enumerable) where TEnumerable : ICollection<TElem>
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (checked(arrayIndex + enumerable.Count) > array.Length)
                throw new ArgumentException("arrayIndex + enumerable.Count > array.Length");

            int index = arrayIndex;
            foreach (var v in enumerable)
                array.SetValue(v, index++);
        }

        #region ICollection<KeyValuePair<TKey, TValue>>
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> kv)
        {
            if (!AddIfAbsent(kv.Key, kv.Value))
                throw new ArgumentException(Invariant($"Key '{kv.Key}' already exists"));
        }
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> kv)
        {
            if (TryGetValue(kv.Key, out TValue mapped))
                return mapped?.Equals(kv.Value) ?? kv.Value == null;
            return false;
        }
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => CopyEnumerableTo(array, arrayIndex, this);
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!((ICollection<KeyValuePair<TKey, TValue>>)this).Contains(item))
                return false;
            Remove(item.Key);
            return true;
        }
        #endregion

        #region IEnumerable<TKey>
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        #endregion

        #region IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        #endregion

        #region ISerializable
        private SerializationInfo _pendingSerializetionInfo;
        protected OrderedDictionary(SerializationInfo info, StreamingContext context)
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
            TValue[] values = (TValue[])_pendingSerializetionInfo.GetValue("V", typeof(TValue[]));

            if ((keys?.Length ?? -1) != (values?.Length ?? -1))
                throw new SerializationException();

            if (keys != null)
            {
                Resize(NextValidCapacity(keys.Length));
                for (int ndx = 0; ndx < keys.Length; ++ndx)
                    AddOrReplace(keys[ndx], values[ndx]);
            }

            _pendingSerializetionInfo = null;
        }
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            TKey[] keys = null;
            TValue[] values = null;
            int count = Count;

            if (count > 0)
            {
                keys = new TKey[count];
                values = new TValue[count];

                int index = 0;
                foreach (var v in this)
                {
                    keys[index] = v.Key;
                    values[index] = v.Value;
                    index++;
                }
            }

            info.AddValue("K", keys);
            info.AddValue("V", values);
        }
        #endregion

        #region ICollection
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        void ICollection.CopyTo(Array array, int arrayIndex) => CopyEnumerableTo<KeyValuePair<TKey, TValue>, OrderedDictionary<TKey, TValue>>(array, arrayIndex, this);
        #endregion

        #region IDictionary<TKey, TValue>
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        #endregion

        #region IDictionary
        bool IDictionary.IsFixedSize => false;
        bool IDictionary.IsReadOnly => false;
        ICollection IDictionary.Keys => Keys;
        ICollection IDictionary.Values => Values;
        object IDictionary.this[object key]
        {
            get
            {
                if (!TryCastToTypedKey(key, out TKey typedKey))
                    return null;
                if (!TryGetValue(typedKey, out TValue value))
                    return null;
                return value;
            }
            set
            {
                if (!TryCastToTypedKey(key, out TKey typedKey))
                    throw new ArgumentException(Invariant($"Key '{key}' ({key?.GetType().Name ?? "null"}) must be of type {typeof(TKey).Name}"));
                if (value is TValue typedValue)
                    AddOrReplace(typedKey, typedValue);
                else if (value == null && !typeof(TValue).IsValueType)
                    AddOrReplace(typedKey, default);
                else
                    throw new ArgumentException(Invariant($"Value ({value}) must be of type {typeof(TValue).Name}"));
            }
        }
        void IDictionary.Add(object key, object value)
        {
            if (!TryCastToTypedKey(key, out TKey typedKey))
                throw new ArgumentException(Invariant($"Key '{key}' ({key?.GetType().Name ?? "null"}) must be of type {typeof(TKey).Name}"));
            bool wasAdded;
            if (value is TValue typedValue)
                wasAdded = AddIfAbsent(typedKey, typedValue);
            else if (value == null && !typeof(TValue).IsValueType)
                wasAdded = AddIfAbsent(typedKey, default);
            else
                throw new ArgumentException(Invariant($"Value ({value}) must be of type {typeof(TValue).Name}"));
            if (!wasAdded)
                throw new ArgumentException(Invariant($"Key '{key}' already exists"));
        }
        bool IDictionary.Contains(object key)
        {
            if (TryCastToTypedKey(key, out TKey typedKey))
                return ContainsKey(typedKey);
            return false;
        }
        IDictionaryEnumerator IDictionary.GetEnumerator() => new DictionaryEnumerator(this);
        void IDictionary.Remove(object key)
        {
            if (TryCastToTypedKey(key, out TKey typedKey))
                Remove(typedKey);
        }
        bool TryCastToTypedKey(object key, out TKey typedKey)
        {
            if (key is TKey typedKey_)
            {
                typedKey = typedKey_;
                return true;
            }
            else if (key == null && !typeof(TKey).IsValueType)
            {
                // Null is valid key if key is a reference type, but not if it is a value type
                typedKey = default;
                return true;
            }
            typedKey = default;
            return false;
        }
        #endregion

        #region IReadOnlyDictionary<TKey, TValue>
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
        #endregion
        private class DebugView
        {
            private OrderedDictionary<TKey,TValue> _container;
            private DebugView(OrderedDictionary<TKey, TValue> container)
            {
                _container = container;
            }

            [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.RootHidden)]
            public KeyValuePair<TKey, TValue>[] Items
            {
                get
                {
                    KeyValuePair<TKey, TValue>[] items = new KeyValuePair<TKey, TValue>[_container?.Count ?? 0];
                    ((ICollection<KeyValuePair<TKey, TValue>>)_container)?.CopyTo(items, 0);
                    return items;
                }
            }
        }
    }
}
