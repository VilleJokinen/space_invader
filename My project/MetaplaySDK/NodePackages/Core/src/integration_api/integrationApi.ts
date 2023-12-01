// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import type { RouteRecord, RouteRecordRaw } from 'vue-router'

import { useUiStore } from '@metaplay/meta-ui'
import type { GameSpecificReward } from '@metaplay/meta-ui'
import { fetchSubscriptionDataOnceOnly } from '@metaplay/subscriptions'

import { getGameDataSubscriptionOptions } from '../subscription_options/general'
import { useCoreStore } from '../coreStore'
import { router } from '../router'

import {
  addUiComponent,
  removeUiComponent,
  removeAllUiComponents,
} from './uiPlacementApis'

import {
  addGeneratedUiFormComponent,
  addGeneratedUiViewComponent,
  addStringIdDecorator,
} from './generatedUiApis'

import {
  addPlayerDetailsOverviewListItem,
  addPlayerReconnectAccountPreviewListItem,
  addGuildDetailsOverviewListItem
} from './overviewListsApis'

/**
 * Add an array of new rewards to the library of player rewards that can be visualised in the UI.
 * @param reward The reward to add.
 * @example
 * initializationApi.addPlayerRewards([{
    getDisplayValue: (reward) => `💰 Gold x${reward.amount}`,
    $type: 'Game.Logic.RewardGold',
   }])
 */
export function addPlayerRewards (rewards: GameSpecificReward[]) {
  const uiStore = useUiStore()
  uiStore.gameSpecific.playerRewards = uiStore.gameSpecific.playerRewards.concat(rewards)
}

/**
 * The data needed to visualise a player's resource in the UI.
 */
export interface GameSpecificPlayerResource {
  /**
   * The name of the resource to be shown in the UI.
   * @example 'Gems'
   */
  displayName: string
  /**
   * A function that returns the amount of the resource to be shown in the UI.
   * @param playerModel The player's PlayerModel.
   * @example (playerModel: any) => playerModel.wallet.numGems
   */
  getAmount: (playerModel: any) => number
}

/**
 * Add an array of new resources to the library of player resources that can be visualised in the UI.
 * @param resource The resource to add.
 * @example
 * setGameSpecificInitialization(async (initializationApi) => {
    initializationApi.addPlayerResources([{
      displayName: 'Gold',
      getAmount: (playerModel) => playerModel.wallet.numGold,
    }])
   })
 */
export function addPlayerResources (resources: GameSpecificPlayerResource[]) {
  const coreStore = useCoreStore()
  coreStore.gameSpecific.playerResources = coreStore.gameSpecific.playerResources.concat(resources)
}

/**
 * The data needed to visualise a player's in-app-purchase in the UI.
 */
export interface GameSpecificInAppPurchaseContent {
  /**
   * The C# type of the in-app-purchase so it can be identified.
   * @example 'Game.Logic.ResolvedPurchaseGameContent'
   */
  $type: string
  /**
   * A function that returns a list of human readable contents of the purchase to be shown in the UI.
   * @example (purchase) => [`💰 Gold x${purchase.numGold}`, `💎 Gems x${purchase.numGems}`]
   */
  getDisplayContent: (purchase: any) => string[]
}

/**
 * Add an array of new in-app-purchases to the library of purchases that can be visualised in the UI.
 * @param purchase The purchase to add.
 * @example
 * setGameSpecificInitialization(async (initializationApi) => {
    initializationApi.addInAppPurchaseContents([{
      $type: 'Game.Logic.ResolvedPurchaseGameContent',
      getDisplayContent: (purchase) => [`💰 Gold x${purchase.numGold}`, `💎 Gems x${purchase.numGems}`]
    }])
   })
 */
export function addInAppPurchaseContents (purchases: GameSpecificInAppPurchaseContent[]) {
  const coreStore = useCoreStore()
  coreStore.gameSpecific.iapContents = coreStore.gameSpecific.iapContents.concat(purchases)
}

/**
 * A utility function to add game-specific actor information into the dashboard's landing page. The intention is to replace this mechanism with something more automatic in a future SDK release.
 * @param key The name of the actor to add. Must match the game server's actor names as returned by the /status endpoint.
 * @param overviewListDisplayName Optional: Add this item to the leftmost column of the overview list with the given display name as a text label.
 * @param chartDisplayName Optional: Add a chart to the landing page with the given display name.
 * @param databaseListDisplayName Optional: Add this item to the rightmost column of the overview list with the given display name as a text label.
 * setGameSpecificInitialization(async (initializationApi) => {
    initializationApi.addActorInfoToOverviewPage(
      'Guild',
      'Live Guild Actors',
      undefined,
      'Total Guilds'
    )
   })
 */
export function addActorInfoToOverviewPage (key: string, overviewListDisplayName?: string, chartDisplayName?: string, databaseListDisplayName?: string) {
  const coreStore = useCoreStore()
  if (overviewListDisplayName) {
    coreStore.actorOverviews.overviewListEntries.push({ key, displayName: overviewListDisplayName })
  }
  if (chartDisplayName) {
    coreStore.actorOverviews.charts.push({ key, displayName: chartDisplayName })
  }
  if (databaseListDisplayName) {
    coreStore.actorOverviews.databaseListEntries.push({ key, displayName: databaseListDisplayName })
  }
}

// UI & NAVIGATION -----------------------------------------------------------------------------------------------------

/**
 * Used to render the route in the navigation sidebar.
 */
export interface NavigationEntryOptions {
  /**
   * Font awesome icon to use for the route.
   * @example 'fa-users'
   */
  icon?: string
  /**
   * Sidebar display name for the route.
   * @example 'Players'
   */
  sidebarTitle?: string
  /**
   * Relative order of the route in the sidebar. Smaller numbers are displayed first.
   */
  sidebarOrder?: number
  /**
   * The sidebar heading to insert this route into.
   * @example 'Game'
   */
  category?: string
  /**
   * The permission needed to access this route.
   * The route will be visually disabled if the user doesn't have this permission.
   */
  permission?: string
  /**
   * Array of paths that should also make this route visually active in the sidebar.
   * Useful pages with lots of sub-routes.
   */
  secondaryPathHighlights?: string[]
}

/**
 * Adds a new route to the dashboard's vue-router. Define all the meta fields to also add an entry to the navigation sidebar.
 * @param entry The route to add to the dashboard navigation.
 * @param options The customization options to use when rendering the route in the navigation sidebar.
 * @param replace Set to true if you intend to replace an existing route.
 * @example
 * setGameSpecificInitialization(async (initializationApi) => {
    initializationApi.addNavigationEntry({
      path: '/guilds',
      name: 'Manage Guilds',
      component: import('./views/GuildListView.vue')
    },
    {
      icon: 'chess-rook',
      sidebarTitle: 'Guilds',
      sidebarOrder: 20,
      category: 'Game',
      permission: 'api.guilds.view'
    })
  })
 */
export function addNavigationEntry (entry: RouteRecordRaw, options: NavigationEntryOptions, replace = false) {
  const currentRoutes = router.getRoutes()

  // Assining options to the entry's meta.
  entry.meta = {
    ...entry.meta,
    ...options
  }

  // Check for obvious unintended issues.
  if (currentRoutes.find(x => x.name === entry.name)) {
    if (!replace) throw new Error(`Attempting to replace existing route entry for '${entry.name?.toString() ?? 'unknown'}' (${entry.path}) without specifying "replace"`)
  }

  // Note: addRoute replaces existing entry by name
  router.addRoute(entry)
}

// OVERVIEW LISTS -----------------------------------------------------------------------------------------------------

/**
 * Fetch game data from the game server.
 * @returns Game data
 * @example const gameData = await getGameData()
 */
export async function getGameData (): Promise<{ $type: string, gameConfig: any, serverGameConfig: any }> {
  return await fetchSubscriptionDataOnceOnly(getGameDataSubscriptionOptions())
}

/**
 * Set a custom image to be used in the top left corner of the dashboard.
 * @param url The url to the game icon.
 * @example
 * import GameIconUrl from './GameIcon.png'

   setGameSpecificInitialization((initializationApi) => {
     initializationApi.setGameIconUrl(GameIconUrl)
   })
 */
function setGameIconUrl (url: string) {
  const coreStore = useCoreStore()
  coreStore.gameSpecific.gameIconUrl = url
}

/**
 * The available APIs for game-specific dashboard customization.
 */
const InitializationApi = {
  addPlayerRewards,
  addPlayerResources,
  addInAppPurchaseContents,
  addActorInfoToOverviewPage,
  addNavigationEntry,
  addUiComponent,
  removeUiComponent,
  removeAllUiComponents,
  addGeneratedUiViewComponent,
  addGeneratedUiFormComponent,
  addStringIdDecorator,
  addPlayerDetailsOverviewListItem,
  addPlayerReconnectAccountPreviewListItem,
  addGuildDetailsOverviewListItem,
  getGameData,
  setGameIconUrl,
}

/**
 * Set game-specific initialization logic to be run during the dashboard initialization.
 * The provided function can be `async` and will block the dashboard initialization until it completes. Thus, we can guarantee that the game-specific initialization will be completed before the dashboard exits the initial loading screen.
 * @param initializationFunction Function to run during the dashboard initialization. The function will receive an `InitializationApi` object as an overload that exposes the various integration API functions you can use.
 * @example
 * setGameSpecificInitialization(async (initializationApi) => {
     initializationApi.setGameIconUrl(GameIconUrl)
   })
 */
export function setGameSpecificInitialization (initializationFunction: (initializationApi: typeof InitializationApi) => Promise<void>) {
  gameSpecificInitializationStep = async () => await initializationFunction(InitializationApi)
}

/**
 * A function to call when the game is initialized that can be used to add game-specific initialization code. Defaults to no-op.
 */
export let gameSpecificInitializationStep: Function
