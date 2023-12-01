<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  title="Build Logs"
  description="A history of all past game config builds."
  :itemList="allGameConfigsData"
  :searchFields="searchFields"
  :filterSets="filterSets"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  :pageSize="20"
  icon="clipboard-list"
  )
  template(#item-card="{ item: gameConfig }")
    MListItem
      span(:class="{ 'tw-text-red-500': gameConfig.status === 'Failed' }") {{ gameConfig.name || 'No name available' }}

      template(#badge)
        MBadge(v-if="gameConfig.status !== 'Succeeded'" :variant="badgeVariantFromStatus(gameConfig.status)") {{ gameConfig.status }}
        MBadge(v-if="gameConfig.isArchived" variant="neutral") Archived

      template(#top-right)
        div(v-if="gameConfig.status !== 'Building'") Uploaded #[meta-time(:date="gameConfig.persistedAt")]

      template(#bottom-left)
        div {{ gameConfig.description || 'No description available' }}

      template(#bottom-right)
        div(v-if="gameConfig.buildReportSummary?.totalLogLevelCounts.Error" class="tw-text-red-500") {{ gameConfig.buildReportSummary.totalLogLevelCounts.Error }} build errors
        div(v-if="gameConfig.buildReportSummary?.totalLogLevelCounts.Warning" class="tw-text-orange-500") {{ gameConfig.buildReportSummary.totalLogLevelCounts.Warning }} build warnings
        meta-button(v-if="gameConfig.status !== 'Building'" link :to="getLogPagePath(gameConfig.id)") View log
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MBadge, MListItem, type Variant } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import type { StaticGameConfigInfo, GameDataBuildStatus } from '../../gameConfigServerTypes'
import { getAllGameConfigsSubscriptionOptions } from '../../subscription_options/gameConfigs'

/**
 * Subscription to get all game configs.
 */
const {
  data: allGameConfigsData,
} = useSubscription<StaticGameConfigInfo[]>(getAllGameConfigsSubscriptionOptions())

/**
 * Calculate badge variant based on game config build status.
 * @param status Status to check.
 */
function badgeVariantFromStatus (status: GameDataBuildStatus): Variant {
  const mappings: { [id in GameDataBuildStatus]: Variant } = {
    Building: 'primary',
    Succeeded: 'success',
    Failed: 'danger',
  }
  return mappings[status] ?? 'warning'
}

const route = useRoute()
const detailsRoute = computed(() => route.path)

/**
 * Get log page path by joining it to the current path,
 * but take into account if there's already a trailing slash.
 * \todo Do a general fix with router and get rid of this.
 */
function getLogPagePath (gameConfigId: string) {
  const path = detailsRoute.value
  const maybeSlash = path.endsWith('/') ? '' : '/'
  return path + maybeSlash + gameConfigId + '/logs'
}

/**
 * Sorting options for game configs.
 */
const defaultSortOption = 1
const sortOptions = [
  new MetaListSortOption('Time', 'persistedAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Time', 'persistedAt', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Descending),
]

/**
 * Search fields for game configs.
 */
const searchFields = ['id', 'name', 'description']

/**
 * Filtering options for game configs.
 */
const filterSets = [
  new MetaListFilterSet('status',
    [
      new MetaListFilterOption('Building', (x: any) => x.status === 'Building'),
      new MetaListFilterOption('Succeeded', (x: any) => x.status === 'Succeeded'),
      new MetaListFilterOption('Failed', (x: any) => x.status === 'Failed')
    ]
  ),
  new MetaListFilterSet('archived',
    [
      new MetaListFilterOption('Archived', (x: any) => x.isArchived),
      new MetaListFilterOption('Not archived', (x: any) => !x.isArchived, true)
    ]
  )
]
</script>
