// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.Guild.Actions
{
    /*
     * This file contains commonly used guild actions. These are standard / example implementations of
     * various operations.
     */

    [ModelAction(ActionCodesCore.GuildMemberKick)]
    public class GuildMemberKick : GuildClientActionBase
    {
        public EntityId                 TargetPlayerId      { get; private set; }
        public IGuildMemberKickReason   KickReasonOrNull    { get; private set; }

        GuildMemberKick() { }
        public GuildMemberKick(EntityId targetPlayerId, IGuildMemberKickReason kickReasonOrNull)
        {
            TargetPlayerId = targetPlayerId;
            KickReasonOrNull = kickReasonOrNull;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (!guild.TryGetMember(TargetPlayerId, out GuildMemberBase kickedMember))
                return MetaActionResult.NoSuchGuildMember;
            if (!guild.HasPermissionToKickMember(InvokingPlayerId, TargetPlayerId))
                return MetaActionResult.GuildOperationNotPermitted;

            if (commit)
            {
                guild.Log.Debug("Player {PlayerId} is kicked from {GuildId} by {KickerPlayerId}. Reason={Reason}.", TargetPlayerId, guild.GuildId, InvokingPlayerId, PrettyPrint.Compact(KickReasonOrNull));
                guild.RemoveMemberAndUpdateRoles(TargetPlayerId);
                guild.ServerListenerCore.PlayerKicked(kickedPlayerId: TargetPlayerId, kickedMember: kickedMember, kickingPlayerId: InvokingPlayerId, KickReasonOrNull);
                guild.ServerListenerCore.MemberRemoved(TargetPlayerId);
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.GuildMemberEditRole)]
    public class GuildMemberEditRole : GuildClientActionBase
    {
        public EntityId                                     TargetPlayerId  { get; private set; }
        public GuildMemberRole                              TargetNewRole   { get; private set; }
        public OrderedDictionary<EntityId, GuildMemberRole> ExpectedChanges { get; private set; }

        GuildMemberEditRole() { }
        public GuildMemberEditRole(EntityId targetPlayerId, GuildMemberRole targetNewRole, OrderedDictionary<EntityId, GuildMemberRole> expectedChanges)
        {
            TargetPlayerId = targetPlayerId;
            TargetNewRole = targetNewRole;
            ExpectedChanges = expectedChanges;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (!guild.TryGetMember(InvokingPlayerId, out GuildMemberBase invokingMember))
                return MetaActionResult.NoSuchGuildMember;
            if (!guild.TryGetMember(TargetPlayerId, out GuildMemberBase targetMember))
                return MetaActionResult.NoSuchGuildMember;

            if (!guild.HasPermissionToChangeRoleTo(InvokingPlayerId, TargetPlayerId, TargetNewRole))
                return MetaActionResult.GuildOperationNotPermitted;

            // Check that ExpectedChanges are really what we will get out. Otherwise roles
            // might have changed, and user does not get what user expected.
            OrderedDictionary<EntityId, GuildMemberRole> actualChanges = guild.ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberEdit, TargetPlayerId, TargetNewRole);
            if (ExpectedChanges.Count != actualChanges.Count)
                return MetaActionResult.GuildOperationStale;
            foreach ((EntityId actualPlayerId, GuildMemberRole actualRole) in actualChanges)
            {
                if (!ExpectedChanges.TryGetValue(actualPlayerId, out GuildMemberRole expectedRole))
                    return MetaActionResult.GuildOperationStale;
                if (expectedRole != actualRole)
                    return MetaActionResult.GuildOperationStale;
            }

            if (commit)
            {
                guild.ApplyMemberRoleChanges(ExpectedChanges);
            }
            return MetaActionResult.Success;
        }
    }
}

#endif
