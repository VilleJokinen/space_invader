<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A utility to display humanized time stamps. -->

<template lang="pug">
MTooltip
  template(#content v-if="!disableTooltip")
    div {{ tooltipContent.firstLine }}
    div {{ tooltipContent.utc }}
  span(v-if="bodyContent !== 'invalid'") {{ bodyContent }}
  span(v-else).text-danger Invalid showAs prop!
</template>

<script lang="ts" setup>
import moment from 'moment'
import { DateTime } from 'luxon'
import { ref, computed, onUnmounted } from 'vue'
import { MTooltip } from '@metaplay/meta-ui-next'
import { toSentenceCase } from '../utils'

const props = withDefaults(defineProps<{
  /**
   * The date to display. Can be a string or a luxon DateTime object.
   */
  date: string | DateTime
  /**
   * Optional: Disable the tooltip that shows the full date and time.
   */
  disableTooltip?: boolean
  /**
   * Optional: How to display the date. Defaults to 'timeago'.
   */
  showAs?: 'timeago' | 'timeagoSentenceCase' | 'time' | 'date' | 'date-utc' | 'datetime' | 'datetime-utc'
}>(), {
  showAs: 'timeago'
})

/**
 * Which tooltip to show depending on exactDuration showAs.
 */
const tooltipContent = computed(() => {
  const datetime = moment(dateIsoString.value)
  const utcDateTime = datetime.utc().format('YYYY-MM-DD HH:mm:ss') // The date and time in UTC. e.g "2021-01-01 12:00:00"

  if (props.showAs.startsWith('timeago')) {
    return {
      firstLine: 'Local: ' + datetime.local().format('YYYY-MM-DD HH:mm:ss'), // The date and time in local time. e.g "2021-01-01 12:00:00"
      utc: 'UTC: ' + utcDateTime
    }
  } else {
    return {
      firstLine: toSentenceCase(timeAgoString.value),
      utc: 'UTC: ' + utcDateTime
    }
  }
})

/**
 * The string for the body of the tooltip.
 */
const bodyContent = computed(() => {
  const datetime = moment(dateIsoString.value)

  switch (props.showAs) {
    case 'timeago':
      return timeAgoString.value
    case 'timeagoSentenceCase':
      return toSentenceCase(timeAgoString.value)
    case 'time':
      return datetime.local().format('HH:mm:ss') // The time in local time. e.g "12:00:00"
    case 'date':
      return datetime.local().format('MMM Do, YYYY') // The date in local time. e.g "Jan 1st, 2021"
    case 'date-utc':
      return datetime.utc().format('MMM Do, YYYY') + 'UTC' // The date in UTC. e.g "Jan 1st, 2021"
    case 'datetime':
      return datetime.local().format('MMM Do, YYYY HH:mm:ss') // The date and time in local time. e.g "Jan 1st, 2021 12:00:00"
    case 'datetime-utc':
      return datetime.utc().format('MMM Do, YYYY HH:mm:ss') + 'UTC' // The date and time in UTC. e.g "Jan 1st, 2021 12:00:00"
    default:
      return 'invalid'
  }
})

/**
 * The date as an ISO string.
 */
const dateIsoString = computed(() => {
  if (props.date instanceof DateTime) {
    return props.date.toISO()
  } else {
    return props.date
  }
})

/**
 * Humanised time string.
 * @example "5 minutes ago"
 */
const timeAgoString = computed(() => {
  // Fake dependency so that timeAgoString gets re-computed whenever the refresh timer gets reset
  if (refreshTimer.value === undefined) {
    // First time we compute this string, start a periodic refresh timer
    refreshPeriodically()
  }

  return moment(dateIsoString.value).fromNow()
})

// Hack to update the above text.
const refreshTimer = ref<ReturnType<typeof setTimeout>>()
onUnmounted(() => clearTimeout(refreshTimer.value))

function refreshPeriodically () {
  refreshTimer.value = setTimeout(() => refreshPeriodically(), 5000)
}
</script>
