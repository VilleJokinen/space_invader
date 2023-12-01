<template lang="pug">
div
  //- Header
  div(
    @click="isOpen = !isOpen"
    :class="['tw-flex tw-cursor-pointer', variantClasses]"
    )
    //- Icon from https://heroicons.com/
    svg(
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 20 20"
      fill="currentColor"
      :class="['tw-block tw-w-5 tw-h-5 tw-transition-transform tw-relative tw-shrink-0 -tw-left-1', { 'tw-rotate-90': isOpen, 'tw-mt-3': extraMListItemMargin, 'tw-ml-3': extraMCalloutMargin }]"
      style="top: 1px;"
      )
      path(
        fill-rule="evenodd"
        d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z"
        clip-rule="evenodd"
        )

    div(class="tw-grow")
      slot(name="header")
        span Header content TBD

  //- Body. This is the part that collapses.
  MTransitionCollapse
    div(
      v-if="isOpen"
      )
      slot
        span Body content TBD
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Variant } from '../utils/types'
import { MTransitionCollapse } from '..'

const isOpen = ref(false)

const props = withDefaults(defineProps<{
  /**
   * Optional: Set the color of the hover and active states. Defaults to `neutral`.
   */
  variant?: Variant
  /**
  * Optional: Adds a top margin to accommodate for [MListItem](figure out correct filepath here if possible) being wrapped inside this component.
  */
  extraMListItemMargin?: boolean
  /**
  * Optional: Adds a left margin to accommodate for [MErrorCallout](figure out correct filepath here if possible) being wrapped outside this component.
  */
  extraMCalloutMargin?: boolean
}>(), {
  variant: 'neutral',
})

const variantClasses = computed(() => {
  const classes = {
    primary: 'hover:tw-bg-blue-200 active:tw-bg-blue-300',
    warning: 'hover:tw-bg-orange-200 active:tw-bg-orange-300',
    success: 'hover:tw-bg-green-200 active:tw-bg-green-300',
    danger: 'hover:tw-bg-red-200 active:tw-bg-red-300',
    neutral: 'hover:tw-bg-neutral-200 active:tw-bg-neutral-300',
  }
  return classes[props.variant] ?? undefined as never
})
</script>
