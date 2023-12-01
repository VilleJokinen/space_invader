<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A component to display humanized durations. -->

<template lang="pug">
MTooltip(:content="tooltipContent")
  span(v-if="bodyContent !== 'invalid'") {{ bodyContent }}
  span(v-else).text-danger Invalid showAs prop!
</template>

<script lang="ts" setup>
import moment from 'moment' // TODO: refactor to Luxon
import { computed } from 'vue'
import { MTooltip } from '@metaplay/meta-ui-next'
import { constructExactTimeCompactString, constructExactTimeVerboseString } from '../../src/metaDurationUtils'
import { toSentenceCase } from '../utils'

const props = withDefaults(defineProps<{
  /**
   * Duration to show in the component.
   */
  duration: number | string | any // TODO: migrate to Luxon and make this strongly typed
  /**
   * Optional: How to show the duration. Defaults to 'humanized'.
   */
  showAs?: 'humanized' | 'humanizedSentenceCase' | 'top-two' | 'top-three' | 'exactDuration'
  /**
   * Optional: Disable the tooltip that shows the exact time.
   */
  disableTooltip?: boolean
  /**
   * Optional: Always show the duration as days.
   */
  showAsTotalDays?: boolean
  /**
   * Optional: Hide milliseconds.
   */
  hideMilliseconds?: boolean
}>(), {
  showAs: 'humanized'
})

const tooltipContent = computed(() => {
  // Whether to hide the tooltip.
  if (moment.duration(props.duration).asMilliseconds() === 0 || props.disableTooltip) {
    return undefined
  } else {
    // Which tooltip to show depending on exactDuration showAs.
    if (props.showAs === 'exactDuration') {
      return exactTimeTooltip.value
    } else {
      return exactDurationString.value
    }
  }
})

/**
 * The string for the body of the tooltip.
 */
const bodyContent = computed(() => {
  switch (props.showAs) {
    case 'humanized':
      return humanizedString.value
    case 'humanizedSentenceCase':
      return toSentenceCase(humanizedString.value)
    case 'top-two':
      return topString(2)
    case 'top-three':
      return topString(3)
    case 'exactDuration':
      return exactTimeString.value
    default:
      return 'invalid'
  }
})

/**
 * Precise exact duration string. (e.g., '1y 1m 1d 1h 1min 1s' when there are multiple units of time and '45 minutes' when there is only one unit of time).
 */
const exactTimeString = computed(() => {
  return constructExactTimeCompactString(props.duration, props.hideMilliseconds)
})

/**
 * Precise exact duration tooltip. (e.g., '1 year 1 month 1 day 1 hour 1 minute 1 second and 1 millisecond' when there are multiple units of time and '45 minutes 0 seconds exactly' when there is only one unit of time).
 */
const exactTimeTooltip = computed(() => {
  return constructExactTimeVerboseString(props.duration, props.hideMilliseconds) + ' exactly'
})

/**
 * Humanized duration string that rounds. (e.g., '45 minutes' and '55 minutes' becomes 'an hour').
 */
const humanizedString = computed(() => {
  const duration = moment.duration(props.duration)
  if (duration.asMilliseconds() === 0) return '0 seconds'
  else return duration.humanize()
})

/**
 * Humanized exact duration string. (e.g., '45 minutes').
 */
const exactDurationString = computed(() => {
  const duration = moment.duration(props.duration)
  let output = ''
  if (duration.asSeconds() === 0) {
    output = '0 seconds'
  } else {
    if (!props.hideMilliseconds) {
      output = constructExactTimeUnit(output, duration.milliseconds(), 'millisecond')
    }
    output = constructExactTimeUnit(output, duration.seconds(), 'second')
    output = constructExactTimeUnit(output, duration.minutes(), 'minute')
    output = constructExactTimeUnit(output, duration.hours(), 'hour')
    if (props.showAsTotalDays) {
      output = constructExactTimeUnit(output, Math.trunc(duration.asDays()), 'day')
    } else {
      output = constructExactTimeUnit(output, duration.days(), 'day')
      output = constructExactTimeUnit(output, duration.months(), 'month')
      output = constructExactTimeUnit(output, duration.years(), 'year')
    }
  }
  return output + ' exactly'
})

/**
 * Utility to help generate a formatted duration string with the top N time units.
 * @param count Number of time units to show.
 */
function topString (count: number) {
  const values: string[] = []
  const duration = moment.duration(props.duration)
  values.unshift(`${Math.round(duration.milliseconds())}ms`)
  if (duration.asSeconds() >= 1) values.unshift(`${duration.seconds()}s`)
  if (duration.asMinutes() >= 1) values.unshift(`${duration.minutes()}m`)
  if (duration.asHours() >= 1) values.unshift(`${duration.hours()}h`)
  if (props.showAsTotalDays) {
    if (duration.asDays() >= 1) values.unshift(`${duration.asDays()}d`)
  } else {
    if (duration.asDays() >= 1) values.unshift(`${duration.days()}d`)
    if (duration.asMonths() >= 1) values.unshift(`${duration.months()}m`)
    if (duration.asYears() >= 1) values.unshift(`${duration.years()}y`)
  }
  return values.slice(0, count).join(' ')
}

/**
 * Utility to help generate the exact duration string.
 */
function constructExactTimeUnit (current: string, value: number, unit: string) {
  if (value === 0) {
    return current
  } else {
    const prefix = current === '' ? '' : (current.includes('and') ? ', ' : ' and ')
    const suffix = value === 1 ? '' : 's'
    return `${value} ${unit}${suffix}${prefix}${current}`
  }
}
</script>
