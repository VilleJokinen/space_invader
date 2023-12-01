// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// Operation enqueued by Guild member (Player) (to be executed by the Guild).
    /// </summary>
    [MetaSerializable]
    public abstract class GuildMemberGuildOpLogEntry
    {
    }

    /// <summary>
    /// Operation enqueued on Guild member (Player) (to be executed by the Player).
    /// </summary>
    [MetaSerializable]
    public abstract class GuildMemberPlayerOpLogEntry
    {
    }
}

#endif
