<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-card.shadow-sm(:class="{ 'bg-light': !hasContent || !hasPermission }" style="min-height: 12rem" no-body)
  //- Header
  b-row(align-h="between" align-v="center" no-gutters @click="utilsOpen = !utilsOpen").card-manual-title-padding.tw-cursor-pointer
    b-card-title.d-flex.align-items-center
      MTooltip(:content="tooltip" noUnderline)
        fa-icon(v-if="icon" :icon="icon").mr-2
        | {{ title }}
      //- MBadge styling TODO: "1/701" type of pill number looks off.
      MBadge(v-if="eventList && hasPermission" shape="pill" :variant="badgeVariant" data-cy="badge-text").ml-2 {{ badgeText }}
    span(style="margin-top: -1rem")
      small(v-if="anySearchActive && searchIsFilter").text-muted.font-italic.mr-2 filter active
      small(v-if="anySearchActive && !searchIsFilter").text-muted.font-italic.mr-2 search active
      b-button(:disabled="!hasContent" variant="outline-secondary" :class="{ 'text-primary': anySearchActive }").p-1.subtle-button
        fa-icon(icon="angle-right" size="sm").mr-1
        fa-icon(icon="search" size="sm")

  //- Loading
  //- Permission handling
  div(v-if="!hasPermission").text-center.mb-4.pt-4.small.text-muted You need the #[MBadge {{ permission }}] permission to view this card.

  div(v-else-if="!hasContent").w-100.card-manual-content-padding.pt-2
    b-skeleton(width="85%")
    b-skeleton(width="55%")
    b-skeleton(width="70%")

    b-skeleton(width="80%").mt-3
    b-skeleton(width="65%")

  //- Utilities menu
  MTransitionCollapse
    div(v-if="eventList.length && utilsOpen" :id="title")
      div.bg-light.w-100.card-manual-content-padding.border-top.border-bottom
        div.mt-3.mb-3
          div.mb-2
              div.small.font-weight-bold Search
              div.mt-1
                b-input-group
                  b-form-input(
                    size="sm"
                    v-model="userSearchString"
                    :state="userSearchActive ? true : null"
                    placeholder="Type your search here..."
                    debounce="500"
                    :disabled="freezeSearch"
                    )
                  b-button(size="sm" variant="outline-secondary" :disabled="!userSearchActive" @click="clearUserSearchString()" style="border-radius: 0 3px 3px 0; background: gray; color: white"): fa-icon(icon="times")
          div
            div.small.font-weight-bold Event types
            b-form-checkbox-group(
              v-if="filters.eventTypes.length > 0"
              v-model="selectedEventTypes"
              name="event type filters")
              b-form-checkbox(
                v-for="eventType in filters.eventTypes"
                :key="eventType"
                :value="eventType"
                :disabled="freezeEventTypeFilters"
                )
                span.small {{ eventType }} ({{ countTypeMatches(eventType) }})
            span(v-else).text-muted.small.font-italic None available for this event stream.

          div.mt-1
            div.small.font-weight-bold Keywords
            b-form-checkbox-group(
              v-if="filters.eventKeywords.length > 0"
              v-model="selectedKeywords"
              name="keyword filters")
              b-form-checkbox(
                v-for="eventKeywords in filters.eventKeywords"
                :key="eventKeywords"
                :value="eventKeywords"
                :disabled="freezeKeywordFilters"
                )
                span.small {{ eventKeywords }} ({{ countKeywordMatches(eventKeywords) }})
            span(v-else).text-muted.small.font-italic None available for this event stream.

          div.font-italics.text-muted.small.mt-1(v-if="anySearchActive") {{ uiFilterHintLabel }}

  //- Main body
  div(v-if="hasPermission && eventList.length" :style="`max-height: ${maxHeight}; overflow-y: auto;`").d-flex.flex-column.justify-content-between.h-100
    //- Sticks to the top
    div
      //- Pause stream
      div(v-if="allowPausing").text-center.text-muted.small.pb-2
        span(v-show="!isPaused") Last update #[meta-time(:date="lastUpdated")].
        span(v-show="isPaused") Updates paused #[meta-time(:date="lastUpdated")].
        meta-button(link @click="togglePlayPauseState" data-cy="play-pause-button").ml-1 {{ isPaused ? 'Resume' : 'Pause' }} updates
        div
          MBadge(v-if="updateAvailable" shape="pill" variant="primary") New data available

      //- List of events
      div(v-if="decoratedSearchedAndUnfoldedEventList.length > 0").group-element-borders.group-element-stripes
        //- Render actual events
        meta-lazy-loader(v-for="(event, index) in decoratedSearchedAndUnfoldedEventList" :key="index" :class="event.highlight && !searchIsFilter ? 'search-highlight' : ''" :placeholderEventHeight="50").group

          //- Session header row
          div(v-if="event.type === 'Session'" @click="toggleFoldedPath(event.path)" :class="{ 'group-open': event.decorations.isPathUnfolded }").py-2.card-manual-content-padding.d-flex.clickable-list-group-item
            fa-icon(icon="angle-right").mr-3.mt-2
            div.w-100
              b-row(no-gutters align-h="between")
                div
                  MTooltip.font-weight-bold(:content="`Session ID: ${event.id}`" noUnderline) Session \#{{ event.typeData.sessionNumber}}
                  span.small.ml-1.text-muted {{ event.typeData.numEvents }} events
                div.small: meta-time(:date="event.typeData.startTime")
              b-row(no-gutters align-h="between")
                div.small.text-muted {{ event.typeData.deviceName }}
                div.small lasted for #[meta-duration(:duration="event.typeData.duration")]

          //- Day header row
          div(v-if="event.type === 'Day'" @click="toggleFoldedPath(event.path)" :class="{ 'group-open': event.decorations.isPathUnfolded }").py-2.card-manual-content-padding.d-flex.clickable-list-group-item
            fa-icon(icon="angle-right").mr-3.mt-2
            div.w-100
              b-row(no-gutters align-h="between")
                div
                  span.font-weight-bold: meta-time(:date="event.typeData.date" showAs="date")
                div.small: meta-time(:date="event.typeData.date")
              b-row(no-gutters align-h="between")
                div.small.text-muted {{ event.typeData.numEvents }} events

          //- Aggregated events header row
          div(v-else-if="event.type === 'RepeatedEvents'" @click="toggleFoldedPath(event.path)" :class="{ 'group-open': event.decorations.isPathUnfolded }").card-manual-content-padding.clickable-list-group-item
            div(v-if="false") Collapsed events: {{ event.typeData.numEvents }} x #[span.font-weight-bold {{ event.typeData.repeatedTitle }}] #[span.small.text-muted Lasted #[meta-duration(:duration="event.typeData.duration")]]
            div(v-else :class="`${event.decorations.lineVariant !== 'none' ? 'three' : 'two'}-column-layout`")
              //- Timestamps
              div.py-1.text-right.pr-2
                div(v-if="!event.decorations.isPathUnfolded")
                  div #[meta-time(:date="event.time" showAs="time")]
                  div(v-if="showTimeDeltas").small.text-muted
                    span(v-if="event.decorations.timeBetweenEvents") + #[meta-duration(:duration="event.decorations.timeBetweenEvents" showAs="top-two")]
                    span(v-else) Oldest event
              div(v-if="event.decorations.lineVariant !== 'none'")
                meta-event-stream-card-group-line(:variant="event.decorations.lineVariant")
              div.pl-2
                div(v-if="!event.decorations.isPathUnfolded").py-2
                  fa-icon(icon="angle-right").mr-2
                  span {{ event.typeData.numEvents }} x #[span.font-weight-bold {{ event.typeData.repeatedTitle }}] #[b-link.small Expand]
                div(v-else)
                  span.text-muted.small {{ event.typeData.numEvents }} repeating events. #[b-link Collapse]

          //- Individual event row (when it is renderable)
          div(
            v-else-if="event.type === 'Event'"
            data-cy="log-event-row"
            ref="event-item"
            :class="`${event.decorations.lineVariant !== 'none' ? 'three' : 'two'}-column-layout`"
            ).card-manual-content-padding
            //- Timestamps
            div.py-1.text-right.pr-2
              div #[meta-time(:date="event.time" showAs="time")]
              div(v-if="showTimeDeltas").small.text-muted
                span(v-if="event.decorations.timeBetweenEvents") + #[meta-duration(:duration="event.decorations.timeBetweenEvents" showAs="top-two")]
                span(v-else) Oldest event

            //- Group line
            div(v-if="event.decorations.lineVariant !== 'none'")
              meta-event-stream-card-group-line(:variant="event.decorations.lineVariant")

            //- Event payload
            div
              div(@click="toggleEventExpanded(event)").clickable-list-group-item.d-flex.pr-1.rounded-sm.py-1
                div(v-if="event.decorations.isEventExpandable || true" :class="{ 'not-collapsed': isEventExpanded(event) }")
                  fa-icon(icon="angle-right").mx-2
                div.w-100
                  div.d-flex.justify-content-between
                    div
                      //- fa-icon(v-if="event.icon" :icon="event.icon").mr-1 We don't support icons for event types, but we could.
                      span.font-weight-bold {{ event.typeData.title }}
                      meta-username(v-if="event.typeData.author" :username="event.typeData.author").font-weight-normal.small.text-muted.ml-1
                    div.small(style="padding-top: .2rem")
                      meta-button(data-cy="view-more-link" v-if="showViewMoreLink && event.typeData.viewMoreLink" link :to="event.typeData.viewMoreLink") View {{ event.typeData.viewMore }}
                  div.text-muted.small.text-break-word {{ event.typeData.description }}
              transition(name="collapse-transition")
                div.border.rounded-sm.bg-light.small.my-1(v-if="isEventExpanded(event)")
                  pre.p-2.m-0.text-break-word {{ event.typeData.sourceData }}

      //- Empty list after filtering
      b-row(align-h="center" v-else no-gutters).text-center.mb-4.pt-4
        span.small.text-muted {{ noResultsMessage }}

  //- Empty
  b-row(v-else align-h="center" no-gutters).mb-4.pb-4.text-center.my-auto.px-3
    span.text-muted {{ emptyMessage }}

</template>

<script lang="ts" setup>
import { computed, ref, shallowRef, watch } from 'vue'
import moment from 'moment'

import { getFiltersForEventStream } from './eventStreamUtils'
import { EventStreamItemBase } from './eventStreamItems'
import { useGameServerApiStore } from '@metaplay/game-server-api'

import MetaEventStreamCardGroupLine from './MetaEventStreamCardGroupLine.vue'
import { DateTime } from 'luxon'

import { MTransitionCollapse, MTooltip, MBadge } from '@metaplay/meta-ui-next'

// Props ------------------------

const props = withDefaults(defineProps<{
  /**
   * Title of the card.
   */
  title: string
  /**
   * Optional Font-Awesome icon shown before the card's title (for example: 'table').
   */
  icon?: string
  /**
   * Optional tooltip to be shown when mouse-overing the card's title.
   */
  tooltip?: string
  /**
   * Stream of events to be shown. Must be supplied in the order of oldest to newest.
   */
  eventStream?: EventStreamItemBase[] | null
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
   * Optional: Custom message to be shown when event array is null or empty.
   */
  emptyMessage?: string
  /**
   * Optional: Custom message to be shown when there are no search results.
   */
  noResultsMessage?: string
  /**
   * Optional: Limit height of the card.
   */
  maxHeight?: string
  /**
   * Optional: Permission needed to view this card's data.
   */
  permission?: string
  /**
   * Optional: If true then each event shows a "view more" link.
   */
  showViewMoreLink?: boolean
  /**
   * Optional: If true then show delta time between events. Defaults to true.
   */
  showTimeDeltas?: boolean
  /**
   * Optional: Show the controls to pause and resume the event stream.
   */
  allowPausing?: boolean
}>(), {
  icon: '',
  tooltip: '',
  searchIsFilter: false,
  initialSearchString: '',
  initialEventTypeFilters: () => [],
  initialKeywordFilters: () => [],
  emptyMessage: 'No events in this stream.',
  noResultsMessage: 'No events found. Try a different search string? 🤔',
  eventStream: null,
  maxHeight: '',
  permission: '',
  showViewMoreLink: false,
  showTimeDeltas: true,
})

// Searching & filtering ----------------------------------------------------------------------------------------------

/**
 * Filters available for this event stream.
 */
const filters = computed(() => {
  return getFiltersForEventStream(props.eventStream ?? [])
})

/**
 * User's search string as entered from the utilities menu. Can be passed in as a prop but is purposefully /not/
 * reactive to changes in that prop.
 */
const userSearchString = ref(props.initialSearchString)

/**
 * Used as a v-model to remember which event types the user has selected.
 */
const selectedEventTypes = ref<string[]>(props.initialEventTypeFilters)

/**
 * Used as a v-model to remember which event keywords the user has selected.
 */
const selectedKeywords = ref<string[]>(props.initialKeywordFilters)

/**
 * Transforms the event list by marking entries that match against any search strings with `highlight'. If filtering is
 * active then any non-matches are filtered out of the list.
 */
const filteredOrHighlightedEventList = computed(() => {
  if (anySearchActive.value) {
    let events: any[] = eventList.value
      .map(event => {
        if (doesEventMatchCurrentSearchOrFilters(event)) {
          return {
            highlight: true,
            ...event
          }
        } else {
          return event
        }
      })

    if (props.searchIsFilter) {
      events = events.filter(event => { return event.highlight })
    }

    return events
  } else {
    return eventList.value
  }
})

const uiFilterHintLabel = computed(() => {
  const activeFilters: string[] = []
  if (userSearchActive.value) {
    activeFilters.push('search string')
  }
  if (selectedEventTypes.value.length > 0) {
    activeFilters.push('event type(s)')
  }
  if (selectedKeywords.value.length > 0) {
    activeFilters.push('keyword(s)')
  }

  return `Showing events that match the selected ${activeFilters.join(' AND ')}.`
})

/**
 * True if any type of search or filtering is active.
 */
const anySearchActive = computed(() => {
  return userSearchActive.value || filtersActive.value
})

/**
 * True if user search string is active.
 */
const userSearchActive = computed(() => {
  // Search string must be at least 3 chars long before it is considered to be usable.
  return userSearchString.value.length >= 3
})

/**
 * True if any pre defined search strings are selected.
 */
const filtersActive = computed(() => {
  return selectedEventTypes.value.length > 0 || selectedKeywords.value.length > 0
})

/**
 * Reset the users search string.
 */
function clearUserSearchString () {
  userSearchString.value = ''
}

/**
 * Utility to check if a given event in the stream matches the currently possibly active search and filters.
 */
function doesEventMatchCurrentSearchOrFilters (event: EventStreamItemBase): boolean {
  const eventType = event.getEventDisplayType()
  const typeMatch: boolean = !!eventType && selectedEventTypes.value?.includes(eventType)

  const eventKeywords = event.getEventKeywords()
  const keywordMatch: boolean = !!eventKeywords && selectedKeywords.value.some(keyword => (eventKeywords).includes(keyword))

  const searchMatch: boolean = !!userSearchString.value && event.search(userSearchString.value.toLocaleLowerCase())

  if ((userSearchActive.value ? searchMatch : true) && (selectedEventTypes.value.length > 0 ? typeMatch : true) && (selectedKeywords.value.length > 0 ? keywordMatch : true)) {
    return true
  } else {
    return false
  }
}

/**
 * Count the number of events in the stream that match a given type.
 */
function countTypeMatches (eventType: string): number {
  if (eventType) {
    return eventList.value.reduce((pre, cur) => {
      return cur.getEventDisplayType() === eventType ? pre + 1 : pre
    }, 0)
  } else {
    return 0
  }
}

/**
 * Count the number of events in the stream that match a given keyword.
 */
function countKeywordMatches (keyword: string): number {
  if (keyword) {
    return eventList.value.reduce((pre, cur) => {
      return (cur.getEventKeywords() ?? []).includes(keyword) ? pre + 1 : pre
    }, 0)
  } else {
    return 0
  }
}
/**
 * When search strings change, ensure that all search results are made visible.
 */
watch(userSearchString, openPathsToHighlightedorFilteredEvents)
watch(selectedEventTypes, openPathsToHighlightedorFilteredEvents)
watch(selectedKeywords, openPathsToHighlightedorFilteredEvents)

// Folding paths ------------------------------------------------------------------------------------------------------

/**
 * List of paths that are unfolded, ie: events with children that have been expanded.
 */
const unfoldedPaths = ref<string[]>([])

/**
 * Open paths so that any searched events are visible.
 */
function openPathsToHighlightedorFilteredEvents () {
  // TODO: refactor to also apply filters.

  if (anySearchActive.value) {
    // Find all paths that contained events matching the search or filter.
    const uniqueSearchPaths = new Set<string>(eventList.value
      .filter(event => event.path && doesEventMatchCurrentSearchOrFilters(event))
      .map(event => event.path))

    // Open those paths and all paths above them.
    uniqueSearchPaths.forEach(searchPath => {
      let openPath = ''
      searchPath.split('.').forEach(searchPathSegment => {
        openPath += searchPathSegment
        openFoldedPath(openPath)
        openPath += '.'
      })
    })
  }
}

/**
 * Open or close a folded path.
 * @param path Path to toggle.
 */
function toggleFoldedPath (path: string) {
  if (path) {
    if (!unfoldedPaths.value.includes(path)) {
      let pathToUnfold = ''
      path.split('.').forEach(pathSegment => {
        pathToUnfold += pathSegment
        openFoldedPath(pathToUnfold)
        pathToUnfold += '.'
      })
    } else {
      closeFoldedPath(path)
    }
  }
}

/**
 * Opens a path. Safe to call on an already opened path.
 * @param path Path to open.
 */
function openFoldedPath (path: string) {
  if (!unfoldedPaths.value.includes(path)) {
    unfoldedPaths.value.push(path)
  }
}

/**
 * Closes a path. Safe to call on an already closed path.
 * @param path Path to open.
 */
function closeFoldedPath (path: string) {
  const index = unfoldedPaths.value.indexOf(path)
  if (index !== -1) {
    unfoldedPaths.value.splice(index, 1)
  }
}

/**
 * Returns true if a path is opened.
 * @param path Path to open.
 */
function isPathUnfolded (path: string) {
  return unfoldedPaths.value.includes(path)
}

// Event expansion ----------------------------------------------------------------------------------------------------

/**
 * List of expanded event IDs, ie: events whose contents has been expanded for viewing.
 */
const expandedEvents = ref<string[]>([])

/**
 * Can an event be expanded?
 * @param event Event to test.
 */
function isEventExpandable (event: EventStreamItemBase) {
  return !!event.id
}

/**
 * Toggle an event so that the details can be seen (or hidden) in the UI.
 * @param event Event to expand.
 */
function toggleEventExpanded (event: EventStreamItemBase) {
  const id = event.id
  if (id) {
    const index = expandedEvents.value.indexOf(id)
    if (index === -1) {
      expandedEvents.value.push(id)
    } else {
      expandedEvents.value.splice(index, 1)
    }
  }
}

/**
 * Is an event expanded?
 * @param event Event to expand.
 */
function isEventExpanded (event: EventStreamItemBase) {
  return expandedEvents.value.includes(event.id)
}

// Events ------------------------

/**
 * Stream events are decorated for the UI.
 */
interface DecoratedEventStreamItem extends EventStreamItemBase {
  decorations: {
    isPathUnfolded?: boolean
    lineVariant: 'none' | 'solid' | 'dashed' | 'dotted'
    timeBetweenEvents: number
    isEventExpandable?: boolean
  }
  highlight?: boolean
}

/**
 * List of all events before searching, filtering and decorating.
 */
const eventList = shallowRef<EventStreamItemBase[]>([])

/**
 * List of events after searching (and possibly filtering).
 */
const searchedAndUnfoldedEventList = computed((): EventStreamItemBase[] => {
  return filteredOrHighlightedEventList.value
    .filter(event => {
      let path = event.path || ''
      if (event.type !== 'Event') {
        path = path.substring(0, path.lastIndexOf('.'))
      }
      if (!path) {
        return true
      }
      let pathToCheck = ''
      let isUnfolded = true
      const pathSegments = path.split('.')
      for (let i = 0; i < pathSegments.length; ++i) {
        pathToCheck += pathSegments[i]
        if (!isPathUnfolded(pathToCheck)) {
          isUnfolded = false
          break
        }
        pathToCheck += '.'
      }
      return isUnfolded
    })
})

/**
 * After searching and filtering, events are decorated for the UI.
 */
const decoratedSearchedAndUnfoldedEventList = computed((): DecoratedEventStreamItem[] => {
  let index = 0
  return searchedAndUnfoldedEventList.value.map((event: any) => {
    const decoratedEvent = {
      ...event,
      decorations: {
        timeBetweenEvents: timeBetweenEvents(searchedAndUnfoldedEventList.value[index], searchedAndUnfoldedEventList.value[index + 1]),
        lineVariant: calculateLineVariant(event),
      }
    }

    // Spare setting of this data.
    if (isPathUnfolded(event.path)) { decoratedEvent.decorations.isPathUnfolded = true }
    if (isEventExpandable(event)) { decoratedEvent.decorations.isEventExpandable = true }

    ++index
    return decoratedEvent
  })
})

/**
 * Return the time between two events.
 * @param firstEvent First event to consider.
 * @param secondEvent Second event to consider
 * @return Time between events or null if either event is missing or has no timestamp.
 */
function timeBetweenEvents (firstEvent: EventStreamItemBase, secondEvent: EventStreamItemBase) {
  const firstEventTime = firstEvent?.time
  const secondEventTime = secondEvent?.time
  if (firstEventTime && secondEventTime) {
    const diffTime = moment.duration(moment(firstEventTime).diff(moment(secondEventTime)))
    return diffTime
  } else {
    return null
  }
}

/**
 * When paused is true, `eventList` does not update when new stream data arrives.
 */
const isPaused = ref(false)

/**
 * True when the stream is paused but we have received new data.
 */
const updateAvailable = ref(false)

/**
 * Remembers the last time that we updated `eventList` from `eventStream`.
 */
const lastUpdated = ref(DateTime.now())

/**
 * Toggle pause state.
 */
function togglePlayPauseState () {
  isPaused.value = !isPaused.value
}

let hasReceivedEventStream = false

/**
 * Triggered when new stream data arrives or pause state changes.
 */
watch([() => props.eventStream, isPaused],
  ([eventStream, isPaused], [_previousEventStream, previousIsPaused]) => {
    if (!isPaused) {
      // If data updates but we are paused, then we just don't update `eventList` with the new data. If the user
      // unpauses then this watcher will fire and the data will get updated immediately.
      eventList.value = eventStream?.slice().reverse() ?? []
      updateAvailable.value = false
      lastUpdated.value = DateTime.now()
    } else if (isPaused && previousIsPaused) {
      // If we are are paused now and we were paused before this watcher fired then it must be `eventStream` that
      // changed. Now we know that we are paused and new data has arrived.
      updateAvailable.value = true
    }

    // When the event stream loads for the first time we need to trigger opening the search string paths - otherwise
    // we don't see the initialSearchString opening the paths.
    if (!hasReceivedEventStream) {
      hasReceivedEventStream = true
      openPathsToHighlightedorFilteredEvents()
    }
  },
  { deep: false }
)

/**
 * True once stream data has arrived.
 */
const hasContent = computed(() => {
  return props.eventStream !== null
})

// UI ------------------------

/**
 * Model for when the utilities window is open.
 */
const utilsOpen = ref(false)

/**
 * Does the user have the required permissions to view the data?
 */
const hasPermission = computed(() => {
  if (props.permission) return useGameServerApiStore().doesHavePermission(props.permission)
  else return true
})

/**
 * Test used in the badge that follows the card's title.
 */
const badgeText = computed(() => {
  const total = eventList.value.length
  if (anySearchActive.value) {
    // If search or filter is active then show the number found against the total.
    const highlighted: number = filteredOrHighlightedEventList.value.reduce((prev, cur) => {
      return prev + ((cur).highlight ? 1 : 0)
    }, 0)
    return `${highlighted} / ${total}`
  } else {
    // Show the total number of events.
    return `${total}`
  }
})

/**
 * Which variant (ie: color) to use when rendering the badge.
 */
const badgeVariant = computed(() => {
  return eventList.value.length === 0 ? 'neutral' : 'primary'
})

/**
 * Calculate the UI style to use when drawing the connection line for this event.
 * @param event Event to examine.
 * @return String representing the variant type.
 */
function calculateLineVariant (event: EventStreamItemBase): string {
  if (event.type === 'Event') {
    if (event.typeData.terminatorStyle) return event.typeData.terminatorStyle
    else if (!event.path) return 'none'
    else if (event.path.startsWith('repeat')) return 'none'
    else return 'grouped-event'
  } else if (event.type === 'RepeatedEvents') {
    if (!event.path) return 'error'
    else if (event.path.startsWith('repeat')) return 'none'
    else return isPathUnfolded(event.path) ? 'line' : 'skip'
  } else {
    return 'error'
  }
}
</script>

<style scoped>
.group-open .fa-angle-right {
  transform: rotateZ(90deg);
}

.group-element-stripes .group:nth-child(even) {
  background: #f7f7f7;
}

.group-element-stripes .search-highlight:nth-child(even) {
  background: #fde8aa;
}

.group-element-stripes .search-highlight:nth-child(odd) {
  background: #fdefc3;
}

.group-element-borders .group {
  border-bottom: solid 1px var(--metaplay-grey-light);
}

.group-element-borders .group:last-child {
  border-bottom: none;
}

.group-element-borders:last-child {
  margin-bottom: .8rem;
}

.three-column-layout {
  display: grid;
  grid-template-columns: 5rem 1.7rem 1fr;
  grid-template-rows: 1fr;
}

.two-column-layout {
  display: grid;
  grid-template-columns: 5rem 1fr;
  grid-template-rows: 1fr;
}

.collapse-transition-enter-active {
  transition: all 0.25s ease-out;
}

.collapse-transition-enter-from,
.collapse-transition-leave-to {
  height: 0;
  opacity: 0;
}
</style>
