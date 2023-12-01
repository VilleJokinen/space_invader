// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.League;
using Metaplay.Server.Matchmaking;
using Metaplay.Server.Web3;

namespace Metaplay.Server.AdminApi
{
    [AdminApiPermissionGroup("Metaplay core game server permissions")]
    public static class MetaplayPermissions
    {
        // General API access
        [MetaDescription("Access general API features.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiGeneralView = "api.general.view";


        // Notifications
        [MetaDescription("View notification campaigns.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string ApiNotificationsView = "api.notifications.view";

        [MetaDescription("Manage notification campaigns.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiNotificationsEdit = "api.notifications.edit";


        // Broadcasts
        [MetaDescription("View broadcasts.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string ApiBroadcastView = "api.broadcasts.view";

        [MetaDescription("Manage broadcasts.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiBroadcastEdit = "api.broadcasts.edit";


        // Analytics events
        [MetaDescription("View analytics events.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string ApiAnalyticsEventsView = "api.analytics_events.view";


        // Audit logs
        [MetaDescription("View an individual audit log event.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiAuditLogsView = "api.audit_logs.view";

        [MetaDescription("Search audit log events.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string ApiAuditLogsSearch = "api.audit_logs.search";


        // Segmentation
        [MetaDescription("View segmentation.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiSegmentationView = "api.segmentation.view";


        // Activables
        [MetaDescription("View activables, such as in-game events and offers.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiActivablesView = "api.activables.view";


        // Game config
        [MetaDescription("View game configs.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string ApiGameConfigView = "api.game_config.view";

        [MetaDescription("Manage existing game configs.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiGameConfigEdit = "api.game_config.edit";

        // Localizations
        [MetaDescription("View localizations.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        [LocalizationsEnabledCondition]
        public const string ApiLocalizationView = "api.localization.view";

        [MetaDescription("Manage existing localizations.")]
        [Permission(DefaultRole.GameAdmin)]
        [LocalizationsEnabledCondition]
        public const string ApiLocalizationEdit = "api.localization.edit";

        // Experiments
        [MetaDescription("View experiments.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiExperimentsView = "api.experiments.view";

        [MetaDescription("Manage experiments.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiExperimentsEdit = "api.experiments.edit";


        // System
        [MetaDescription("Edit logic versioning compatibility settings.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiSystemEditLogicVersioning = "api.system.edit_logicversioning";

        [MetaDescription("Manage maintenance mode settings.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiSystemEditMaintenance = "api.system.edit_maintenance";

        [MetaDescription("Use the player re-deletion feature.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiSystemPlayerRedelete = "api.system.player_redelete";

        [MetaDescription("Manage scan jobs.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiSystemMaintenanceJobs = "api.scan_jobs.manage";


        // Runtime options
        [MetaDescription("View server runtime options.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiRuntimeOptionsView = "api.runtime_options.view";

        [MetaDescription("View quick links.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiQuickLinksView = "api.quick_links.view";


        // Database
        [MetaDescription("View detailed database status.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiDatabaseStatus = "api.database.status";

        [MetaDescription("Inspect entity raw data.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiDatabaseInspectEntity = "api.database.inspect_entity";


        // Scan jobs
        [MetaDescription("View detailed scan job information.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiScanJobsView = "api.scan_jobs.view";

        [MetaDescription("Cancel a database scan job.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiScanJobsCancel = "api.scan_jobs.cancel";


        // Entity archives
        [MetaDescription("Export entity archives.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiEntityArchiveExport = "api.entity_archive.export";

        [MetaDescription("Import entity archives.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiEntityArchiveImport = "api.entity_archive.import";


        // Incident reports
        [MetaDescription("View player incident reports.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiIncidentReportsView = "api.incident_reports.view";


        // Generated forms & views
        [MetaDescription("Fetch type schema.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiSchemaView = "api.schema.view";

        [MetaDescription("Validate type schema.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiSchemaValidate = "api.schema.validate";


        // Players
        [MetaDescription("View individual players.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersView = "api.players.view";

        [MetaDescription("Ban/unban an individual player.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersBan = "api.players.ban";

        [MetaDescription("Edit an individual player's name.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersEditName = "api.players.edit_name";

        [MetaDescription("Schedule and unschedule player deletion.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersEditScheduledDeletion = "api.players.edit_scheduled_deletion";

        [MetaDescription("Export individual player's personal data.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersGdprExport = "api.players.gdpr_export";

        [MetaDescription("Request a platform refund for a successful in-app-purchase.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersIapRefund = "api.players.iap_refund";

        [MetaDescription("Overwrite an individual player with new data.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersOverwrite = "api.players.overwrite";

        [MetaDescription("Reconnect the auth methods of individual players.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersReconnectAccount = "api.players.reconnect_account";

        [MetaDescription("Reset an individual player.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior)]
        public const string ApiPlayersResetPlayer = "api.players.reset_player";

        [MetaDescription("Manage in-game mail to individual players.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersMail = "api.players.mail";

        [MetaDescription("Trigger any player action via the generic action endpoint.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersTriggerGenericAction = "api.players.trigger_generic_action";

        [MetaDescription("Set player as a developer.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersSetDeveloper = "api.players.set_developer";

        [GuildsEnabledCondition]
        [MetaDescription("View an individual player's guild tools.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersGuildTools = "api.players.guild_tools";

        [MetaDescription("Manage an individual player's devices & social auths.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiPlayersAuth = "api.players.auth";

        [MetaDescription("Edit which Player Experiment Groups an individual player's is assigned into.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersEditExperimentGroups = "api.players.edit_experiment_groups";

        [MetaDescription("Development/debugging tool: Force a specific phase (e.g. active) for an activable (e.g. an in-game event), for an individual player.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersForceActivablePhase = "api.players.force_activable_phase";

        [MetaDescription("Remove an IAP subscription from a player.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersRemoveIAPSubscription = "api.players.remove_iap_subscription";


        // Guilds
        [GuildsEnabledCondition]
        [MetaDescription("View individual guilds.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiGuildsView = "api.guilds.view";

        [GuildsEnabledCondition]
        [MetaDescription("Kick guild members.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiGuildsKickMember = "api.guilds.kick_member";

        [GuildsEnabledCondition]
        [MetaDescription("Edit guild details.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiGuildsEditDetails = "api.guilds.edit_details";

        [GuildsEnabledCondition]
        [MetaDescription("Edit guild roles.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiGuildsEditRoles = "api.guilds.edit_roles";

        [GuildsEnabledCondition]
        [MetaDescription("Inspect guild recommender internal state.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiGuildsInspectRecommender = "api.guilds.inspect_recommender";

        [MetaDescription("View status of matchmakers.")]
        [AsyncMatchmakingEnabledCondition]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiMatchmakersView = "api.matchmakers.view";

        [MetaDescription("Test or simulate matchmaking.")]
        [AsyncMatchmakingEnabledCondition]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string ApiMatchmakersTest = "api.matchmakers.test";

        [MetaDescription("Use admin actions in the matchmaker.")]
        [AsyncMatchmakingEnabledCondition]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiMatchmakersAdmin = "api.matchmakers.admin";

        [Web3EnabledCondition]
        [MetaDescription("View NFT states present in the game server.")]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiNftView = "api.nft.view";

        [Web3EnabledCondition]
        [MetaDescription("Initialize a new NFT state in the game server.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiNftInitialize = "api.nft.initialize";

        [Web3EnabledCondition]
        [MetaDescription("Edit an NFT's state in the game server.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiNftEdit = "api.nft.edit";

        [Web3EnabledCondition]
        [MetaDescription("Set the owner of an NFT. This applies to a debug/development feature, since NFT ownership is normally not controlled by the game server.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiNftSetOwnershipDebug = "api.nft.set_ownership_debug";

        [Web3EnabledCondition]
        [MetaDescription("Refresh NFT-related information from an NFT ledger (e.g. Immutable X).")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiNftRefreshFromLedger = "api.nft.refresh_from_ledger";

        [Web3EnabledCondition]
        [MetaDescription("Republish an NFT's metadata.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiNftRepublishMetadata = "api.nft.republish_metadata";

        // Leagues
        [MetaDescription("View status of leagues and divisions")]
        [LeaguesEnabledCondition]
        [Permission(DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string ApiLeaguesView = "api.leagues.view";

        [MetaDescription("Forcibly set or reset the current phase of a season or division, bypassing normal season progression.")]
        [LeaguesEnabledCondition]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiLeaguesPhaseDebug = "api.leagues.phase_debug";

        [MetaDescription("Forcibly add or remove a participant from a league.")]
        [LeaguesEnabledCondition]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiLeaguesEditParticipants = "api.leagues.participant_edit";

        // Dashboard only
        [MetaDescription("View the Dashboard.")]
        [Permission(isDashboardOnly: true, DefaultRole.GameAdmin, DefaultRole.GameViewer, DefaultRole.CustomerSupportSenior, DefaultRole.CustomerSupportAgent)]
        public const string DashboardView = "dashboard.view";

        [MetaDescription("Use the developer mode.")]
        [Permission(isDashboardOnly: true, DefaultRole.GameAdmin)]
        public const string DashboardDeveloperMode = "dashboard.developer_mode";

        [MetaDescription("View server environment.")]
        [Permission(isDashboardOnly: true, DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string DashboardEnvironmentView = "dashboard.environment.view";

        [MetaDescription("View links to Grafana dashboard.")]
        [Permission(isDashboardOnly: true, DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string DashboardGrafanaView = "dashboard.grafana.view";

        [MetaDescription("View system settings.")]
        [Permission(isDashboardOnly: true, DefaultRole.GameAdmin, DefaultRole.GameViewer)]
        public const string DashboardSystemView = "dashboard.system.view";


        // Automated test endpoints
        [MetaDescription("Use all automated-test endpoints.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiTestsAll = "api.tests.all";
    }
}
