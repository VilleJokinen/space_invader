// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Server.AdminApi.AuditLog;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class MetaplayAuditLogEventCodes
    {
        public const int Invalid                                                = MetaplayAuditLogEventCodesCore.Invalid; // shadow for convenience

        public const int GameServerMaintenanceModeScheduled           = 1000;
        public const int GameServerMaintenanceModeUnscheduled         = 1001;
        public const int GameServerEntityArchiveExported              = 1002;
        public const int GameServerEntityArchiveImported              = 1003;
        public const int GameServerPlayerRedeleteExecuted             = 1005;
        public const int NotificationCreated                          = 1006;
        public const int NotificationUpdated                          = 1007;
        public const int NotificationCancelled                        = 1008;
        public const int NotificationDeleted                          = 1009;
        public const int GameServerIAPRefundRequested                 = 1010;
        public const int GameServerClientCompatibilitySettingsUpdated = 1011;
        public const int GameServerDatabaseScanJobCancelled           = 1015;
        public const int GameServerDatabaseScanGlobalPauseSet         = 1023;
        public const int GameServerMaintenanceJobCreated              = 1016;
        public const int GameServerMaintenanceJobDeleted              = 1017;
        public const int GameConfigStaticGameConfigUploaded           = 1018;
        public const int GameConfigPublished                          = 1019;
        public const int GameConfigStaticGameConfigBuildStarted       = 1020;
        public const int GameConfigStaticGameConfigEdited             = 1021;
        public const int GameConfigUnpublished                        = 1022;
        public const int LocalizationPublished                        = 1030;
        public const int LocalizationUnpublished                      = 1031;
        public const int LocalizationUploaded                         = 1032;
        public const int LocalizationEdited                         = 1033;

        public const int PlayerNameChanged                                      = 2000;
        public const int PlayerBanned                                           = 2001;
        public const int PlayerUnbanned                                         = 2002;
        public const int PlayerGdprExported                                     = 2003;
        public const int PlayerMailSent                                         = 2004;
        public const int PlayerMailDeleted                                      = 2005;
        public const int PlayerDeletionScheduled                                = 2006;
        public const int PlayerDeletionUnscheduled                              = 2007;
        public const int PlayerViewed                                           = 2008;
        public const int PlayerOverwritten                                      = 2009;
        public const int PlayerReset                                            = 2010;
        public const int PlayerGenericServerActionPerformed                     = 2011;
        public const int PlayerAccountReconnectedFrom                           = 2012;
        public const int PlayerAccountReconnectedTo                             = 2013;
        public const int PlayerRedeleted                                        = 2014;
        public const int PlayerKickedFromGuild                                  = 2015;
        public const int PlayerGuildRoleEdited                                  = 2016;
        public const int PlayerDisconnectSingleAuth                             = 2017;
        public const int PlayerExperimentAssignmentChanged                      = 2018;
        public const int PlayerChangeDeveloperStatus                            = 2019;
        public const int PlayerIAPSubscriptionRemoved                           = 2020;
        public const int PlayerEnrolledToMatchmaker                             = 2021;
        public const int PlayerRemovedFromMatchmaker                            = 2022;
        // RESERVED                                                               2023
        public const int PlayerOwnedNftsRefreshed                               = 2030;
        public const int PlayerDebugAssignedNftOwnership                        = 2031;
        public const int PlayerDebugRemovedFromLeague                           = 2032;
        public const int PlayerDebugAddedToLeague                               = 2033;
        public const int PlayerDebugMovedToLeague                               = 2034;

        public const int GuildPlayerKicked                                      = 3000;
        public const int GuildNameAndDescriptionChanged                         = 3001;
        public const int GuildEventRolesChanged                                 = 3002;

        public const int BroadcastCreated                                       = 4000;
        public const int BroadcastUpdated                                       = 4001;
        public const int BroadcastDeleted                                       = 4002;

        public const int ExperimentEdited                                       = 5000;
        public const int ExperimentPhaseChange                                  = 5001;
        public const int ExperimentDeleted                                      = 5002;

        public const int DatabaseEntityInspected                                = 6001;

        public const int MatchmakerRebalanced                                   = 7001;
        public const int MatchmakerReset                                        = 7002;

        public const int NftCollectionRefreshed                                 = 8000;

        public const int NftInitialized                                         = 9000;
        public const int NftStateEdited                                         = 9001;
        public const int NftOwnershipDebugAssigned                              = 9002;
        public const int NftRefreshed                                           = 9003;
        public const int NftMetadataRepublished                                 = 9004;

        public const int LeagueSeasonDebugAdvanced                              = 9500;
        public const int LeagueParticipantRemoved                               = 9501;
        public const int LeagueParticipantAdded                                 = 9502;
        public const int LeagueParticipantMoved                                 = 9503;
    }
}
