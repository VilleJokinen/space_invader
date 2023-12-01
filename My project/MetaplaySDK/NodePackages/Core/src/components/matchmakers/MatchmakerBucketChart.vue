<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-card(data-cy="matchmaker-bucket-chart")
  b-card-title Bucket Distribution

  //- Loading.
  div(v-if="isLoadingData")
    b-skeleton(width="95%")
    b-skeleton(width="75%")
    b-skeleton(width="80%")
    b-skeleton(width="85%")
    b-skeleton(width="95%")
  //- Chart.
  meta-bar-chart(
    v-else
    :data="chartData"
    :options="chartOptions"
    :height="150"
    chart-id="bucket-fill-chart"
    )
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import type { ChartOptions } from 'chart.js'
import { useGameServerApi } from '@metaplay/game-server-api'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleMatchmakerSubscriptionOptions } from '../../subscription_options/matchmaking'

const props = defineProps<{
  matchmakerId: string
}>()

/**
 * Subscribe to the data of a single matchmaker based on its id.
 */
const { data: singleMatchmakerData } = useSubscription(getSingleMatchmakerSubscriptionOptions(props.matchmakerId))
const isLoadingData = computed(() => !singleMatchmakerData.value?.data.hasFinishedBucketUpdate)

// Bucket chart.
const chartData = computed(() => {
  const buckets = singleMatchmakerData.value.data.bucketInfos
  return {
    labels: buckets.map((bucket: any) => `${bucket.mmrLow} - ${bucket.mmrHigh}`),
    datasets: [
      {
        label: 'Participants',
        backgroundColor: 'rgb(45, 144, 220)',
        data: buckets.map((bucket: any) => bucket.numPlayers),
      },
      {
        label: 'Remaining capacity',
        backgroundColor: 'rgb(216, 216, 216)',
        data: buckets.map((bucket: any) => bucket.capacity - bucket.numPlayers),
      },
    ],
  }
})

const selectedBucketData = ref<any>(null)
async function getBucketData (bucketIndex: number) {
  selectedBucketData.value = null
  const res = await useGameServerApi().get(`matchmakers/${props.matchmakerId}/bucket/${bucketIndex}`)
  selectedBucketData.value = res.data
}

// Chart.
// const inspectBucket = ref(null)
const chartOptions: ChartOptions<'bar'> = {
  responsive: true,
  scales: {
    y: {
      stacked: true,
      type: 'logarithmic',
      min: 0.8,
      ticks: {
        callback: function (tickValue) {
          const powOf10 = Math.pow(10, Math.round(Math.log10(Number(tickValue))))
          if (powOf10 === tickValue || 10 * powOf10 === tickValue) {
            return tickValue
          }
        }
      },
    },
    x: {
      stacked: true,
    }
  },
  // TODO: This broke in R22 as we no longer have a reference to the modal.
  // onClick: (event, hits, chart) => {
  //   if (hits.length > 0) {
  //     getBucketData(matchmakerData.value.data.bucketInfos[hits[0].index].labelHash)
  //     if (inspectBucket.value) {
  //       (inspectBucket.value as any).show()
  //     }
  //   }
  // },
}

</script>
