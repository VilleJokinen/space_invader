// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;

namespace Metaplay.Core.GuildDiscovery
{
    /// <summary>
    /// Contains the public information of a guild in guild search or recommendation (discovery)
    /// result. These are filled in <see cref="GuildActor.CreateGuildDiscoveryInfo"/>.
    /// This class is split to two parts, Metaplay part (-Base suffix) and Game-specific (no suffix).
    ///
    /// To add additional data, add the new fields in GuildDiscoveryInfo and fill them in
    /// CreateGuildDiscoveryInfo.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildDiscoveryInfoBase
    {
        // These are Metaplay-builtin fields. Do not edit these

        [MetaMember(1)] public EntityId GuildId;
        [MetaMember(2)] public string   DisplayName;
        [MetaMember(3)] public int      NumMembers;
        [MetaMember(4)] public int      MaxNumMembers;

        public GuildDiscoveryInfoBase() { }
        protected GuildDiscoveryInfoBase(EntityId guildId, string displayName, int numMembers, int maxNumMembers)
        {
            GuildId = guildId;
            DisplayName = displayName;
            NumMembers = numMembers;
            MaxNumMembers = maxNumMembers;
        }
    }
}

#endif
