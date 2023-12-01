// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// Defines the set of roles there can be in a Guild.
    ///
    /// <para>
    /// The roles defined here can changed to fit game needs. Note that the roles here have no inherent meaning. All behavior
    /// tied to the roles is defined by <see cref="GuildModel"/>. Specifically, to change guild roles, you need to change this
    /// enum here and change the behavior in the following places: <br/>
    /// * Permissions tied to roles in IGuildModelBase.HasPermissionTo* methods. <br/>
    /// * Role mutation rules in <see cref="IGuildModelBase.ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent, EntityId, GuildMemberRole)"/>. <br/>
    /// * Dashboard rendering in game_specific/GuildMemberListEntry.vue <br/>
    /// * Dashboard selection in GuildActionEditRole.vue <br/>
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum GuildMemberRole
    {
        // Example mode: We have

        Leader = 0,
        MiddleTier = 1,
        LowTier = 2
    }
}

#endif
