<template lang="pug">
segments-card(
  v-if="playerData"
  :ownerTitle="`${playerName}`"
  :segments="playerData.matchingSegments"
  )
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import SegmentsCard from '../global/SegmentsCard.vue'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * ID of the player whose segments to list.
   */
  playerId: string
}>()

const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

const playerName = computed(() => playerData.value.model.playerName || 'n/a')
</script>
