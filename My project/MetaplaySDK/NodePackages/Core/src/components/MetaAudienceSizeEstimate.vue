<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A component to visualize a humanized version of the estimated size for the number of players. -->

<template lang="pug">
MTooltip(:content="tooltipContent")
  span(v-if="sizeEstimate === null") Estimate pending...
  span(v-else-if="sizeEstimate === undefined") Everyone
  span(v-else) ~#[meta-abbreviate-number(:value="sizeEstimate" roundDown disableTooltip unit="player")]
</template>

<script lang="ts" setup>
import moment from 'moment'
import { computed } from 'vue'

import { getPlayerSegmentsSubscriptionOptions } from '../subscription_options/general'
import { MTooltip } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * Optional: Use one of either:
   * number: The count of players in the audience.
   * null: Size estimate not available.
   * undefined: Everyone.
   */
  sizeEstimate?: number | null // TODO: this interface is... creative. Refactor.
  /**
   * Optional: Disable tooltips.
   */
  noTooltip?: boolean
}>()

/**
 * Subscribe to player segment data.
 */
const {
  data: playerSegmentsData
} = useSubscription(getPlayerSegmentsSubscriptionOptions())

const tooltipContent = computed(() => {
  // Whether to hide tooltip, for props.sizeEstimate we manually pass in undefined here to indicate a custom scenario.
  // e.g in ActivableDetailView we are targeting everyone.
  if (props.sizeEstimate === undefined || props.noTooltip) {
    return undefined
  } else if (props.sizeEstimate === null) {
    return 'Audience size estimates have not yet been generated.'
  } else {
    // Tooltip text that shows when the audience size estimate was taken.
    const updateTime = playerSegmentsData.value?.playerSegmentsLastUpdateTime || null
    const formattedUpdateTime = updateTime ? moment(updateTime).fromNow() : 'some time ago'
    return `Size estimate based on sampling taken ${formattedUpdateTime}. Actual sizes may differ and change over time.`
  }
})

</script>
