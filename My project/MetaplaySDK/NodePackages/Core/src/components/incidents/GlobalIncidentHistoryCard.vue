<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A card that shows incidents for all players. Defers rendering to the incident-history-card. -->

<template lang="pug">
div(v-if="!incidentReportsError")
  incident-history-card(
    data-cy="global-incident-history-card"
    :incidents="incidentReportsData || []"
    :isLimitedTo="count"
    :showMainPageLink="showMainPageLink"
  )
//- TODO: This is a placeholder for until we migrate to tailwind with unified styling and extract this logic to own component.
div(v-else)
  b-card.mb-2
    b-card-title
      b-row(align-v="center" no-gutters)
        fa-icon(:icon="['fas','ambulance']").mr-2
        span Incident History
    p.text-danger Failed to load the incident history.
    MErrorCallout(:error="incidentReportsError")
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'

import { getIncidentReportsSubscriptionOptions } from '../../subscription_options/general'

import { MErrorCallout } from '@metaplay/meta-ui-next'
import IncidentHistoryCard from './IncidentHistoryCard.vue'

const props = defineProps<{
  /**
  * The maximum number of incidents to show.
  */
  count: number
  /**
   * Optional: Show incidents that match this fingerprint.
   */
  fingerprint?: string
  /**
   * Optional: Includes a link to the main incidents page inside the card.
   */
  showMainPageLink?: boolean
}>()

const {
  data: incidentReportsData,
  error: incidentReportsError
} = useSubscription(getIncidentReportsSubscriptionOptions(props.count, props.fingerprint))
</script>
