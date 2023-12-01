<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleBroadcastData"
  :meta-api-error="singleBroadcastError"
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the broadcast!

  template(#overview)
    meta-page-header-card(:id="`${decoratedBroadcasts.message.params.id}`")
      template(#title) {{ decoratedBroadcasts.message.params.name }}

      template(#title-badge)
        MBadge.ml-2(:variant="decoratedBroadcasts.decoration.variant")
          template(#icon)
            fa-icon(:icon="decoratedBroadcasts.decoration.icon")
          | {{ decoratedBroadcasts.decoration.status }}

      div
        span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
        b-table-simple(small responsive).mt-1
          b-tbody
            b-tr
              b-td Audience Size Estimate
              b-td.text-right #[meta-audience-size-estimate(:sizeEstimate="decoratedBroadcasts.message.params.isTargeted ? audienceSizeEstimate : undefined")]
            b-tr
              b-td Start Time
              b-td.text-right #[meta-time(:date="decoratedBroadcasts.message.params.startAt" showAs="datetime")]
            b-tr
              b-td Expiry Time
              b-td.text-right #[meta-time(:date="decoratedBroadcasts.message.params.endAt" showAs="datetime")]
            b-tr
              b-td Received By
              b-td.text-right #[meta-abbreviate-number(:value="decoratedBroadcasts.message.stats.receivedCount", unit="player")]
            b-tr
              b-td Trigger
              b-td.text-right {{ triggerCondition ? `On ${triggerCondition}` : 'Immediate' }}

      template(#buttons)
        //- Duplicate broadcast.
        broadcast-form-button(
          v-if="updatedBroadcast"
          button-text="Duplicate"
          :prefillData="updatedBroadcast"
          @refresh="singleBroadcastRefresh()"
          :editBroadcast="false").mr-2

        //- Edit broadcast.
        broadcast-form-button(
          button-text="Edit"
          :prefillData="updatedBroadcast"
          @refresh="singleBroadcastRefresh()"
          :disabled="decoratedBroadcasts.decoration.status === 'Expired'"
          :editBroadcast="true").mr-2

        broadcast-delete-form-button(:id="`${route.params.id}`") Delete

  template(#default)
    core-ui-placement(placementId="Broadcasts/Details" :broadcastId="String(decoratedBroadcasts.message.params.id)")

    meta-raw-data(:kvPair="singleBroadcastData" name="broadcast")
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { DateTime } from 'luxon'

import { useRoute } from 'vue-router'

import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getAllAnalyticsEventsSubscriptionOptions } from '../subscription_options/analyticsEvents'
import { getSingleBroadcastSubscriptionOptions } from '../subscription_options/broadcasts'

import { routeParamToSingleValue } from '../coreUtils'

import BroadcastFormButton from '../components/mails/BroadcastFormButton.vue'
import BroadcastDeleteFormButton from '../components/mails/BroadcastDeleteFormButton.vue'
import MetaAudienceSizeEstimate from '../components/MetaAudienceSizeEstimate.vue'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import type { ConditionOption } from '../components/mails/mailUtils'

const route = useRoute()

/**
 * Subscribe to analytics events data.
 */
const { data: analyticsEvents } = useSubscription(getAllAnalyticsEventsSubscriptionOptions())

/**
 * Subscribe to target broadcast data.
 */
const {
  data: singleBroadcastData,
  refresh: singleBroadcastRefresh,
  error: singleBroadcastError
} = useSubscription(getSingleBroadcastSubscriptionOptions(routeParamToSingleValue(route.params.id) || ''))

/**
 * Broadcast data to be displayed in this card.
 */
const decoratedBroadcasts = computed(() => {
  if (!singleBroadcastData.value) return undefined
  const now = DateTime.now()
  const startAtDate = DateTime.fromISO(singleBroadcastData.value.message.params.startAt)
  const endAtDate = DateTime.fromISO(singleBroadcastData.value.message.params.endAt)
  if (endAtDate > now && startAtDate < now) {
    return {
      ...singleBroadcastData.value,
      decoration: {
        status: 'Active',
        variant: 'success',
        icon: 'broadcast-tower'
      }
    }
  } else if (endAtDate > now) {
    return {
      ...singleBroadcastData.value,
      decoration: {
        status: 'Scheduled',
        variant: 'primary',
        icon: 'calendar-alt'
      }
    }
  } else {
    return {
      ...singleBroadcastData.value,
      decoration: {
        status: 'Expired',
        variant: 'neutral',
        icon: 'times'
      }
    }
  }
})

/**
 * Estimated of number of players targeted in the broadcast.
 */
const audienceSizeEstimate = computed(() => {
  return decoratedBroadcasts.value?.audienceSizeEstimate
})

/**
 * Existing broadcast content which is to be edited or duplicated.
 */
const updatedBroadcast = computed(() => {
  return decoratedBroadcasts.value?.message.params
})

/**
 * Condition that must be true for a targeted broadcast to be sent.
 */
const triggerCondition = computed((): ConditionOption => {
  return decoratedBroadcasts.value.message.params.triggerCondition == null
    ? null
    : analyticsEvents.value?.find((x: any) => x.typeCode === decoratedBroadcasts.value.message?.params.triggerCondition.eventTypeCode)?.displayName ?? 'UNKNOWN'
})

</script>
