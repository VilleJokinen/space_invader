<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Graphs
b-row.justify-content-center
  b-col(lg=6).pb-3
    b-card(data-cy="concurrents-card" no-body).shadow-sm
      b-card-body
        b-card-title
          b-row(align-v="center" no-gutters)
            | Concurrents
            MBadge(shape="pill" :variant="backendStatus.numConcurrents > 0 ? 'primary' : 'neutral'").ml-1 {{ backendStatus.numConcurrents }}
        meta-bar-chart(
          :data="allCharts.Concurrents"
          chart-id="concurrents-chart"
          )

  b-col(lg=6 v-for="chart in coreStore.actorOverviews.charts" :key="chart.key").pb-3
    b-card(:data-cy="toKebabCase(chart.displayName) + '-card'" no-body).shadow-sm
      b-card-body
        b-card-title
          b-row(align-v="center" no-gutters)
            | {{ chart.displayName }}
            MBadge(shape="pill" :variant="backendStatus.liveEntityCounts[chart.key] > 0 ? 'primary' : 'neutral'").ml-1 {{ backendStatus.liveEntityCounts[chart.key] }}
        meta-bar-chart(
          v-if="allCharts[chart.key]"
          :data="allCharts[chart.key]"
          chart-id="`${chart.key}-chart`"
          )
</template>

<script lang="ts" setup>
import { onBeforeUnmount, ref } from 'vue'

import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { useCoreStore } from '../../coreStore'
import { toKebabCase } from '../../coreUtils'
import { getBackendStatusSubscriptionOptions } from '../../subscription_options/general'

const coreStore = useCoreStore()
const {
  data: backendStatus,
} = useSubscription(getBackendStatusSubscriptionOptions())

/**
 * Object containing all chart data. Initializes with empty data.
 */
const allCharts = ref<{ [key: string]: { labels: string[], datasets: Array<{ data: number[] }> }}>(
  Object.fromEntries([
    { key: 'Concurrents' },
    ...coreStore.actorOverviews.charts
  ].map((chart) => [
    chart.key,
    {
      labels: ['', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', ''],
      datasets: [{
        data: Array.from({ length: 20 }, () => 0)
      }]
    }
  ]))
)

// Update charts on a timer.
const timer = ref<ReturnType<typeof setTimeout> | undefined>(undefined)

/**
 * Async function that polls the chart data.
 */
function poll () {
  if (backendStatus.value) {
    // Copy allCharts to a new object and mutate that object before saving it back to allCharts.
    const newAllCharts = JSON.parse(JSON.stringify(allCharts.value))
    newAllCharts.Concurrents.datasets[0].data.shift()
    newAllCharts.Concurrents.datasets[0].data.push(backendStatus.value.numConcurrents)

    for (const chart of coreStore.actorOverviews.charts) {
      newAllCharts[chart.key].datasets[0].data.shift()
      newAllCharts[chart.key].datasets[0].data.push(backendStatus.value.liveEntityCounts[chart.key])
    }
    allCharts.value = newAllCharts
  }
  timer.value = setTimeout(() => poll(), 5000)
}
onBeforeUnmount(() => clearTimeout(timer.value))

poll()
</script>
