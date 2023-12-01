// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Server.DatabaseScan.Priorities
{
    /// <summary>
    /// Centralized registry for priorities of database scan jobs.
    /// Higher value means higher priority, i.e. takes precedence over lower-priority jobs.
    /// </summary>
    public static class DatabaseScanJobPriorities
    {
        public const int ScheduledPlayerDeletion    = 0;
        public const int EntitySchemaMigrator       = 1;
        public const int EntityRefresher            = 1;
        public const int NotificationCampaign       = 2;
    }
}
