// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;

namespace Metaplay.Core.Guild
{
    [MetaSerializable]
    public enum GuildInviteType
    {
        /// <summary>
        /// Invite Code based invite. Inviter user creates an invite code, and shares it with the target player(s) via some
        /// external tool. Target players may user the invite code, and they will be offered to join a guild.
        /// </summary>
        InviteCode,

        /// <summary>
        /// Invite Link based invite. Inviter user creates an invite link, and shares it with the target player(s) via some
        /// external tool. Target players tap on the link and they will be offered to join a guild.
        /// </summary>
        // InviteLink = 1,

        /// <summary>
        /// Direct in-game invite. Inviter invites a target player. Target player receives the invitation, and may accept the it.
        /// </summary>
        // DirectPlayerInvite = 2,
    }

    /// <summary>
    /// The authorative per-member state of an invite.
    /// </summary>
    [MetaSerializable]
    public class GuildInviteState
    {
        /// <summary>
        /// The type of the invite. This value does not change.
        /// </summary>
        [MetaMember(1)] public GuildInviteType              Type;

        /// <summary>
        /// The moment in time when the invite was created.
        /// </summary>
        [MetaMember(2)] public MetaTime                     CreatedAt;

        /// <summary>
        /// The duration the invite is valid for. If null, the invite has no expiration time.
        /// </summary>
        [MetaMember(3)] public MetaDuration?                ExpiresAfter;

        /// <summary>
        /// Number of times the invite code or link may be used before it expires. If set to 0, there is no limit.
        /// </summary>
        [MetaMember(4)] public int                          NumMaxUsages;

        /// <summary>
        /// Number of times the invite code has been used so far.
        /// </summary>
        [MetaMember(5)] public int                          NumTimesUsed;

        /// <summary>
        /// Invite code when <see cref="Type"/> is <see cref="GuildInviteType.InviteCode"/>. Only visible on server and on
        /// the member that created the invite code.
        /// </summary>
        [MetaMember(6)] public GuildInviteCode              InviteCode;

        GuildInviteState() { }
        public GuildInviteState(GuildInviteType type, MetaTime createdAt, MetaDuration? expiresAfter, int numMaxUsages, int numTimesUsed, GuildInviteCode inviteCode)
        {
            Type = type;
            CreatedAt = createdAt;
            ExpiresAfter = expiresAfter;
            NumMaxUsages = numMaxUsages;
            NumTimesUsed = numTimesUsed;
            InviteCode = inviteCode;
        }

        public bool IsExpired(MetaTime currentTime)
        {
            if (NumMaxUsages > 0 && NumTimesUsed >= NumMaxUsages)
                return true;
            if (ExpiresAfter != null && currentTime >= CreatedAt + ExpiresAfter.Value)
                return true;
            return false;
        }
    }
}

#endif
