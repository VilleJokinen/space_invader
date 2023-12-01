// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Cloud.Persistence;
using Metaplay.Core.Guild;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Metaplay.Server.Guild
{
    /// <summary>
    /// Invite-code-to-invite look-up record persisted into a database.
    /// </summary>
    [GuildsEnabledCondition]
    [Table("GuildInviteCodes")]
    public sealed class PersistedGuildInviteCode : IPersistedItem
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(32)]
        [Column(TypeName = "varchar(32)")]
        public string   InviteCode          { get; set; }

        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   GuildId             { get; set; }

        [Required]
        public int      InviteId            { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime CreatedAt           { get; set; }

        PersistedGuildInviteCode() { }
        public PersistedGuildInviteCode(string inviteCode, string guildId, int inviteId, DateTime createdAt)
        {
            InviteCode = inviteCode;
            GuildId = guildId;
            InviteId = inviteId;
            CreatedAt = createdAt;
        }
    }
}

#endif
