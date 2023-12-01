<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-activables-base-card(
  data-cy="activables-card"
  :hideDisabled="hideDisabled"
  :hideConversion="hideConversion"
  :activables="activables"
  @refreshActivables="offersRefresh"
  :playerId="playerId"
  category="OfferGroup"
  :longList="longList"
  :emptyMessage="emptyMessage"
  :title="title"
  :noCollapse="noCollapse"
  linkUrlPrefix="/offerGroups"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  permission="api.activables.view"
)
  template(#displayShortInfo="slot")
    | (
    span(:class="slot.item.activable.config.displayShortInfo.startsWith('0') ? 'text-warning' : 'text-muted'") {{ slot.item.activable.config.displayShortInfo }}
    | )

  template(#additionalTexts="slot")
    div(v-if="!hidePlacement") Placement: {{ slot.item.activable.config.placement }}
    div(v-if="!hidePriority") Priority: #[meta-ordinal-number(:number="slot.item.activable.config.priority")]
    div(v-if="!hideRevenue") Revenue: ${{ slot.item.activable.revenue.toFixed(2) }}

  template(#collapseContents="slot")
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Next Schedule Phase
      template(#top-right)
        span(v-if="!slot.item.activable.config.activableParams.isEnabled").text-muted.font-italic Disabled
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
    MListItem(class="tw-px-4 !tw-py-2.5" condensed) Individual Offers
      template(#top-right)
        div(v-for="(offer, key) in slot.item.activable.offers" :key="key")
          | {{ offer.config.displayName }} #[span.text-muted(v-if="offer.referencePrice !== null") (${{ offer.referencePrice.toFixed(2) }})] - #[meta-plural-label(:value="offer.state.numPurchasedInGroup", label="purchase")]
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getStaticConfigSubscriptionOptions } from '../../subscription_options/general'
import { getAllOffersSubscriptionOptions, getAllOffersForPlayerSubscriptionOptions } from '../../subscription_options/offers'
import MetaActivablesBaseCard from '../activables/MetaActivablesBaseCard.vue'
import MetaActivablePhaseBadge from '../activables/MetaActivablePhaseBadge.vue'

const props = defineProps<{
  /**
   * Title to be shown on the card.
   */
  title: string
  /**
   * Custom message to be shown when there are no offers available to display.
   */
  emptyMessage: string
  /**
   * Optional: Don't display offers that are disabled.
   */
  hideDisabled?: boolean
  /**
   * Optional: Don't show conversion rates for offers.
   */
  hideConversion?: boolean
  /**
   * Optional: Don't show placement information for offers.
   */
  hidePlacement?: boolean
  /**
   * Optional: Don't show priority information for offers.
   */
  hidePriority?: boolean
  /**
   * Optional: Don't show revenue information for offers.
   */
  hideRevenue?: boolean
  /**
   * Optional: The ID of the player whose player-specific offer data we are interested in.
   */
  playerId?: string
  /**
   * Optional: Custom time other than the current time used to fetch offer data for that specific time.
   */
  customEvaluationIsoDateTime?: string
  /**
   * Optional: Only show offers that match this placement.
   */
  placement?: string
  /**
   * Optional: Show ore items on the list card.
   */
  longList?: boolean
  /**
   * Optional: Hide extra information that's available by expanding the card item.
   */
  noCollapse?: boolean
  /**
   * Optional: Default sort option.
   */
  defaultSortOption?: number
}>()

/**
 * Fetch static config data.
 */
const {
  data: staticConfig,
} = useSubscription(getStaticConfigSubscriptionOptions())

/**
 * Get global activables data.
 */
const activablesMetadata = computed(() => {
  return staticConfig.value?.serverReflection.activablesMetadata
})

/**
 * Get human readable category name.
 */
const categoryDisplayName = computed(() => {
  return activablesMetadata.value?.categories.OfferGroup.shortSingularDisplayName
})

/**
 * Subscribe to offers. Resubscribe whenever source data changes.
 */
const {
  data: offersData,
  refresh: offersRefresh,
} = useSubscription(() => {
  if (props.playerId) {
    return getAllOffersForPlayerSubscriptionOptions(props.playerId, props.customEvaluationIsoDateTime)
  } else {
    return getAllOffersSubscriptionOptions(props.customEvaluationIsoDateTime)
  }
})

/**
 * Extract relevant activables.
 */
const activables = computed((): any => {
  if (offersData.value) {
    const activables = Object.fromEntries(
      Object.entries(offersData.value.offerGroups)
        .filter(([key, value]: any) => !props.placement || value.config.placement === props.placement)
    )
    return {
      OfferGroup: {
        activables
      }
    }
  } else {
    return null
  }
})

/**
 * Search fields for the card.
 */
const searchFields = [
  'activable.config.displayName',
  'activable.config.description',
  'phaseDisplayString'
]

/**
 * Sort options for the card.
 */
const sortOptions = computed(() => {
  const sortOptions = [
    MetaListSortOption.asUnsorted(),
    new MetaListSortOption('Priority', 'activable.config.priority', MetaListSortDirection.Ascending),
    new MetaListSortOption('Priority', 'activable.config.priority', MetaListSortDirection.Descending),
    new MetaListSortOption('Phase', 'phaseSortOrder', MetaListSortDirection.Ascending),
    new MetaListSortOption('Phase', 'phaseSortOrder', MetaListSortDirection.Descending),
    new MetaListSortOption('Name', 'activable.config.displayName', MetaListSortDirection.Ascending),
    new MetaListSortOption('Name', 'activable.config.displayName', MetaListSortDirection.Descending),
  ]
  if (!props.hideConversion) {
    sortOptions.push(new MetaListSortOption('Conversion', 'conversion', MetaListSortDirection.Ascending))
    sortOptions.push(new MetaListSortOption('Conversion', 'conversion', MetaListSortDirection.Descending))
  }
  if (!props.hideRevenue) {
    sortOptions.push(new MetaListSortOption('Revenue', 'activable.revenue', MetaListSortDirection.Ascending))
    sortOptions.push(new MetaListSortOption('Revenue', 'activable.revenue', MetaListSortDirection.Descending))
  }

  return sortOptions
})
</script>
