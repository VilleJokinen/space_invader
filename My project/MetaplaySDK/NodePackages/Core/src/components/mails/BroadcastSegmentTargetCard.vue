<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
targeting-card(
  v-if="data?.message"
  ownerTitle="This broadcast"
  :targetCondition="data.message.params.targetCondition"
  )
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'
import TargetingCard from './TargetingCard.vue'
import { getSingleBroadcastSubscriptionOptions } from '../../subscription_options/broadcasts'

const props = defineProps<{
  /**
   * ID of the broadcast to show.
   */
  broadcastId: string
}>()

const { data } = useSubscription(getSingleBroadcastSubscriptionOptions(props.broadcastId))
</script>
