<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- The meta page container below is not doing any is loading or error handling.
meta-page-container
  template(#overview)
    meta-page-header-card
      template(#title) Search Audit Log Events

      b-row
        b-col(md).mb-3
          h6 By Event
          b-form-group.mb-2
            label(for="redirectHost") Event Type
            b-form-select(v-model="searchAuditLogEventsTargetType" :options="targetTypes" :state="searchAuditLogEventTypeUiState")
          b-form-group.mb-2
            label(for="redirectHost") Event ID
            b-input-group
              b-form-input(v-model="searchAuditLogEventsTargetId" :disabled="!searchAuditLogEventsTargetType" type="text" :state="searchAuditLogEventIdUiState")
              b-button(size="sm" variant="outline-secondary" :disabled="!searchAuditLogEventsTargetId" @click="clearSearchAuditLogEventsTargetId()"  style="border-radius: 0 3px 3px 0; background: gray; color: white"): fa-icon(icon="times")
            small(v-if="!searchAuditLogEventsTargetType").text-muted Can't search for IDs without a type selected.

        b-col(md)
          h6 By User
          b-form-group.mb-2
            label(for="redirectHost") User Account
            b-input-group
              b-form-input(v-model="searchAuditLogEventsSourceId" type="text" :state="searchAuditLogEventUserAccountUiState" placeholder='username@domain')
              b-button(size="sm" variant="outline-secondary" :disabled="!searchAuditLogEventsSourceId" @click="clearSearchAuditLogEventsSourceId()" style="border-radius: 0 3px 3px 0; background: gray; color: white"): fa-icon(icon="times")
          b-form-group.mb-2
            label(for="redirectHost") User IP Address
            b-input-group
              b-form-input(v-model="searchAuditLogEventsSourceIpAddress" type="text" :state="searchAuditLogEventUserIpAddressUiState" placeholder='192.168.0.1')
              b-button(size="sm" variant="outline-secondary" :disabled="!searchAuditLogEventsSourceIpAddress" @click="clearSearchAuditLogEventsSourceIpAddress()" style="border-radius: 0 3px 3px 0; background: gray; color: white"): fa-icon(icon="times")
          b-form-group.mb-2
            label(for="redirectHost") User Country ISO Code
            b-input-group
              b-form-input(v-model="searchAuditLogEventsSourceCountryIsoCode" type="text" :state="searchAuditLogEventUserCountryIsoCodeUiState" placeholder='FI')
              b-button(size="sm" variant="outline-secondary" :disabled="!searchAuditLogEventsSourceCountryIsoCode" @click="clearSearchAuditLogEventsSourceCountryIsoCode()" style="border-radius: 0 3px 3px 0; background: gray; color: white"): fa-icon(icon="times")

  template(#default)
    b-row
      b-col.mb-3
        b-card(title="Search Results" :class="!showSearchResults ? 'bg-light' : ''").shadow-sm
          b-row(no-gutters v-if="showSearchResults && searchEvents.length > 0")
            b-table(small striped hover responsive :items="searchEvents" :fields="tableFields" primary-key="eventId" sort-by="startAt" sort-desc @row-clicked="clickOnEventRow" tbody-tr-class="table-row-link")
              template(#cell(target)="data")
                span.text-nowrap {{ data.item.target.targetType.replace(/\$/, '') }}:{{ data.item.target.targetId }}

              template(#cell(displayTitle)="data")
                MTooltip(:content="data.item.displayDescription" noUnderline).text-nowrap {{ data.item.displayTitle }}

              template(#cell(source)="data")
                span.text-nowrap #[meta-username(:username="data.item.source.sourceId")]

              template(#cell(createdAt)="data")
                meta-time(:date="data.item.createdAt").text-nowrap

            div.mt-2.w-100.text-center
              b-button.m-2(v-if="searchEventsHasMore" variant="light" size="sm" @click="showMoreSearch" align-h="center") Show More

          b-row(no-gutters align-h="center" v-else).mt-4.mb-3
            p.m-0.text-muted No search results. Select filters to search for audit logs!

    b-row
      b-col.mb-3
        b-card(title="Latest Audit Log Events").shadow-sm
          b-table.table-fixed-column(small striped hover responsive :items="latestEvents" :fields="tableFields" primary-key="eventId" sort-by="startAt" sort-desc @row-clicked="clickOnEventRow" tbody-tr-class="table-row-link")
            template(#cell(target)="data")
              span.text-nowrap {{ data.item.target.targetType.replace(/\$/, '') }}:{{ data.item.target.targetId }}

            template(#cell(displayTitle)="data")
              MTooltip(:content="data.item.displayDescription" noUnderline).text-nowrap {{ data.item.displayTitle }}

            template(#cell(source)="data")
              span.text-nowrap #[meta-username(:username="data.item.source.sourceId")]

            template(#cell(createdAt)="data")
              meta-time(:date="data.item.createdAt").text-nowrap

          div.mt-2.w-100.text-center
            b-button.m-2(v-if="latestEventsHasMore" variant="light" size="sm" @click="showMoreLatest" align-h="center") Load More

    meta-raw-data(:kvPair="searchEvents" name="search results")
</template>

<script lang="ts" setup>
import { BFormSelect } from 'bootstrap-vue'
import { computed, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { isoCodeToCountryName } from '@metaplay/meta-ui'
import { MTooltip } from '@metaplay/meta-ui-next'
import { useManuallyManagedStaticSubscription } from '@metaplay/subscriptions'

import { getAllAuditLogEventsSubscriptionOptions, getAuditLogEventsSearchSubscriptionOptions } from '../subscription_options/auditLogs'
import { useCoreStore } from '../coreStore'
import { extractSingleValueFromQueryStringOrDefault } from '../coreUtils'

const coreStore = useCoreStore()
const route = useRoute()
const router = useRouter()

const pageSize = 10

// The "search" results list ------------------------------------------------------------------------------------------

const searchAuditLogEventsTargetType = ref(extractSingleValueFromQueryStringOrDefault(route.query, 'targetType', ''))
const searchAuditLogEventsTargetId = ref(extractSingleValueFromQueryStringOrDefault(route.query, 'targetId', ''))
const searchAuditLogEventsSourceId = ref(extractSingleValueFromQueryStringOrDefault(route.query, 'sourceId', ''))
const searchAuditLogEventsSourceIpAddress = ref(extractSingleValueFromQueryStringOrDefault(route.query, 'sourceIpAddress', ''))
const searchAuditLogEventsSourceCountryIsoCode = ref(extractSingleValueFromQueryStringOrDefault(route.query, 'sourceCountryIsoCode', ''))

const targetTypes = computed(() => {
  const targetTypes = [
    {
      value: null,
      text: 'Select an event type'
    },
    {
      value: 'Player',
      text: 'Player'
    },
    {
      value: '$GameServer',
      text: 'GameServer'
    },
    {
      value: '$GameConfig',
      text: 'GameConfig'
    },
    {
      value: '$Broadcast',
      text: 'Broadcast'
    },
    {
      value: '$Notification',
      text: 'Notification'
    },
    {
      value: '$Experiment',
      text: 'Experiment'
    },
    {
      value: 'AsyncMatchmaker',
      text: 'AsyncMatchmaker'
    },
  ]

  // Add types for features behind feature flags
  if (coreStore.hello.featureFlags.guilds) {
    targetTypes.push({
      value: 'Guild',
      text: 'Guild'
    })
  }
  if (coreStore.hello.featureFlags.web3) {
    targetTypes.push({
      value: '$Nft',
      text: 'NFT'
    },
    {
      value: '$NftCollection',
      text: 'NFT Collection'
    })
  }
  if (coreStore.hello.featureFlags.playerLeagues) {
    targetTypes.push({
      value: 'LeagueManager',
      text: 'League'
    })
  }

  return targetTypes
})

/**
 * Subscription details for search events.
 */
const searchEventsSubscription = ref<any>()

/**
 * Current fetch size for search events subscription.
 */
const searchEventsLimit = ref(pageSize)

/**
 * Is there a search in progress?
 */
const showSearchResults = computed(() => {
  return searchAuditLogEventTypeUiState.value ??
    searchAuditLogEventIdUiState.value ??
    searchAuditLogEventUserAccountUiState.value ??
    searchAuditLogEventUserIpAddressUiState.value ??
    searchAuditLogEventUserCountryIsoCodeUiState.value
})

/**
 * Search events.
 */
const searchEvents = computed((): any[] => {
  return searchEventsSubscription.value?.data?.entries || []
})

/**
 * Are there more search events to fetch?
 */
const searchEventsHasMore = computed(() => {
  return !!searchEventsSubscription.value?.data?.hasMore
})

/**
 * Increase fetch size.
 */
function showMoreSearch () {
  searchEventsLimit.value += pageSize
  subscribeShowSearch()
}

/**
 * Set up new subscription for search events.
 */
function subscribeShowSearch () {
  if (searchEventsSubscription.value) {
    searchEventsSubscription.value.unsubscribe()
  }
  searchEventsSubscription.value = useManuallyManagedStaticSubscription(getAuditLogEventsSearchSubscriptionOptions(
    searchAuditLogEventsTargetType.value,
    searchAuditLogEventsTargetId.value,
    searchAuditLogEventsSourceId.value ? '$AdminApi:' + searchAuditLogEventsSourceId.value : '',
    searchAuditLogEventsSourceIpAddress.value,
    searchAuditLogEventsSourceCountryIsoCode.value,
    searchEventsLimit.value
  ))
}

/**
 * Kick off the initial subscription.
 */
subscribeShowSearch()

/**
 * Remember to unsubscribe when page unmounts.
 */
onUnmounted(() => {
  searchEventsSubscription.value.unsubscribe()
})

// If any search parameter updates...
watch([searchAuditLogEventsTargetType, searchAuditLogEventsTargetId, searchAuditLogEventsSourceId, searchAuditLogEventsSourceIpAddress, searchAuditLogEventsSourceCountryIsoCode, searchEventsLimit], async () => {
  // Update the URL with the new search parameters.
  const params: {[key: string]: string} = {}

  if (searchAuditLogEventsTargetType.value) {
    params.targetType = searchAuditLogEventsTargetType.value
    if (searchAuditLogEventsTargetId.value) {
      params.targetId = searchAuditLogEventsTargetId.value
    }
  }
  if (searchAuditLogEventsSourceId.value) {
    params.sourceId = searchAuditLogEventsSourceId.value
  }
  if (searchAuditLogEventsSourceIpAddress.value) {
    params.sourceIpAddress = searchAuditLogEventsSourceIpAddress.value
  }
  if (upperCasedSearchAuditLogEventsSourceCountryIsoCode.value) {
    params.sourceCountryIsoCode = upperCasedSearchAuditLogEventsSourceCountryIsoCode.value
  }

  // Touching the router will immediately re-initialize the whole page and thus reload the subscription.
  await router.replace({ path: '/auditLogs', query: params })
})

const upperCasedSearchAuditLogEventsSourceCountryIsoCode = computed(() => {
  return searchAuditLogEventsSourceCountryIsoCode.value ? searchAuditLogEventsSourceCountryIsoCode.value.toUpperCase() : null
})

/* UI state for the Event Type form component. */
const searchAuditLogEventTypeUiState = computed(() => {
  return searchAuditLogEventsTargetType.value ? true : null
})

/* UI state for the Event Id form component. */
const searchAuditLogEventIdUiState = computed(() => {
  return searchAuditLogEventsTargetId.value ? true : null
})

/* UI state for the User Account form component. */
const searchAuditLogEventUserAccountUiState = computed(() => {
  return searchAuditLogEventsSourceId.value ? true : null
})

/* UI state for the User IP Address form component. */
const searchAuditLogEventUserIpAddressUiState = computed(() => {
  return searchAuditLogEventsSourceIpAddress.value ? true : null
})

/* UI state for the User Country ISO Code form component. */
const searchAuditLogEventUserCountryIsoCodeUiState = computed(() => {
  const isoCode = upperCasedSearchAuditLogEventsSourceCountryIsoCode.value
  if (isoCode) {
    return isoCodeToCountryName(isoCode) !== isoCode
  } else {
    return null
  }
})

async function clearSearchAuditLogEventsTargetId () {
  searchAuditLogEventsTargetId.value = ''
}

async function clearSearchAuditLogEventsSourceId () {
  searchAuditLogEventsSourceId.value = ''
}

async function clearSearchAuditLogEventsSourceIpAddress () {
  searchAuditLogEventsSourceIpAddress.value = ''
}

async function clearSearchAuditLogEventsSourceCountryIsoCode () {
  searchAuditLogEventsSourceCountryIsoCode.value = ''
}

// The "latest" events list -------------------------------------------------------------------------------------------

/**
 * Subscription details for latest events.
 */
const latestEventsSubscription = ref<any>()

/**
 * Current fetch size for latest events subscription.
 */
const latestEventsLimit = ref(pageSize)

/**
 * Latest events.
 */
const latestEvents = computed((): any[] => {
  return latestEventsSubscription.value?.data?.entries || []
})

/**
 * Are there more latest events to fetch?
 */
const latestEventsHasMore = computed(() => {
  return !!latestEventsSubscription.value?.data?.hasMore
})

/**
 * Increase fetch size.
 */
function showMoreLatest () {
  latestEventsLimit.value += pageSize
  subscribeShowLatest()
}

/**
 * Set up new subscription for latest events.
 */
function subscribeShowLatest () {
  if (latestEventsSubscription.value) {
    latestEventsSubscription.value.unsubscribe()
  }
  latestEventsSubscription.value = useManuallyManagedStaticSubscription(getAllAuditLogEventsSubscriptionOptions(
    '',
    '',
    latestEventsLimit.value
  ))
}

/**
 * Kick off the initial subscription.
 */
subscribeShowLatest()

/**
 * Remember to unsubscribe when page unmounts.
 */
onUnmounted(() => {
  latestEventsSubscription.value.unsubscribe()
})

// Other --------------------------------------------------------------------------------------------------------------

const tableFields = [
  {
    key: 'target',
    label: 'Event'
  },
  {
    key: 'displayTitle',
    label: 'Title'
  },
  {
    key: 'source',
    label: 'User'
  },
  {
    key: 'createdAt',
    label: 'Date'
  }
]

async function clickOnEventRow (item: any) {
  await router.push(`/auditLogs/${item.eventId}`)
}
</script>
