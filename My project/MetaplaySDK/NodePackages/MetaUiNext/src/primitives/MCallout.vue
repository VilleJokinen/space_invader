<template lang="pug">
div(
  :class="['tw-border tw-rounded-md tw-py-2 tw-px-3', variantClasses]"
  )
  div(class="tw-flex tw-flex-wrap tw-mb-1")
    //- Title.
    span(
      role="heading"
      :class="['tw-font-semibold tw-mr-1 tw-text-ellipsis tw-overflow-hidden']"
      ) {{ title }}
    //- Badge
    span
      slot(name="badge")

  //- Body.
  div(class="tw-overflow-x-auto")
    slot

  //- Buttons. TODO: Update this to use nice button layouts once we figure out what it should look like in cards.
  div(
    v-if="$slots.buttons"
    class="tw-flex tw-justify-end tw-mt-3"
    )
    slot(name="buttons")
</template>

<script setup lang="ts">
import type { Variant } from '../utils/types'
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  title: string
  variant?: Variant
}>(), {
  variant: 'warning'
})

const variantClasses = computed(() => {
  const variantToClasses: { [index: string]: string } = {
    warning: 'tw-border-orange-200 tw-bg-orange-100 tw-text-orange-900',
    danger: 'tw-border-red-200 tw-bg-red-100 tw-text-red-900',
    success: 'tw-border-green-200 tw-bg-green-100 tw-text-green-900',
    default: 'tw-border-neutral-200 tw-bg-neutral-100 tw-text-neutral-600'
  }
  return variantToClasses[props.variant] || variantToClasses.default
})

</script>
