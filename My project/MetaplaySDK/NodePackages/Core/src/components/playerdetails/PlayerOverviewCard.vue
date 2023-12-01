<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-header-card(:id="displayPlayerId" data-cy="player-overview-card")
  template(#title)
    core-ui-placement(alwaysFullWidth smallBottomMargin placementId="Players/Details/Overview:Title" :playerId="playerId")

  template(#subtitle)
    core-ui-placement(alwaysFullWidth placementId="Players/Details/Overview:Subtitle" :playerId="playerId")

  template(#caption)
    | Save file size:
    |
    meta-button(link data-cy="model-size-link" permission="api.database.inspect_entity" :to="`/entities/${playerData.model.playerId}/dbinfo`")
      meta-abbreviate-number(:value="playerData.persistedSize" unit="byte")

  b-row
    b-col(sm v-if="showLeftPanel")
      core-ui-placement(alwaysFullWidth placementId="Players/Details/Overview:LeftPanel" :playerId="playerId")

    b-col(sm)
      overview-list(listTitle="Overview" icon="chart-bar" :sourceObject="playerData" :items="coreStore.overviewLists.player" )
</template>

<script lang="ts" setup>
import OverviewList from '../global/OverviewList.vue'
import CoreUiPlacement from '../system/CoreUiPlacement.vue'
import { computed } from 'vue'
import { useSubscription } from '@metaplay/subscriptions'
import { useCoreStore } from '../../coreStore'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the player whose overview card to show.
   */
  playerId: string
}>()

const coreStore = useCoreStore()
const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

const showLeftPanel = computed(() => {
  if (coreStore.uiComponents['Players/Details/Overview:LeftPanel']) {
    return coreStore.uiComponents['Players/Details/Overview:LeftPanel'].length > 0
  }
  return false
})

const displayPlayerId = computed(() => {
  if (playerData.value?.model.playerName) {
    return playerData.value?.model.playerId
  }
  return ''
})

</script>
