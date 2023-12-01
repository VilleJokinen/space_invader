<template lang="pug">
div(class="tw-w-full tw-h-5 tw-rounded-full tw-bg-neutral-200 tw-relative tw-flex tw-justify-center tw-items-center")
  div(
    :class="['tw-absolute tw-top-0 tw-left-0 tw-h-full tw-rounded-full', variantClasses]"
    :style="{ width: `${progressPercentage}%` }"
    )
  span(
    v-if="!hidePercentageValue"
    :class="['tw-text-xs tw-z-10', textColorClass]"
    ) {{ Math.floor(progressPercentage) }}%
</template>

<script setup lang="ts">
import type { Variant } from '../utils/types'
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  value: number
  min?: number
  max?: number
  hidePercentageValue?: boolean
  variant?: Variant
}>(), {
  min: 0,
  max: 1,
  hidePercentageValue: false,
  variant: 'primary',
})

const variantClasses = computed(() => {
  const defaultClasses: { [index: string]: string } = {
    primary: 'tw-bg-blue-500',
    success: 'tw-bg-green-500',
    warning: 'tw-bg-orange-500',
    danger: 'tw-bg-red-500',
    neutral: 'tw-bg-neutral-400'
  }

  return defaultClasses[props.variant] || defaultClasses.primary
})

/**
 * Computes the progress percentage based on the current value, minimum, and maximum values.
 */
const progressPercentage = computed(() => {
  const { value, min, max } = props
  return (value - min) / (max - min) * 100
})

/**
 * Determines the text color class based on the progress percentage.
 * If the progress is more than half, the text color will be white, otherwise black.
 */
const textColorClass = computed(() => {
  return progressPercentage.value > 54 ? 'tw-text-white' : 'tw-text-black'
})

</script>
