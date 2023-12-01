// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core;
using Metaplay.Core.GuildDiscovery;
using System.Collections.Generic;

namespace Metaplay.Server.GuildDiscovery
{
    /// <summary>
    /// A pool of <see cref="GuildInfo"/>s. A pool contains a subset of guilds
    /// that fulfill a certain pre-filter criteria. For example, a certain pool could contain
    /// guilds that are larger than a certain size.
    /// </summary>
    public interface IGuildDiscoveryPool
    {
        /// <summary>
        /// The combination of public <see cref="GuildDiscoveryInfo"/> (shared with client) and potential
        /// private data <see cref="GuildDiscoveryServerOnlyInfo"/> (not shared with clients) that is needed in search and recommendation filtering
        /// and scoring. This is created in <see cref="Metaplay.Server.Guild.GuildActorBase{TModel, TPersisted}.CreateGuildDiscoveryInfo"/>.
        /// </summary>
        public readonly struct GuildInfo
        {
            public readonly GuildDiscoveryInfoBase              PublicDiscoveryInfo;
            public readonly GuildDiscoveryServerOnlyInfoBase    ServerOnlyDiscoveryInfo;

            public GuildInfo(GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo)
            {
                PublicDiscoveryInfo = publicDiscoveryInfo;
                ServerOnlyDiscoveryInfo = serverOnlyDiscoveryInfo;
            }
        }

        public readonly struct InspectionInfo
        {
            public readonly GuildInfo   GuildInfo;
            public readonly bool        PassesFilter;
            public readonly MetaTime    LastRefreshedAt;

            public InspectionInfo(GuildInfo guildInfo, bool passesFilter, MetaTime lastRefreshedAt)
            {
                GuildInfo = guildInfo;
                PassesFilter = passesFilter;
                LastRefreshedAt = lastRefreshedAt;
            }
        }

        /// <summary>
        /// Identifies the pool instance.
        /// </summary>
        string PoolId { get; }

        /// <summary>
        /// The number of GuildInfos currently in the pool.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Fetches at most <paramref name="maxCount"/> valid pool entries that are suitable for <paramref name="playerContext"/>. Call may modify pool state.
        /// </summary>
        List<GuildInfo> Fetch(GuildDiscoveryPlayerContextBase playerContext, int maxCount);

        /// <summary>
        /// Fetches at most <paramref name="maxCount"/> pool entries without modifying pool state, and inspects the entry validity.
        /// </summary>
        List<InspectionInfo> Inspect(int maxCount);
    }
}

#endif
