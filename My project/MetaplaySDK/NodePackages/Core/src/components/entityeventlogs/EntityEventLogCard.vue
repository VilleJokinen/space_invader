<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- Display event logs for a given entity. -->

<template lang="pug">
meta-event-stream-card(
  data-cy="entity-event-log-card"
  :title="`Latest ${entityKind} Events`",
  icon="clipboard-list",
  :tooltip="`Shows events that the ${entityKind.toLowerCase()} has performed.`",
  :eventStream="loaded ? eventStream : null",
  :searchIsFilter="searchIsFilter"
  :initialSearchString="initialSearchString"
  :initialKeywordFilters="initialKeywordFilters"
  :initialEventTypeFilters="initialEventTypeFilters"
  :freezeSearch="freezeSearch"
  :freezeKeywordFilters="freezeKeywordFilters"
  :freezeEventTypeFilters="freezeEventTypeFilters"
  :maxHeight="maxHeight"
  :permission="requiredPermissionToGetEvents"
  :showTimeDeltas="folding !== 'day'"
  showViewMoreLink
  allow-pausing
)
</template>

<script lang="ts" setup>
import { keyBy } from 'lodash-es'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

import { EventStreamItemEvent, generateStats, MetaEventStreamCard, wrapDays, wrapRepeatingEvents, wrapSessions } from '@metaplay/event-stream'

import { ApiPoller, useGameServerApiStore } from '@metaplay/game-server-api'
import { useSubscription } from '@metaplay/subscriptions'
import { getAllAnalyticsEventsSubscriptionOptions } from '../../subscription_options/analyticsEvents'

// Props ------------------------

const props = withDefaults(defineProps<{
  /**
   * Kind of entity that we are interested in.
   */
  entityKind: string
  /**
   * Id of the entity that we are interested in or a function that retrieves the needed Id.
   */
  entityId: string | (() => string)
  /**
   * Optional: A timestamp of the last full entity reset (caused by entity reset or overwrite). The purpose of this is
   * to invalidate the ongoing event log scan when an entity gets reset and the existing cached log segments are known
   * to no longer be valid.
   */
  entityResetTimestamp?: string | null
  /**
    * Optional: If true then search filters the list to contain only the matching events. If false then the list
    * contains all events but the ones that matched the search are highlighted.
    */
  searchIsFilter?: boolean
  /**
   * Optional: Pre-fill the search with this string.
  */
  initialSearchString?: string
  /**
   * Optional: Prevent the user from changing the search string.
   */
  freezeSearch?: boolean
  /**
   * Optional: Pre-select these event keywords.
   */
  initialKeywordFilters?: string[]
  /**
   * Optional: Prevent the user from changing the keyword filters.
   */
  freezeKeywordFilters?: boolean
  /**
   * Optional: Pre-select these event types.
   */
  initialEventTypeFilters?: string[]
  /**
   * Optional: Prevent the user from changing the event type filters.
   */
  freezeEventTypeFilters?: boolean
  /**
   * Optional: Limit height of the card.
   */
  maxHeight?: string
  /**
   * Optional: Number of events to fetch at a time.
   */
  fetchPageSize?: number
  /**
   * Optional: Fold events into 'session's or 'day's.
   */
  folding?: string
}>(), {
  entityResetTimestamp: null,
  searchIsFilter: true,
  initialSearchString: '',
  initialEventTypeFilters: () => [],
  initialKeywordFilters: () => [],
  maxHeight: '30rem',
  fetchPageSize: 1000,
  folding: 'session',
})

// UI & misc ------------------------

const emit = defineEmits(['stats'])

/**
 * Subscribe to data needed to render this component.
 */
const { data: analyticsEvents } = useSubscription(getAllAnalyticsEventsSubscriptionOptions())
const analyticsEventsByTypeName = computed(() => keyBy(analyticsEvents.value, ev => ev.type.split(',')[0]))

const loaded = computed(() => {
  return !loading.value && (!hasPermissionToGetEventNames.value || analyticsEventsByTypeName.value != null)
})

// UI & Event fetching ------------------------

const latestEventsPoller = ref<ApiPoller | null>(null)

/**
 * Start fetching data when the page loads.
 */
onMounted(() => {
  if (hasPermissionToGetEvents.value) {
    startFetchingData()
  } else {
    loading.value = false
  }
})

/**
 * We need to manually cancel the poller when the page unloads.
 */
onUnmounted(() => {
  if (latestEventsPoller.value) {
    latestEventsPoller.value.stop()
  }
})

/**
 * Id of the entity that is to be displayed.
 * Note: Either the Id is passed in as a string or as a function that retrieves the entity Id.
 */
const entityId = computed(() => {
  let computedEntityId = ''
  if (typeof props.entityId === 'string') {
    computedEntityId = props.entityId
  } else {
    computedEntityId = props.entityId()
  }
  if (!computedEntityId) {
    throw new Error('Entity Id cannot be empty or undefined.')
  }
  return computedEntityId
})

/**
 * Re-initialize if the entity changes.
 */
watch([() => entityId.value, () => props.entityResetTimestamp],
  () => {
    startFetchingData()
  }
)

/**
 * Calculates the endpoint that we will fetch the data from.
 */
const apiEndpoint = computed(() => {
  if (props.entityKind === 'Player') return `/players/${entityId.value}/eventLog`
  else if (props.entityKind === 'Guild') return `/guilds/${entityId.value}/eventLog`
  else throw new Error('Invalid entityKind for EntityEventLogCard: ' + props.entityKind)
})

const loading = ref(true)
const rawEvents = ref<any[]>([])
const eventScanCursor = ref('')

/**
 * Start fetching data from the API. Removes and reset and existing data.
 */
function startFetchingData () {
  // Reset data.
  rawEvents.value = []
  loading.value = true
  if (latestEventsPoller.value) {
    latestEventsPoller.value.stop()
    latestEventsPoller.value = null
  }

  // Start fetching.
  eventScanCursor.value = '0_0' // Start scanning from the very beginning. 0_0 means segmentId=0, entryIndexWithinSegment=0.
  latestEventsPoller.value = new ApiPoller(
    () => loading.value ? 100 : 10000, // Fast-load the initial data set, then trickle in any updates.
    'GET', apiEndpoint.value,
    () => {
      return {
        startCursor: eventScanCursor.value,
        numEntries: props.fetchPageSize
      }
    },
    (data: any) => {
      if (data.entries.length > 0) {
        // Warn if there's a gap in the entries' sequentialIds.
        // This can happen in rare cases when an old segment was just deleted.
        if (rawEvents.value.length > 0) {
          const lastOldId = rawEvents.value[rawEvents.value.length - 1].sequentialId
          const firstNewId = data.entries[0].sequentialId
          if (firstNewId !== lastOldId + 1) {
            console.warn(`Gap in event log: entry ${firstNewId} follows ${lastOldId}`)
          }
        }

        rawEvents.value = rawEvents.value.concat(data.entries)
        eventScanCursor.value = data.continuationCursor
      }
      if (data.entries.length < props.fetchPageSize) {
        // First incomplete page fetched means that we are at the head of the stream.
        loading.value = false
      }
    }
  )
}

/**
 * Event stream data, generated from the fetched data.
 */
const eventStream = computed(() => {
  // Create an event stream.
  if (rawEvents.value) {
    let eventStream = rawEvents.value.map((event) => {
      return new EventStreamItemEvent(
        event.collectedAt,
        analyticsEventsByTypeName.value?.[event.payload.$type]?.displayName || event.payload.$type,
        event.payload.eventDescription,
        event.uniqueId,
        event,
        '',
        'timeline', `/entityEventLog/${props.entityKind}/${entityId.value}?search=${event.uniqueId}`
      )
    })

    // Fold what we can.
    if (props.folding === 'session') {
      eventStream = wrapSessions(eventStream)
    } else if (props.folding === 'day') {
      eventStream = wrapDays(eventStream)
    }
    eventStream = wrapRepeatingEvents(eventStream)

    // Blast out some stats data.
    emit('stats', generateStats(eventStream))

    return eventStream
  } else {
    return null
  }
})

// Permissions ------------------------

const gameServerApiStore = useGameServerApiStore()

const hasPermissionToGetEventNames = computed(() => {
  return gameServerApiStore.doesHavePermission('api.analytics_events.view')
})

const hasPermissionToGetEvents = computed(() => {
  return gameServerApiStore.doesHavePermission(requiredPermissionToGetEvents.value)
})

const requiredPermissionToGetEvents = computed((): string => {
  if (props.entityKind === 'Player') return 'api.players.view'
  else if (props.entityKind === 'Guild') return 'api.guilds.view'
  else throw new Error('Invalid entityKind for EntityEventLogCard: ' + props.entityKind)
})

</script>
