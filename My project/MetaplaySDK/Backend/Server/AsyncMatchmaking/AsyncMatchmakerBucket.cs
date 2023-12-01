// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Model;

namespace Metaplay.Server.Matchmaking
{
    public enum BucketFillLevelThreshold
    {
        Always,
        LowPopulation,
        HighPopulation,
        Never,
    }

    /// <summary>
    /// <para>
    /// A MatchmakerBucket is essentially just a fixed-size hashset, where the player's <see cref="EntityId"/> is used as the key.
    /// </para>
    /// <para>
    /// When a player is inserted, an index is calculated from the player's <see cref="EntityId"/> by hashing it, and using the
    /// modulo operator to map it to the range of the underlying array's capacity. If there is an existing player in the same
    /// index, the old player is replaced with the new one. This means that some players will get replaced sooner than others by luck,
    /// but every player has roughly the same chance to be replaced.
    /// </para>
    /// <para>
    /// Iterating all the players in a bucket is roughly a O(n) operation, where n is the capacity of the bucket.
    /// This means that iterating an empty bucket is going to take almost as long as iterating a full one.
    /// When fulfilling matchmaking queries, a whole bucket is almost never iterated to the end. Iteration ends when enough
    /// candidates are found, so in actuality the iteration will be very fast if the buckets are mostly filled.
    /// </para>
    /// <para>
    /// Insert, update, delete and get operations that happen via EntityId are O(1).
    /// </para>
    /// </summary>
    /// <typeparam name="TMmPlayerModel"></typeparam>
    [MetaSerializableDerived(1)]
    [MetaReservedMembers(11, 20)]
    public sealed class AsyncMatchmakerBucket<TMmPlayerModel> : ReplaceOnInsertCappedDictionary<EntityId, TMmPlayerModel> where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
    {
        public const float BucketLowPopThreshold  = 0.2f;
        public const float BucketHighPopThreshold = 0.8f;

        [MetaMember(11)] public ulong          HashSeed     { get; set; }
        [MetaMember(12)] public IBucketLabel[] Labels       { get; set; }

        /// <summary>
        /// Returns true if Count is less or equals to 20% of the Capacity
        /// </summary>
        public bool IsLowPopulation => Count <= (Capacity * BucketLowPopThreshold);

        /// <summary>
        /// Returns true if Count is more or equals to 80% of the Capacity
        /// </summary>
        public bool IsHighPopulation => Count >= (Capacity * BucketHighPopThreshold);

        /// <summary>
        /// Returns 0 at low population threshold, 1 at high population threshold.
        /// These thresholds are set at 20% and 80% respectively.
        /// </summary>
        public float PopulationDensity =>
            Math.Clamp(
                ((Count / (float) Capacity) - BucketLowPopThreshold) / (BucketHighPopThreshold - BucketLowPopThreshold),
                0, 1);

        internal AsyncMatchmakerBucket() : base() { }

        public AsyncMatchmakerBucket(int capacity, IBucketLabel[] labels) : base(capacity)
        {
            HashSeed = RandomPCG.CreateNew().NextULong();
            Labels   = labels;
        }

        protected override int IndexFor(EntityId key)
        {
            ulong val = key.Value;

            // Hash entityId since playerUpdates are already mapped to id.Value % NumMatchmakers
            unchecked
            {
                val ^= HashSeed;
                val *= 31;
                val ^= HashSeed;
            }

            return (int) (val % (ulong) Capacity);
        }

        protected override EntityId KeyOf(in TMmPlayerModel item)
        {
            return item.PlayerId;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public void ValidateState(AsyncMatchmakerOptionsBase mmOpts)
        {
            MetaDebug.Assert(Labels != null, "ValidateState failed in MmrBucket: Labels is null!");
            MetaDebug.Assert(Labels.All(x => x != null), "ValidateState failed in MmrBucket: Labels contains a null label!");
            MetaDebug.Assert(Count >= 0, "ValidateState failed in MmrBucket: Count is less than 0");
            MetaDebug.Assert(Count <= Capacity, "ValidateState failed in MmrBucket: Count is more than Capacity");
            MetaDebug.Assert(Capacity >= mmOpts.MmrBucketMinSize, "ValidateState failed in MmrBucket: Capacity is less than minimum");
            MetaDebug.Assert(Items.Count(x => x.IsSet) == Count, "ValidateState failed in MmrBucket: true count is different from Count");
        }

        public IEnumerator<TMmPlayerModel> GetRandomOrderEnumerator()
        {
            PrimeRandomOrderEnumerator<DictionaryItem> enumerator = new PrimeRandomOrderEnumerator<DictionaryItem>(Items);

            for (int i = 0; i < Count; i++)
            {
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.IsSet)
                        yield return enumerator.Current.Value;
                }
            }
        }

        public bool IsAboveThreshold(BucketFillLevelThreshold threshold)
        {
            switch (threshold)
            {
                case BucketFillLevelThreshold.Always:
                    return true;
                case BucketFillLevelThreshold.Never:
                    return false;
                case BucketFillLevelThreshold.HighPopulation:
                    return IsHighPopulation;
                case BucketFillLevelThreshold.LowPopulation:
                    return !IsLowPopulation;
                default:
                    throw new ArgumentOutOfRangeException(nameof(threshold), threshold, null);
            }
        }

        public bool LabelsEquals(IBucketLabel[] otherLabels)
        {
            if (Labels.Length != otherLabels.Length)
                return false;

            for (int i = 0; i < Labels.Length; i++)
            {
                if (!Labels[i].Equals(otherLabels[i]))
                    return false;
            }

            return true;
        }

        public int LabelsHash()
            => Labels.HashLabels();

        public int GetMmrLow()
        {
            return Labels.OfType<MmrBucketingStrategyLabel>().FirstOrDefault()?
                .MmrLow ?? 0;
        }

        public int GetMmrHigh()
        {
            return Labels.OfType<MmrBucketingStrategyLabel>().FirstOrDefault()?
                .MmrHigh ?? 0;
        }
    }

    /// <summary>
    /// A dictionary-like data structure that is preallocated to a given size.
    /// The key must be contained within- or can be calculated from the value.
    /// If an inserted element indexes to the same index as an existing one, the old item is replaced.
    /// </summary>
    /// <typeparam name="TKey">The key type to use. Must be <see cref="IEquatable{T}"/> and a struct-type</typeparam>
    /// <typeparam name="TValue">The value type to use. Must contain or otherwise provide a member of type <typeparamref name="TKey"/></typeparam>
    [MetaSerializable]
    [MetaReservedMembers(1, 10)]
    public abstract class ReplaceOnInsertCappedDictionary<TKey, TValue> : IEnumerable<TValue>,
        IReadOnlyDictionary<TKey, TValue>
        where TKey : struct, IEquatable<TKey>
    {
        [MetaSerializable]
        public struct DictionaryItem
        {
            [MetaMember(1)] public TValue Value;
            [MetaMember(2)] public bool   IsSet;

            public DictionaryItem(TValue value, bool isSet)
            {
                Value = value;
                IsSet = isSet;
            }
        }

        public int  Capacity => Items?.Length ?? 0;
        public bool IsFull   => Count == Capacity;

        [MetaMember(1)] public    int              Count { get; protected set; }
        [MetaMember(2)] protected DictionaryItem[] Items { get; private set; }


        protected ReplaceOnInsertCappedDictionary() { }

        protected ReplaceOnInsertCappedDictionary(int capacity)
        {
            Count = 0;
            Items = new DictionaryItem[capacity];
        }

        /// <summary>
        /// Resize the underlying array of the dictionary.
        /// Existing items might index into the same slot after resizing,
        /// so loss of data is possible.
        /// </summary>
        public void Resize(int newCapacity)
        {
            int oldCapacity = Capacity;

            if (oldCapacity == newCapacity)
                return;

            DictionaryItem[] oldItems = Items;
            DictionaryItem[] newItems = new DictionaryItem[newCapacity];

            Items = newItems;

            foreach (DictionaryItem oldItem in oldItems)
            {
                if (oldItem.IsSet)
                {
                    int newIndex = IndexFor(KeyOf(oldItem.Value));
                    newItems[newIndex] = oldItem;
                }
            }

            Count = Items.Count(x => x.IsSet);
        }

        /// <summary>
        /// Insert an item into the dictionary and replace the old item if a slot is full.
        /// If a previous item was replaced, the replaced item's <typeparamref name="TKey"/> is returned. Otherwise this method returns null.
        /// If an item with the same key already exists in the slot, the item is updated instead.
        /// </summary>
        /// <returns>A <see cref="Nullable{TKey}"/> of <typeparamref name="TKey"/> of the replaced item, if any.</returns>
        public TKey? InsertOrReplace(in TValue item)
        {
            TKey added = KeyOf(item);
            int index = IndexFor(added);

            if (Items[index].IsSet && KeyOf(Items[index].Value).Equals(added))
            {
                Update(item);
                return null;
            }

            TKey? removed = null;

            if (Items[index].IsSet)
            {
                removed = KeyOf(Items[index].Value);
                Count--;
            }

            Items[index] = new(item, true);

            Count++;

            return removed;
        }

        /// <summary>
        /// Remove an item by its <typeparamref name="TKey"/>. Does nothing if the item does not exist
        /// </summary>
        public void RemoveById(TKey item)
        {
            int index = IndexFor(item);

            if (!Items[index].IsSet || !KeyOf(Items[index].Value).Equals(item))
                return;


            Items[index] = default;

            Count--;
        }

        /// <summary>
        /// Update an existing item in the dictionary. The method does nothing
        /// if an item with the same <typeparamref name="TKey"/> does not exist.
        /// </summary>
        public void Update(in TValue item)
        {
            TKey updated = KeyOf(item);
            int index = IndexFor(updated);

            if (!Items[index].IsSet || !KeyOf(Items[index].Value).Equals(updated))
                return;

            Items[index] = new(item, true);
        }

        protected abstract TKey KeyOf(in TValue item);

        protected virtual int IndexFor(TKey key) => key.GetHashCode() % Capacity;

        #region IReadOnlyDictionary members

        public bool ContainsKey(TKey key)
        {
            int index = IndexFor(key);
            return Items[index].IsSet && KeyOf(Items[index].Value).Equals(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            int index = IndexFor(key);

            if (Items[index].IsSet && KeyOf(Items[index].Value).Equals(key))
            {
                value = Items[index].Value;
                return true;
            }

            value = default;
            return false;
        }

        public TValue this[TKey key]
        {
            get
            {
                int index = IndexFor(key);
                return Items[index].IsSet && KeyOf(Items[index].Value).Equals(key)
                    ? Items[index].Value
                    : throw new KeyNotFoundException();
            }
        }

        #endregion

        #region Enumeration members

        public ValueEnumerator GetEnumerator()
        {
            return new(Items);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.
            GetEnumerator()
        {
            foreach (TValue item in this)
            {
                yield return new(KeyOf(item), item);
            }
        }

        public IEnumerable<TKey> Keys
        {
            get
            {
                for (int i = 0; i < Capacity; i++)
                {
                    if (Items[i].IsSet)
                        yield return KeyOf(Items[i].Value);
                }
            }
        }

        public IEnumerable<TValue> Values => this;

        public struct ValueEnumerator : IEnumerator<TValue>
        {
            readonly DictionaryItem[] _arr;
            int                       _it;

            public ValueEnumerator(DictionaryItem[] arr)
            {
                _arr = arr;
                _it  = -1;
            }

            public bool MoveNext()
            {
                while (++_it < _arr.Length)
                {
                    if (_arr[_it].IsSet)
                        return true;
                }

                return false;
            }

            public void Reset()
            {
                _it = -1;
            }

            public TValue Current => _arr[_it].Value;

            object IEnumerator.Current => Current;

            public void Dispose() { }
        }

        #endregion
    }
}
