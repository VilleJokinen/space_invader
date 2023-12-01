<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  title="All Leagues"
  :itemList="decoratedAllLeaguesData"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  :filterSets="filterSets"
  icon="trophy"
  emptyMessage="No leagues to list!"
  data-cy="league-list-card"
  )
  template(#item-card="{ item: leagueData }")
    MListItem {{ leagueData.displayName || 'League name not available.' }}
      template(#badge)
        MBadge(v-if="leagueData.enabled" variant="success") Enabled
        MBadge(v-else tooltip="This league is not enabled for this deployment. Contact your game team to set it up." ) Disabled
      template(#top-right)
        b-row(align-h="between" align-v="center" no-gutters).text-muted
          | {{ leagueData.leagueId}}
      template(#bottom-left)
        b-row(align-h="between" align-v="center" no-gutters).mt-1
          | {{ leagueData.description || 'There is currently no description for this league.' }}
      template(#bottom-right)
        meta-button(
          link :to="`/leagues/${leagueData.leagueId}`"
          data-cy="view-league"
          ) View league
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getAllLeaguesSubscriptionOptions } from '../../subscription_options/leagues'

const { data: allLeaguesData } = useSubscription(getAllLeaguesSubscriptionOptions())

const decoratedAllLeaguesData = computed(() => {
  return allLeaguesData.value.map((leagueData: any) => {
    let sortOrder
    const phase = leagueData.scheduleStatus.currentPhase.phase
    if (phase === 'Preview') {
      sortOrder = 1
    } else if (phase === 'Active') {
      sortOrder = 2
    } else if (phase === 'EndingSoon') {
      sortOrder = 3
    } else {
      sortOrder = 4
    }
    return {
      ...leagueData,
      sortOrder,
    }
  })
})

// TODO: Search and filter initial implementation done. Sort cannot be easily tested, tested initially with hacked data.
const searchFields = ['leagueId', 'displayName', 'description']

const sortOptions = [
  new MetaListSortOption('Phase', 'sortOrder', MetaListSortDirection.Ascending),
  new MetaListSortOption('Phase', 'sortOrder', MetaListSortDirection.Descending),
]

const filterSets = [
  new MetaListFilterSet('enabled',
    [
      new MetaListFilterOption('Enabled', (leagueData: any) => leagueData.enabled === true, false),
      new MetaListFilterOption('Disabled', (leagueData: any) => leagueData.enabled === false, false),
    ]
  ),
]

</script>
