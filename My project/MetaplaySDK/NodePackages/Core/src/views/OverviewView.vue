<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!backendStatusData || !databaseItemCountsData"
  :meta-api-error="backendStatusError || databaseItemCountsError"
  )
  template(#overview)
    meta-page-header-card(data-cy="overview-card")
      template(#title) Game Server Status
      template(#subtitle) Build: {{ coreStore.hello.buildNumber }}
      template(#caption) Commit ID: {{coreStore.hello.commitId}}

      b-row
        b-col(sm).border-right-md
          span.font-weight-bold Game Server
          table.mt-1.table.table-sm
            tbody
              tr
                td Live Concurrents
                td.text-right #[meta-abbreviate-number(:value="backendStatusData.numConcurrents")]
              tr(v-for="entry in coreStore.actorOverviews.overviewListEntries" :key="entry.key")
                td {{ entry.displayName }}
                td.text-right #[meta-abbreviate-number(:value="backendStatusData.liveEntityCounts[entry.key]")]
              tr
                td Maintenance Mode
                td.text-right
                  MBadge(v-if="backendStatusData.maintenanceStatus.isInMaintenance" variant="danger") On
                  MBadge(v-else-if="backendStatusData.maintenanceStatus.scheduledMaintenanceMode" variant="warning") Scheduled
                  MBadge(v-else) Off

        b-col(sm)
          span.font-weight-bold Database
          table.mt-1.table.table-sm
            tbody
              tr
                td Type
                td.text-right: MBadge {{ backendStatusData.databaseStatus.backend }}
              tr
                td Active Shards
                td.text-right {{ backendStatusData.databaseStatus.activeShards }}/{{ backendStatusData.databaseStatus.totalShards }}
              tr(v-for="entry in coreStore.actorOverviews.databaseListEntries" :key="entry.key")
                td {{ entry.displayName }}
                td.text-right #[meta-abbreviate-number(:value="databaseItemCount(entry.key)")]

  template(#default)
    core-ui-placement(placementId="OverviewView")

    meta-raw-data(:kvPair="backendStatusData" name="backendStatusData")
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { getBackendStatusSubscriptionOptions, getDatabaseItemCountsSubscriptionOptions } from '../subscription_options/general'
import { useCoreStore } from '../coreStore'
import { MViewContainer, MBadge } from '@metaplay/meta-ui-next'

const coreStore = useCoreStore()
const {
  data: backendStatusData,
  error: backendStatusError
} = useSubscription(getBackendStatusSubscriptionOptions())
const {
  data: databaseItemCountsData,
  error: databaseItemCountsError
} = useSubscription(getDatabaseItemCountsSubscriptionOptions())

/**
 * Calculates number of items by type across all database shards.
 * @param itemType Type of item to count (Players, Guilds, etc).
 * @returns The number of items of the specified type in the database.
 */
function databaseItemCount (itemType: string): number {
  return databaseItemCountsData.value?.totalItemCounts?.[itemType + 's'] || 0
}
</script>
