// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core;
using Metaplay.Core.Model;

namespace Metaplay.Server.GuildDiscovery
{
    /// <summary>
    /// Contains the server-only information of a guild in guild search or recommendation (discovery)
    /// computation. These are filled in <see cref="Metaplay.Server.Guild.GuildActorBase{TModel, TPersisted}.CreateGuildDiscoveryInfo"/>.
    ///
    /// To add additional data, add the new fields in GuildDiscoveryInfo and fill them in
    /// CreateGuildDiscoveryInfo.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildDiscoveryServerOnlyInfoBase
    {
        [MetaMember(1)] public MetaTime GuildCreatedAt;

        /// <summary>
        /// The lastest point in time when a currently member player was online. In particular,
        /// if a new player joins, is online and then leaves, this value will return to the original
        /// value.
        /// </summary>
        [MetaMember(2)] public MetaTime MemberOnlineLatestAt;

        public GuildDiscoveryServerOnlyInfoBase() { }
        protected GuildDiscoveryServerOnlyInfoBase(MetaTime guildCreatedAt, MetaTime memberOnlineLatestAt)
        {
            GuildCreatedAt = guildCreatedAt;
            MemberOnlineLatestAt = memberOnlineLatestAt;
        }
    }
}

#endif
