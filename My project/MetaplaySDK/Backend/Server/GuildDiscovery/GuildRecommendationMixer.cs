// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core;
using Metaplay.Core.GuildDiscovery;
using System.Collections.Generic;

namespace Metaplay.Server.GuildDiscovery
{
    /// <summary>
    /// Helper utility for combining guild recommendations from multiple sources
    /// and providing a selection ("mix") from them with a certain requirements.
    /// </summary>
    public class GuildRecommendationMixer
    {
        OrderedDictionary<EntityId, IGuildDiscoveryPool.GuildInfo> _mustBeIncludedSet = new OrderedDictionary<EntityId, IGuildDiscoveryPool.GuildInfo>();
        List<IGuildDiscoveryPool.GuildInfo> _fillerset = new List<IGuildDiscoveryPool.GuildInfo>();

        public GuildRecommendationMixer()
        {
        }

        /// <summary>
        /// Mixes in the result from a pool query. The resulting mix will have at least <paramref name="minCount"/>
        /// guilds from this pool, and at most <paramref name="maxCount"/> guild.
        /// </summary>
        public void AddSource(IGuildDiscoveryPool pool, GuildDiscoveryPlayerContextBase playerContext, int minCount, int maxCount)
        {
            List<IGuildDiscoveryPool.GuildInfo> infos = pool.Fetch(playerContext, maxCount);
            SortByDescendingQualityInPlace(infos);

            int ndx = 0;
            int numAddedToMustSet = 0;

            // add must-haves

            for (; ndx < infos.Count; ++ndx)
            {
                if (numAddedToMustSet >= minCount)
                    break;
                if (_mustBeIncludedSet.AddIfAbsent(infos[ndx].PublicDiscoveryInfo.GuildId, infos[ndx]))
                    numAddedToMustSet++;
            }

            // the rest

            for (; ndx < infos.Count; ++ndx)
                _fillerset.Add(infos[ndx]);
        }

        public List<GuildDiscoveryInfoBase> Mix(int maxCount)
        {
            // Construct the final mix into mustBeIncludedSet. If it has less than
            // required, we need to mix in some more.

            if (_mustBeIncludedSet.Count < maxCount)
            {
                SortByDescendingQualityInPlace(_fillerset);

                foreach (IGuildDiscoveryPool.GuildInfo filler in _fillerset)
                {
                    if (_mustBeIncludedSet.AddIfAbsent(filler.PublicDiscoveryInfo.GuildId, filler))
                    {
                        if (_mustBeIncludedSet.Count >= maxCount)
                            break;
                    }
                }
            }

            // Extract result from mustBeIncludedSet

            List<GuildDiscoveryInfoBase> result = new List<GuildDiscoveryInfoBase>(capacity: maxCount);
            foreach (IGuildDiscoveryPool.GuildInfo info in _mustBeIncludedSet.Values)
            {
                if (result.Count >= maxCount)
                    break;
                result.Add(info.PublicDiscoveryInfo);
            }

            // \todo: sort one last time?

            return result;
        }

        protected virtual void SortByDescendingQualityInPlace(List<IGuildDiscoveryPool.GuildInfo> infos)
        {
            // \todo: need to sort infos by some custom rule. This code should be close the other pooling logic.
        }
    }
}

#endif
