<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!event || !analyticsEventBigQueryExampleData"
  :meta-api-error="analyticsEventBigQueryExampleError"
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the analytics event!

  template(#overview)
    meta-page-header-card(data-cy="overview" :id="`${event.typeCode}`")
      template(#title) {{ event.displayName }}
      template(#subtitle)  {{ event.docString || "No description provided." }}

      span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive).mt-1
        b-tbody
          b-tr
            b-td Category
            b-td.text-right {{ event.categoryName }}
          b-tr
            b-td Type
            b-td.text-right {{ event.eventType }}
          b-tr
            b-td Schema version
            b-td.text-right {{ event.schemaVersion }}
          b-tr
            b-td Parameters
            //b-td.text-right {{ event.parameters.join('\n') }}
            b-td.text-right
              div(v-for="event in eventParameters" :key="event")
                MBadge {{ event }}

  template(#center-column)
    b-card(data-cy="bq-event")
      b-card-title Example BigQuery Event

      p.text-muted.small
        | A hypothetical BigQuery row, formatted as JSON for coarse testing. All event parameters are dummy values and may not represent the true value domain.
        | All list-typed parameters are expanded as having 2 elements. All dynamically typed parameters are expanded by repeating
        | the field values for each possible type. A real event will never have more than one type.

      div.log.text-monospace.border.rounded.bg-light.w-100
        pre {{ analyticsEventBigQueryExampleData }}

  template(#default)
    meta-raw-data(:kvPair="event" name="event")
    meta-raw-data(:kvPair="analyticsEventBigQueryExampleData" name="exampleEvent")
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { routeParamToSingleValue } from '../coreUtils'
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getAllAnalyticsEventsSubscriptionOptions, getAnalyticsEventBigQueryExampleSubscriptionOptions } from '../subscription_options/analyticsEvents'
import { keyBy } from 'lodash-es'

const { data: allAnalyticsEventsData } = useSubscription(getAllAnalyticsEventsSubscriptionOptions())
const analyticsEventsByTypeCode = computed(() => keyBy(allAnalyticsEventsData.value, ev => ev.typeCode))

const route = useRoute()
const {
  data: analyticsEventBigQueryExampleData,
  error: analyticsEventBigQueryExampleError
} = useSubscription(getAnalyticsEventBigQueryExampleSubscriptionOptions(routeParamToSingleValue(route.params.id) || ''))

// TODO: Make a dedicated enpoint to get a single event.
const event = computed(() => analyticsEventsByTypeCode.value?.[routeParamToSingleValue(route.params.id)])
const eventParameters = computed<any[]>(() => event.value?.parameters || [])
</script>
