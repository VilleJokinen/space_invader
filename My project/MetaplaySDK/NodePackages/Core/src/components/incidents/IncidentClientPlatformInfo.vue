<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-card(
  v-if="playerIncidentData"
  title="Client Platform Info"
  :class="!playerIncidentData.clientPlatformInfo ? 'bg-light' : ''"
).shadow-sm
  b-table-simple(small responsive)
    b-tbody
      b-tr(v-for="field in getObjectPrintableFields(playerIncidentData.clientPlatformInfo)" :key="field.key")
        b-td {{ field.name }}
        b-td.text-right {{ field.value }}
</template>

<script lang="ts" setup>
import { getObjectPrintableFields } from '@metaplay/meta-ui'
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
</script>
