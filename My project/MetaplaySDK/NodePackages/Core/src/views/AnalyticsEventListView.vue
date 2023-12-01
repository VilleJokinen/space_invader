<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allAnalyticsEventsData"
  :meta-api-error="allAnalyticsEventsError"
  )
  template(#overview)
    meta-page-header-card
      template(#title) View Analytics Event Types

      p These are all the currently implemented server-side analytics events.
      div.small.text-muted You can use this page to explore and inspect the individual analytics events. This might be especially useful for data analysts who need to know the exact contents and formatting of events.

  template(#default)
    b-row
      b-col(md="6")
        meta-list-card(
          data-cy="core-events"
          title="Core Event Types"
          icon="list"
          :itemList="coreEvents"
          :searchFields="searchFields"
          :filterSets="filterSets"
          :sortOptions="sortOptions"
          :pageSize="20"
        )
          template(v-slot:item-card="slot")
            analytics-type-list-group-item(:event="slot.item")

      b-col(md="6")
        meta-list-card(
          data-cy="custom-events"
          title="Custom Event Types"
          icon="list"
          :itemList="customEvents"
          :searchFields="searchFields"
          :filterSets="filterSets"
          :sortOptions="sortOptions"
          emptyMessage="No custom analytics events have been registered in the game."
          :pageSize="20"
        )
          template(v-slot:item-card="slot")
            analytics-type-list-group-item(:event="slot.item")

    meta-raw-data(:kvPair="allAnalyticsEventsData" name="analyticsEvents")
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'

import AnalyticsTypeListGroupItem from '../components/analytics/AnalyticsTypeListGroupItem.vue'
import { getAllAnalyticsEventsSubscriptionOptions } from '../subscription_options/analyticsEvents'

/**
 * Subscribe to events data.
 */
const {
  data: allAnalyticsEventsData,
  error: allAnalyticsEventsError
} = useSubscription(getAllAnalyticsEventsSubscriptionOptions())

/**
 * Sort options for the card.
 */
const sortOptions = [
  new MetaListSortOption('Category', 'categoryName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Category', 'categoryName', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Descending),
]

/**
 * Search fields for the card.
 */
const searchFields = [
  'categoryName',
  'displayName',
  'docString',
  'typeCode',
  'eventType'
]

/**
 * Filters for the card.
 */
const filterSets = computed(() => {
  return [
    MetaListFilterSet.asDynamicFilterSet(allAnalyticsEventsData.value, 'category', (eventSpec: any) => eventSpec.categoryName)
  ]
})

/**
 * Core events are events that are defined internally by Metaplay. Any other event is considered game-specific,
 * aka "custom".
 */
function isCoreEvent (event: any) {
  return event.type.startsWith('Metaplay.')
}

/**
 * All core (ie: internal) events.
 */
const coreEvents = computed(() => {
  return Object.values(allAnalyticsEventsData.value).filter((e: any) => isCoreEvent(e)) as any[]
})

/**
 * All custom (ie: game-specific) events.
 */
const customEvents = computed(() => {
  return Object.values(allAnalyticsEventsData.value).filter((e: any) => !isCoreEvent(e)) as any[]
})
</script>
