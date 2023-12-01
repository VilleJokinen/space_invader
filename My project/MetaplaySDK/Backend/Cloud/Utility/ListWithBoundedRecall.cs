// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Collections.Generic;
using System.Linq;
using Metaplay.Core.Model;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Contains a count and a bounded list of most recent items.
    /// </summary>
    /// <typeparam name="TItem">The type of item</typeparam>
    [MetaSerializable]
    public class ListWithBoundedRecall<TItem>
    {
        [MetaMember(1)] public int Count { get; private set; } = 0;
        [MetaMember(2)] public List<TItem> Recent { get; private set; } = new List<TItem>();
        [MetaMember(3)] public int MaxRecentItemsRemembered { get; private set; } = 10;

        public ListWithBoundedRecall() { }
        public ListWithBoundedRecall(int maxRecentItemsRemembered) { MaxRecentItemsRemembered = maxRecentItemsRemembered; }

        public void Add(TItem item)
        {
            Count++;

            Recent.Add(item);
            while (Recent.Count > MaxRecentItemsRemembered)
                Recent.RemoveAt(0);
        }

        public void AddAllFrom(ListWithBoundedRecall<TItem> other)
        {
            Count += other.Count;
            Recent = Recent.Concat(other.Recent).TakeLast(MaxRecentItemsRemembered).ToList();
        }
    }
}
