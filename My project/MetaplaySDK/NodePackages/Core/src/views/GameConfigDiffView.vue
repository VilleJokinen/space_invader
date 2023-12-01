<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!activeGameConfigIdData || !allGameConfigsData"
  :show-error-card="loadingState === LoadingState.Error"
  permission="api.game_config.view"
)
  template(#error-card-message)
    div Oh no, something went wrong while trying to compare two game configs:
    div.mt-1 {{ errorMessage }}
    div.mt-1 {{ errorExtendedInfo }}

  template(#default)
    //- Extra wide header card to fit the UI elements.
    meta-page-header-card.mb-5
      template(#title) Compare Game Config Versions
      p Select two game configs to see the differences between them.

      b-row.mt-4(align-v="center")
        //- Baseline
        b-col.mb-3
          div
            h6 Baseline game config
            meta-input-select(
              :value="baselineGameConfigId"
              @input="baselineGameConfigId = $event"
              :options="gameConfigOptions"
              no-clear
              style="height: 5rem"
              )
              template(#option="{ option }")
                game-config-select-option(v-if="option" :gameConfigId="option")

              template(#selectedOption="{ option }")
                div(
                  style="line-height: 1.5"
                  )
                  game-config-select-option(v-if="option" :gameConfigId="option")

            div.w-100.text-right
              small #[b-link(:to="`/gameConfigs/${baselineGameConfigId}`") View baseline game config]

        //- Swap
        b-col(md="1").mb-3.text-center
          meta-button(variant="primary" size="sm" @click="swapGameConfigs"): fa-icon(icon="right-left")

        //- New
        //- TODO - Would be nice to not have to repeat this.
        b-col.mb-3
          div
            h6 New game config
            meta-input-select(
              :value="newGameConfigId"
              @input="newGameConfigId = $event"
              :options="gameConfigOptions"
              no-clear
              style="height: 5rem"
              )
              template(#option="{ option }")
                game-config-select-option(v-if="option" :gameConfigId="option")

              template(#selectedOption="{ option }")
                div(
                  style="line-height: 1.5"
                  )
                  game-config-select-option(v-if="option" :gameConfigId="option")

            div.w-100.text-right
              small #[b-link(:to="`/gameConfigs/${newGameConfigId}`") View new game config]

    core-ui-placement(placementId="GameConfig/Diff")

    //- Overview of changes.
    b-row(align-h="center")
      b-col(md="7")
        meta-list-card(
          title="Overview of changes"
          :itemList="loadingState === LoadingState.Loading ? undefined : items"
          :searchFields="['displayName']"
          :filterSets="overviewFilterSets"
          :sortOptions="overviewSortOptions"
          :emptyMessage="emptyChangeOverviewMessage"
          :page-size="15"
        ).mb-3
          template(#item-card="slot")
            b-row(align-v="center" no-gutters)
              MBadge(v-if="slot.item.reason === DiffItemReason.Added" variant="success" tooltip="Added").mr-2 A
              MBadge(v-if="slot.item.reason === DiffItemReason.Removed" variant="danger" tooltip="Removed").mr-2 R
              MBadge(v-if="slot.item.reason === DiffItemReason.Modified" variant="primary" tooltip="Modified").mr-2 M
              span.mr-2 {{ slot.item.displayName }}

    //- List of changes.
    b-card(v-if="items.length > 0" no-body).shadow-sm.mb-3
      //- Title
      b-card-body
        b-row(align-h="between" align-v="center" no-gutters)
          b-card-title Detailed Changes

          MInputSegmentedSwitch(
            :model-value="diffViewType"
            @update:model-value="diffViewType = $event"
            :options="diffViewTypeOptions"
            )

      //- Body
      b-list-group(flush).list-group-stripes
        b-list-group-item(v-for="(item, index) in items" :key="item.anchorName")
          meta-lazy-loader
            //- Header
            div(:id="item.anchorName" style="cursor: pointer" @click="toggleItemCollapsed(item.anchorName)")
              span(:class="{ 'not-collapsed': !isItemCollapsed(item.anchorName) }")
                fa-icon(v-if="shouldShowDiffItemBody(item.reason)" icon="angle-right").mr-2
              span.font-weight-bold.mr-2 {{ item.libraryName }}{{ item.itemName !== '' ? '.' : '' }}{{ item.itemName }}
              MBadge(v-if="item.reason === DiffItemReason.Added" variant="success") Added
              MBadge(v-if="item.reason === DiffItemReason.Removed" variant="danger") Removed
              MBadge(v-if="item.reason === DiffItemReason.Modified" variant="primary") Modified
              b-link(v-if="shouldShowDiffItemBody(item.reason)").small.font-weight-normal.ml-2 {{ isItemCollapsed(item.anchorName) ? 'Show' : 'Hide' }}

            //- Body. We skip unnecessary items based on the diff mode
            div.mt-2(v-if="!isItemCollapsed(item.anchorName) && shouldShowDiffItemBody(item.reason)")
              //- Draw each line with line reason specific rendering
              div.diff.text-monospace(v-for="(line, lineNumber) in itemDiffText(item.libraryName, item.itemName)")
                //- No changes
                div(v-if="[DiffViewType.Diff, DiffViewType.Baseline, DiffViewType.Destination].includes(diffViewType) && line.reason == DiffTextReason.Unchanged")
                  | {{ line.text }}

                //- Additions
                div(v-else-if="[DiffViewType.Diff, DiffViewType.Additions, DiffViewType.Destination].includes(diffViewType) && line.reason == DiffTextReason.Added")
                  | #[span.text-success +] #[span.added {{ line.text.slice(2, line.text.length) }}]

                //- Removed
                div(v-else-if="[DiffViewType.Diff, DiffViewType.Baseline].includes(diffViewType) && line.reason == DiffTextReason.Removed")
                  | #[span.text-danger -] #[span.removed {{ line.text.slice(2, line.text.length) }}]

    meta-raw-data(:kvPair="baselineGameConfig" name="baselineGameConfig")
    meta-raw-data(:kvPair="newGameConfig" name="newGameConfig")
</template>

<script lang="ts" setup>
import { diffLines } from 'diff'
import { computed, onBeforeMount, ref, watch, watchEffect } from 'vue'

import { extractSingleValueFromQueryStringOrUndefined } from '../coreUtils'
import { useGameServerApi } from '@metaplay/game-server-api'
import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption, showSuccessToast } from '@metaplay/meta-ui'
import GameConfigSelectOption from '../components/gameconfig/GameConfigSelectOption.vue'
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { MInputSegmentedSwitch, MBadge } from '@metaplay/meta-ui-next'

import { useRoute, useRouter } from 'vue-router'
import { useSubscription } from '@metaplay/subscriptions'
import { getActiveGameConfigIdSubscriptionOptions, getAllGameConfigsSubscriptionOptions } from '../subscription_options/gameConfigs'
import type { StaticGameConfigInfo, StaticGameConfigContents } from '../gameConfigServerTypes'

// Subscriptions and core systems -------------------------------------------------------------------------------------

// Subscriptions and core systems -------------------------------------------------------------------------------------

const gameServerApi = useGameServerApi()
const route = useRoute()
const router = useRouter()

const {
  data: activeGameConfigIdData
} = useSubscription(getActiveGameConfigIdSubscriptionOptions())
const {
  data: allGameConfigsData
} = useSubscription<StaticGameConfigInfo[]>(getAllGameConfigsSubscriptionOptions())

// Game config Ids for baseline and new -------------------------------------------------------------------------------

/**
 * Id of the baseline (left-hand) game config. Initially this comes from the query string. If the query string is not
 * found then see the `watchEffect` for how the Id gets resolved.
 */
const baselineGameConfigId = ref<string | undefined>(extractSingleValueFromQueryStringOrUndefined(route.query, 'baselineRoot'))

/**
 * Id of the new (right-hand) game config. Initially this comes from the query string. If the query string is not found
 * then see the `watchEffect` for how the Id gets resolved.
 */
const newGameConfigId = ref<string | undefined>(extractSingleValueFromQueryStringOrUndefined(route.query, 'newRoot'))

/**
 * When the component loads we need to kick off loads for any configs that were specified in the query string.
 */
onBeforeMount(async () => {
  await Promise.all([loadGameConfig(baselineGameConfigId.value), loadGameConfig(newGameConfigId.value)])
})

/**
 * Watch for changes in the `baseline` game config Id. This can happen either from the multi-select or by the resolution
 * inside the `WatchEffect`.
 */
watch(baselineGameConfigId, async () => {
  // Try to fetch the game config. If that fails then we need to load it.
  baselineGameConfig.value = fetchLoadedGameConfig(baselineGameConfigId.value)
  if (!baselineGameConfig.value) {
    await loadGameConfig(baselineGameConfigId.value)
  }
  updateBrowserAddressBar()
})

/**
 * Watch for changes in the `new` game config Id. This can happen either from the multi-select or by the resolution
 * inside the `WatchEffect`.
 */
watch(newGameConfigId, async () => {
  // Try to fetch the game config. If that fails then we need to load it.
  newGameConfig.value = fetchLoadedGameConfig(newGameConfigId.value)
  if (!newGameConfig.value) {
    await loadGameConfig(newGameConfigId.value)
  }
  updateBrowserAddressBar()
})

/**
 * When the page initially loads, if the base/new config Ids are not loaded from the query string then they will default
 * to undefined. We want them to default to the active game config Id. We kick off a subscription to get this id and
 * resolve the unknown Ids when it loads.
 */
watchEffect(() => {
  if (activeGameConfigIdData.value) {
    if (!baselineGameConfigId.value) {
      baselineGameConfigId.value = activeGameConfigIdData.value
    }
    if (!newGameConfigId.value) {
      newGameConfigId.value = activeGameConfigIdData.value
    }
  }
})

/**
 * Update the URL in the browser address bar to show the currently selected configs.
 */
function updateBrowserAddressBar () {
  const permalink = route.path +
    `?baselineRoot=${baselineGameConfigId.value}` +
    `&newRoot=${newGameConfigId.value}`
  const urlParts = permalink.split('?')
  const url = urlParts[0]
  const queryParams = Object.fromEntries(urlParts[1].split('&').map(x => x.split('=')))
  router.replace({ path: url, query: queryParams })
    .catch((error: any) => {
      // Vue will sometimes complain that we are navigating to the same page, but we can safely
      // catch and ignore this.
      if (error.name !== 'NavigationDuplicated') {
        throw Error(error)
      }
    })
}

/**
 * Swap the game configs - `new` becomes `baseline`, `baseline` becomes `new`.
 */
function swapGameConfigs () {
  const temp = baselineGameConfigId.value
  baselineGameConfigId.value = newGameConfigId.value
  newGameConfigId.value = temp
  showSuccessToast('Configs swapped.')
}

// Async loading of game configs from the game server -----------------------------------------------------------------

/**
 * Game configs are loaded from the game server and stored in here, keyed by their id. Because loading of these is
 * async, we need to be able to indicate that loading is in progress. We do this by setting the value to `null`, so a
 * `null` value here means "still loading".
 */
const sourceGameConfigs: {[key: string]: StaticGameConfigContents | null} = {}

/**
 * Used to track the state of game configs loading.
 */
enum LoadingState {
  'Loading' = 'Loading',
  'Loaded' = 'Loaded',
  'Error' = 'Error',
}

/**
 * Game configs are loaded in the background. This function starts and then manages their loading.
 * @param gameConfigId Id of the game config that we want to load.
 */
async function loadGameConfig (gameConfigId: string | undefined) {
  // Don't try to load empty Ids, and don't try to load if we're already in an error state.
  if (gameConfigId !== undefined && loadingState.value !== LoadingState.Error) {
    // Is this config loading or already loaded? Remember the value will be `null` if it's loading, `something` if it's
    // loaded, or `undefined` if loading hasn't been attempted yet.
    if (sourceGameConfigs[gameConfigId] === undefined) {
      // Diff source isn't already loaded/loading so we need to request it.

      // Enter the loading state.
      loadingState.value = LoadingState.Loading

      // Mark the config as `loading` in the list of configs.
      sourceGameConfigs[gameConfigId] = null

      try {
        // Make the actual request to the game server for tis game config data.
        const response = await gameServerApi.get(`/gameConfig/${gameConfigId}`)
        const data: StaticGameConfigContents = response.data
        if (data.status !== 'Succeeded') {
          // Load failed.
          errorMessage.value = `Failed to load diff source: ${gameConfigId}`
          errorExtendedInfo.value = `The status of the game config was ${data.status}, expected status was Succeeded. You can't compare against this game config because it failed to build.`
          loadingState.value = LoadingState.Error
        } else {
          // Load succeeded.
          sourceGameConfigs[gameConfigId] = data
          // Have all game configs loaded now? ie: are there no longer any configs with the loading `null` state?
          if (loadingState.value as LoadingState !== LoadingState.Error && !Object.values(sourceGameConfigs).some(x => x == null)) {
            baselineGameConfig.value = fetchLoadedGameConfig(baselineGameConfigId.value)
            newGameConfig.value = fetchLoadedGameConfig(newGameConfigId.value)
            loadingState.value = LoadingState.Loaded
          }
        }
      } catch (e) {
        // Some unknown/unexpected error ocurred during loading.
        errorMessage.value = `Failed to load diff source: ${gameConfigId}`
        loadingState.value = LoadingState.Error
      }
    }
  }
}

/**
 * Are we currently loading any game configs?
 */
const loadingState = ref<LoadingState>(LoadingState.Loading)

/**
 * Valid when we are in loading state `Error`.
 */
const errorMessage = ref<string>()

/**
 * Valid when we are in loading state `Error`.
 */
const errorExtendedInfo = ref<string>()

// Baseline and new game configs --------------------------------------------------------------------------------------

/**
 * Complete baseline game config, or `undefined` if the config isn't available.
 */
const baselineGameConfig = ref<{[key: string]: any} | undefined>()

/**
 * Complete new game config, or `undefined` if the config isn't available.
 */
const newGameConfig = ref<{[key: string]: any} | undefined>()

/**
 * Explains the reason behind an item's difference.
 */
enum DiffItemReason {
  'Added' = 'Added',
  'Removed' = 'Removed',
  'Modified' = 'Modified',
}

/**
 * Fully describes a difference between items.
 */
interface Item {
  anchorName: string
  displayName: string
  libraryName: string
  itemName: string
  reason: DiffItemReason
}

/**
 * The differences between the baseline and new configs. Each difference is an `Item`.
 */
const items = computed((): Item[] => {
  const baselineConfig = baselineGameConfig.value
  const newConfig = newGameConfig.value

  // Early-out if one or more of the configs is not yet loaded.
  if (!baselineConfig || !newConfig) {
    return []
  }

  const items: Item[] = []

  // Added items.
  const newConfigKeys = Object.keys(newConfig)
  const baselineConfigKeys = Object.keys(baselineConfig)
  newConfigKeys.forEach(libraryName => {
    const newConfigLibrary = newConfig[libraryName] ?? null
    const baseConfigLibrary = baselineConfig[libraryName] ?? null
    if (newConfigLibrary === null) {
      // Config library cannot be parsed. Check if the whole library is new.
      if (baseConfigLibrary) {
        items.push({
          anchorName: `${libraryName}-whole-library`,
          displayName: `${libraryName}`,
          libraryName,
          itemName: '',
          reason: DiffItemReason.Added
        })
      }
      return
    }
    Object.keys(newConfigLibrary).forEach(itemName => {
      if (!Object.prototype.hasOwnProperty.call(baseConfigLibrary, itemName)) {
        items.push({
          anchorName: `${libraryName}-${itemName}`,
          displayName: `${libraryName}.${itemName}`,
          libraryName,
          itemName,
          reason: DiffItemReason.Added
        })
      }
    })
  })

  // Removed items.
  baselineConfigKeys.forEach(libraryName => {
    const baseConfigLibrary = baselineConfig[libraryName] ?? null
    const newConfigLibrary = newConfig[libraryName] ?? null
    if (baseConfigLibrary === null) {
      // Config library cannot be parsed. Check if the whole library is removed.
      if (newConfigLibrary) {
        items.push({
          anchorName: `${libraryName}-whole-library`,
          displayName: `${libraryName}`,
          libraryName,
          itemName: '',
          reason: DiffItemReason.Removed
        })
      }
      return
    }
    Object.keys(baseConfigLibrary).forEach(itemName => {
      if (!Object.prototype.hasOwnProperty.call(newConfigLibrary, itemName)) {
        items.push({
          anchorName: `${libraryName}-${itemName}`,
          displayName: `${libraryName}.${itemName}`,
          libraryName,
          itemName,
          reason: DiffItemReason.Removed
        })
      }
    })
  })

  // Modified items.
  baselineConfigKeys.forEach(libraryName => {
    const baseConfigLibrary = baselineConfig[libraryName] ?? null
    const newConfigLibrary = newConfig[libraryName] ?? null
    if (!baseConfigLibrary || !newConfigLibrary) {
      // Skip missing libraries. They are handled already.
      return
    }
    Object.keys(baseConfigLibrary).forEach(itemName => {
      if (Object.prototype.hasOwnProperty.call(newConfigLibrary, itemName)) {
        const baselineItem = JSON.stringify(baseConfigLibrary[itemName])
        const newItem = JSON.stringify(newConfigLibrary[itemName])
        if (baselineItem !== newItem) {
          items.push({
            anchorName: `${libraryName}-${itemName}`,
            displayName: `${libraryName}.${itemName}`,
            libraryName,
            itemName,
            reason: DiffItemReason.Modified
          })
        }
      }
    })
  })

  // Sort items by name.
  items.sort((a, b) => {
    const nameA = a.anchorName.toLowerCase()
    const nameB = b.anchorName.toLowerCase()
    if (nameA < nameB) return -1
    else if (nameA > nameB) return 1
    else return 0
  })
  return items
})

/**
 * Clean up source game config and create one that is suitable for viewing.
 * @param sourceGameConfigId Id of the config that we want to fetch.
 */
function fetchLoadedGameConfig (sourceGameConfigId: string | undefined) {
  const sourceGameConfig = sourceGameConfigs[sourceGameConfigId ?? '']
  if (sourceGameConfig) {
    // Get the source configs by joining together the shared and server configs. Return a deep clone using stringify/parse.
    const config = JSON.parse(JSON.stringify(Object.assign(
      {},
      sourceGameConfig.contents.sharedConfig,
      sourceGameConfig.contents.serverConfig
    )))

    // TODO PKG - fixing up of naming
    // the type names are converted to camelCase by the C# JSON exporter
    // the replacement key names in the patches are *not* converted because they are dictionary keys in C#
    // and then nothing matches..
    // the horrible/hacky solution for now is to fix up the case of all top level objects
    // the source data also has some extra keys that I'm not sure make sense - so those get removed too
    const removals = [
      'archiveVersion',
      'defaultLanguage',
      'defaultParseOptions',
      'metaplay.Core.Config.ISharedGameConfig.ArchiveVersion'
    ]
    const keys = Object.keys(config)
    for (const key of keys) {
      if (!removals.includes(key)) {
        const correctKey = key.charAt(0).toUpperCase() + key.slice(1)
        const propertyDescriptor = Object.getOwnPropertyDescriptor(config, key)
        if (propertyDescriptor) {
          Object.defineProperty(config, correctKey, propertyDescriptor)
        } else {
          console.warn(`Failed to fixup config for '${key}'`)
        }
      }
      // Note: ignoring this error as we are in the process of migrating this logic elsewhere. Not worth fixing.
      // eslint-disable-next-line @typescript-eslint/no-dynamic-delete
      delete config[key]
    }

    // Done.
    return config
  } else {
    // No config found, or config still loading.
    return undefined
  }
}

// Config Diff Logic --------------------------------------------------------------------------------------------------

/**
 * Explains how a line differs between two game configs.
 */
enum DiffTextReason {
  'Unchanged' = 'Unchanged',
  'Added' = 'Added',
  'Removed' = 'Removed',
}

/**
 * Fully describes the difference between the lines of two game configs.
 */
interface DiffText {
  reason: DiffTextReason
  text: string
}

/**
 * Compare items from two game configs libraries and return a list of changes on each line.
 * @param libraryName Name of the library, ie: the root item.
 * @param itemName Name of the item inside the library.
 */
function itemDiffText (libraryName: string, itemName: string): DiffText[] {
  let baselineItem = {}
  if (Object.prototype.hasOwnProperty.call(baselineGameConfig.value?.[libraryName], itemName)) {
    baselineItem = baselineGameConfig.value?.[libraryName][itemName]
  }
  let newItem = {}
  if (Object.prototype.hasOwnProperty.call(newGameConfig.value?.[libraryName], itemName)) {
    newItem = newGameConfig.value?.[libraryName][itemName]
  }
  const baselineText = makeIntoText(baselineItem)
  const newText = makeIntoText(newItem)
  const diffItems = diffLines(baselineText, newText)
  const diffText: DiffText[] = []
  diffItems.forEach((diff: any) => {
    let reason: DiffTextReason = DiffTextReason.Unchanged
    if (diff.added) reason = DiffTextReason.Added
    else if (diff.removed) reason = DiffTextReason.Removed
    const lines = diff.value.split(/\r?\n/).slice(0, diff.count)
    diffText.push(...lines.map((text: string) => {
      // Remove some double commas to make diffs easier to read.
      const key = text.split(':')[0]
      const newKey = key.replace(/"/g, '')
      const prettyText = text.replace(key, newKey)
      return { reason, text: prettyText }
    }))
  })
  return diffText
}

/**
 * Converts the given data into human readable text format.
 * @param data The data that needs to be converted into text.
 */
function makeIntoText (data: any): string {
  if (typeof data === 'string') return `  "${data}"`
  if (typeof data === 'number') return `  ${data}`
  if (Object.keys(data).length === 0) return ' '
  return JSON.stringify(data, (k, v) => v, 2).slice(2, -2)
}

/**
 * Used to track how we want to view the differences.
 */
enum DiffViewType {
  'Diff' = 'Diff',
  'Additions' = 'Additions',
  'Baseline' = 'Baseline',
  'Destination' = 'Destination',
}

const diffViewTypeOptions = [
  { label: 'Baseline', value: DiffViewType.Baseline },
  { label: 'Full Diff', value: DiffViewType.Diff },
  { label: 'Only Additions', value: DiffViewType.Additions },
  { label: 'New', value: DiffViewType.Destination }
]
/**
 * How are we currently viewing the differences between the two configs?
 */
const diffViewType = ref<DiffViewType>(DiffViewType.Diff)

/**
 * Helper function for deciding whether to show the body of a diff item.
 * @param reason Modification reason for the item.
 */
function shouldShowDiffItemBody (reason: DiffItemReason): boolean {
  if (diffViewType.value === DiffViewType.Diff) return true
  else if (diffViewType.value === DiffViewType.Additions && reason !== DiffItemReason.Removed) return true
  else if (diffViewType.value === DiffViewType.Baseline && reason !== DiffItemReason.Added) return true
  else if (diffViewType.value === DiffViewType.Destination && reason !== DiffItemReason.Removed) return true
  else return false
}

// Opening/closing of items in the Detailed Changes list --------------------------------------------------------------

/**
 * A list of diff items that are open (ie: expanded) so that their contents are visible.
 */
const openDiffItems = ref<string[]>([])

/**
 * Toggle the visual open/collapsed state of a diff item based on its name.
 * @param anchorName Key of item to toggle.
 */
function toggleItemCollapsed (anchorName: string) {
  const index = openDiffItems.value.indexOf(anchorName)
  if (index === -1) {
    openDiffItems.value.push(anchorName)
  } else {
    openDiffItems.value.splice(index, 1)
  }
}

/**
 * Should an item be visible as open or collapsed?
 * @param anchorName Key of item to check.
 */
function isItemCollapsed (anchorName: string): boolean {
  return !openDiffItems.value.includes(anchorName)
}

// MetaListCard configuration -----------------------------------------------------------------------------------------

/**
 * Filter set for the change overview card. Constructed dynamically from library names and statically from the
 * possible types of modifications.
 */
const overviewFilterSets = computed(() => {
  return [
    MetaListFilterSet.asDynamicFilterSet(items.value, 'library', x => (x as Item).libraryName),
    new MetaListFilterSet('reason', [
      new MetaListFilterOption('Modified', x => (x as Item).reason === DiffItemReason.Modified),
      new MetaListFilterOption('Added', x => (x as Item).reason === DiffItemReason.Added),
      new MetaListFilterOption('Removed', x => (x as Item).reason === DiffItemReason.Removed)
    ])
  ]
})

/**
 * Sort options for the change overview card.
 */
const overviewSortOptions = [
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Descending),
  new MetaListSortOption('Reason', 'reason', MetaListSortDirection.Ascending),
  new MetaListSortOption('Reason', 'reason', MetaListSortDirection.Descending)
]

/**
 * A message to be shown when there are no differences between the two game config versions.
 */
const emptyChangeOverviewMessage = computed(() => {
  if (baselineGameConfigId.value === newGameConfigId.value) {
    return 'You are comparing a game config version against itself. Unsurprisingly, this means that there are no differences to be found 🤔'
  } else {
    return 'No differences found between the two game config versions 🤔'
  }
})

// Game config Id searching -------------------------------------------------------------------------------------------

/**
 * Stores the search query of the baseline (left-hand) multi-select component.
 */
const baselineSearchQuery = ref<string>('')

/**
 * Stores the search query of the new (right-hand) multi-select component.
 */
const newSearchQuery = ref<string>('')

/**
 * List of game config options for selector.
 */
const gameConfigOptions = computed(() => {
  return searchGameConfigIds(undefined).map((id) => {
    return {
      id,
      value: id
    }
  })
})

/**
 * List of Ids of all valid, non-archived game configs.
 */
const gameConfigIds = computed((): string[] => {
  return (allGameConfigsData.value ?? [])
    .filter((config) => config.status === 'Succeeded' && !config.isArchived)
    .map(config => config.id)
})

/**
 * Search the list of all game config Ids and return a list that match the given search string. Game configs are
 * searched by their Ids, their names and their descriptions.
 * @param searchQuery Search string to use. If empty then all Ids are returned.
 */
function searchGameConfigIds (searchQuery?: string): string[] {
  if (searchQuery) {
    const lowerSearchQuery = searchQuery.toLowerCase()
    return gameConfigIds.value?.filter((id: string) => {
      if (id.toLowerCase().includes(lowerSearchQuery)) {
        // There was a match on the Id.
        return true
      } else {
        // Get config data for the Id s that we cna check for a match on the name or description.
        const config = allGameConfigsData.value?.find((x: any) => x.id === id)
        return (config?.name ?? 'No name available').toLowerCase().includes(lowerSearchQuery) || config?.description.toLowerCase().includes(lowerSearchQuery)
      }
    })
  } else {
    return gameConfigIds.value
  }
}
</script>

<style scoped>
.diff {
  white-space: pre-wrap;
  font-size: 0.7rem;
  line-height: 1.2;
  margin-bottom: 1px;
}
.added {
  background: #bfb;
}
.removed {
  background: #fbb;
}
</style>
