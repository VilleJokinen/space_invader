<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  data-cy="available-configs"
  title="Available Game Configs"
  description="These game configs have built successfully and are ready to be published."
  :itemList="allGameConfigsData"
  :searchFields="searchFields"
  :filterSets="filterSets"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  :pageSize="20"
  icon="table"
)
  template(#item-card="{ item: gameConfig }")
    MListItem
      | {{ gameConfig.name || 'No name available' }}

      template(#badge)
        MBadge(v-if="gameConfig.isActive" variant="success") Active
        MBadge(v-if="gameConfig.isArchived" variant="neutral") Archived

      template(#top-right)
        div Uploaded #[meta-time(:date="gameConfig.persistedAt")]

      template(#bottom-left)
        div {{ gameConfig.description || 'No description available' }}

      template(#bottom-right)
        div(v-if="gameConfig.buildReportSummary?.totalLogLevelCounts.Warning" class="tw-text-orange-500") {{ gameConfig.buildReportSummary.totalLogLevelCounts.Warning }} build warnings
        div #[meta-button(link :to="`gameConfigs/diff?newRoot=${gameConfig.id}`" data-cy="view-config" :disabled="gameConfig.isActive") Diff to active] / #[meta-button(link :to="getDetailPagePath(gameConfig.id)" data-cy="view-config") View config]
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import type { StaticGameConfigInfo } from '../../gameConfigServerTypes'
import { getAllGameConfigsSubscriptionOptions } from '../../subscription_options/gameConfigs'

/**
 * Subscription to get all game configs.
 */
const {
  data: allGameConfigsDataRaw,
} = useSubscription<StaticGameConfigInfo[]>(getAllGameConfigsSubscriptionOptions())

/**
 * Filtered list containing only game configs that are publishable.
 */
const allGameConfigsData = computed(() => {
  if (allGameConfigsDataRaw.value !== undefined) {
    return allGameConfigsDataRaw.value.filter((x: StaticGameConfigInfo) => !x.bestEffortIsPublishBlockedByErrors)
  } else {
    return undefined
  }
})

const route = useRoute()
const detailsRoute = computed(() => route.path)

/**
 * Get detail page path by joining it to the current path,
 * but take into account if there's already a trailing slash.
 * \todo Do a general fix with router and get rid of this.
 */
function getDetailPagePath (gameConfigId: string) {
  const path = detailsRoute.value
  const maybeSlash = path.endsWith('/') ? '' : '/'
  return path + maybeSlash + gameConfigId
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
  new MetaListFilterSet('archived',
    [
      new MetaListFilterOption('Archived', (x: any) => x.isArchived),
      new MetaListFilterOption('Not archived', (x: any) => !x.isArchived, true)
    ]
  )
]
</script>
