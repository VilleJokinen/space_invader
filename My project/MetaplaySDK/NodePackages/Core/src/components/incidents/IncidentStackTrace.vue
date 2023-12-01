<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-card(v-if="playerIncidentData" :class="!stackTraceRows ? 'bg-light' : ''").shadow-sm
  b-card-title
    b-row(no-gutters align-v="center")
      fa-icon(icon="code").mr-2
      span.mr-1 Stack Trace
      meta-clipboard-copy(:contents="rawStackTrace")

  p.m-0.text-muted.text-center(v-if="!stackTraceRows") Stack trace not included in this incident report.

  div.log.border.rounded.bg-light.w-100(v-else style="max-height: 20rem")
    pre
      div.m-0(v-for="(row, index) in stackTraceRows" :key="index")
        span(v-for="(word, index) in row.split(' ')" :key="index" :class="index === 0 ? '' : 'text-muted'")  {{ word }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { useSubscription } from '@metaplay/subscriptions'

import { getPlayerIncidentSubscriptionOptions } from '../../subscription_options/incidents'

const props = defineProps<{
  /**
   * ID of the incident to show.
   */
  incidentId: string
  /**
   * ID of the player to show.
   */
  playerId: string
}>()

const {
  data: playerIncidentData,
} = useSubscription(getPlayerIncidentSubscriptionOptions(props.playerId, props.incidentId))

/**
 * Either the raw stack trace data to be copied or an empty string when there is no data.
 */
const rawStackTrace = computed((): string => playerIncidentData.value?.stackTrace || '')

/**
 * List of stack trace data to be displayed in rows.
 */
const stackTraceRows = computed(() => {
  return playerIncidentData.value?.stackTrace?.split('\n')
})

</script>
