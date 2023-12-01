// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Utilities for collecting statistics, used in database scan jobs.
    /// </summary>
    public static class StatsUtil
    {
        public static void IncrementCounter<TKey>(IDictionary<TKey, int> dict, TKey key)
        {
            IncreaseCounter(dict, key, byAmount: 1);
        }

        public static void IncreaseCounter<TKey>(IDictionary<TKey, int> dict, TKey key, int byAmount)
        {
            dict[key] = GetOrDefault(dict, key, 0) + byAmount;
        }

        public static void AccumulateValue<TKey, TValue>(IDictionary<TKey, TValue> dict, TKey key, TValue delta, Func<TValue, TValue, TValue> sum) where TValue : struct
        {
            dict[key] = sum(GetOrDefault(dict, key, new TValue()), delta);
        }

        public static void AddToList<TKey, TItem>(IDictionary<TKey, ListWithBoundedRecall<TItem>> dict, TKey key, TItem item)
        {
            GetOrAddDefaultConstructed(dict, key).Add(item);
        }

        public static void AddToListAllFrom<TKey, TItem>(IDictionary<TKey, ListWithBoundedRecall<TItem>> dict, TKey key, ListWithBoundedRecall<TItem> items)
        {
            GetOrAddDefaultConstructed(dict, key).AddAllFrom(items);
        }

        public static void AggregateCounters<TKey>(IDictionary<TKey, int> dst, IDictionary<TKey, int> src)
        {
            foreach ((TKey key, int srcCounter) in src)
                IncreaseCounter(dst, key, srcCounter);
        }

        public static void AggregateValues<TKey, TValue>(IDictionary<TKey, TValue> dst, IDictionary<TKey, TValue> src, Func<TValue, TValue, TValue> sum) where TValue : struct
        {
            foreach ((TKey key, TValue srcValue) in src)
                AccumulateValue(dst, key, srcValue, sum);
        }

        public static void AggregateLists<TKey, TItem>(IDictionary<TKey, ListWithBoundedRecall<TItem>> dst, IDictionary<TKey, ListWithBoundedRecall<TItem>> src)
        {
            foreach ((TKey key, ListWithBoundedRecall<TItem> srcItems) in src)
                AddToListAllFrom(dst, key, srcItems);
        }

        static TValue GetOrDefault<TKey, TValue>(IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue) where TValue : struct
        {
            if (dict.TryGetValue(key, out TValue existingValue))
                return existingValue;
            else
                return defaultValue;
        }

        public static TValue GetOrAddDefaultConstructed<TKey, TValue>(IDictionary<TKey, TValue> dict, TKey key) where TValue : class, new()
        {
            if (!dict.TryGetValue(key, out TValue item))
            {
                item = new TValue();
                dict.Add(key, item);
            }

            return item;
        }
    }
}
