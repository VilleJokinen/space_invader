<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  //- Buckets list
  meta-list-card(
    data-cy="matchmaker-buckets-list-card"
    title="All Buckets"
    :itemList="singleMatchmakerData.data.bucketInfos"
    :sortOptions="bucketSortOptions"
    :filterSets="bucketFilterSets"
  )
    template(#item-card="{ item: bucket, index }")
      MListItem
        | Bucket &#x0023;{{ index }}
        template(#top-right) {{ bucket.numPlayers }} participants
        template(#bottom-left)
          span(v-for="label in bucket.labels" :key="label.dashboardLabel")
            MBadge(v-if="label.dashboardLabel").mr-1 {{ label.dashboardLabel }}
        template(#bottom-right)
          div {{ Math.round(bucket.fillPercentage * 10000) / 100 }}% full
          div: meta-button(link modal="inspect-bucket" @click="getBucketData(bucket.labelHash)") View bucket

  //- Bucket modal
  b-modal#inspect-bucket(ref="inspectBucketModal" title="Inspect Bucket" size="md" centered no-close-on-backdrop scrollable)
    //- Loading
    div(v-if="!selectedBucketData").w-100.text-center.pt-3
      b-spinner(label="Loading...")

    //- Players list
    div(v-else)
      p Top 20 players of the bucket #[MBadge {{ selectedBucketData.mmrLow }} - {{ selectedBucketData.mmrHigh }}]
      //- Styling below to make this look like old b-list/group.
      MList(v-if="selectedBucketData.players && selectedBucketData.players.length > 0" showBorder)
        MListItem(
          v-for="player in selectedBucketData.players"
          :key="player.model.playerId"
          class="tw-px-3"
          striped
          )
          | {{ player.name }}
          template(#top-right) {{ player.model.playerId }}
          template(#bottom-left) {{ player.summary }}
          template(#bottom-right): meta-button(link permission="api.players.view" :to="`/players/${player.model.playerId}`") View player
      //- TODO R26 Add v-else for MList
      //- div(v-else)
    template(#modal-footer="{ ok }")
      meta-button(variant="secondary" @click="ok") Close
</template>

<script lang="ts" setup>
import { ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MetaListSortDirection, MetaListSortOption, MetaListFilterOption, MetaListFilterSet } from '@metaplay/meta-ui'
import { MBadge, MList, MListItem } from '@metaplay/meta-ui-next'
import { getSingleMatchmakerSubscriptionOptions } from '../../subscription_options/matchmaking'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  matchmakerId: string
}>()

/**
 * Subscribe to the data of a single matchmaker based on its id.
 */
const { data: singleMatchmakerData } = useSubscription(getSingleMatchmakerSubscriptionOptions(props.matchmakerId))

const selectedBucketData = ref<any>(null)
const inspectBucketModal = ref(null)

// Bucket list
const bucketSortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Fill rate', 'fillPercentage', MetaListSortDirection.Descending),
  new MetaListSortOption('Fill rate', 'fillPercentage', MetaListSortDirection.Ascending),
  new MetaListSortOption('Participants', 'numPlayers', MetaListSortDirection.Descending),
  new MetaListSortOption('Participants', 'numPlayers', MetaListSortDirection.Ascending),
]
const bucketFilterSets = [
  new MetaListFilterSet('participants',
    [
      new MetaListFilterOption('Full', (x: any) => x.numPlayers === x.capacity, true),
      new MetaListFilterOption('Partially full', (x: any) => x.numPlayers > 0 && x.numPlayers < x.capacity, true),
      new MetaListFilterOption('Empty', (x: any) => x.numPlayers === 0),
    ]
  ),
]

async function getBucketData (bucketIndex: number) {
  selectedBucketData.value = null
  const res = await useGameServerApi().get(`matchmakers/${props.matchmakerId}/bucket/${bucketIndex}`)
  selectedBucketData.value = res.data
}

</script>
