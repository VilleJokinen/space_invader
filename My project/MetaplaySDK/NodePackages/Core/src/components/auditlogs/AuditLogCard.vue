<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- Displays a list of audit log events for a given entity. -->

<template lang="pug">
meta-event-stream-card(
  data-cy="audit-log-card"
  title="Latest Audit Log Events"
  icon="clipboard-list"
  :tooltip="`Shows the ${limit} latest admin actions targeting this entity.`"
  :eventStream="eventStream"
  :searchIsFilter="true"
  :maxHeight="maxHeight"
  permission="api.audit_logs.view"
  showViewMoreLink
).mb-3
  template(#item-card="slotProps")
    audit-log-entry(:item="slotProps.item")
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { EventStreamItemEvent, wrapRepeatingEvents, MetaEventStreamCard } from '@metaplay/event-stream'

import { useSubscription } from '@metaplay/subscriptions'
import { getAllAuditLogEventsSubscriptionOptions } from '../../subscription_options/auditLogs'

import AuditLogEntry from './AuditLogEntry.vue'

const props = withDefaults(defineProps<{
  /**
   * Type of entity that we are interested in.
   */
  targetType: string
  /**
   * Id of the entity that we are interested in or a function that retrieves the needed Id.
   */
  targetId: string | (() => string)
  /**
   * Optional: Limits the number of events that are fetched from the backend.
   */
  limit?: number
  /**
   * Optional: Limits the height of the card.
   */
  maxHeight?: string
}>(), {
  limit: 50,
  maxHeight: '30rem',
})

/**
 * Id of the entity that is to be displayed.
 * Note: Either the Id is passed in as a string or as a function that retrieves the target Id.
 */
const targetId = computed((): string => {
  let computedTargetId = ''
  if (typeof props.targetId === 'string') {
    computedTargetId = props.targetId
  } else {
    computedTargetId = props.targetId()
  }
  if (!computedTargetId) {
    throw new Error('Target Id cannot be empty or undefined.')
  }
  return computedTargetId
})

const { data: auditLogData } = useSubscription(getAllAuditLogEventsSubscriptionOptions(props.targetType, targetId.value, props.limit))

/**
 * Event stream data, generated from the fetched data.
 */
const eventStream = computed(() => {
  // Create an event stream.
  if (auditLogData.value) {
    let eventStream = auditLogData.value.entries.map((entry: any) => {
      return new EventStreamItemEvent(
        entry.createdAt,
        entry.displayTitle,
        entry.displayDescription,
        entry.eventId,
        entry,
        entry.source.sourceId,
        'event',
        `/auditLogs/${entry.eventId}`
      )
    }).reverse()

    // Fold what we can.
    eventStream = wrapRepeatingEvents(eventStream)

    return eventStream
  } else {
    return null
  }
})
</script>
