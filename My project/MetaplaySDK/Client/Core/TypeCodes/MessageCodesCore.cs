// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.TypeCodes
{
    /// <summary>
    /// Message code registry for Metaplay core messages. To add custom messages
    /// see <see cref="MessageCodes"/>.
    /// </summary>
    public static class MessageCodesCore
    {
        // CoreMessages
        public const int ServerHello                                            = 4;
        public const int ClientHello                                            = 5;
        public const int ClientAbandon                                          = 6;
        public const int DeviceLoginRequest                                     = 7;
        public const int SocialAuthenticationLoginRequest                       = 31;
        public const int DualSocialAuthenticationLoginRequest                   = 32;
        public const int LoginSuccessResponse                                   = 8;
        public const int OngoingMaintenance                                     = 88;
        public const int OperationStillOngoing                                  = 89;
        public const int ClientHelloAccepted                                    = 90;
        public const int SessionPing                                            = 40;
        public const int SessionPong                                            = 41;
        public const int UpdateLocalizationVersions                             = 9;
        public const int LogicVersionMismatch                                   = 13;
        public const int LoginProtocolVersionMismatch                           = 91;
        public const int CreateGuestAccountRequest                              = 92;
        public const int CreateGuestAccountResponse                             = 93;
        public const int RedirectToServer                                       = 14;
        public const int UpdateScheduledMaintenanceMode                         = 15;
        public const int SessionStartRequest                                    = 16;
        public const int SessionStartSuccess                                    = 17;
        public const int SessionStartFailure                                    = 18;
        public const int SessionStartAbort                                      = 42;
        public const int SessionStartAbortReasonTrailer                         = 43;
        public const int SessionStartResourceCorrection                         = 44;
        public const int LoginAndResumeSessionRequest                           = 19;
        public const int SessionResumeSuccess                                   = 20;
        public const int SessionResumeFailure                                   = 21;
        public const int SessionAcknowledgementMessage                          = 30;
        public const int SessionForceTerminateMessage                           = 105;
        public const int SessionMetaRequestMessage                              = 110;
        public const int SessionMetaResponseMessage                             = 111;

        // Core player messages (client <-> server)
        public const int PlayerChecksumMismatch                                 = 104;
        public const int PlayerChecksumMismatchDetails                          = 106;
        public const int PlayerFlushActions                                     = 200;
        public const int PlayerAckActions                                       = 201;
        public const int PlayerExecuteUnsynchronizedServerAction                = 203;
        public const int PlayerChangeOwnNameRequest                             = 204;
        public const int PlayerScheduleDeletionRequest                          = 205;
        public const int PlayerCancelScheduledDeletionRequest                   = 206;
        public const int PlayerEnqueueSynchronizedServerAction                  = 207;

        // Player incident reporting (client <-> server)
        public const int PlayerAvailableIncidentReports                         = 1020;
        public const int PlayerRequestIncidentReportUploads                     = 1021;
        public const int PlayerUploadIncidentReport                             = 1022;
        public const int PlayerAckIncidentReportUpload                          = 1023;

        // Social authentication (client <-> server)
        public const int SocialAuthenticateRequest                              = 8100;
        public const int SocialAuthenticateResult                               = 8101;
        public const int SocialAuthenticateResolveConflict                      = 8102;
        public const int SocialAuthenticateForceReconnect                       = 8103;
        public const int SocialAuthenticateDetach                               = 8104;

        // Server-internal core session messages
        #if !METAPLAY_DISABLE_GUILDS
        public const int InternalSessionGuildMemberKicked                       = 305;
        public const int InternalSessionPlayerJoinedAGuild                      = 306;
        public const int InternalSessionPlayerKickedFromGuild                   = 307;
        public const int InternalSessionGuildTimelineUpdate                     = 308;
        public const int InternalSessionPlayerGuildCreateFailed                 = 309;
        #endif
        public const int InternalSessionStartNewRequest                         = 310;
        public const int InternalSessionStartNewResponse                        = 311;
        public const int InternalSessionStartNewRefusal                         = 312;
        public const int InternalSessionResumeRequest                           = 313;
        public const int InternalSessionResumeResponse                          = 314;
        public const int InternalSessionResumeRefusal                           = 315;
        public const int SessionMessageFromClient                               = 316;
        public const int InternalSessionNotifyClientAppStatusChanged            = 317;
        public const int InternalSessionEntityAssociationUpdate                 = 318;
        public const int InternalSessionEntityBroadcastMessage                  = 319;
        public const int InternalSessionDivisionDebugReset                      = 320;

        // General entity event log management (server-internal)
        public const int TriggerEntityEventLogFlushing                          = 1000;
        public const int EntityEventLogScanRequest                              = 1001;

        // General EntityActor messages (server-internal)
        public const int HandleMessageRequest                                   = 1050;
        public const int HandleMessageResponse                                  = 1051;

        // Server-internal core player messages
        public const int PlayerEventLogScanResponse                             = 1101;
        public const int TriggerConfirmDynamicInAppPurchase                     = 1103;
        public const int TriggerInAppPurchaseValidation                         = 1104;
        public const int TriggerIAPSubscriptionReuseCheck                       = 1102;
        public const int PlayerResetState                                       = 1106;
        public const int PlayerCopyAuthFromRequest                              = 1109;
        public const int PlayerDetachAllAuthRequest                             = 1111;
        public const int PlayerChangeNameRequest                                = 1113;
        public const int PlayerChangeNameResponse                               = 1114;
        public const int PlayerImportModelDataRequest                           = 1115;
        public const int PlayerImportModelDataResponse                          = 1116;
        public const int PlayerForceKickOwner                                   = 1117;
        public const int PersistStateRequestRequest                             = 1118;
        public const int PersistStateRequestResponse                            = 1119;
        public const int PlayerRefundPurchaseRequest                            = 1120;
        public const int PlayerRefundPurchaseResponse                           = 1121;
        public const int PlayerRemovePushNotificationTokenRequest               = 1123;
        public const int PlayerRemovePushNotificationTokenResponse              = 1124;
        public const int PlayerCompleteScheduledDeletionRequest                 = 1125;
        public const int PlayerCompleteScheduledDeletionResponse                = 1126;
        #if !METAPLAY_DISABLE_GUILDS
        public const int InternalGuildDiscoveryPlayerContextRequest             = 1128;
        public const int InternalGuildDiscoveryPlayerContextResponse            = 1129;
        #endif
        public const int InternalPlayerSessionSubscribeRequest                  = 1130;
        public const int InternalPlayerSessionSubscribeResponse                 = 1131;
        #if !METAPLAY_DISABLE_GUILDS
        public const int InternalPlayerJoinGuildRequest                         = 1134;
        public const int InternalPlayerJoinGuildResponse                        = 1135;
        public const int InternalPlayerGuildLeaveRequest                        = 1136;
        public const int InternalPlayerGuildLeaveResponse                       = 1137;
        public const int InternalPlayerPendingGuildOpsCommitted                 = 1138;
        public const int InternalPlayerKickedFromGuild                          = 1139;
        #endif
        public const int InternalPlayerFacebookAuthenticationRevokedRequest     = 1140;
        public const int InternalPlayerFacebookAuthenticationRevokedResponse    = 1141;
        public const int InternalPlayerResolveSocialAuthConflictRequest         = 1142;
        public const int InternalPlayerResolveSocialAuthConflictResponse        = 1143;
        public const int InternalPlayerResolveDeviceIdAuthConflictRequest       = 1144;
        public const int InternalPlayerResolveDeviceIdAuthConflictResponse      = 1145;
        #if !METAPLAY_DISABLE_GUILDS
        public const int InternalPlayerGetGuildInviterAvatarRequest             = 1146;
        public const int InternalPlayerGetGuildInviterAvatarResponse            = 1147;
        #endif
        public const int PlayerRemoveSingleAuthRequest                          = 1148;
        public const int PlayerRemoveSingleAuthResponse                         = 1149;
        public const int InternalPlayerSetExperimentGroupRequest                = 1150;
        public const int InternalPlayerSetExperimentGroupResponse               = 1151;
        public const int TriggerConfirmStaticPurchaseContext                    = 1152;
        public const int InternalPlayerSessionSubscribeRefused                  = 1153;
        public const int InternalPlayerSetExperimentGroupWaitRequest            = 1154;
        public const int InternalPlayerSetExperimentGroupWaitResponse           = 1155;
        public const int InternalPlayerGetPlayerExperimentDetailsRequest        = 1156;
        public const int InternalPlayerGetPlayerExperimentDetailsResponse       = 1157;
        public const int TriggerPlayerBanFlagChanged                            = 1158;
        public const int InternalPlayerForceActivablePhaseMessage               = 1159;
        public const int InternalPlayerDeveloperStatusChangedMessage            = 1160;
        public const int InternalPlayerEvaluateTriggers                         = 1161;
        public const int InternalPlayerScheduleDeletionRequest                  = 1162;
        public const int InternalPlayerScheduleDeletionResponse                 = 1163;
        public const int InternalPlayerExecuteServerActionRequest               = 1164;
        public const int InternalPlayerExecuteServerActionResponse              = 1165;
        public const int InternalPlayerEnqueueServerActionRequest               = 1166;
        public const int InternalPlayerEnqueueServerActionResponse              = 1167;
        public const int InternalPlayerExecuteServerActionMessage               = 1168;
        public const int InternalPlayerEnqueueServerActionMessage               = 1169;
        public const int InternalNewPlayerIncidentRecorded                      = 1170;
        public const int InternalPlayerLoginWithNewSocialAccountRequest         = 1171;
        public const int InternalPlayerLoginWithNewSocialAccountResponse        = 1172;
        public const int InternalPlayerAddNewSocialAccountRequest               = 1175;

        // GlobalStateManager.cs (server-internal)
        public const int GlobalStatusRequest                             = 6101;
        public const int GlobalStatusResponse                            = 6102;
        public const int CreateOrUpdateLocalizationsRequest              = 6103;
        public const int PublishLocalizationsRequest                     = 6129;
        public const int PublishLocalizationsResponse                    = 6130;
        public const int RemoveLocalizationsResponse                     = 6131;
        //public const int UploadLocalizationsResponse                     = 6104;
        public const int CreateOrUpdateGameConfigRequest                 = 6120;
        public const int CreateOrUpdateGameDataResponse                  = 6121;
        public const int PublishGameConfigRequest                        = 6122;
        public const int PublishGameConfigResponse                       = 6123;
        public const int RemoveStaticGameConfigRequest                   = 6124;
        public const int UpdateClientCompatibilitySettingsRequest        = 6105;
        public const int UpdateClientCompatibilitySettingsResponse       = 6106;
        public const int ScheduledMaintenanceModeRequest                 = 6107;
        public const int ScheduledMaintenanceModeResponse                = 6108;
        public const int ExportEntityArchiveRequest                      = 6113;
        public const int ExportEntityArchiveResponse                     = 6114;
        public const int ImportEntityArchiveRequest                      = 6115;
        public const int ImportEntityArchiveResponse                     = 6116;
        public const int SetDeveloperPlayerRequest                       = 6117;
        public const int SetDeveloperPlayerResponse                      = 6118;
        public const int GlobalStateRequest                              = 12001;
        public const int GlobalStateSnapshot                             = 12002;
        public const int GlobalStateUpdateGameConfig                     = 12010;
        public const int GlobalStateUpdateLocalizationsConfigVersion     = 12011;
        public const int GlobalStateExperimentStateRequest               = 12013;
        public const int GlobalStateExperimentStateResponse              = 12014;
        public const int GlobalStatePlayerExperimentAssignmentInfoUpdate = 12015;
        public const int GlobalStateModifyExperimentRequest              = 12016;
        public const int GlobalStateModifyExperimentResponse             = 12017;
        public const int GlobalStateSetExperimentPhaseRequest            = 12018;
        public const int GlobalStateSetExperimentPhaseResponse           = 12019;
        public const int GlobalStateForgetActivableAndOfferStatistics    = 12020;
        public const int GlobalStateGetAllExperimentsRequest             = 12021;
        public const int GlobalStateGetAllExperimentsResponse            = 12022;
        public const int GlobalStateDeleteExperimentRequest              = 12023;
        public const int GlobalStateDeleteExperimentResponse             = 12024;
        public const int GlobalStateEditExperimentTestersRequest         = 12025;
        public const int GlobalStateEditExperimentTestersResponse        = 12026;
        public const int GlobalStateForgetPlayerExperimentSamples        = 12027;
        public const int GlobalStateExperimentCombinationsRequest        = 12028;
        public const int GlobalStateExperimentCombinationsResponse       = 12029;

        // Broadcast.cs (server-internal)
        public const int AddBroadcastMessage                                    = 12003;
        public const int UpdateBroadcastMessage                                 = 12004;
        public const int DeleteBroadcastMessage                                 = 12005;
        public const int AddBroadcastMessageResponse                            = 12006;
        public const int BroadcastConsumedCountInfo                             = 12007;

        // PushNotificationActor (server-internal)
        public const int SendPushNotification                                   = 6200;

        // InAppPurchaseValidatorActor (server-internal)
        public const int ValidateInAppPurchaseRequest                           = 6400;
        public const int ValidateInAppPurchaseResponse                          = 6401;
        public const int InAppPurchaseSubscriptionStateRequest                  = 6402;
        public const int InAppPurchaseSubscriptionStateResponse                 = 6403;

        // DiagnosticToolActor (server-internal)
        public const int DiagnosticToolPing                                     = 6600;
        public const int DiagnosticToolPong                                     = 6601;
        public const int TestEntityAskFailureRequest                            = 6602;
        public const int TestAsyncHandlerEntityAskFailureRequest                = 6603;


        // GuildDiscovery (server-internal)
        #if !METAPLAY_DISABLE_GUILDS
        public const int InternalGuildRecommendationRequest                     = 6700;
        public const int InternalGuildRecommendationResponse                    = 6701;
        public const int InternalGuildSearchRequest                             = 6703;
        public const int InternalGuildSearchResponse                            = 6704;
        public const int InternalGuildRecommenderGuildUpdate                    = 6705;
        public const int InternalGuildRecommenderGuildRemove                    = 6706;
        public const int InternalGuildRecommenderInspectPoolsRequest            = 6707;
        public const int InternalGuildRecommenderInspectPoolsResponse           = 6708;
        public const int InternalGuildRecommenderInspectPoolRequest             = 6709;
        public const int InternalGuildRecommenderInspectPoolResponse            = 6710;
        public const int InternalGuildRecommenderTestGuildRequest               = 6711;
        public const int InternalGuildRecommenderTestGuildResponse              = 6712;
        #endif

        // Entity sharding (server-internal)
        public const int UpdateShardLiveEntityCount                             = 6300;
        public const int RemoteEntityError                                      = 6301;

        // Entity ask generic messages (server-internal)
        public const int EntityAskOk                                            = 6302;

        // Entity PubSub (server-internal)
        public const int EntitySubscribe                                        = 8000;
        public const int EntitySubscribeAck                                     = 8001;
        public const int EntityUnsubscribe                                      = 8002;
        public const int EntityUnsubscribeAck                                   = 8003;
        public const int EntityPubSubMessage                                    = 8004;
        public const int EntitySubscriberKicked                                 = 8005;
        public const int EntityWatchedEntityTerminated                          = 8006;

        // Entity Synchronize (server-internal)
        public const int EntitySynchronizationS2SBeginRequest                   = 8050;
        public const int EntitySynchronizationS2SBeginResponse                  = 8051;
        public const int EntitySynchronizationS2SChannelMessage                 = 8052;

        // Entity schema version migration forcing (server-internal)
        public const int EntityEnsureOnLatestSchemaVersionRequest               = 9000;
        public const int EntityEnsureOnLatestSchemaVersionResponse              = 9001;

        // Entity refresh (server-internal)
        public const int EntityRefreshRequest                                   = 9002;
        public const int EntityRefreshResponse                                  = 9003;

        // Pig
        public const int InternalPig                                            = 8060;

        // Database scan system (server-internal)
        public const int DatabaseScanWorkerEnsureInitialized                    = 14000;
        public const int DatabaseScanWorkerInitializedOk                        = 14001;
        public const int DatabaseScanWorkerEnsureResumed                        = 14002;
        public const int DatabaseScanWorkerResumedOk                            = 14003;
        public const int DatabaseScanWorkerEnsurePaused                         = 14004;
        public const int DatabaseScanWorkerPausedOk                             = 14005;
        public const int DatabaseScanWorkerEnsureStopped                        = 14006;
        public const int DatabaseScanWorkerStoppedOk                            = 14007;
        public const int DatabaseScanWorkerEnsureAwake                          = 14100;
        public const int DatabaseScanWorkerStatusReport                         = 14101;
        public const int ListDatabaseScanJobsRequest                            = 14200;
        public const int ListDatabaseScanJobsResponse                           = 14201;
        public const int BeginCancelDatabaseScanJobRequest                      = 14202;
        public const int BeginCancelDatabaseScanJobResponse                     = 14203;
        public const int SetGlobalDatabaseScanPauseRequest                      = 14204;
        public const int SetGlobalDatabaseScanPauseResponse                     = 14205;

        // NotificationCampaign (server-internal)
        public const int ListNotificationCampaignsRequest                       = 15000;
        public const int ListNotificationCampaignsResponse                      = 15001;
        public const int AddNotificationCampaignRequest                         = 15002;
        public const int AddNotificationCampaignResponse                        = 15003;
        public const int GetNotificationCampaignRequest                         = 15004;
        public const int GetNotificationCampaignResponse                        = 15005;
        public const int UpdateNotificationCampaignRequest                      = 15006;
        public const int UpdateNotificationCampaignResponse                     = 15007;
        public const int BeginCancelNotificationCampaignRequest                 = 15008;
        public const int BeginCancelNotificationCampaignResponse                = 15009;
        public const int DeleteNotificationCampaignRequest                      = 15010;
        public const int DeleteNotificationCampaignResponse                     = 15011;

        // MaintenanceJob (server-internal)
        public const int GetMaintenanceJobsRequest                              = 15020;
        public const int GetMaintenanceJobsResponse                             = 15021;
        public const int AddMaintenanceJobRequest                               = 15022;
        public const int AddMaintenanceJobResponse                              = 15023;
        public const int RemoveMaintenanceJobRequest                            = 15024;
        public const int RemoveMaintenanceJobResponse                           = 15025;

        #if !METAPLAY_DISABLE_GUILDS

        // Guild (server-internal)
        public const int InternalGuildEventLogScanResponse                      = 15101;
        public const int InternalGuildLeaveRequest                              = 15106;
        public const int InternalGuildLeaveResponse                             = 15107;
        public const int InternalGuildPlayerDashboardInfoRequest                = 15112;
        public const int InternalGuildPlayerDashboardInfoResponse               = 15113;
        public const int InternalGuildTransactionPlayerSyncBegin                = 15114;
        public const int InternalGuildTransactionPlayerSyncPlanned              = 15115;
        public const int InternalGuildTransactionPlayerSyncCommit               = 15116;
        public const int InternalGuildTransactionPlayerSyncCommitted            = 15117;
        public const int InternalGuildTransactionGuildSyncBegin                 = 15118;
        public const int InternalGuildTransactionGuildSyncPlannedAndCommitted   = 15119;
        public const int InternalGuildDiscoveryGuildDataRequest                 = 15120;
        public const int InternalGuildDiscoveryGuildDataResponse                = 15121;
        public const int InternalGuildViewerSubscribeRequest                    = 15122;
        public const int InternalGuildViewerSubscribeResponse                   = 15123;
        public const int InternalGuildMemberPlayerDataUpdate                    = 15124;
        public const int InternalGuildJoinGuildSyncBegin                        = 15125;
        public const int InternalGuildJoinGuildSyncPreflightDone                = 15126;
        public const int InternalGuildJoinGuildSyncPlayerCommitted              = 15127;
        public const int InternalGuildJoinGuildSyncGuildCommitted               = 15128;
        public const int InternalGuildSetupSyncBegin                            = 15129;
        public const int InternalGuildSetupSyncSetupResponse                    = 15130;
        public const int InternalGuildSetupSyncPlayerCommitted                  = 15131;
        public const int InternalGuildSetupSyncGuildCommitted                   = 15132;
        public const int InternalGuildEnqueueActionsRequest                     = 15133;
        public const int InternalGuildEnqueueMemberActionRequest                = 15134;
        public const int InternalGuildRunPendingGuildOpsRequest                 = 15135;
        public const int InternalGuildPlayerOpsCommitted                        = 15137;
        public const int InternalGuildPeekKickedStateRequest                    = 15138;
        public const int InternalGuildPeekKickedStateResponse                   = 15139;
        public const int InternalGuildPlayerClearKickedState                    = 15140;
        public const int InternalGuildAdminKickMember                           = 15141;
        public const int InternalGuildAdminChangeDisplayNameAndDetailsRequest   = 15142;
        public const int InternalGuildAdminChangeDisplayNameAndDetailsReponse   = 15143;
        public const int InternalGuildMemberGdprExportRequest                   = 15144;
        public const int InternalGuildMemberGdprExportResponse                  = 15145;
        public const int InternalGuildImportModelDataRequest                    = 15146;
        public const int InternalGuildImportModelDataResponse                   = 15147;
        public const int InternalGuildAdminEditRolesRequest                     = 15148;
        public const int InternalGuildAdminEditRolesResponse                    = 15149;
        public const int InternalGuildInspectInviteCodeRequest                  = 15150;
        public const int InternalGuildInspectInviteCodeResponse                 = 15151;
        public const int InternalGuildMemberSubscribeRefused                    = 15152;
        public const int InternalOwnedGuildAssociationRef                       = 15153;

        // Guild (server -> client)
        public const int GuildUpdate                                            = 15200;
        public const int GuildCreateResponse                                    = 15201;
        public const int GuildJoinResponse                                      = 15202;
        public const int GuildDiscoveryResponse                                 = 15203;
        public const int GuildTransactionResponse                               = 15204;
        public const int GuildSearchResponse                                    = 15205;
        public const int GuildViewResponse                                      = 15206;
        public const int GuildSwitchedMessage                                   = 15207;
        public const int GuildViewEnded                                         = 15208;
        public const int GuildCreateInvitationResponse                          = 15209;
        public const int GuildInspectInvitationResponse                         = 15210;

        // Guild (client -> server)
        public const int GuildCreateRequest                                     = 15300;
        public const int GuildLeaveRequest                                      = 15301;
        public const int GuildEnqueueActionsRequest                             = 15302;
        public const int GuildJoinRequest                                       = 15303;
        public const int GuildDiscoveryRequest                                  = 15304;
        public const int GuildSearchRequest                                     = 15305;
        public const int GuildBeginViewRequest                                  = 15307;
        public const int GuildEndViewRequest                                    = 15308;
        public const int GuildTransactionRequest                                = 15309;
        public const int GuildCreateInvitationRequest                           = 15310;
        public const int GuildRevokeInvitationRequest                           = 15311;
        public const int GuildInspectInvitationRequest                          = 15312;

        #endif

        // ScheduledPlayerDeletion (server-internal)
        public const int DeletionSweepRequest                                   = 16000;

        // BackgroundTask (server-internal)
        public const int StartBackgroundTaskRequest                             = 17000;
        public const int StartBackgroundTaskResponse                            = 17001;
        public const int BackgroundTaskStatusRequest                            = 17002;
        public const int BackgroundTaskStatusResponse                           = 17003;
        public const int ForgetBackgroundTaskRequest                            = 17004;
        public const int BackgroundTaskProgressUpdate                           = 17005;

        // PlayerSegmentSizeEstimatorActor (server-internal)
        public const int SegmentSizeEstimateRequest                             = 17100;
        public const int SegmentSizeEstimateResponse                            = 17101;

        // StatsCollectorManager, StatsCollectorProxy (server-internal)
        public const int StatsCollectorStateRequest                             = 17200;
        public const int StatsCollectorStateResponse                            = 17201;
        public const int MetaActivableStatisticsInfo                            = 17202;
        public const int StatsCollectorLiveEntityCountRequest                   = 17203;
        public const int StatsCollectorLiveEntityCountResponse                  = 17204;
        public const int UpdateShardActiveEntityList                            = 17205;
        public const int ActiveEntitiesRequest                                  = 17206;
        public const int ActiveEntitiesResponse                                 = 17207;
        public const int StatsCollectorNumConcurrentsRequest                    = 17208;
        public const int StatsCollectorNumConcurrentsResponse                   = 17209;
        public const int StatsCollectorNumConcurrentsUpdate                     = 17210;
        public const int StatsCollectorPlayerExperimentAssignmentSampleUpdate   = 17211;
        public const int StatsCollectorDatabaseEntityCountRequest               = 17212;
        public const int StatsCollectorDatabaseEntityCountResponse              = 17213;

        // Client Lifecycle Hints (client->server)
        public const int ClientLifecycleHintPausing                             = 17300;
        public const int ClientLifecycleHintUnpausing                           = 17301;
        public const int ClientLifecycleHintUnpaused                            = 17302;

        // Multiplayer Entity (server-internal)
        public const int InternalEntityStateRequest                             = 17400;
        public const int InternalEntityStateResponse                            = 17401;
        public const int InternalEntitySubscribeRequestDefault                  = 17402;
        public const int InternalEntitySubscribeResponseDefault                 = 17403;
        public const int InternalEntitySubscribeRefusedResourceCorrection       = 17404;
        public const int InternalEntitySubscribeRefusedDryRunSuccess            = 17405;
        public const int InternalEntitySubscribeRefusedTryAgain                 = 17406;
        public const int InternalEntitySubscribeRefusedNotAParticipant          = 17407;
        public const int InternalEntitySetupRequest                             = 17408;
        public const int InternalEntitySetupResponse                            = 17409;
        public const int InternalEntitySetupRefusal                             = 17410;
        public const int InternalEntityAssociatedEntityRefusedRequest           = 17411;

        // Multiplayer Entity (client->server)
        public const int EntityClientToServerEnvelope                           = 17500;
        public const int EntityEnqueueActionsRequest                            = 17501;
        public const int EntityActivated                                        = 17502;
        public const int EntityChecksumMismatchDetails                          = 17503;
        public const int EntityTimelinePingTraceQuery                           = 17504;

        // Multiplayer Entity (server->client)
        public const int EntityTimelineUpdateMessage                            = 17600;
        public const int EntityServerToClientEnvelope                           = 17601;
        public const int EntitySwitchedMessage                                  = 17602;
        public const int EntityTimelinePingTraceMarker                          = 17603;

        // Matchmaker (server-internal)
        public const int AsyncMatchmakingRequest                                = 18000;
        public const int AsyncMatchmakingResponse                               = 18001;
        public const int AsyncMatchmakingPlayerStateUpdate                      = 18002;
        public const int AsyncMatchmakerInfoRequest                             = 18003;
        public const int AsyncMatchmakerInfoResponse                            = 18004;
        public const int AsyncMatchmakerRebalanceBucketsRequest                 = 18005;
        public const int AsyncMatchmakerClearStateRequest                       = 18006;
        public const int AsyncMatchmakerInspectBucketRequest                    = 18007;
        public const int AsyncMatchmakerInspectBucketResponse                   = 18008;
        public const int AsyncMatchmakerPlayerInfoRequest                       = 18009;
        public const int AsyncMatchmakerPlayerInfoResponse                      = 18010;
        public const int AsyncMatchmakerPlayerEnrollRequest                     = 18011;
        public const int AsyncMatchmakerPlayerEnrollResponse                    = 18012;

        // NFT management, server-internal
        public const int QueryNftsRequest                                       = 19000;
        public const int QueryNftsResponse                                      = 19001;
        public const int RefreshNftsOwnedByEntityRequest                        = 19002;
        public const int RefreshNftsOwnedByEntityResponse                       = 19003;
        public const int OwnerUpdateNftStatesRequest                            = 19004;
        public const int OwnerUpdateNftStatesResponse                           = 19005;
        public const int BatchInitializeNftsRequest                             = 19025;
        public const int BatchInitializeNftsResponse                            = 19026;
        public const int BatchInitializeNftsRefusal                             = 19031;
        public const int TryGetExistingNftIdInRangeRequest                      = 19029;
        public const int TryGetExistingNftIdInRangeResponse                     = 19030;
        public const int SetNftOwnershipDebugRequest                            = 19008;
        public const int SetNftOwnershipDebugResponse                           = 19009;
        public const int NftOwnershipRemoved                                    = 19010;
        public const int NftOwnershipGained                                     = 19011;
        public const int NftStateUpdated                                        = 19012;
        public const int RefreshOwnedNftsRequest                                = 19013;
        public const int RefreshOwnedNftsResponse                               = 19014;
        public const int GetNftRequest                                          = 19015;
        public const int GetNftResponse                                         = 19016;
        public const int RefreshNftRequest                                      = 19017;
        public const int RefreshNftResponse                                     = 19018;
        public const int RepublishNftMetadataRequest                            = 19019;
        public const int RepublishNftMetadataResponse                           = 19020;
        public const int GetNftCollectionInfoRequest                            = 19021;
        public const int GetNftCollectionInfoResponse                           = 19022;
        public const int RefreshCollectionLedgerInfoRequest                     = 19023;
        public const int RefreshCollectionLedgerInfoResponse                    = 19024;

        // NFT management, client->server
        public const int PlayerNftTransactionRequest                            = 19100;

        // League (server-internal)
        public const int InternalDivisionSubscribeRefusedParticipantAvatarDesync= 19300;
        public const int InternalDivisionScoreEventMessage                      = 19301;
        public const int InternalDivisionForceSetupDebugRequest                 = 19302;
        public const int InternalDivisionForceSetupDebugResponse                = 19303;
        public const int InternalDivisionParticipantHistoryRequest              = 19304;
        public const int InternalDivisionParticipantHistoryResponse             = 19305;
        public const int InternalDivisionProgressStateRequest                   = 19306;
        public const int InternalDivisionProgressStateResponse                  = 19307;
        public const int InternalDivisionProgressStateChangedMessage            = 19308;
        public const int InternalPlayerDivisionJoinOrUpdateAvatarRequest        = 19309;
        public const int InternalDivisionMoveToNextSeasonPhaseDebugRequest      = 19310;
        public const int InternalDivisionParticipantResultRequest               = 19311;
        public const int InternalDivisionParticipantResultResponse              = 19312;
        public const int InternalPlayerDivisionAvatarBatchUpdate                = 19313;
        public const int InternalDivisionDebugSeasonScheduleUpdate              = 19314;
        public const int InternalPlayerDivisionJoinOrUpdateAvatarResponse       = 19315;
        public const int InternalDivisionParticipantIdRequest                   = 19316;
        public const int InternalDivisionParticipantIdResponse                  = 19317;

        public const int InternalLeagueJoinRequest                              = 19350;
        public const int InternalLeagueJoinRankRequest                          = 19351;
        public const int InternalLeagueJoinResponse                             = 19352;
        public const int InternalLeagueStateRequest                             = 19353;
        public const int InternalLeagueStateResponse                            = 19354;
        public const int InternalLeagueDebugAdvanceSeasonRequest                = 19355;
        public const int InternalLeagueLeaveRequest                             = 19356;
        public const int InternalLeagueDebugAddRequest                          = 19357;
        public const int InternalLeagueDebugAddResponse                         = 19358;
        public const int InternalLeagueParticipantDivisionForceUpdated          = 19359;

        // League (server -> client)

        // League (client -> server)
        public const int LeaguePutIntoDivisionDebug                             = 19503;
        public const int LeagueLeaveDivisionDebug                               = 19504;

        // BotCoordinator and BotClient (bot system internal)
        public const int InitializeBot                                          = 100_000;

        // Networking status pseudo messages (Client internal)
        public const int ConnectedToServer                                      = 200_001;
        public const int DisconnectedFromServer                                 = 200_002;
        public const int ConnectionHandshakeFailure                             = 200_003;
        public const int MessageTransportInfoWrapperMessage                     = 200_004;
    }
}
