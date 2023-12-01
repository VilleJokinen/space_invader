// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    ///  Deletion status of a player. Possible transitions are:
    ///     None -> ScheduledBy* = Player is scheduled for deletion
    ///     ScheduledBy* -> None = Admin or Player or System cancels scheduled deletion
    ///     ScheduledBy* -> Deleted = Player is deleted by scheduled deletion
    /// </summary>
    /// <p>
    /// Bit layout is as follows:
    /// <code>
    ///  7 6 5 4 3 2 1 0
    /// +-+-+-+-+-+-+-+-+
    /// |   Issuer  |D|S|
    /// +-+-+-+-+-+-+-+-+
    /// where:
    ///  S: Is Scheduled bit
    ///  D: Is Deleted bit
    ///  Issuer: Identifies the who scheduled the deletion.
    /// </code>
    /// </p>
    /// </summary>
    [MetaSerializable]
    public enum PlayerDeletionStatus
    {
        /// <summary>
        /// Normal player status
        /// </summary>
        None = 0x00,

        /// <summary>
        /// Player has been deleted
        /// </summary>
        Deleted = 0x02,

        /// <summary>
        /// Player is scheduled to be deleted by Game Admin via Dashboard
        /// </summary>
        ScheduledByAdmin = (0 << 2) | 0x01,

        /// <summary>
        /// Player is scheduled to be deleted by user itself
        /// </summary>
        ScheduledByUser = (1 << 2) | 0x01,

        /// <summary>
        /// Player is scheduled to be deleted by system (i.e. server logic)
        /// </summary>
        ScheduledBySystem = (2 << 2) | 0x01,
    }

    public static class PlayerDeletionStatusExtensions
    {
        /// <summary>
        /// Is enum any ScheduledBy* state.
        /// </summary>
        public static bool IsScheduled(this PlayerDeletionStatus status)
        {
            return ((uint)status & 0x01) != 0;
        }
    }
}
