// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.TypeCodes
{
    public static class ActionCodesCore
    {
        public const int PlayerAddMail                                          = 7000;
        public const int PlayerConsumeMail                                      = 7001;
        public const int PlayerDeleteMail                                       = 7002;
        public const int PlayerForceDeleteMail                                  = 7003;
        public const int PlayerToggleMailIsRead                                 = 7004;

        public const int PlayerSetIsOnline                                      = 7010;

        public const int PlayerChangeLanguage                                   = 7020;

        public const int PlayerSetIsBanned                                      = 7030;

        public const int PlayerChangeName                                       = 7040;

        public const int PlayerSetIsScheduledForDeletionAt                      = 7050;
        public const int PlayerSetUnscheduledForDeletion                        = 7051;

        public const int PlayerAddFirebaseMessagingToken                        = 7060;
        public const int PlayerServerCleanupRemoveFirebaseMessagingToken        = 7061;

        public const int PlayerConfirmPendingDynamicPurchaseContent             = 7100;
        public const int PlayerConfirmPendingNonDynamicPurchaseAnalyticsContext = 7101;
        public const int PlayerInAppPurchased                                   = 7102;
        public const int PlayerInAppPurchaseValidated                           = 7103;
        public const int PlayerClaimPendingInAppPurchase                        = 7104;
        public const int PlayerClearPendingDuplicateInAppPurchase               = 7105;
        public const int PlayerPreparePurchaseContext                           = 7106;
        public const int PlayerInAppPurchaseClientRefused                       = 7107;

        public const int PlayerUpdateSubscriptionInstanceState                  = 7110;
        public const int PlayerSetSubscriptionInstanceDisablementDueToReuse     = 7111;
        public const int PlayerDebugRemoveSubscription                          = 7112;

        public const int PlayerAttachAuthentication                             = 8100;
        public const int PlayerDetachAuthentication                             = 8101;
        public const int PlayerSynchronizedServerActionMarker                   = 8102;
        public const int PlayerUnsynchronizedServerActionMarker                 = 8103;

        public const int PlayerRefreshMetaOffers                                = 8110;
        public const int PlayerPreparePurchaseMetaOffer                         = 8111;

        public const int PlayerAddNft                                           = 8120;
        public const int PlayerUpdateNft                                        = 8121;
        public const int PlayerRemoveNft                                        = 8122;
        public const int PlayerMarkNftMinted                                    = 8123;
        public const int PlayerExecuteNftTransaction                            = 8124;
        public const int PlayerFinalizeNftTransaction                           = 8125;
        public const int PlayerCancelNftTransaction                             = 8126;

        public const int PlayerDebugForceSetActivablePhase                      = 8200; // \note Development-only
        public const int PlayerServerDebugForceSetActivablePhase                = 8201; // \note Development-only
        public const int PlayerDebugAddMailToSelf                               = 8202; // \note Development-only

#if !METAPLAY_DISABLE_GUILDS
        public const int GuildMemberAdd                                         = 9000;
        public const int GuildMemberIsOnlineUpdate                              = 9001;
        public const int GuildMemberPlayerDataUpdate                            = 9002;
        public const int GuildMemberRemove                                      = 9003;
        public const int GuildNameAndDescriptionUpdate                          = 9004;
        public const int GuildMemberRolesUpdate                                 = 9005;
        public const int GuildInviteUpdate                                      = 9007;
        public const int GuildHiddenAction                                      = 9008;

        public const int GuildMemberKick                                        = 9100;
        public const int GuildMemberEditRole                                    = 9101;
#endif

        public const int DivisionSetSeasonStartsAtDebug                         = 10000;
        public const int DivisionSetSeasonEndsAtDebug                           = 10001;
        public const int DivisionConcludeSeasonDebug                            = 10002;
        public const int DivisionParticipantRemove                              = 10003;
        public const int DivisionConclude                                       = 10004;

        public const int PlayerDivisionAddOrUpdateParticipant                   = 10100;
        public const int PlayerDivisionUpdateContribution                       = 10101;

        public const int PlayerClaimHistoricalPlayerDivisionRewards             = 10200;
        public const int PlayerSetCurrentDivision                               = 10201;
        public const int PlayerAddHistoricalDivisionEntry                       = 10202;
    }
}
