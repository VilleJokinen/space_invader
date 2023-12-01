// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Metaplay.Server.PlayerDeletion
{
    public class PlayerDeletionRecords
    {
        /// <summary>
        /// Database-persisted player-deletion entry.
        /// </summary>
        [Table("PlayerDeletionRecords")]
        [NonPartitioned]
        public class PersistedPlayerDeletionRecord : IPersistedItem
        {
            [Key]
            [Required]
            [MaxLength(64)]
            [Column(TypeName = "varchar(64)")]
            public string PlayerId { get; set; }

            [Required]
            [Column(TypeName = "DateTime")]
            public DateTime ScheduledDeletionAt { get; set; }

            [Column(TypeName = "varchar(128)")]
            public string DeletionSource { get; set; }

            public PersistedPlayerDeletionRecord() { }
            public PersistedPlayerDeletionRecord(EntityId playerId, MetaTime scheduledDeletionAt, string deletionSource)
            {
                PlayerId = playerId.ToString();
                ScheduledDeletionAt = scheduledDeletionAt.ToDateTime();
                DeletionSource = deletionSource;
            }
        }
    }
}
