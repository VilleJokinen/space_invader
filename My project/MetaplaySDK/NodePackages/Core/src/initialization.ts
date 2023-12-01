// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import type { AxiosError } from 'axios'
import { useAttrs, watch } from 'vue'

import { initializeAuth, useGameServerApi, SseHandler, useGameServerApiStore, registerErrorVisualizationHandler } from '@metaplay/game-server-api'
import { showErrorToast, useUiStore } from '@metaplay/meta-ui'
import { registerHandler, usePermissions } from '@metaplay/meta-ui-next'
import { fetchSubscriptionDataOnceOnly, useManuallyManagedStaticSubscription } from '@metaplay/subscriptions'

import { addNavigationEntry, addActorInfoToOverviewPage, gameSpecificInitializationStep, getGameData } from './integration_api/integrationApi'
import { addUiComponent } from './integration_api/uiPlacementApis'
import { OverviewListItem } from './integration_api/overviewListsApis'
import { getBackendStatusSubscriptionOptions, getDashboardOptionsSubscriptionOptions, getRuntimeOptionsSubscriptionOptions, getStaticConfigSubscriptionOptions } from './subscription_options/general'
import { useCoreStore } from './coreStore'
import { router } from './router'
import { sleep } from './coreUtils'
import { handleMetaplayApiError } from './coreErrorHandler'

let sseHandler = null
const gameServerApi = useGameServerApi()

/**
 * Type for the initialization step.
 */
interface InitializationStep {
  /**
     * Used internally in the steps runner.
     */
  name: string
  /**
     * Shown in the loading sequence and error page UI.
     */
  displayName: string
  /**
     * User facing error message shown when the initialization fails.
     */
  errorMessage: string
  /**
     * User facing error resolution shown when the initialization fails.
     */
  errorResolution: string
  /**
     * Async function that performs this step.
     * @param changeDisplayName Optionally used to change displayName when step is running.
     */
  action: (changeDisplayName: (name: string) => void) => Promise<void>
}

/**
 * Initialize the Dashboard based on game server config. This is done in a number of discrete steps. If a
 * step throws an Error then the initialization process has failed and an error is shown to the user.
 * The return value from each step is passed to the next.
 */
export const initializationStepsRunner = async () => {
  const coreStore = useCoreStore()

  for (const [i, stepConfig] of allInitializationSteps.entries()) {
    const stepName = `${i + 1}: ${stepConfig.displayName}`
    try {
      coreStore.backendConnectionStatus.status = stepConfig.name
      coreStore.backendConnectionStatus.displayName = stepConfig.displayName
      await stepConfig.action((newDisplayName: string) => {
        coreStore.backendConnectionStatus.displayName = newDisplayName
      })
    } catch (err) {
      const error = err as Error
      // A step failed. This is a fatal error.
      coreStore.backendConnectionStatus.status = 'error'
      coreStore.backendConnectionStatus.error = {
        stepName: stepConfig.name,
        errorMessage: stepConfig.errorMessage,
        errorResolution: stepConfig.errorResolution,
        errorObject: error
      }
      throw new Error(`Failed initialization step ${stepName}, reason: ${err}`)
    }
  }
}
/**
 * List of initialization steps.
 */
const allInitializationSteps: InitializationStep[] = [
  /**
   * Register a handler for the game server API's error visualization.
   */
  {
    name: 'initializeErrorHandler',
    displayName: 'Initializing the error handler',
    errorMessage: 'Failed to initialize the error handler.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      // Register handler for custom MetaplayAPI errors so that MErrorCallout knows how to display them nicely.
      registerHandler(handleMetaplayApiError)

      registerErrorVisualizationHandler((title: string, message: string) => {
        showErrorToast(message, title)
      })
    }
  },
  /**
   * Perform the initial /hello handshake to bootstrap the connection.
   */
  {
    name: 'hello',
    displayName: 'Connecting to the game server',
    errorMessage: 'Failed to connect to the game server.',
    errorResolution: 'Is the game server running and accessible?',
    action: async (changeDisplayName: (name: string) => void) => {
      const coreStore = useCoreStore()
      const gameServerApiStore = useGameServerApiStore()
      // Read initial config data from the game server.
      let hello: any = null
      let retries = 5
      while (!hello) {
        try {
          hello = (await gameServerApi.get('/hello', { timeout: 2000 })).data
        } catch (err) {
          const error = err as Error
          if (error.message === 'Network Error' || error.message.startsWith('timeout of ')) {
            if (retries-- <= 0) {
              // If nothing's happening after a number of retries, we'll try reloading
              // the page completely.
              changeDisplayName('Please hold while we try to connect you')
              document.location.reload()
              await sleep(10_000)
            } else {
              await sleep(1_000)
            }
          } else {
            throw error
          }
        }
      }

      // Check that we got sensible data back from the endpoint /hello.
      if (typeof hello !== 'object') {
        throw new Error(`/hello returned a ${typeof hello} ('${String(hello).substring(0, 20)}...') rather than an object!`)
      }
      ['authConfig'].forEach(x => {
        if (!hello[x]) {
          throw new Error(`/hello missing required config value '${x}'!`)
        }
      })

      if (hello.grafanaUri) {
        // Ensure that there are no trailing '/' on the uri.
        hello.grafanaUri = hello.grafanaUri.replace(/[/]*$/, '')
      }

      coreStore.hello = hello
      gameServerApiStore.hasConnected = true
      gameServerApiStore.isConnected = true
    }
  },
  /**
   * Set up the default UI components. The order is important here.
   */
  {
    name: 'uiDefaultComponent',
    displayName: 'Preparing the UI',
    errorMessage: 'Error occurred while placing the default UI components.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      // Players Views
      addUiComponent('Players/Details/Overview:Title', { uniqueId: 'Title', vueComponent: async () => await import('./components/playerdetails/PlayerOverviewTitle.vue') })
      addUiComponent('Players/Details/Overview:Subtitle', { uniqueId: 'PlayerGuildMembership', vueComponent: async () => await import('./components/playerdetails/PlayerGuildMembership.vue') })
      addUiComponent('Players/Details/Overview:LeftPanel', { uniqueId: 'PlayerResourcesList', vueComponent: async () => await import('./components/playerdetails/PlayerResourcesList.vue') })

      addUiComponent('Players/Details/AdminActions:Dangerous', { uniqueId: 'ExportPlayer', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionExport.vue') })
      addUiComponent('Players/Details/AdminActions:Dangerous', { uniqueId: 'OverwritePlayer', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionOverwrite.vue') })
      addUiComponent('Players/Details/AdminActions:Dangerous', { uniqueId: 'ResetPlayer', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionReset.vue') })
      addUiComponent('Players/Details/AdminActions:Dangerous', { uniqueId: 'DeletePlayer', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionDelete.vue') })

      addUiComponent('Players/Details/AdminActions:Disruptive', { uniqueId: 'EditName', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionEditName.vue') })
      addUiComponent('Players/Details/AdminActions:Disruptive', { uniqueId: 'ReconnectDevices', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionReconnectAccounts.vue') })
      addUiComponent('Players/Details/AdminActions:Disruptive', { uniqueId: 'JoinExperiment', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionJoinExperiments.vue') })
      addUiComponent('Players/Details/AdminActions:Disruptive', { uniqueId: 'BanPlayer', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionBan.vue') })
      addUiComponent('Players/Details/AdminActions:Disruptive', { uniqueId: 'MarkAsDeveloper', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionSetDeveloper.vue') })

      addUiComponent('Players/Details/AdminActions:Gentle', { uniqueId: 'SendMail', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionSendMail.vue') })
      addUiComponent('Players/Details/AdminActions:Gentle', { uniqueId: 'GdprExport', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionGdprExport.vue') })
      addUiComponent('Players/Details/AdminActions:Gentle', { uniqueId: 'GuildTools', vueComponent: async () => await import('./components/playerdetails/adminactions/PlayerActionGuildTools.vue') })

      addUiComponent('Players/Details/Tab0', { uniqueId: 'Inbox', vueComponent: async () => await import('./components/playerdetails/PlayerInboxCard.vue') })
      addUiComponent('Players/Details/Tab0', { uniqueId: 'BroadcastHistory', vueComponent: async () => await import('./components/playerdetails/PlayerBroadcastsCard.vue') })

      addUiComponent('Players/Details/Tab1', { uniqueId: 'LoginMethods', vueComponent: async () => await import('./components/playerdetails/PlayerLoginMethodsCard.vue') })
      addUiComponent('Players/Details/Tab1', { uniqueId: 'DeviceHistory', vueComponent: async () => await import('./components/playerdetails/PlayerDeviceHistoryCard.vue') })
      addUiComponent('Players/Details/Tab1', { uniqueId: 'LoginHistory', vueComponent: async () => await import('./components/playerdetails/PlayerLoginHistoryCard.vue') })
      addUiComponent('Players/Details/Tab1', { uniqueId: 'EventLog', vueComponent: async () => await import('./components/playerdetails/PlayerEventLogCard.vue') })
      addUiComponent('Players/Details/Tab1', {
        uniqueId: 'AuditLog',
        vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
        props: {
          targetType: 'Player',
          targetId: () => {
            // useAttrs to retrive the targetId from the component attributes.
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.playerId.split(':')[1]
          }
        }
      })

      addUiComponent('Players/Details/Tab2', { uniqueId: 'PurchaseHistory', vueComponent: async () => await import('./components/playerdetails/PlayerPurchaseHistoryCard.vue') })
      addUiComponent('Players/Details/Tab2', { uniqueId: 'SubscriptionHistory', vueComponent: async () => await import('./components/playerdetails/PlayerSubscriptionHistoryCard.vue') })

      addUiComponent('Players/Details/Tab3', { uniqueId: 'Segments', vueComponent: async () => await import('./components/playerdetails/PlayerSegmentsCard.vue') })
      addUiComponent('Players/Details/Tab3', { uniqueId: 'Experiments', vueComponent: async () => await import('./components/playerdetails/PlayerExperimentsCard.vue') })
      // addUiComponent('Players/Details/Tab3', { uniqueId: 'Offers', vueComponent: async () => await import('./components/playerdetails/offer.vue') })
      // addUiComponent('Players/Details/Tab3', { uniqueId: 'Events', vueComponent: async () => await import('./components/playerdetails/events.vue') })

      addUiComponent('Players/Details/Tab4', { uniqueId: 'IncidentHistory', vueComponent: async () => await import('./components/playerdetails/PlayerIncidentHistoryCard.vue') })

      // Guilds Views
      addUiComponent('Guilds/Details/AdminActions:Gentle', { uniqueId: 'ChangeRole', vueComponent: async () => await import('./components/guilds/adminactions/GuildActionChangeRole.vue') })
      addUiComponent('Guilds/Details/AdminActions:Gentle', { uniqueId: 'EditNameAndDescription', vueComponent: async () => await import('./components/guilds/adminactions/GuildActionEditNameAndDescription.vue') })

      addUiComponent('Guilds/Details/AdminActions:Disruptive', { uniqueId: 'KickMember', vueComponent: async () => await import('./components/guilds/adminactions/GuildActionKickMember.vue') })

      addUiComponent('Guilds/Details/GameState', { uniqueId: 'GuildMembers', vueComponent: async () => await import('./components/guilds/GuildMemberListCard.vue') })
      addUiComponent('Guilds/Details/GameState', { uniqueId: 'GuildInvites', vueComponent: async () => await import('./components/guilds/GuildInviteListCard.vue') })

      addUiComponent('Guilds/Details/GuildAdminLogs', {
        uniqueId: 'GuildEntityEventLog',
        vueComponent: async () => await import('./components/entityeventlogs/EntityEventLogCard.vue'),
        props: {
          entityKind: 'Guild',
          entityId: () => {
            // useAttrs to access and pass the entityId to the EntityEventLogCard component.
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.guildId
          }
        }
      })

      addUiComponent('Guilds/Details/GuildAdminLogs', {
        uniqueId: 'GuildAuditLog',
        vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
        props: {
          targetType: 'Guild',
          targetId: () => {
          // useAttrs to retrive the targetId from the component attributes.
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.guildId.split(':')[1]
          }
        }
      })

      // Incidents Views
      addUiComponent('PlayerIncidents/List', { uniqueId: 'IncidentStatistics', vueComponent: async () => await import('./components/incidents/GlobalIncidentStatisticsCard.vue') })
      addUiComponent('PlayerIncidents/List', { uniqueId: 'GlobalIncidents', vueComponent: async () => await import('./components/incidents/GlobalIncidentHistoryCard.vue'), props: { count: 200 } })

      addUiComponent('PlayerIncidents/Details', { uniqueId: 'NetworkStatus', vueComponent: async () => await import('./components/incidents/IncidentNetworkStatus.vue'), width: 'full' })
      addUiComponent('PlayerIncidents/Details', { uniqueId: 'StackTrace', vueComponent: async () => await import('./components/incidents/IncidentStackTrace.vue'), width: 'full' })
      addUiComponent('PlayerIncidents/Details', { uniqueId: 'ClientLogs', vueComponent: async () => await import('./components/incidents/IncidentClientLogs.vue'), width: 'full' })
      addUiComponent('PlayerIncidents/Details', { uniqueId: 'ClientSystemInfo', vueComponent: async () => await import('./components/incidents/IncidentClientSystemInfo.vue') })
      addUiComponent('PlayerIncidents/Details', { uniqueId: 'ClientPlatformInfo', vueComponent: async () => await import('./components/incidents/IncidentClientPlatformInfo.vue') })
      addUiComponent('PlayerIncidents/Details', { uniqueId: 'ClientApplicationInfo', vueComponent: async () => await import('./components/incidents/IncidentClientApplicationInfo.vue') })

      addUiComponent('PlayerIncidents/ByType', { uniqueId: 'GlobalIncidents', vueComponent: async () => await import('./components/incidents/GlobalIncidentHistoryCard.vue'), width: 'full', props: { count: 200 } })

      // ScanJobs Views
      addUiComponent('ScanJobs/List', { uniqueId: 'ActiveScanJobs', vueComponent: async () => await import('./components/scanjobs/ActiveScanJobsCard.vue') })
      addUiComponent('ScanJobs/List', { uniqueId: 'LatestScanJobs', vueComponent: async () => await import('./components/scanjobs/LatestScanJobsCard.vue') })
      addUiComponent('ScanJobs/List', { uniqueId: 'PastScanJobs', vueComponent: async () => await import('./components/scanjobs/PastScanJobsCard.vue') })

      // Leagues Views
      addUiComponent('Leagues/List', { uniqueId: 'LeaguesList', vueComponent: async () => await import('./components/leagues/LeagueListCard.vue') })

      addUiComponent('Leagues/Details', { uniqueId: 'LeagueSeasons', vueComponent: async () => await import('./components/leagues/LeagueSeasonsCard.vue') })
      addUiComponent('Leagues/Details', { uniqueId: 'LeagueSchedule', vueComponent: async () => await import('./components/leagues/LeagueScheduleCard.vue') })
      addUiComponent('Leagues/Details', {
        uniqueId: 'LeagueAuditLog',
        vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
        props: {
          targetType: 'LeagueManager',
          dataCy: 'league-audit-log-card',
          targetId: () => {
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.leagueId.split(':')[1]
          }
        }
      })

      addUiComponent('Leagues/Season/Details', { uniqueId: 'LeagueSeasonRanks', vueComponent: async () => await import('./components/leagues/LeagueSeasonRanksCard.vue'), width: 'full' })

      addUiComponent('Leagues/Season/RankDivision/Details', { uniqueId: 'LeagueSeasonRankDivisionParticipants', vueComponent: async () => await import('./components/leagues/LeagueSeasonRankDivisionParticipantsCard.vue'), width: 'full' })
      addUiComponent('Leagues/Season/RankDivision/Details', {
        uniqueId: 'LeagueAuditLog',
        vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
        props: {
          targetType: 'LeagueManager',
          dataCy: 'league-audit-log-card',
          targetId: () => {
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.leagueId.split(':')[1]
          }
        }
      })

      // Broadcasts Views
      addUiComponent('Broadcasts/Details', { uniqueId: 'BroadcastContent', vueComponent: async () => await import('./components/mails/BroadcastContentCard.vue'), width: 'full' })
      addUiComponent('Broadcasts/Details', { uniqueId: 'BroadcastSegmentTarget', vueComponent: async () => await import('./components/mails/BroadcastSegmentTargetCard.vue') })
      addUiComponent('Broadcasts/Details', { uniqueId: 'BroadcastPlayerTarget', vueComponent: async () => await import('./components/mails/BroadcastPlayerTargetCard.vue') })
      addUiComponent('Broadcasts/Details', {
        uniqueId: 'BroadcastAuditLog',
        vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
        props: {
          targetType: '$Broadcast',
          targetId: () => {
            // useAttrs to retrieve the targetId from the component attributes.
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.broadcastId
          }
        }
      })

      // System Views
      addUiComponent('System/Details', { uniqueId: 'MaintenanceMode', vueComponent: async () => await import('./components/system/MaintenanceModeSettings.vue') })
      addUiComponent('System/Details', { uniqueId: 'LogicVersionRedirectSettings', vueComponent: async () => await import('./components/system/LogicVersionRedirectSettings.vue') })
      addUiComponent('System/Details', { uniqueId: 'ImportEntities', vueComponent: async () => await import('./components/system/ImportEntities.vue') })
      addUiComponent('System/Details', { uniqueId: 'RedeletePlayers', vueComponent: async () => await import('./components/system/RedeletePlayers.vue') })

      // Localization views
      addUiComponent('Localization/List', { uniqueId: 'LocalizationListCard', vueComponent: async () => await import('./components/localization/LocalizationListCard.vue'), width: 'full' })

      addUiComponent('Localization/Details', {
        uniqueId: 'LocalizationAuditLog',
        vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
        props: {
          targetType: '$Localization',
          targetId: () => {
          // useAttrs to retrive the targetId from the component attributes.
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.localizationId
          }
        }
      })

      // GameConfig Views
      addUiComponent('GameConfigs/List', { uniqueId: 'GameConfigListCard', vueComponent: async () => await import('./components/gameconfig/GameConfigListCard.vue') })
      addUiComponent('GameConfigs/List', { uniqueId: 'GameConfigBuildHistoryListCard', vueComponent: async () => await import('./components/gameconfig/GameConfigBuildHistoryListCard.vue') })

      addUiComponent('GameConfigs/Details', {
        uniqueId: 'GameConfigAuditLog',
        vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
        props: {
          targetType: '$GameConfig',
          targetId: () => {
          // useAttrs to retrive the targetId from the component attributes.
            const attrs: {[ key: string ]: any } = useAttrs()
            return attrs.gameConfigId
          }
        }
      })

      addUiComponent('GameConfigs/Logs', { uniqueId: 'GameConfigBuildReport', vueComponent: async () => await import('./components/gameconfig/GameConfigBuildReportCard.vue') })
      addUiComponent('GameConfigs/Logs', { uniqueId: 'GameConfigValidationMessages', vueComponent: async () => await import('./components/gameconfig/GameConfigValidationMessagesCard.vue') })

      // Overview Views
      addUiComponent('OverviewView', { uniqueId: 'OverviewChart', vueComponent: async () => await import('./components/overview/OverviewChartCard.vue'), width: 'full' })
      addUiComponent('OverviewView', { uniqueId: 'IncidentStatistics', vueComponent: async () => await import('./components/incidents/GlobalIncidentStatisticsCard.vue'), props: { limit: 5 } })
      addUiComponent('OverviewView', { uniqueId: 'GlobalIncidents', vueComponent: async () => await import('./components/incidents/GlobalIncidentHistoryCard.vue'), props: { count: 10, showMainPageLink: true } })
      addUiComponent('OverviewView', { uniqueId: 'GrafanaLinks', vueComponent: async () => await import('./components/overview/GrafanaLinkCard.vue'), width: 'full' })

      // Developers Views
      addUiComponent('Developers/List', { uniqueId: 'DevelopersList', vueComponent: async () => await import('./components/developers/DeveloperListCard.vue') })
    }
  },
  /**
   * Start authentication.
   */
  {
    name: 'initializeAuth',
    displayName: 'Preparing to authenticate',
    errorMessage: 'An error occurred during the authentication initialization process.',
    errorResolution: 'Is the authentication properly configured for this environment?',
    action: async () => {
      const coreStore = useCoreStore()
      const gameServerApiStore = useGameServerApiStore()

      // Attempt to initialize authentication.
      const authResult = await initializeAuth(coreStore.hello.authConfig)

      if (authResult.state === 'error') {
        // An error occurred during authentication initialization.
        throw new Error('Failed to initialize authentication: ' + authResult.details)
      } else if (authResult.state === 'not_enough_permissions') {
        // The user lacks the necessary permissions to continue the login process.
        gameServerApiStore.auth.requiresBasicPermissions = true

        // Pause execution indefinitely.
        while (true) {
          await sleep(60_000)
        }
      } else if (authResult.state === 'require_login') {
        // The user needs to log in.
        // Pause execution briefly so the user can see the loading message before being redirected to the login page.
        await sleep(500)
        gameServerApiStore.auth.requiresLogin = true

        // Pause execution indefinitely.
        while (true) {
          await sleep(60_000)
        }
      } else if (authResult.state === 'success') {
        // Authentication initialization was successful.
        // Set the UI permissions.
        // Note: This could probably now be cleaned up a bit as there is less need for the gameServerApiStore to hold so much auth state.
        const { setPermissions } = usePermissions()
        setPermissions(gameServerApiStore.auth.userPermissions)

        // Watch for future permission changes to update the UI.
        watch(() => gameServerApiStore.auth.userPermissions, (newValue) => setPermissions(newValue))
      }
    }
  },
  /**
   * Set up guilds if the game server is configured to use them.
   */
  {
    name: 'guilds',
    displayName: 'Thinking about guilds',
    errorMessage: 'Error occurred while initializing guild features.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      const coreStore = useCoreStore()
      if (coreStore.hello.featureFlags.guilds) {
        // Add guilds to navigation.
        addNavigationEntry(
          {
            path: '/guilds',
            name: 'Manage Guilds',
            component: async () => await import('./views/GuildListView.vue')
          },
          {
            icon: 'chess-rook',
            sidebarTitle: 'Guilds',
            sidebarOrder: 20,
            category: 'Game',
            permission: 'api.guilds.view'
          })
        addNavigationEntry(
          {
            path: '/guilds/:id',
            name: 'Manage Guild',
            component: async () => await import('./views/GuildDetailView.vue')
          },
          {
            icon: 'chess-rook'
          })
        // Add guild actor info to the landing page.
        addActorInfoToOverviewPage('Guild', 'Live Guild Actors', undefined, 'Total Guilds')

        // Create default view for guild detail overview card.
        coreStore.overviewLists.guild = [
          OverviewListItem.asDate('Created', (guild: any) => { return guild.model.createdAt }),
          OverviewListItem.asString('Members', (guild: any) => { return `${Object.keys(guild.model.members).length} / ${guild.model.maxNumMembers}` }, undefined, true),
          OverviewListItem.asNumber('Members Online', (guild: any) => { return Object.values(guild.model.members).filter((member: any) => member.isOnline).length }),
          OverviewListItem.asString(
            'Active Members in 7 Days',
            (guild: any) => {
              const dateNow = Date.now()
              const active7Days = Object.values(guild.model.members).filter(member => {
                const memberLastOnline = new Date((member as any).lastOnlineAt).getTime()
                const diffTime = Math.abs(dateNow - memberLastOnline)
                const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24))
                return diffDays <= 7
              }).length
              return !active7Days ? '-' : `${Math.round(active7Days / Object.keys(guild.model.members).length * 100)} %`
            },
            undefined,
            true),
          OverviewListItem.asString('Lifecycle Phase', (guild: any) => { return guild.model.lifecyclePhase }),
        ]
      }
    }
  },
  /**
   * Set up matchmaking if the game server is configured to use it.
   */
  {
    name: 'matchmaking',
    displayName: 'Thinking about matchmaking',
    errorMessage: 'Error while loading matchmaking feature.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      const coreStore = useCoreStore()
      // Enable async matchmaker
      if (coreStore.hello.featureFlags.asyncMatchmaker) {
        addNavigationEntry(
          {
            path: '/matchmakers',
            name: 'Manage Matchmaking',
            component: async () => await import('./views/MatchmakerListView.vue')
          },
          {
            icon: 'chess',
            sidebarTitle: 'Matchmaking',
            sidebarOrder: 30,
            category: 'Game',
            permission: 'api.matchmakers.view'
          })
        addNavigationEntry(
          {
            path: '/matchmakers/:matchmakerId',
            name: 'Manage Matchmaker',
            component: async () => await import('./views/MatchmakerDetailView.vue'),
            props: true
          },
          {
            icon: 'chess'
          })
        addUiComponent('Players/Details/Tab0', { uniqueId: 'player-matchmakers', vueComponent: async () => await import('./components/playerdetails/PlayerMatchmakingCard.vue') })
        addUiComponent('Matchmakers/List', { uniqueId: 'AsyncMatchmakerList', vueComponent: async () => await import('./components/matchmakers/AsyncMatchmakerListCard.vue') })
        addUiComponent('Matchmakers/List', { uniqueId: 'RealtimeMatchmakerList', vueComponent: async () => await import('./components/matchmakers/RealtimeMatchmakerListCard.vue') })
        addUiComponent('Matchmakers/Details', { uniqueId: 'MatchmakerBucketChart', vueComponent: async () => await import('./components/matchmakers/MatchmakerBucketChart.vue'), width: 'full' })
        addUiComponent('Matchmakers/Details', { uniqueId: 'MatchmakerAllBuckets', vueComponent: async () => await import('./components/matchmakers/MatchmakerBucketsCard.vue') })
        addUiComponent('Matchmakers/Details', { uniqueId: 'MatchmakerTopPlayers', vueComponent: async () => await import('./components/matchmakers/MatchmakerTopPlayersCard.vue') })
        addUiComponent('Matchmakers/Details',
          {
            uniqueId: 'AsyncMatchmakerAuditLog',
            vueComponent: async () => await import('./components/auditlogs/AuditLogCard.vue'),
            props: {
              targetType: 'AsyncMatchmaker',
              targetId: () => {
                // useAttrs to retrive the targetId from the component attributes.
                const attrs: {[ key: string ]: any } = useAttrs()
                return attrs.matchmakerId.split(':')[1]
              }
            },
            width: 'full'
          }
        )
      }
    }
  },
  /**
   * Set up Web3 features if the game server is configured to use them
   */
  {
    name: 'web3',
    displayName: 'Thinking about Web3',
    errorMessage: 'Error while initializing the Web3 feature.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      const coreStore = useCoreStore()
      if (coreStore.hello.featureFlags.web3) {
        addNavigationEntry(
          {
            path: '/web3',
            name: 'Web3',
            component: async () => await import('./views/Web3View.vue')
          },
          {
            icon: 'cubes',
            sidebarTitle: 'Web3',
            sidebarOrder: 40,
            category: 'Game',
            permission: 'api.nft.view'
          })
        addNavigationEntry(
          {
            path: '/web3/nft/:collectionId',
            name: 'NFT Collection',
            component: async () => await import('./views/Web3NftCollectionDetailView.vue'),
            props: true
          },
          {
            icon: 'cubes',
            permission: 'api.nft.view'
          })
        addNavigationEntry(
          {
            path: '/web3/nft/:collectionId/:tokenId',
            name: 'NFT',
            component: async () => await import('./views/Web3NftDetailView.vue'),
            props: true
          },
          {
            icon: 'cube',
            permission: 'api.nft.view'
          })

        addUiComponent('Players/Details/Tab2', {
          uniqueId: 'player-nfts',
          vueComponent: async () => await import('./components/playerdetails/PlayerNftsCard.vue')
        })
      }
    }
  },
  /**
  * Set up leagues if the game server is configured to use them
  */
  {
    name: 'playerLeagues',
    displayName: 'Thinking about leagues',
    errorMessage: 'Error while loading the leagues feature.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      const coreStore = useCoreStore()
      // Enable player leagues
      if (coreStore.hello.featureFlags.playerLeagues) {
        addNavigationEntry(
          {
            path: '/leagues',
            name: 'Manage Leagues',
            component: async () => await import('./views/LeagueListView.vue')
          },
          {
            icon: 'trophy',
            sidebarTitle: 'Leagues',
            sidebarOrder: 36,
            category: 'Game',
            permission: 'api.leagues.view'
          })
        addNavigationEntry(
          {
            path: '/leagues/:leagueId',
            name: 'Manage League',
            component: async () => await import('./views/LeagueDetailView.vue')
          },
          {
            icon: 'trophy',
            permission: 'api.leagues.view'
          })
        addNavigationEntry(
          {
            path: '/leagues/:leagueId/:seasonId',
            name: 'View League Season',
            component: async () => await import('./views/LeagueSeasonDetailView.vue')
          },
          {
            icon: 'trophy',
            permission: 'api.leagues.view'
          })
        addNavigationEntry(
          {
            path: '/leagues/:leagueId/:seasonId/:divisionId',
            name: "Manage Season Rank's Division",
            component: async () => await import('./views/LeagueSeasonRankDivisionDetailView.vue')
          },
          {
            icon: 'trophy',
            permission: 'api.leagues.view'
          })

        addUiComponent('Players/Details/Tab0', {
          vueComponent: async () => await import('./components/leagues/PlayerLeaguesCard.vue'),
          uniqueId: 'player-leagues'
        }
        )
      }
    }
  },
  /**
  * Set up localization if the game server is configured to use them
  */
  {
    name: 'Localization',
    displayName: 'Thinking about localizations',
    errorMessage: 'Error while loading the localization feature.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      const coreStore = useCoreStore()
      // Enable player leagues
      if (coreStore.hello.featureFlags.localization) {
        addNavigationEntry(
          {
            path: '/localization',
            name: 'Manage Localizations',
            component: async () => await import('./views/LocalizationListView.vue'),
          },
          {
            icon: 'language',
            sidebarTitle: 'Localizations',
            sidebarOrder: 50,
            category: 'Game',
            permission: 'api.localization.view'
          })
        addNavigationEntry(
          {
            path: '/localization/:id',
            name: 'Manage Localization',
            component: async () => await import('./views/LocalizationDetailView.vue')
          },
          {
            icon: 'table',
            permission: 'api.localization.view'
          })
        addNavigationEntry(
          {
            path: '/localization/diff',
            name: 'Compare Localizations',
            component: async () => await import('./views/LocalizationDiffView.vue')
          },
          {
            icon: 'binoculars',
            permission: 'api.localization.view'
          })
      }
    }
  },
  /**
   * Fetch the rest of the game data from the game server.
   */
  {
    name: 'fetchGameData',
    displayName: 'Fetching game data',
    errorMessage: 'An error occurred while fetching game data.',
    errorResolution: 'Did your game configs build without errors?',
    action: async () => {
      // Poke the game data subscription to warm up the cache.
      const promise = getGameData()
      const coreStore = useCoreStore()
      const staticConfig = await fetchSubscriptionDataOnceOnly<any>(getStaticConfigSubscriptionOptions())

      // Dynamic components shenanigans.
      // TODO R20: Convert this auto-discovery stuff into an integration API?
      /**
       * Helper function to create component registry by converting filenames to component names.
       * @param components List of components.
       * @returns Dictionary of `component name -> components` mappings.
       */
      function mapComponents (components: { [key: string]: any }) {
        const mappedComponents = Object.fromEntries(Object.entries(components).map(([filename, component]) => {
          const componentName = filename.split('/').pop()?.replace('.vue', '')
          return [componentName, component]
        }))
        return mappedComponents
      }

      // Resolve all core views that exist.
      const viewComponents = {
        ...mapComponents(import.meta.glob('./views/**/*.vue'))
      }

      // Add nav for all game-specific activable categories.
      for (const [categoryKey, categoryDataUnknown] of Object.entries(staticConfig.serverReflection.activablesMetadata.categories)) {
        const categoryData = categoryDataUnknown as any
        const customization = coreStore.gameSpecific.activableCustomization[categoryKey]

        // Get the icon for this category.
        const icon = customization?.icon || 'calendar-alt'

        // Try to find a view for this category.
        const listPageComponent = viewComponents[`${categoryKey}ListView`] || viewComponents.ActivableListView
        const detailPageComponent = viewComponents[`${categoryKey}ListView`] ? viewComponents[`${categoryKey}DetailView`] || viewComponents.ActivableDetailView : viewComponents.ActivableDetailView
        const urlPathName = '/' + (customization?.pathName || `activables/${categoryKey}`)

        // Add detail pages for each kind.
        const secondaryPathHighlights = []
        for (const kindId of categoryData.kinds) {
          const kindInfo = staticConfig.serverReflection.activablesMetadata.kinds[kindId]
          addNavigationEntry(
            {
              props: { kindId },
              path: `${urlPathName}/${kindId}/:id`,
              name: `View ${kindInfo.displayName}`,
              component: detailPageComponent
            }, {})
          secondaryPathHighlights.push(`${urlPathName}/${kindId}/`)
        }

        // Add sidebar and sidebar route
        const displayName = customization?.sidebarNavName || categoryData.displayName
        addNavigationEntry(
          {
            props: { categoryKey },
            path: `${urlPathName}`,
            name: `View ${displayName}`,
            component: listPageComponent
          },
          {
            icon,
            sidebarTitle: displayName,
            sidebarOrder: 60,
            category: 'LiveOps',
            permission: 'api.activables.view',
            secondaryPathHighlights
          })
      }

      await promise
    }
  },
  /**
   * Initialize overview lists.
   */
  {
    name: 'initializeOverviewLists',
    displayName: 'Creating overview lists',
    errorMessage: 'An error occurred while creating overview lists.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      const coreStore = useCoreStore()

      // Create default view for player detail overview card.
      coreStore.overviewLists.player = [
        OverviewListItem.asDate('Joined', (player: any) => { return player.model.stats.createdAt }),
        OverviewListItem.asLanguage('Language', (player: any) => { return player.model.language }),
        OverviewListItem.asCountry('Country', (player: any) => { return !player.model.lastKnownLocation ? 'Unknown' : player.model.lastKnownLocation.country.isoCode }),
        OverviewListItem.asString('Last Device Model', (player: any) => { return !player.model.loginHistory.length ? 'Unknown' : player.model.loginHistory[0].deviceModel }),
        OverviewListItem.asTimeAgo('Last Login', (player: any) => { return player.model.stats.lastLoginAt }),
        OverviewListItem.asNumber('Total Logins', (player: any) => { return player.model.stats.totalLogins }),
        OverviewListItem.asCurrency('Total Spend', (player: any) => { return player.model.totalIapSpend }),
        OverviewListItem.asNumber('Incident Reports', (player: any) => { return player.incidentHeaders.length }, undefined, true)
      ]

      // Create default view for player reconnect account overview card.
      coreStore.overviewLists.playerReconnectPreview = [
        OverviewListItem.asString('Name', (player: any) => { return player.model.playerName || 'n/a' }),
        OverviewListItem.asDate('Joined', (player: any) => { return player.model.stats.createdAt }),
        OverviewListItem.asTimeAgo('Last Login', (player: any) => { return player.model.stats.lastLoginAt }),
        OverviewListItem.asCurrency('Total Spend', (player: any) => { return player.model.totalIapSpend })
      ]
    }
  },
  /**
   * Call game-specific initialization code.
   */
  {
    name: 'gameSpecificInit',
    displayName: 'Adding game-specific customizations',
    errorMessage: 'An error occurred while adding game-specific customizations.',
    errorResolution: 'Are there any errors in your `gameSpecific.ts` file?',
    action: async () => {
      if (!gameSpecificInitializationStep) {
        return
      }
      await gameSpecificInitializationStep()
    }
  },
  /**
   * Subscribe to server side events.
   */
  {
    name: 'sseSubscriptions',
    displayName: 'Setting up data subscriptions',
    errorMessage: 'An error occurred while setting up data subscriptions',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      // Start an SSE handler to listen for change events coming from the server.
      sseHandler = new SseHandler('/api/sse')

      // Listen for messages telling us that the game config has changed.
      const uiStore = useUiStore()
      sseHandler.addMessageHandler(
        'activeGameConfigChanged',
        () => {
          uiStore.isNewGameConfigAvailable = true
        },
      )

      // Subscribing to options just so that we can trigger refresh based on SSE events.
      // TODO: move this to a discreet polling policy to get rid of this code and to make it support missing permissions.
      const runtimeOptionsSubscription = useManuallyManagedStaticSubscription(getRuntimeOptionsSubscriptionOptions())
      const dashboardOptionsSubscription = useManuallyManagedStaticSubscription(getDashboardOptionsSubscriptionOptions())

      // Listen for messages telling us that the runtime options have changed.
      sseHandler.addMessageHandler(
        'runtimeOptionsChanged',
        () => {
          runtimeOptionsSubscription.refresh()
          dashboardOptionsSubscription.refresh()
        }
      )
      await sseHandler.start()
    }
  },
  /**
   * Initialize the user interface. Local storage, timers, etc.
   */
  {
    name: 'browserInit',
    displayName: 'Initializing user interface',
    errorMessage: 'An error occurred during the user interface initialization process.',
    errorResolution: 'Unfortunately, this needs to be fixed by Metaplay. Please let us know!',
    action: async () => {
      const coreStore = useCoreStore()
      const uiStore = useUiStore()
      const gameServerApiStore = useGameServerApiStore()
      // Load previous UI settings from browser local storage, if any
      if (localStorage?.showDeveloperUi === 'true' && gameServerApiStore.doesHavePermission('dashboard.developer_mode')) uiStore.toggleDeveloperUi(true)
      else if (localStorage.getItem('showDeveloperUi') === null) {
        if (coreStore.isProd) uiStore.toggleDeveloperUi(false) // Hide developer UI by default in production
        else uiStore.toggleDeveloperUi(true) // Show developer UI by default in development
      }
      if (localStorage?.isSafetyLockOn === 'true') uiStore.toggleSafetyLock(true)
      else if (localStorage.getItem('toggleSafetyLock') === null) {
        if (coreStore.isProd) uiStore.toggleSafetyLock(true) // Default to safety lock on in production
        else uiStore.toggleSafetyLock(false) // Default to safety lock off in development
      }

      // Listen to changes in local storage from other browser tabs
      window.addEventListener('storage', (event) => {
        // NB: This event fires on all open browser tabs in the same domain
        // *except* for the one that originated the value change
        // Update our state to match what was set in the other tab so that
        // configs are consistent across tabs
        if (event.key === 'showDeveloperUi') uiStore.showDeveloperUi = localStorage.showDeveloperUi === 'true'
        if (event.key === 'isSafetyLockOn') uiStore.isSafetyLockOn = localStorage.isSafetyLockOn === 'true'
      })

      // Start checking for auth token expiration.
      const statusSubscription = useManuallyManagedStaticSubscription(getBackendStatusSubscriptionOptions())
      const backendStatusChecker = () => {
        // If we have data then we know that we've already managed to reach the server at least once...
        if (statusSubscription.data) {
          // ..then if we suddenly start getting a 401 error it's because the auth token has expired.
          const axiosError = statusSubscription.error.value as AxiosError
          if (axiosError && axiosError.response?.status === 401) {
            gameServerApiStore.auth.hasTokenExpired = true
          }
        }
        // Check the status every five seconds.
        setTimeout(backendStatusChecker, 5000)
      }
      backendStatusChecker()

      // When we dynamically add routes with `addRoute`, the page doesn't automatically re-navigate. We need to
      // manually make the re-navigation happen so that initial dynamic routes get recognized.
      await router.isReady()
      await router.replace(router.currentRoute.value.fullPath)
    }
  },
  /**
   * And we're done!
   */
  {
    name: 'completed',
    displayName: 'Initializing UI',
    errorMessage: 'If we made it to this step it cannot fail.',
    errorResolution: 'Something might be wrong in `initialization.ts`?',
    action: async () => {}
  },
  /**
   * Uncomment the following step to force an error to be thrown.
   * Useful for testing error handling during initialization.
   */
  // {
  //   name: 'makeBoomBoom',
  //   displayName: 'Boom boom, a-boom boom boom',
  //   errorMessage: 'You have purposely triggered to fail in initialization steps.',
  //   errorResolution: 'Disable this error by commenting out the `makeBoomBoom` step in `initialization.ts`.',
  //   action: async (store) => {
  //     await sleep(1_000)
  //     throw new Error('Unexpected BOOM 🔥')
  //   }
  // },
]

// // Declare this file as non-hot-reloadable.
if (import.meta.hot) {
  import.meta.hot.invalidate()
}
