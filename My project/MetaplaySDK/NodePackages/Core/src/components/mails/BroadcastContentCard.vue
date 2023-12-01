<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-generated-section(
  v-if="data?.message"
  :typeName="'Metaplay.Server.BroadcastMessageContents'"
  :value="data.message.params.contents"
  title="Content"
  )
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleBroadcastSubscriptionOptions } from '../../subscription_options/broadcasts'
import MetaGeneratedSection from '../generatedui/components/MetaGeneratedSection.vue'

const props = defineProps<{
  /**
   * ID of the broadcast to show.
   */
  broadcastId: string
}>()

const { data } = useSubscription(getSingleBroadcastSubscriptionOptions(props.broadcastId))
</script>
