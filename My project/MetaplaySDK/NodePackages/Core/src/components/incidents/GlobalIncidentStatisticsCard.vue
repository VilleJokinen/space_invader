<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- TODO: This components sister component GlobalIncidentHistoryCard has basic error handling due to use of subscriptions, this might need something similar.
meta-list-card(
  data-cy="global-incident-statistics-card"
  :title="limit ? 'Top Incidents in the Last 24h' : 'Unique Incidents in the Last 24h'"
  :itemList="limitedIncidentCounts"
  allowPausing
  dangerous
  icon="ambulance"
  :searchFields="searchFields"
  :filterSets="filterSets"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  emptyMessage="No incidents reported."
  permission="api.incident_reports.view"
)
  template(#item-card="slot")
    b-row(no-gutters align-h="between")
      span.font-weight-bold.text-break-word {{ slot.item.type }}
      meta-auth-tooltip(permission="api.audit_logs.search")
        b-link(
          class="small"
          :to="`/playerIncidents/${slot.item.fingerprint}`"
          :disabled="!gameServerApiStore.doesHavePermission('api.incident_reports.view')"
          ) View All {{ abbreviateNumber(slot.item.count) }}
    b-row(no-gutters align-h="start")
      span.small.text-muted.text-break-word {{ slot.item.reason }}
</template>

<script lang="ts" setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'

import { ApiPoller, useGameServerApiStore } from '@metaplay/game-server-api'
import { MetaListFilterSet, MetaListSortDirection, MetaListSortOption, abbreviateNumber } from '@metaplay/meta-ui'

// Props --------------------------------------------------------------------------------------------------------------

const props = defineProps<{
  limit?: number
}>()

// Data polling -------------------------------------------------------------------------------------------------------

const gameServerApiStore = useGameServerApiStore()
const statsPoller = ref<ApiPoller>()
const loading = ref(true)
const error = ref<any>()
const incidentCounts = ref<any>()

onMounted(() => {
  if (gameServerApiStore.doesHavePermission('api.incident_reports.view')) {
    statsPoller.value = new ApiPoller(
      5000, 'GET', '/incidentReports/statistics',
      null,
      (data) => {
        incidentCounts.value = data
        loading.value = false
        error.value = null
      },
      (err) => {
        loading.value = false
        error.value = err
      }
    )
  }
})

onUnmounted(() => {
  if (statsPoller.value) {
    statsPoller.value.stop()
  }
})

const limitedIncidentCounts = computed(() => {
  if (props.limit && incidentCounts.value) {
    return incidentCounts.value.slice(0, props.limit)
  }
  return incidentCounts.value || []
})

// MetaListCard configuration -----------------------------------------------------------------------------------------

const searchFields = [
  'type',
  'reason',
  'incidentId'
]

const filterSets = computed(() => {
  return [
    MetaListFilterSet.asDynamicFilterSet(limitedIncidentCounts.value, 'type', (x: any) => x.type)
  ]
})

const sortOptions = [
  new MetaListSortOption('Count', 'count', MetaListSortDirection.Ascending),
  new MetaListSortOption('Count', 'count', MetaListSortDirection.Descending),
  new MetaListSortOption('Type', 'type', MetaListSortDirection.Ascending),
  new MetaListSortOption('Type', 'type', MetaListSortDirection.Descending),
]

const defaultSortOption = 1
</script>
