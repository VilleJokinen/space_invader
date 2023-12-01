// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { createRouter, createWebHistory } from 'vue-router'
import { useHeaderbar } from '@metaplay/meta-ui-next'

const defaultRoutes = [ // const defaultRoutes: RouteConfig[] = [
  {
    path: '/',
    name: 'Overview',
    component: async () => await import('./views/OverviewView.vue'),
    meta: { icon: 'tachometer-alt' }
  },

  // GAME

  {
    path: '/players',
    name: 'Manage Players',
    component: async () => await import('./views/PlayerListView.vue'),
    meta: { icon: 'users', sidebarTitle: 'Players', sidebarOrder: 10, category: 'Game', permission: 'api.players.view' }
  },
  {
    path: '/players/:id',
    name: 'Manage Player',
    component: async () => await import('./views/PlayerDetailView.vue'),
    props: true,
    meta: { icon: 'user' },
  },
  {
    path: '/players/:id/raw',
    name: 'View Raw Player Details',
    component: async () => await import('./views/PlayerDetailRawView.vue'),
    props: true,
    meta: { icon: 'ambulance' }
  },

  // Note: sidebarOrders 20 and 30 are used for guilds and matchmaking

  {
    path: '/players/:playerId/:incidentId',
    name: 'View Player Incident Report',
    component: async () => await import('./views/PlayerIncidentDetailView.vue'),
    props: true,
    meta: { icon: 'user' }
  },
  {
    path: '/playerIncidents',
    name: 'Player Incidents',
    component: async () => await import('./views/PlayerIncidentsList.vue'),
    meta: { icon: 'bug', sidebarTitle: 'Player Incidents', sidebarOrder: 40, category: 'Game', permission: 'api.incident_reports.view' }
  },
  {
    path: '/playerIncidents/:fingerprint',
    name: 'Player Incidents By Type',
    component: async () => await import('./views/PlayerIncidentsByTypeView.vue'),
    props: true,
    meta: { icon: 'bug' }
  },

  {
    path: '/gameConfigs',
    name: 'Manage Game Configs',
    component: async () => await import('./views/GameConfigListView.vue'),
    meta: { icon: 'table', sidebarTitle: 'Game Configs', sidebarOrder: 50, category: 'Game', permission: 'api.game_config.view' }
  },
  {
    path: '/gameConfigs/diff',
    name: 'Compare Game Configs',
    component: async () => await import('./views/GameConfigDiffView.vue'),
    meta: { icon: 'binoculars' }
  },
  {
    path: '/gameConfigs/:id/logs',
    name: 'View Game Config Build Log',
    component: async () => await import('./views/GameConfigBuildLogView.vue'),
    props: true,
    meta: { icon: 'clipboard-list' }
  },
  {
    path: '/gameConfigs/:id',
    name: 'Manage Game Config',
    component: async () => await import('./views/GameConfigDetailView.vue'),
    props: true,
    meta: { icon: 'table' }
  },

  // LIVEOPS

  {
    path: '/broadcasts',
    name: 'Manage Broadcasts',
    component: async () => await import('./views/BroadcastListView.vue'),
    meta: { icon: 'broadcast-tower', sidebarTitle: 'Broadcasts', sidebarOrder: 10, category: 'LiveOps', permission: 'api.broadcasts.view' }
  },
  {
    path: '/broadcasts/:id',
    name: 'Manage Broadcast',
    component: async () => await import('./views/BroadcastDetailView.vue'),
    props: true,
    meta: { icon: 'broadcast-tower' }
  },

  {
    path: '/notifications',
    name: 'Manage Notification Campaigns',
    component: async () => await import('./views/NotificationListView.vue'),
    meta: { icon: 'comment-alt', sidebarTitle: 'Push Notifications', sidebarOrder: 20, category: 'LiveOps', permission: 'api.notifications.view' }
  },
  {
    path: '/notifications/:id',
    name: 'Manage Notification Campaign',
    component: async () => await import('./views/NotificationDetailView.vue'),
    props: true,
    meta: { icon: 'comment-alt' }
  },

  {
    path: '/segments',
    name: 'View Player Segments',
    component: async () => await import('./views/SegmentListView.vue'),
    meta: { icon: 'user-tag', sidebarTitle: 'Player Segments', sidebarOrder: 30, category: 'LiveOps', permission: 'api.segmentation.view' }
  },
  {
    path: '/segments/:id',
    name: 'View Player Segment',
    component: async () => await import('./views/SegmentDetailView.vue'),
    props: true,
    meta: { icon: 'user-tag' }
  },

  {
    path: '/entityEventLog/:type/:id',
    name: 'View Entity Event Log',
    component: async () => await import('./views/EntityEventLogView.vue'),
    props: true,
    meta: { icon: 'clipboard-list' }
  },

  {
    path: '/entities/:id/dbinfo',
    name: 'Inspect Database Entity',
    component: async () => await import('./views/DatabaseEntityDetailView.vue'),
    props: true,
    meta: { icon: 'search' }
  },

  {
    path: '/experiments',
    name: 'View Experiments',
    component: async () => await import('./views/ExperimentListView.vue'),
    meta: { icon: 'flask', sidebarTitle: 'Experiments', sidebarOrder: 40, category: 'LiveOps', permission: 'api.experiments.view' }
  },
  {
    path: '/experiments/:id',
    name: 'Manage Experiment',
    component: async () => await import('./views/ExperimentDetailView.vue'),
    props: true,
    meta: { icon: 'flask', permission: 'api.experiments.view' }
  },

  {
    path: '/offerGroups/offer/:id',
    name: 'View Offer',
    component: async () => await import('./views/OfferDetailView.vue'),
    props: true,
    meta: { icon: 'tags', permission: 'api.activables.view' }
  },

  // TECHNICAL

  {
    path: '/developers',
    name: 'Developer Page',
    component: async () => await import('./views/DeveloperListView.vue'),
    meta: { icon: 'user-astronaut', sidebarTitle: 'Developers', sidebarOrder: 8, category: 'Technical', permission: 'api.players.view' }
  },
  {
    path: '/environment',
    name: 'View Environment Details',
    component: async () => await import('./views/EnvironmentView.vue'),
    meta: { icon: 'cloud', sidebarTitle: 'Environment', sidebarOrder: 10, category: 'Technical', permission: 'dashboard.environment.view' }
  },

  {
    path: '/scanJobs',
    name: 'Manage Database Scan Jobs',
    component: async () => await import('./views/ScanJobsListView.vue'),
    meta: { icon: 'business-time', sidebarTitle: 'Scan Jobs', sidebarOrder: 20, category: 'Technical', permission: 'api.scan_jobs.view' }
  },

  {
    path: '/analyticsEvents',
    name: 'Analytics Events',
    component: async () => await import('./views/AnalyticsEventListView.vue'),
    meta: { icon: 'list', sidebarTitle: 'Analytics Events', sidebarOrder: 30, category: 'Technical', permission: 'api.analytics_events.view' }
  },
  {
    path: '/analyticsEvents/:id',
    name: 'View Analytics Event Type',
    component: async () => await import('./views/AnalyticsEventDetailView.vue'),
    props: true,
    meta: { icon: 'list', permission: 'api.analytics_events.view' }
  },

  {
    path: '/auditLogs',
    name: 'View Audit Logs',
    component: async () => await import('./views/AuditLogListView.vue'),
    meta: { icon: 'clipboard-list', sidebarTitle: 'Audit Logs', sidebarOrder: 40, category: 'Technical', permission: 'api.audit_logs.search' }
  },
  {
    path: '/auditLogs/:id',
    name: 'View Audit Log Event',
    component: async () => await import('./views/AuditLogDetailView.vue'),
    props: true,
    meta: { icon: 'clipboard-list' }
  },

  {
    path: '/runtimeOptions',
    name: 'View Runtime Options',
    component: async () => await import('./views/RuntimeOptionsView.vue'),
    meta: { icon: 'cogs', sidebarTitle: 'Runtime Options', sidebarOrder: 50, category: 'Technical', permission: 'api.runtime_options.view' }
  },

  // OTHER

  {
    path: '/grafana',
    name: 'Grafana',
    component: async () => await import('./views/GrafanaView.vue'),
    meta: { icon: 'cloud' }
  },

  {
    path: '/system',
    name: 'Manage Deployment',
    component: async () => await import('./views/SystemView.vue'),
    meta: { icon: 'cog', sidebarTitle: 'Settings', sidebarOrder: 60, category: 'Technical', permission: 'dashboard.system.view' }
  },

  {
    path: '/user',
    name: 'My Profile',
    component: async () => await import('./views/UserView.vue'),
    meta: { icon: 'user' }
  },

  {
    path: '/:pathMatch(.*)*',
    name: 'Not Found!',
    component: async () => await import('./views/NotFoundView.vue'),
    meta: { icon: 'times' }
  },
]

/**
 * The main Vue Router instance that owns the navigation. Use this to navigate elsewhere and to ask details about the current route.
 */
const router = createRouter({
  history: createWebHistory('/'),
  routes: defaultRoutes,
  /**
   * Note the scrollBehavior () that just returns left and top 0 fixes problem when window that is scrolled down, it stays in the same location when it should load to the top.
   */
  // scrollBehavior () {
  //   return { left: 0, top: 0 }
  // }
  scrollBehavior: (to, from, savedPosition) => {
    if (savedPosition) {
      // Browser's back/forward pressed
      return savedPosition
    } else if (to.hash) {
      // For anchors
      return {
        selector: to.hash,
        behavior: 'smooth',
        offset: { left: 0, top: 80 }
      }
    } else if (from.path === to.path) {
      // By changing queries we are still in the same component, so "from.path" === "to.path" (new query changes just "to.fullPath", but not "to.path").
    } else {
      // For anything else, scroll to top
      return { left: 0, top: 0 }
    }
  }
})

export {
  router,
}

// Update page title on route navigation
let projectName = 'LiveOps Dashboard'
const { title } = useHeaderbar()
router.beforeEach((to, from, next) => {
  title.value = String(to.name)
  document.title = to.name ? `${projectName} - ${String(to.name)}` : projectName
  next()
})

/**
 * Changes the page title to begin with a custom string.
 * @param name New default page title.
 */
export function updateProjectName (name: string) {
  // Store for later
  projectName = name

  // Set immediately
  document.title = router.currentRoute.value.name ? `${name} - ${String(router.currentRoute.value.name)}` : name
}
