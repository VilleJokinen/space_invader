// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;

namespace Metaplay.Server.GuildDiscovery
{
    [MetaMessage(MessageCodesCore.InternalGuildSearchRequest, MessageDirection.ServerInternal)]
    public class InternalGuildSearchRequest : MetaMessage
    {
        public GuildSearchParamsBase SearchParams;
        public GuildDiscoveryPlayerContextBase SearchContext;

        public InternalGuildSearchRequest() { }
        public InternalGuildSearchRequest(GuildSearchParamsBase searchParams, GuildDiscoveryPlayerContextBase searchContext)
        {
            SearchParams = searchParams;
            SearchContext = searchContext;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildSearchResponse, MessageDirection.ServerInternal)]
    public class InternalGuildSearchResponse : MetaMessage
    {
        public bool IsError;
        public List<GuildDiscoveryInfoBase> GuildInfos;

        public InternalGuildSearchResponse() { }
        public InternalGuildSearchResponse(bool isError, List<GuildDiscoveryInfoBase> guildInfos)
        {
            IsError = isError;
            GuildInfos = guildInfos;
        }
    }
}

#endif
