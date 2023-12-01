// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.Guild.Actions
{
    /*
     * This file contains Guild system actions. These are required and used internally by the guild system.
     */

    [ModelAction(ActionCodesCore.GuildMemberAdd)]
    public class GuildMemberAdd : GuildActionBase
    {
        public EntityId                     PlayerId            { get; private set; }
        public GuildMemberPlayerDataBase    PlayerData          { get; private set; }
        public int                          MemberInstanceId    { get; private set; }

        public GuildMemberAdd() { }
        public GuildMemberAdd(EntityId playerId, int memberInstanceId, GuildMemberPlayerDataBase playerData)
        {
            PlayerId = playerId;
            PlayerData = playerData;
            MemberInstanceId = memberInstanceId;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (commit)
            {
                guild.Log.Debug("Player {PlayerId} added to guild {GuildId}", PlayerId, guild.GuildId);
                guild.AddMemberAndUpdateRoles(PlayerId, MemberInstanceId, PlayerData);
                guild.ServerListenerCore.MemberPlayerDataUpdated(PlayerId);
                guild.ClientListenerCore.MemberPlayerDataUpdated(PlayerId);
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.GuildMemberRemove)]
    public class GuildMemberRemove : GuildActionBase
    {
        public EntityId                 PlayerId    { get; private set; } // \todo: how is this different from "InvokingPlayerId"

        public GuildMemberRemove() { }
        public GuildMemberRemove(EntityId playerId)
        {
            PlayerId = playerId;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (commit)
            {
                guild.Log.Debug("Player {PlayerId} left the guild {GuildId}", PlayerId, guild.GuildId);
                guild.RemoveMemberAndUpdateRoles(PlayerId);
                guild.ServerListenerCore.MemberRemoved(PlayerId);
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.GuildMemberIsOnlineUpdate)]
    public class GuildMemberIsOnlineUpdate : GuildActionBase
    {
        public EntityId                 PlayerId        { get; private set; } // \todo: how is this different from "InvokingPlayerId"
        public bool                     IsOnline        { get; private set; }
        public MetaTime                 LastUpdateAt    { get; private set; }

        public GuildMemberIsOnlineUpdate() { }
        public GuildMemberIsOnlineUpdate(EntityId playerId, bool isOnline, MetaTime lastUpdateAt)
        {
            PlayerId = playerId;
            IsOnline = isOnline;
            LastUpdateAt = lastUpdateAt;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (!guild.TryGetMember(PlayerId, out GuildMemberBase member))
                return MetaActionResult.NoSuchGuildMember;

            if (commit)
            {
                member.IsOnline = IsOnline;
                member.LastOnlineAt = LastUpdateAt;
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.GuildMemberPlayerDataUpdate)]
    public class GuildMemberPlayerDataUpdate : GuildActionBase
    {
        public EntityId                     PlayerId        { get; private set; } // \todo: InvokingPlayerId
        public GuildMemberPlayerDataBase    PlayerData      { get; private set; }

        public GuildMemberPlayerDataUpdate() { }
        public GuildMemberPlayerDataUpdate(EntityId playerId, GuildMemberPlayerDataBase playerData)
        {
            PlayerId = playerId;
            PlayerData = playerData;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (!guild.TryGetMember(PlayerId, out GuildMemberBase member))
                return MetaActionResult.NoSuchGuildMember;

            if (commit)
            {
                PlayerData.ApplyOnMember(member, guild, GuildMemberPlayerDataUpdateKind.UpdateMember);
                guild.ServerListenerCore.MemberPlayerDataUpdated(PlayerId);
                guild.ClientListenerCore.MemberPlayerDataUpdated(PlayerId);
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.GuildNameAndDescriptionUpdate)]
    public class GuildNameAndDescriptionUpdate : GuildActionBase
    {
        public string   DisplayName { get; private set; }
        public string   Description { get; private set; }

        public GuildNameAndDescriptionUpdate() { }
        public GuildNameAndDescriptionUpdate(string displayName, string description)
        {
            DisplayName = displayName;
            Description = description;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (commit)
            {
                guild.DisplayName = DisplayName;
                guild.Description = Description;
                guild.ServerListenerCore.GuildDiscoveryInfoChanged();
                guild.ClientListenerCore.GuildNameAndDescriptionUpdated();
            }
            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.GuildMemberRolesUpdate)]
    public class GuildMemberRolesUpdate : GuildActionBase
    {
        public OrderedDictionary<EntityId, GuildMemberRole> Roles { get; private set; }

        GuildMemberRolesUpdate() { }
        public GuildMemberRolesUpdate(OrderedDictionary<EntityId, GuildMemberRole> roles)
        {
            Roles = roles;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (commit)
            {
                guild.ApplyMemberRoleChanges(Roles);
            }
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Adds, removes or updates a single Invitation of a member.
    /// </summary>
    [ModelAction(ActionCodesCore.GuildInviteUpdate)]
    public class GuildInviteUpdate : GuildActionBase
    {
        public EntityId         PlayerId    { get; private set; }
        public int              InviteId    { get; private set; }
        public GuildInviteState NewState    { get; private set; }

        GuildInviteUpdate() { }
        public GuildInviteUpdate(EntityId playerId, int inviteId, GuildInviteState newState)
        {
            PlayerId = playerId;
            InviteId = inviteId;
            NewState = newState;
        }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            if (!guild.TryGetMember(PlayerId, out GuildMemberBase member))
                return MetaActionResult.NoSuchGuildMember;

            if (NewState == null)
            {
                // remove
                if (!member.Invites.ContainsKey(InviteId))
                    return MetaActionResult.GuildOperationStale;

                if (commit)
                    member.Invites.Remove(InviteId);
            }
            else
            {
                // add or update
                if (commit)
                    member.Invites[InviteId] = NewState;
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Placeholder for a hidden action
    /// </summary>
    [ModelAction(ActionCodesCore.GuildHiddenAction)]
    public class GuildHiddenAction : GuildActionBase
    {
        public GuildHiddenAction() { }
        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            return MetaActionResult.Success;
        }
    }
}

#endif
