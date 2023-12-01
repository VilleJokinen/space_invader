<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  data-cy="individual-offers"
  :title="title"
  icon="tag"
  :emptyMessage="emptyMessage"
  :itemList="decoratedOffers"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
)
  template(#item-card="slot")
    MCollapse(extraMListItemMargin)
      //- Row header
      template(#header)
        MListItem
          | {{ slot.item.config.displayName }} #[span.text-muted.font-weight-normal.small(v-if="slot.item.referencePrice !== null") ${{ slot.item.referencePrice.toFixed(2) }}]

          template(#badge): MBadge(v-if="!isUsed(slot.item)" variant='warning').ml-1 Unused
          template(#top-right)
            MTooltip(:content="slot.item.conversionTooltip") {{ slot.item.conversion.toFixed(0) }}% conversion
          template(#bottom-left) {{ slot.item.config.description}}
          template(#bottom-right): b-link(:to="`/offerGroups/offer/${slot.item.config.offerId}`" data-cy="view-offer") View offer

      //- Collapse content
      MList(showBorder class="tw-mt-2 tw-mb-3")
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) #[MTooltip(content="Value in the context of this offer group.") Local] Seen by
          template(#top-right) #[meta-plural-label(:value="getOfferGroupStatisticsForOffer(slot.item).numActivatedForFirstTime", label="player")]
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) #[MTooltip(content="Value in the context of this offer group.") Local] Purchased by
          template(#top-right) #[meta-plural-label(:value="getOfferGroupStatisticsForOffer(slot.item).numPurchasedForFirstTime", label="player")]
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) #[MTooltip(content="Value in the context of this offer group.") Local] Total Seen Count
          template(#top-right) #[meta-plural-label(:value="getOfferGroupStatisticsForOffer(slot.item).numActivated", label="time")]
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) #[MTooltip(content="Value in the context of this offer group.") Local] Total Purchased Count
          template(#top-right) #[meta-plural-label(:value="getOfferGroupStatisticsForOffer(slot.item).numPurchased", label="time")]
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) #[MTooltip(content="Value in the context of this offer group.") Local] Total Revenue
          template(#top-right) ${{ getOfferGroupStatisticsForOffer(slot.item).revenue.toFixed(2) }}
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) Segments
          template(#top-right)
            div(v-if="slot.item.config.segments && slot.item.config.segments.length > 0") {{ slot.item.config.segments.length }}
            div(v-else).text-muted.font-italic None
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) Additional Conditions
          template(#top-right)
            div(v-if="slot.item.config.additionalConditions && slot.item.config.additionalConditions.length > 0") {{ slot.item.config.additionalConditions.length }}
            div(v-else).text-muted.font-italic None
        MListItem(class="tw-px-5 !tw-py-2.5" condensed) Rewards
          template(#top-right)
            div
              //pre {{ slot.item.config }}
              //- Styling problem, if too many badges it wraps to next line on the left.
              meta-reward-badge(v-for="reward in slot.item.config.rewards" :key="reward.type" :reward="reward")
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListSortDirection, MetaListSortOption, abbreviateNumber } from '@metaplay/meta-ui'
import { MCollapse, MTooltip, MBadge, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getAllOffersSubscriptionOptions } from '../../subscription_options/offers'

const props = withDefaults(defineProps<{
  /**
   * Optional: The title to display at the top of the card. Defaults to "Offers".
   */
  title?: string
  /**
   * The ID of the offer group to display offers for.
   */
  offerGroupId: string
  /**
   * The message to show when there are no offers in a group.
   */
  emptyMessage: string
}>(), {
  title: 'Offers',
})

const {
  data: offersData,
} = useSubscription(getAllOffersSubscriptionOptions())

/**
 * Get the statistics for the given offer in the context of the offer group.
 */
function getOfferGroupStatisticsForOffer (offer: any) {
  if (!props.offerGroupId) return

  const offerId = offer.config.offerId
  const offerGroup = offersData.value.offerGroups[props.offerGroupId]
  const perOfferGroupStatistics = offerGroup.perOfferStatistics[offerId]
  return perOfferGroupStatistics
}

/**
 * A computed property that returns the offers for the given offer group with conversion rate and tooltip text.
 */
const decoratedOffers = computed(() => {
  if (offersData.value) {
    const groupOffers = offersData.value.offerGroups[props.offerGroupId].config.offers
    return Object.values(offersData.value.offers || {})
      .filter((offer: any) => {
        return groupOffers === null || groupOffers.includes(offer.config.offerId)
      })
      .map((offer: any) => {
        // Decorate with conversion rate and tooltip text

        const perOfferGroupStatistics = getOfferGroupStatisticsForOffer(offer)
        const purchased = perOfferGroupStatistics.numPurchasedForFirstTime
        const activated = perOfferGroupStatistics.numActivatedForFirstTime

        return {
          ...offer,
          conversion: conversion(purchased, activated),
          conversionTooltip: conversionTooltip(purchased, activated)
        }
      })
  } else {
    return undefined
  }
})

/**
 * Returns true if the given offer is used in the offer group.
 */
function isUsed (offer: any) {
  return Object.values(offersData.value.offerGroups)
    .some((offerGroup: any) => {
      return offerGroup.config.offers.includes(offer.config.offerId)
    })
}

/**
 * Returns the conversion rate for the given offer.
 */
function conversion (purchased: any, activated: any) {
  if (activated === 0) {
    return 0
  } else {
    return purchased / activated * 100
  }
}

/**
 * Returns a tooltip for the conversion rate.
 */
function conversionTooltip (purchased: any, activated: any) {
  if (activated === 0) {
    return 'Not activated in this offer group by any players yet.'
  } else {
    return `In this offer group, activated by ${abbreviateNumber(activated)} players and purchased by ${abbreviateNumber(purchased)}.`
  }
}

// Search, sort, filter ------------------------------------------------------------------------------------------------

const sortOptions = computed(() => {
  const sortOptions = [
    MetaListSortOption.asUnsorted(),
    new MetaListSortOption('Name', 'config.displayName', MetaListSortDirection.Ascending),
    new MetaListSortOption('Name', 'config.displayName', MetaListSortDirection.Descending),
    new MetaListSortOption('Conversion', 'conversion', MetaListSortDirection.Ascending),
    new MetaListSortOption('Conversion', 'conversion', MetaListSortDirection.Descending),
    new MetaListSortOption('Revenue', 'statistics.revenue', MetaListSortDirection.Ascending),
    new MetaListSortOption('Revenue', 'statistics.revenue', MetaListSortDirection.Descending)
  ]
  return sortOptions
})

const searchFields = [
  'config.displayName',
  'config.description'
]
</script>
