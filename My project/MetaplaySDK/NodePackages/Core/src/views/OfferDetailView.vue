<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Note: Error will not trigger toast since that requires this specific data to be fetched at an unique endpoint.
//- This is a common problem shared with SegmentDetailView, OfferGroupDetailView, ActivableDetailView
meta-page-container(
  :is-loading="!singleOfferData"
  :meta-api-error="singleOfferError"
  )
  template(#overview)
    meta-page-header-card(data-cy="overview" :id="singleOfferData.config.offerId")
      template(#title) {{ singleOfferData.config.displayName }}
      template(#subtitle) {{ singleOfferData.config.description }}

      span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td In App Product
            b-td.text-right(v-if="singleOfferData.config.inAppProduct !== null") {{ singleOfferData.config.inAppProduct }}
            b-td.text-right.text-muted.font-italic(v-else) None
          b-tr(v-if="singleOfferData.referencePrice !== null")
            b-td Reference Price
            b-td.text-right ${{ singleOfferData.referencePrice.toFixed(2) }}

      div.font-weight-bold.mb-1 #[fa-icon(icon="times")] Limits
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Max Activations
            b-td.text-right.text-muted.font-italic(v-if="singleOfferData.config.maxActivationsPerPlayer === null") Unlimited
            b-td.text-right(v-else) {{ singleOfferData.config.maxActivationsPerPlayer }}
          b-tr
            b-td Max Total Purchases
            b-td.text-right.text-muted.font-italic(v-if="singleOfferData.config.maxPurchasesPerPlayer === null") Unlimited
            b-td.text-right(v-else) {{ singleOfferData.config.maxPurchasesPerPlayer }}
          b-tr
            b-td Max Total Purchases Per Offer Group
            b-td.text-right.text-muted.font-italic(v-if="singleOfferData.config.maxPurchasesPerOfferGroup === null") Unlimited
            b-td.text-right(v-else) {{ singleOfferData.config.maxPurchasesPerOfferGroup }}
          b-tr
            b-td Max Purchases Per Activation
            b-td.text-right.text-muted.font-italic(v-if="singleOfferData.config.maxPurchasesPerActivation === null") Unlimited
            b-td.text-right(v-else) {{ singleOfferData.config.maxPurchasesPerActivation }}

      div.font-weight-bold.mb-1 #[fa-icon(icon="chart-line")] Statistics
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td #[MTooltip(content="Total value across all offer groups.") Global] Seen by
            b-td.text-right #[meta-plural-label(:value="singleOfferData.statistics.numActivatedForFirstTime", label="player")]
          b-tr
            b-td #[MTooltip(content="Total value across all offer groups.") Global] Purchased by
            b-td.text-right #[meta-plural-label(:value="singleOfferData.statistics.numPurchasedForFirstTime", label="player")]
          b-tr
            b-td #[MTooltip(content="Total value across all offer groups.") Global] Conversion
            b-td.text-right
              MTooltip(:content="conversionTooltip") {{ conversionRate.toFixed(0) }}%
          b-tr
            b-td Total #[MTooltip(content="Total value across all offer groups.") Global] Seen Count
            b-td.text-right #[meta-plural-label(:value="singleOfferData.statistics.numActivated", label="time")]
          b-tr
            b-td Total #[MTooltip(content="Total value across all offer groups.") Global] Purchased Count
            b-td.text-right #[meta-plural-label(:value="singleOfferData.statistics.numPurchased", label="time")]
          b-tr
            b-td Total #[MTooltip(content="Total value across all offer groups.") Global] Revenue
            b-td.text-right ${{ singleOfferData.statistics.revenue.toFixed(2) }}

  template(#default)
    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Configuration

    b-row.mt-3.mb-2
      b-col(md="6").mb-3
        meta-list-card(
          data-cy="offers"
          title="Contents"
          :itemList="rewards"
          listLayout="flex"
        )
          template(v-slot:item-card="slot")
            meta-reward-badge(:reward="slot.item")

      b-col(md="6").mb-3
        meta-list-card(
          data-cy="references"
          title="Referenced by"
          icon="exchange-alt"
          :itemList="referenceList"
          :filterSets="filterSets"
          :sortOptions="referencesSortOptions"
          :searchFields="['displayName', 'type']"
          emptyMessage="No offer groups or other offers reference this offer."
        )
          template(#item-card="slot")
            MListItem
              MBadge.mr-1
                template(#icon)
                  fa-icon(:icon="slot.item.icon")
                | {{ slot.item.type }}
              span.font-weight-bold {{ slot.item.displayName }}

              template(#top-right v-if="slot.item.linkUrl")
                b-link(:to="slot.item.linkUrl") View {{ `${slot.item.linkText.toLocaleLowerCase()}` }}

    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Targeting

    b-row(align-h="center")
      b-col(md="6").mb-3
        segments-card(:segments="singleOfferData.config.segments" data-cy="segments" ownerTitle="This event")
      b-col(md="6").mb-3
        player-conditions-card(:playerConditions="singleOfferData.config.additionalConditions" data-cy="conditions")

    meta-raw-data(:kvPair="singleOfferData" name="offer")
</template>

<script lang="ts" setup>
import { cloneDeep } from 'lodash-es'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption, abbreviateNumber, rewardsWithMetaData } from '@metaplay/meta-ui'
import { MTooltip, MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleOfferSubscriptionOptions } from '../subscription_options/offers'
import { routeParamToSingleValue } from '../coreUtils'

import PlayerConditionsCard from '../components/global/PlayerConditionsCard.vue'
import SegmentsCard from '../components/global/SegmentsCard.vue'

const route = useRoute()
const offerId = routeParamToSingleValue(route.params.id)

// Offer data ----------------------------------------------------------------------------------------------------

const {
  data: singleOfferData,
  error: singleOfferError
} = useSubscription(getSingleOfferSubscriptionOptions(offerId))

/**
 * Conversion rate for the offer.
 */
const conversionRate = computed(() => {
  const activated = singleOfferData.value.statistics.numActivatedForFirstTime
  const purchased = singleOfferData.value.statistics.numPurchasedForFirstTime
  if (activated === 0) {
    return 0
  } else {
    return purchased / activated * 100
  }
})

/**
 * Tooltip for the conversion rate.
 */
const conversionTooltip = computed(() => {
  const activated = singleOfferData.value.statistics.numActivatedForFirstTime
  const purchased = singleOfferData.value.statistics.numPurchasedForFirstTime
  if (activated === 0) {
    return 'Not activated by any players yet.'
  } else {
    return `Activated by ${abbreviateNumber(activated)} players and purchased by ${abbreviateNumber(purchased)}.`
  }
})

/**
 * Rewards with metadata.
 */
const rewards = computed(() => {
  return rewardsWithMetaData(singleOfferData.value.config.rewards || [])
})

/**
 * List of places where this offer is referenced.
 */
const referenceList = computed(() => {
  return singleOfferData.value.usedBy.map((referenceSource: any) => {
    const reference = cloneDeep(referenceSource)
    switch (reference.type) {
      case 'OfferGroup':
        reference.icon = 'tags'
        reference.linkUrl = `/offerGroups/offerGroup/${reference.id}`
        reference.linkText = 'Offer Group'
        break

      case 'Offer':
        reference.icon = 'tags'
        reference.linkUrl = `/offerGroups/offer/${reference.id}`
        reference.linkText = 'Offer'
        break
    }

    return reference
  })
})

// Search, sort, filter ------------------------------------------------------------------------------------------------

const referencesSortOptions = [
  new MetaListSortOption('Type', 'type', MetaListSortDirection.Ascending),
  new MetaListSortOption('Type', 'type', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Descending),
]

const filterSets = computed(() => {
  return [
    new MetaListFilterSet('type',
      [
        new MetaListFilterOption('Offer Groups', (x: any) => x.type === 'OfferGroup'),
        new MetaListFilterOption('Offers', (x: any) => x.type === 'Offer')
      ].sort((a, b) => {
        const nameA = a.displayName.toUpperCase()
        const nameB = b.displayName.toUpperCase()
        if (nameA < nameB) {
          return -1
        } else if (nameA > nameB) {
          return 1
        } else {
          return 0
        }
      })
    )
  ]
})
</script>
