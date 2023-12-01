<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Note: error handling is completely missing from this page, but this requires a potential bigger refactor.
//- Adding the error handling now might be wasted work if this shares more similarities with other pages.
meta-page-container(fluid)
  template(#overview)
    meta-page-header-card(data-cy="overview" :id="entityId")
      template(#title) {{ entityType }} Event Timeline
      template(#subtitle)
        div.mb-1 A timeline of most recent events for #[MBadge {{ entityId }}].
        div.text-muted.small The amount of cached events on this page is limited to keep the database impact small. The full history of events has been sent to your analytics pipeline!

      div(v-if="eventStreamStats")
        span.font-weight-bold #[fa-icon(icon="chart-line")] Statistics
        b-table-simple(small responsive).mt-1
          b-tbody
            b-tr
              b-td Total events
              b-td.text-right {{ eventStreamStats.numEvents }}
            b-tr(v-if="eventStreamStats.newestEventTime")
              b-td Most recent event
              b-td.text-right #[meta-time(:date="eventStreamStats.newestEventTime" showAs="timeagoSentenceCase")]
            b-tr(v-if="eventStreamStats.oldestEventTime")
              b-td Oldest event
              b-td.text-right #[meta-time(:date="eventStreamStats.oldestEventTime" showAs="timeagoSentenceCase")]
            b-tr(v-if="eventStreamStats.duration")
              b-td Range of events
              b-td.text-right #[meta-duration(:duration="eventStreamStats.duration" showAs="humanizedSentenceCase")]
      div(v-else)

  b-container(fluid)
    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Events
    b-row
      b-col(lg).mb-3
        entity-event-log-card.h-100(
          :entityKind="entityType"
          :entityId="entityId"
          :initialSearchString="String(initialSearchString)"
          :initialKeywordFilters="initialKeywordFilters"
          :initialEventTypeFilters="initialEventTypeFilters"
          :searchIsFilter="false"
          @stats="onStats")
</template>

<script lang="ts" setup>
import EntityEventLogCard from '../components/entityeventlogs/EntityEventLogCard.vue'
import type { EventStreamStats } from '@metaplay/event-stream'
import { useRoute } from 'vue-router'
import { computed, ref } from 'vue'
import { routeParamToSingleValue } from '../coreUtils'
import { MBadge } from '@metaplay/meta-ui-next'

const route = useRoute()
const eventStreamStats = ref<EventStreamStats | null>(null)

const entityType = computed(() => routeParamToSingleValue(route.params.type))
const entityId = computed(() => routeParamToSingleValue(route.params.id))
const initialSearchString = computed(() => route.query?.search ?? '')
const initialKeywordFilters = computed((): string[] => {
  if (route.query?.keywords) {
    const query = route.query.keywords
    if (typeof query === 'string') {
      return decodeURIComponent(query).split(',')
    } else if (Array.isArray(query)) {
      return query.map(query => decodeURIComponent(String(query)))
    }
  }

  return []
})
const initialEventTypeFilters = computed((): string[] => {
  if (route.query?.eventTypes) {
    const query = route.query.eventTypes
    if (typeof query === 'string') {
      return decodeURIComponent(query).split(',')
    } else if (Array.isArray(query)) {
      return query.map(query => decodeURIComponent(String(query)))
    }
  }

  return []
})

function onStats (stats: EventStreamStats) {
  eventStreamStats.value = stats
}
</script>
