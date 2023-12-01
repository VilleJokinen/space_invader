<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allOffersData"
  :meta-api-error="allOffersError"
  )
  template(#overview)
    meta-page-header-card
      template(#title) View Offer Groups
      p Offer groups are sets of in-game offers that can be scheduled, targeted and placed in different parts of your game.
      p.small.text-muted Individual offers can be re-used in multiple groups to show them in many places at the same time. You can set fine-tuned limits and conditions to both individual offers and their groups to create advanced shop management scenarios.

  template(#default)
    div(v-if="uniquePlacements && uniquePlacements.length > 0")
      b-row(no-gutters align-v="center").mt-3.mb-2
        h3 Offer Groups By Placement

      b-row(align-h="center")
        b-col(v-for="placement in uniquePlacements" :key="placement" lg="6").mb-3
          offer-groups-card.h-100(
            :title="`${placement}`"
            :placement="placement"
            emptyMessage="This placements has no offer groups in it."
            :customEvaluationIsoDateTime="customEvaluationTime ? String(customEvaluationTime.toISO()) : undefined"
            noCollapse
            hidePlacement
          )

    b-row(align-h="center").mt-3.mb-2
      b-col(md="8" xl="7").mb-3
        div(data-cy="custom-time").pl-3.pr-3.pb-3.bg-white.rounded.border.shadow-sm
          b-row(align-h="between" no-gutters).mb-2.mt-3
            span.font-weight-bold Enable Custom Evaluation Time
              MBadge(tooltip="The phases on the page are evaluated according to the local time of your browser. Enabling custom evaluation allows you to set an exact time to evaluate against." shape="pill").ml-1 ?
            meta-input-checkbox(v-model="userEvaluationEnabled" name="User Evaluation" size="lg" showAs="switch")
          div(v-if="userEvaluationEnabled").border-top.mt-3.pt-2
            MInputDateTime(
              label="Evaluation Time"
              :model-value="userEvaluationTime"
              @update:model-value="onDateTimeChange"
              )

          div.w-100.text-center.mt-2
            span.small.font-italic.text-muted Schedules evaluated at {{ evaluationTimeUsed }}
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed, ref } from 'vue'

import { MBadge, MInputDateTime } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import OfferGroupsCard from '../components/offers/OfferGroupsCard.vue'
import { getAllOffersSubscriptionOptions } from '../subscription_options/offers'

// Custom user evaluation time ----------------------------------------------------------------------------------------

/**
 * Model for whether custom user evaluation time is enabled or not.
 */
const userEvaluationEnabled = ref(false)

/**
 * Model for custom user evaluation time input.
 */
const userEvaluationTime = ref(DateTime.now())

/**
 * What time to use for evaluating the activables card/
 */
const customEvaluationTime = computed((): DateTime | undefined => {
  if (userEvaluationEnabled.value) {
    return userEvaluationTime.value
  } else {
    return undefined
  }
})

/**
 * Returns ISO string of time that is being used to evaluate availability of activables.
 */
const evaluationTimeUsed = computed((): string => {
  if (userEvaluationEnabled.value) {
    return userEvaluationTime.value.toISO() as string
  } else {
    return DateTime.now().toISO() as string
  }
})

/**
 * Utility function to prevent undefined inputs.
 */
function onDateTimeChange (value?: DateTime): void {
  if (!value) return
  userEvaluationTime.value = value
}

// Activables data ----------------------------------------------------------------------------------------------------

const {
  data: allOffersData,
  error: allOffersError
} = useSubscription(getAllOffersSubscriptionOptions())

const uniquePlacements = computed(() => {
  if (allOffersData.value) {
    const offerGroups = Object.values(allOffersData.value.offerGroups)
    const allPlacements = offerGroups.map((x: any) => x.config.placement)
    const uniquePlacements = [...new Set(allPlacements)]
    return uniquePlacements
  } else {
    return null
  }
})
</script>
