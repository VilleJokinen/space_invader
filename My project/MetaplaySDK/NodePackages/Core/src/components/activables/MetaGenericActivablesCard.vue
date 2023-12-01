<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-activables-base-card(
  data-cy="activables-card"
  :hideDisabled="hideDisabled"
  :hideConversion="hideConversion"
  :activables="activablesData"
  @refreshActivables="activablesRefresh"
  :playerId="playerId"
  :category="category"
  :longList="longList"
  :emptyMessage="emptyMessage"
  :title="title"
  :noCollapse="noCollapse"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  permission="api.activables.view"
)
  template(#collapseContents="slot")
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Next Schedule Phase
      template(#top-right)
        span(v-if="!slot.item.activable.config.activableParams.isEnabled").text-muted.font-italic Disabled
        span(v-else-if="slot.item.activable.debugState").text-warning Debug override!
        span(v-else-if="slot.item.nextPhase") #[meta-activable-phase-badge(:activable="slot.item.activable" :phase="slot.item.nextPhase" :playerId="playerId" :typeName="`${categoryDisplayName.toLocaleLowerCase()}`")] #[meta-time(:date="slot.item.nextPhaseStartTime")]
        span(v-else-if="slot.item.scheduleStatus").text-muted.font-italic None
        span(v-else).text-muted.font-italic No schedule
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Lifetime Left
      template(#top-right)
        meta-duration(v-if="slot.item.activable.state.hasOngoingActivation && slot.item.activable.state.activationRemaining !== null" :duration="slot.item.activable.state.activationRemaining" showAs="humanizedSentenceCase")
        span(v-else-if="slot.item.activable.state.hasOngoingActivation").text-muted.font-italic Forever
        span(v-else).text-muted.font-italic n/a
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Cooldown Left
      template(#top-right)
        meta-duration(v-if="slot.item.activable.state.isInCooldown && slot.item.activable.state.cooldownRemaining !== null" :duration="slot.item.activable.state.cooldownRemaining" showAs="humanizedSentenceCase")
        span(v-else-if="slot.item.activable.state.isInCooldown").text-muted.font-italic Forever
        span(v-else).text-muted.font-italic n/a
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Activations
      template(#top-right) {{ slot.item.activable.state.numActivated }}/{{ slot.item.activable.config.activableParams.maxActivations || '&#8734;' }}
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Consumes During Current Activation
      template(#top-right)
        span(v-if="slot.item.activable.state.hasOngoingActivation") {{ slot.item.activable.state.currentActivationNumConsumed }}/{{ slot.item.activable.config.activableParams.maxConsumesPerActivation || '&#8734;' }}
        span(v-else).text-muted.font-italic n/a
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Total Consumes
      template(#top-right) {{ slot.item.activable.state.totalNumConsumed }}/{{ slot.item.activable.config.activableParams.maxTotalConsumes || '&#8734;' }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListSortOption, MetaListSortDirection } from '@metaplay/meta-ui'
import { MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getAllActivablesForPlayerSubscriptionOptions, getAllActivablesSubscriptionOptions } from '../../subscription_options/activables'
import { getStaticConfigSubscriptionOptions } from '../../subscription_options/general'
import MetaActivablesBaseCard from './MetaActivablesBaseCard.vue'
import MetaActivablePhaseBadge from './MetaActivablePhaseBadge.vue'

const props = defineProps<{
  /**
   * Title to be shown on the card.
   */
  title: string
  /**
   * Group name assigned to similar activables.
   * @example 'In-game events'
   */
  category: string
  /**
   * Custom message to be shown when there are no activables available to display.
   */
  emptyMessage: string
  /**
   * Optional: Custom time other than the current time used to fetch activiable data for that specific time.
   */
  customEvaluationIsoDateTime?: string
  /**
   * Optional: Id of the player whose data we are interested in.
   */
  playerId?: string
  /**
   * Optional: If true hide disabled activable data.
   */
  hideDisabled?: boolean
  /**
   * Optional: If true hide activable conversion statistics.
   */
  hideConversion?: boolean
  /**
   * Optional: If true show the 50 list items on one page.
   * Defaults 8.
   */
  longList?: boolean
  /**
   * Optional: If true renders a non-collapsible list card.
   */
  noCollapse?: boolean
}>()

/**
 * Subscribe to data needed to render this component.
 */
const {
  data: staticConfigData,
  refresh: activablesRefresh,
} = useSubscription(getStaticConfigSubscriptionOptions())

const {
  data: activablesData,
} = useSubscription(() => {
  if (props.playerId) {
    return getAllActivablesForPlayerSubscriptionOptions(props.playerId, props.customEvaluationIsoDateTime)
  } else {
    return getAllActivablesSubscriptionOptions(props.customEvaluationIsoDateTime)
  }
})

/**
 *  Additional data about the activable.
 */
const activablesMetadata = computed(() => {
  return staticConfigData?.value.serverReflection.activablesMetadata
})

/**
 * The activable category name to be displayed.
 */
const categoryDisplayName = computed(() => {
  return activablesMetadata?.value.categories[props.category].shortSingularDisplayName
})

/**
 * Search fields array to be passed to the meta-list-card component.
 */
const searchFields = [
  'activable.config.displayName',
  'activable.config.displayShortInfo',
  'activable.config.description',
  'phaseDisplayString'
]

/**
 * Sort options array to be passed to the meta-list-card component.
 */
const sortOptions = computed(() => {
  const sortOptions = [
    MetaListSortOption.asUnsorted(),
    new MetaListSortOption('Phase', 'phaseSortOrder', MetaListSortDirection.Ascending),
    new MetaListSortOption('Phase', 'phaseSortOrder', MetaListSortDirection.Descending),
    new MetaListSortOption('Name', 'activable.config.displayName', MetaListSortDirection.Ascending),
    new MetaListSortOption('Name', 'activable.config.displayName', MetaListSortDirection.Descending),
  ]
  if (!props.hideConversion) {
    sortOptions.push(new MetaListSortOption('Conversion', 'conversion', MetaListSortDirection.Ascending))
    sortOptions.push(new MetaListSortOption('Conversion', 'conversion', MetaListSortDirection.Descending))
  }
  return sortOptions
})
</script>
