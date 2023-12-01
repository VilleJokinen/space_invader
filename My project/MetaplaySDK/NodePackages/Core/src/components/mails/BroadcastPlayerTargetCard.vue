<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
player-list-card(
  v-if="data?.message"
  :playerIds="data.message.params.targetPlayers"
  title="Individual Players"
  emptyMessage="No individual players targeted."
  )
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'
import PlayerListCard from '../global/PlayerListCard.vue'
import { getSingleBroadcastSubscriptionOptions } from '../../subscription_options/broadcasts'

const props = defineProps<{
  /**
   * ID of the broadcast to show.
   */
  broadcastId: string
}>()

const { data } = useSubscription(getSingleBroadcastSubscriptionOptions(props.broadcastId))
</script>
