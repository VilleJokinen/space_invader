<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- This component prints potentially large numbers in a space efficient and humanized format. -->

<template lang="pug">
span(v-if="abbreviatedNumberString === value.toString()") {{ abbreviatedNumberString }} #[span(v-if="unit") {{ unit }}{{ finalValue == 1 ? '' : 's' }}]
MTooltip(v-else :content="!hideTooltip ? exactNumberString : undefined")
  span {{ abbreviatedNumberString }} #[span(v-if="unit") {{ unit }}{{ finalValue == 1 ? '' : 's' }}]
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { abbreviateNumber } from '../utils'
import { MTooltip } from '@metaplay/meta-ui-next'

const props = defineProps<{
  /**
   * The number to display.
   */
  value: number
  /**
   * Optional: Text label to show after the number.
   * @example 'byte', 'player'.
   */
  unit?: string
  /**
   * Optional: If true the value is rounded down to the nearest integer.
   */
  roundDown?: boolean
  /**
   * Optional: If true hides a tooltip that displays the exact value of the abbreviated value.
   */
  disableTooltip?: boolean
}>()

/**
 * Numeric value to be displayed.
 */
const finalValue = computed(() => {
  return props.roundDown ? Math.floor(props.value) : props.value
})

/**
 * String representing the shortened form of the number value.
 */
const abbreviatedNumberString = computed(() => {
  return abbreviateNumber(finalValue.value)
})

/**
 * String representing the full number value.
 */
const exactNumberString = computed(() => {
  return finalValue.value.toLocaleString()
})

/**
 * Hides the default tooltip showing the exact number value when the abbreviated and final values are equal, or disableTooltip is passed.
 */
const hideTooltip = computed(() => {
  return (abbreviatedNumberString.value === finalValue.value.toString()) || props.disableTooltip
})

</script>
