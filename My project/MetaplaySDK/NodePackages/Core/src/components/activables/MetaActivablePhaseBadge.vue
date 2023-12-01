<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MBadge(:tooltip="tooltip" :variant="badgeVariant") {{ displayString }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import type { ActivableData } from '../../activables'
import { phaseDisplayString, phaseTooltip, phaseBadgeVariant } from '../../activables'

import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * Data about an activable as returned by the "activables/..." or "offers/..." Admin APIs.
   */
  activable: ActivableData
  /**
   * Optional: The current phase of the activable.
   * If phase is not passed then it is derived from the current state of the activable. This is the default behavior.
   * @example 'Preview', 'Active', 'Ending soon' etc.
   */
  phase?: string
  /**
   * Optional: The ID of the player whose player-specific activable data we are interested in.
   */
  playerId?: string
  /**
   * Optional: Name of the parent type of the activable.
   * @example 'Event', 'Offer Group' ....etc.
   */
  typeName?: string
}>()

/**
 * Subscribe to player data when a playerId is provided. We watch the id and resubscribe as it changes.
 */
const {
  data: playerData,
} = useSubscription(() => props.playerId ? getSinglePlayerSubscriptionOptions(props.playerId) : undefined)

/**
 * Name of the activable phase to be displayed.
 */
const displayString = computed(() => {
  return phaseDisplayString(props.activable, props.phase, playerData.value)
})

/**
 * Description of the displayed activable phase to be shown as a tooltip.
 */
const tooltip = computed(() => {
  return phaseTooltip(props.activable, props.phase, playerData.value, props.typeName)
})

/**
 * Variant color to use when rendering the badge.
 */
const badgeVariant = computed(() => {
  return phaseBadgeVariant(props.activable, props.phase, playerData.value)
})
</script>
