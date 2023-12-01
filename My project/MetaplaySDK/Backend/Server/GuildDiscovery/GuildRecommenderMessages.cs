// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;

namespace Metaplay.Server.GuildDiscovery
{
    [MetaMessage(MessageCodesCore.InternalGuildRecommendationRequest, MessageDirection.ServerInternal)]
    public class InternalGuildRecommendationRequest : MetaMessage
    {
        public GuildDiscoveryPlayerContextBase Context;

        public InternalGuildRecommendationRequest() { }
        public InternalGuildRecommendationRequest(GuildDiscoveryPlayerContextBase context) { Context = context; }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommendationResponse, MessageDirection.ServerInternal)]
    public class InternalGuildRecommendationResponse : MetaMessage
    {
        public List<GuildDiscoveryInfoBase> GuildInfos;

        public InternalGuildRecommendationResponse() { }
        public InternalGuildRecommendationResponse(List<GuildDiscoveryInfoBase> guildInfos)
        {
            GuildInfos = guildInfos;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderGuildUpdate, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderGuildUpdate : MetaMessage
    {
        public GuildDiscoveryInfoBase PublicDiscoveryInfo;
        public GuildDiscoveryServerOnlyInfoBase ServerOnlyDiscoveryInfo;

        public InternalGuildRecommenderGuildUpdate() { }
        public InternalGuildRecommenderGuildUpdate(GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo)
        {
            PublicDiscoveryInfo = publicDiscoveryInfo;
            ServerOnlyDiscoveryInfo = serverOnlyDiscoveryInfo;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderGuildRemove, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderGuildRemove : MetaMessage
    {
        public EntityId GuildId;

        public InternalGuildRecommenderGuildRemove() { }
        public InternalGuildRecommenderGuildRemove(EntityId guildId)
        {
            GuildId = guildId;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderInspectPoolsRequest, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderInspectPoolsRequest : MetaMessage
    {
        public InternalGuildRecommenderInspectPoolsRequest() { }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderInspectPoolsResponse, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderInspectPoolsResponse : MetaMessage
    {
        [MetaSerializable]
        public struct PoolInfo
        {
            [MetaMember(1)] public string PoolId;
            [MetaMember(2)] public int Count;

            public PoolInfo(string poolId, int count)
            {
                PoolId = poolId;
                Count = count;
            }
        }
        public List<PoolInfo> PoolInfos;

        InternalGuildRecommenderInspectPoolsResponse() { }
        public InternalGuildRecommenderInspectPoolsResponse(List<PoolInfo> poolInfos)
        {
            PoolInfos = poolInfos;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderInspectPoolRequest, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderInspectPoolRequest : MetaMessage
    {
        public string Poold { get; private set; }
        public int MaxCount { get; private set; }

        InternalGuildRecommenderInspectPoolRequest() { }
        public InternalGuildRecommenderInspectPoolRequest(string poold, int maxCount)
        {
            Poold = poold;
            MaxCount = maxCount;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderInspectPoolResponse, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderInspectPoolResponse : MetaMessage
    {
        [MetaSerializable]
        public struct PoolEntry
        {
            [MetaMember(1)] public bool                             PassesFilter;
            [MetaMember(2)] public GuildDiscoveryInfoBase           PublicInfo;
            [MetaMember(3)] public GuildDiscoveryServerOnlyInfoBase ServerInfo;
            [MetaMember(4)] public MetaTime                         LastRefreshedAt;

            public PoolEntry(bool passesFilter, GuildDiscoveryInfoBase publicInfo, GuildDiscoveryServerOnlyInfoBase serverInfo, MetaTime lastRefreshedAt)
            {
                PassesFilter = passesFilter;
                PublicInfo = publicInfo;
                ServerInfo = serverInfo;
                LastRefreshedAt = lastRefreshedAt;
            }
        }
        public List<PoolEntry> Entries;

        InternalGuildRecommenderInspectPoolResponse() { }
        public InternalGuildRecommenderInspectPoolResponse(List<PoolEntry> entries)
        {
            Entries = entries;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderTestGuildRequest, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderTestGuildRequest : MetaMessage
    {
        public GuildDiscoveryInfoBase           PublicDiscoveryInfo     { get; private set; }
        public GuildDiscoveryServerOnlyInfoBase ServerOnlyDiscoveryInfo { get; private set; }

        InternalGuildRecommenderTestGuildRequest() { }
        public InternalGuildRecommenderTestGuildRequest(GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo)
        {
            PublicDiscoveryInfo = publicDiscoveryInfo;
            ServerOnlyDiscoveryInfo = serverOnlyDiscoveryInfo;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildRecommenderTestGuildResponse, MessageDirection.ServerInternal)]
    public class InternalGuildRecommenderTestGuildResponse : MetaMessage
    {
        [MetaSerializable]
        public struct PoolStatus
        {
            [MetaMember(1)] public bool IncludedInPool;
            [MetaMember(2)] public bool PoolDataPassesFilter;
            [MetaMember(3)] public bool FreshDataPassesFilter;

            public PoolStatus(bool includedInPool, bool poolDataPassesFilter, bool freshDataPassesFilter)
            {
                IncludedInPool = includedInPool;
                PoolDataPassesFilter = poolDataPassesFilter;
                FreshDataPassesFilter = freshDataPassesFilter;
            }
        }
        public OrderedDictionary<string, PoolStatus> Pools;

        InternalGuildRecommenderTestGuildResponse() { }
        public InternalGuildRecommenderTestGuildResponse(OrderedDictionary<string, PoolStatus> pools)
        {
            Pools = pools;
        }
    }
}

#endif
