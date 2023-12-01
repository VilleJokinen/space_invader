<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A wrapper component for displaying overview card title. -->

<template lang="pug">
span(v-if="playerData")
  fa-icon(v-if="playerData.model.isOnline && playerData.model.isClientConnected && playerData.model.clientAppPauseStatus == 'Running'" size="xs" icon="circle").text-success
  fa-icon(v-else-if="playerData.model.isOnline" size="xs" icon="circle").text-warning
  fa-icon(v-else-if="moment().diff(moment(playerData.model.stats.lastLoginAt), 'hours') < 12" size="xs" :icon="['far', 'circle']").text-success
  fa-icon(v-else size="xs" :icon="['far', 'circle']").text-dark
  span.ml-2 {{ displayTitle }} #[meta-clipboard-copy(v-if="displayTitle === playerData.model.playerId" :contents="playerData.model.playerId")]
  MTooltip(v-if="playerData.model.totalIapSpend > 0" :content="'Total IAP spend: $' + playerData.model.totalIapSpend.toFixed(2, 2)" noUnderline).text-muted.ml-2 #[fa-icon(icon="money-check-alt")]
  MTooltip(v-if="playerData.model.isDeveloper" content="This player is a developer." noUnderline).text-muted.ml-2 #[fa-icon(data-cy="player-is-developer-icon" icon="user-astronaut")]
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import moment from 'moment'
import { MTooltip } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the player displayed on the overview card header title.
   */
  playerId: string
}>()

const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

// TODO: Move logic to c# side. (There should be no players that have no names.)
const displayTitle = computed(() => {
  if (playerData.value?.model.playerName) {
    return playerData.value?.model.playerName
  } else {
    return playerData.value?.model.playerId
  }
})
</script>
