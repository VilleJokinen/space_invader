<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- This component prints potentially large numbers in a space efficient and humanized format. -->

<template lang="pug">
MTooltip(:content="tooltipContent")
  span {{ numberDisplayString }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import MTooltip from '../primitives/MTooltip.vue'

const props = defineProps<{
  /**
   * The number to abbreviate.
   */
  number: number
  /**
   * Optional: Text label to show after the number.
   * @example 'byte'
   */
  unit?: string
  /**
   * Optional: Round the number down to the nearest integer.
   */
  roundDown?: boolean
  /**
   * Optional: Do not show the exact number in a tooltip.
   */
  disableTooltip?: boolean
}>()

/**
 * Number to be abbreviated after rounding down if roundDown is true.
 */
const roundedNumber = computed(() => {
  return props.roundDown ? Math.floor(props.number) : props.number
})

/**
 * String representing the shortened form of the number value.
 */
const abbreviatedNumberString = computed(() => {
  return abbreviateNumber(roundedNumber.value)
})

/**
 * Hides the default tooltip showing the exact number value when the abbreviated and final values are equal
 * or disableTooltip is passed other wise shows string representing the full number value.
 */
const tooltipContent = computed(() => {
  if ((abbreviatedNumberString.value === roundedNumber.value.toString()) || props.disableTooltip) {
    return undefined
  } else {
    return abbreviateNumber(roundedNumber.value)
  }
})

const numberDisplayString = computed(() => {
  if (props.unit) {
    return `${abbreviatedNumberString.value} ${props.unit}${roundedNumber.value === 1 ? '' : 's'}`
  } else {
    return abbreviatedNumberString.value
  }
})

/**
 * Abbreviates an arbitrary number into a short display string. For example 123321 -> 123k.
 * @param x The number to abbreviate.
 * @param precision The amount of numbers to display after abbreviation.
 * @returns A string with the abbreviated result.
 */
function abbreviateNumber (x: number, precision: number = 3): string | undefined {
  if (x >= 1000000000.0) {
    return (x / 1000000000.0).toPrecision(precision) + 'B'
  } else if (x >= 1000000.0) {
    return (x / 1000000.0).toPrecision(precision) + 'M'
  } else if (x >= 1000.0) {
    return (x / 1000.0).toPrecision(precision) + 'k'
  } else {
    if (x || x === 0) return x.toString()
    else return undefined
  }
}
</script>
