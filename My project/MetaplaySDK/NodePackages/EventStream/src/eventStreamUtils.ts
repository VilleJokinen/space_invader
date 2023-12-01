// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview An `EventStream` is a list of time-ordered events. These events are stored in a manner that doesn't
 * need to know about their origin or their original data structures. This allows us to render them in the same way
 * regardless of what the underlying events actually are.
 *
 * The `EventStream` is just an array that is built up of `EventStreamItem`-derived types. In the simplest case, all
 * events will be of type `EventStreamItemEvent`, but other types are available.
 *
 * This file contains helper function to validate, decorate and extract information about event streams.
 */

// TODO: replace with luxon
import moment from 'moment'
import { DateTime, Duration } from 'luxon'

import { EventStreamItemBase, EventStreamItemDay, EventStreamItemEvent, EventStreamItemRepeatedEvents, EventStreamItemSession } from './eventStreamItems'

/**
 * Validate a stream of events. Checks types and ordering of events.
 * @param events Stream of events to check.
 * @returns True if the event stream is valid.
 */
export function validateEvents (events: EventStreamItemBase[]) {
  // Event stream must be an array.
  if (!Array.isArray(events)) {
    console.warn('validateEvents failed: Not an array')
    return false
  }

  // All items must be of the right type.
  if (!events.every(event => event.constructor.name.startsWith('EventStreamItem'))) {
    console.warn('validateEvents failed: Array members not of correct type')
    return false
  }

  // Events must be supplied in time order from oldest to newest.
  for (let i = 0; i < events.length - 1; ++i) {
    if (events[i].time > events[i + 1].time) {
      console.warn('validateEvents failed: Array members not in correct time order')
      return false
    }
  }

  // This stream of events looks legit!
  return true
}

/**
 * Take a stream of events that represent a player's events and wraps gameplay sessions inside
 * `EventStreamItemSession`s. This function works specifically on player event streams and won't do anything useful
 * for other streams.
 * @param events Source events.
 * @returns New stream of events.
 */
export function wrapSessions (events: EventStreamItemBase[]): EventStreamItemBase[] {
  // Helper function to emit a list of events, wrapped by a `EventStreamItemSession`.
  const flush = (events: EventStreamItemBase[], startTerminated: boolean, endTerminated: boolean) => {
    const flushedEvents: EventStreamItemBase[] = []
    const newPath = `session_${moment(events[0].time).unix()}_${events[0].id}`

    // First push the events themselves.
    flushedEvents.push(...events.map(x => {
      x.path = x.path + newPath
      return x
    }))

    // Add terminators to the start and end events.
    if (flushedEvents.length > 1) {
      flushedEvents[0].typeData.terminatorStyle = startTerminated ? 'oldest-terminated' : 'oldest-unterminated'
      flushedEvents[flushedEvents.length - 1].typeData.terminatorStyle = 'newest-terminated'
    } else {
      flushedEvents[0].typeData.terminatorStyle = 'both-terminated'
    }

    // Finally, push a wrapper event.
    flushedEvents.push(new EventStreamItemSession(
      newPath,
      events[0],
      events[events.length - 1],
      events.length
    ))
    return flushedEvents
  }

  let newEvents: EventStreamItemBase[] = []
  let sessionEvents: EventStreamItemBase[] = []
  let sessionsStartedCount = 0
  events.forEach(srcEvent => {
    if (srcEvent as EventStreamItemEvent) {
      const eventType = srcEvent.typeData.sourceData.payload.$type
      if (eventType === 'Metaplay.Core.Player.PlayerEventClientConnected') {
        // Found a connect event. This is the start of a session. We'll store these events temporarily until we find a
        // disconnect event.
        sessionEvents.push(srcEvent)
        sessionsStartedCount++
      } else if (eventType === 'Metaplay.Core.Player.PlayerEventClientDisconnected') {
        // Found a disconnect event. This signifies the end of a session.
        if (sessionsStartedCount > 0) {
          // We recorded events for this session so now we'll wrap them in a session event header.
          sessionEvents.push(srcEvent)
          newEvents.push(...flush(sessionEvents, true, true))
          sessionEvents = []
        } else {
          // Found a disconnect event without ever seeing a connect event. We'll assume that the connect event is off
          // the end of the stream and make every event that we've seen up to this point into a new session.
          newEvents.push(srcEvent)
          newEvents = flush(newEvents, false, true)
        }
      } else if (sessionEvents.length) {
        // We're inside a session, so remember this event.
        sessionEvents.push(srcEvent)
      } else {
        // An event in-between a session. Pushed it straight to the stream.
        newEvents.push(srcEvent)
      }
    } else {
      // Some other type of event. Just push it straight to the stream.
      newEvents.push(srcEvent)
    }
  })
  if (sessionEvents.length) {
    // Stream finished while still in a session. Flush that session now.
    newEvents.push(...flush(sessionEvents, true, false))
  }

  return newEvents
}

/**
 * Take a stream of events and return a new stream where each day's worth of `EventStreamItemBase`s are wrapped
 * inside an `EventStreamItemDay`.
 * @param events Source events.
 * @returns New stream of events.
 */
export function wrapDays (events: EventStreamItemBase[]): EventStreamItemBase[] {
  // Helper function to emit a list of events, wrapped by a `EventStreamItemDate`.
  const flush = (events: EventStreamItemBase[]) => {
    const flushedEvents: EventStreamItemBase[] = []
    const newPath = `date_${moment(events[0].time).unix()}_${events[0].id}`

    // First push the events themselves.
    flushedEvents.push(...events.map(x => {
      x.path = x.path + newPath
      return x
    }))

    // Add terminators to the start and end events.
    if (flushedEvents.length > 1) {
      flushedEvents[0].typeData.terminatorStyle = 'oldest-terminated'
      flushedEvents[flushedEvents.length - 1].typeData.terminatorStyle = 'newest-terminated'
    } else {
      flushedEvents[0].typeData.terminatorStyle = 'both-terminated'
    }

    // Finally, push a wrapper event.
    flushedEvents.push(new EventStreamItemDay(
      newPath,
      events[0],
      events[events.length - 1],
      events.length
    ))
    return flushedEvents
  }

  const newEvents: EventStreamItemBase[] = []
  if (events.length) {
    let dayEvents: EventStreamItemBase[] = []
    let lastEventDay = moment(events[0].time).format(moment.HTML5_FMT.DATE)
    events.forEach(srcEvent => {
      dayEvents.push(srcEvent)
      const eventDay = moment(srcEvent.time).format(moment.HTML5_FMT.DATE)
      if (eventDay !== lastEventDay) {
        newEvents.push(...flush(dayEvents))
        dayEvents = []
        lastEventDay = eventDay
      }
    })
    if (dayEvents.length) {
      // Stream finished while still in a day. Flush that day now.
      newEvents.push(...flush(dayEvents))
    }
  }

  return newEvents
}

/**
 * Take a stream of events and return a new stream where repeated `EventStreamItemBase`s are wrapped
 * inside an `EventStreamItemRepeatedEvents`.
 * @param events Source events.
 * @param minimumSize Minimum number of events to wrap.
 * @returns New stream of events.
 */
export function wrapRepeatingEvents (events: EventStreamItemBase[], minimumSize = 5): EventStreamItemBase[] {
  // Helper function to emit a list of events, optionally wrapped by a `EventStreamItemRepeatedEvents`.
  const flush = (events: EventStreamItemBase[]) => {
    if (events.length >= minimumSize) {
      // There are enough events to bother wrapping.
      const newPathSegment = `repeat_${moment(events[0].time).unix()}_${events[0].id}`

      // First push the events themselves.
      const flushedEvents: EventStreamItemBase[] = []
      flushedEvents.push(...events.map(x => {
        x.path = x.path ? `${x.path}.${newPathSegment}` : `${newPathSegment}`
        return x
      }))

      // Then push a wrapper event.
      flushedEvents.push(new EventStreamItemRepeatedEvents(
        events[0].path,
        events[0],
        events[events.length - 1],
        events.length
      ))

      return flushedEvents
    } else {
      // Not enough events to bother wrapping, just emit it normally.
      return events
    }
  }

  // Search through all events. If more than one event in a row has the same title then try to wrap them.
  const newEvents: EventStreamItemBase[] = []
  let eventSeries: EventStreamItemBase[] = []
  events.forEach(srcEvent => {
    if (srcEvent as EventStreamItemEvent) {
      if (eventSeries.length === 0) {
        eventSeries = [srcEvent]
      } else if (eventSeries[0].typeData.title === srcEvent.typeData.title) {
        eventSeries.push(srcEvent)
      } else {
        newEvents.push(...flush(eventSeries))
        eventSeries = [srcEvent]
      }
    } else {
      newEvents.push(srcEvent)
    }
  })
  newEvents.push(...flush(eventSeries))

  return newEvents
}

/**
 * Statistics about an event stream.
 */
export interface EventStreamStats {
  /**
   * Number of raw events, excluding group headers.
   */
  numEvents: number

  /**
   * Timestamp of the oldest event or null if there are no events.
   */
  oldestEventTime: DateTime | null

  /**
   * Timestamp of the newest event or null if there are no events.
   */
  newestEventTime: DateTime | null

  /**
   * Duration of events or null if there are no events.
   */
  duration: Duration | null
}

/**
 * Compute stats about a stream from a stream of events.
 * @param events Stream of events to compute stats about.
 * @returns Computed stats.
 */
export function generateStats (events: EventStreamItemBase[]): EventStreamStats {
  // How many actual events are there?
  const numEvents = events.reduce((p, c) => {
    return p + (c.constructor.name === 'EventStreamItemEvent' ? 1 : 0)
  }, 0)

  // Calculate time range of the events.
  let oldestEventTime = null
  let newestEventTime = null
  let duration = null
  if (events.length) {
    oldestEventTime = DateTime.fromISO(events[0].time)
    newestEventTime = DateTime.fromISO(events[events.length - 1].time)
    duration = newestEventTime.diff(oldestEventTime)
  }

  return {
    numEvents,
    oldestEventTime,
    newestEventTime,
    duration,
  }
}

/**
 * Compute a list of possible filters from a stream of events.
 * @param events Stream of events.
 * @returns List of available filters.
 */
export function getFiltersForEventStream (events: EventStreamItemBase[]): { eventTypes: string[], eventKeywords: string[] } {
  const eventTypes: string[] = []
  const eventKeywords: string[] = []

  // For each event, extract the unique set of event types and keywords.
  events.forEach(event => {
    const type = event.getEventDisplayType()
    if (type && !eventTypes.includes(type)) eventTypes.push(type)

    const keywords = event.getEventKeywords()
    if (keywords) {
      keywords.forEach(keyword => {
        if (!eventKeywords.includes(keyword)) eventKeywords.push(keyword)
      })
    }
  })

  // Sort.
  eventTypes.sort()
  eventKeywords.sort()

  return {
    eventTypes,
    eventKeywords,
  }
}

/**
 * Compute a list of possible search strings for a stream of events.
 * @param events Stream of events to compute search strings from.
 * @returns List of possible search strings.
 */
export function generateSearchStrings (events: EventStreamItemBase[]): string[] {
  // First find unique set of all search strings.
  const allSearchStrings = new Set()
  events.forEach(event => {
    allSearchStrings.add(event.getEventDisplayType())
  })

  // Convert to a list and sort.
  const uniqueSearchStrings = Array.from(allSearchStrings) as string[]
  return uniqueSearchStrings.sort()
}
