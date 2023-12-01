// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;

namespace Metaplay.Server.GuildDiscovery
{
    /// <summary>
    /// The player context in which the discovery operations are made. This might
    /// for example contain the level of the player allow it to be taken into the
    /// account when recommending guilds.
    /// </summary>
    [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
    [MetaReservedMembers(100, 200)]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public abstract class GuildDiscoveryPlayerContextBase
    {
    }
}

#endif
