<template lang="pug">
div(:class="['tw-rounded-lg tw-shadow tw-border tw-flex tw-flex-col', containerVariantClasses]")
  //- Header container.
  div(
    :class="['tw-px-4 tw-pt-3 tw-pb-2 tw-rounded-t', { 'hover:tw-bg-neutral-200 active:tw-bg-neutral-300 tw-cursor-pointer': clickableHeader }]"
    @click="onHeaderClick"
    )
    //- Header.
    div(:class="['sm:tw-flex tw-items-center tw-justify-between tw-space-x-2']")
      //- Left side of header.
      div(:class="['tw-flex tw-items-center tw-space-x-2 tw-my-1 tw-text-lg', headerVariantClasses]")
        //- Icon.
        slot(name="icon")

        //- Title.
        span(
          role="heading"
          :class="['tw-font-bold tw-overflow-ellipsis']"
          ) {{ title }}

          //- Badge.
          MBadge(
            class="tw-ml-1.5 tw-relative"
            style="bottom: 1px"
            v-if="badge"
            :variant="badgeVariant"
            shape="pill"
            ) {{ badge }}

      //- Right side of header.
      div(
        v-if="$slots['header-right'] && !isLoading"
        class="tw-grow tw-text-right"
        style="min-width: 10rem; max-width: 40rem;"
        )
        slot(name="header-right")

    //- Subtitle.
    p(
      v-if="$slots.subtitle || subtitle"
      :class="['tw-text-sm tw-mb-2', subtitleVariantClasses]"
      )
      slot(name="subtitle") {{ subtitle }}

  //- Body container.
  div(
    :class="['tw-grow', bodyVariantClasses, { 'tw-mt-1 tw-mb-5 tw-px-4': !noBodyPadding }]"
    )
    //- Error state.
    MErrorCallout(
      v-if="error"
      :error="error"
      class="tw-shadow"
      )

    //- Loading state.
    div(
      v-else-if="isLoading"
      class="tw-flex tw-items-center tw-justify-center tw-h-32"
      )
      // TODO: Cool loading animation.
      span Loading...

    //- Content.
    div(
      v-else
      class="tw-flex tw-flex-col tw-justify-between tw-h-full"
      )
      div(class="tw-grow")
        slot

      //- Buttons.
      // TODO: Mobile view. Probably stretch the buttons to the full width of the card?
      div(
        v-if="$slots.buttons"
        class="tw-flex tw-flex-col sm:tw-flex-row tw-justify-end tw-mt-5 tw-space-y-2 sm:tw-space-y-0 sm:tw-space-x-2"
        )
        slot(name="buttons")
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { DisplayError } from '../utils/DisplayErrorHandler'
import type { Variant } from '../utils/types'
import MBadge from './MBadge.vue'
import MErrorCallout from './MErrorCallout.vue'

const props = withDefaults(defineProps<{
  /**
   * The title of the card.
   */
  title: string
  /**
   * Optional: Show a loading state.
   */
  isLoading?: boolean
  /**
   * Optional: Show an error. You can directly pass in the `error` property from subscriptions.
   */
  error?: Error | DisplayError
  /**
   * Optional: Content to show in a pill shaped badge on the right side of the title. Usually a number.
   */
  badge?: string | number
  /**
   * Optional: A subtitle to show below the title.
   */
  subtitle?: string
  /**
   * Optional: The visual variant of the badge. Defaults to `primary`.
   */
  variant?: Variant
  /**
   * Optional: Set the visual style of the header badge. Defaults to `neutral`.
   */
  badgeVariant?: Variant
  /**
   * Optional: Set the header to have visual clickable feedback.
   */
  clickableHeader?: boolean
  /**
   * Optional: Remove the default padding from the body. Good for making cards that have only the header and no body.
   */
  noBodyPadding?: boolean
}>(), {
  variant: 'primary',
  badgeVariant: 'neutral',
  error: undefined,
  badge: undefined,
  subtitle: undefined,
})

const emit = defineEmits(['headerClick'])

function onHeaderClick () {
  emit('headerClick')
}

const internalVariant = computed(() => {
  if (props.error) return 'danger'
  else return props.variant
})

const containerVariantClasses = computed(() => {
  const classes = {
    primary: 'tw-border-neutral-200 tw-bg-white',
    neutral: 'tw-border-neutral-200 tw-bg-neutral-50',
    success: 'tw-border-green-200 tw-bg-green-100',
    warning: 'tw-border-orange-200 tw-bg-orange-100',
    danger: 'tw-border-red-200 tw-bg-red-200'
  }

  return classes[internalVariant.value]
})

const headerVariantClasses = computed(() => {
  const classes = {
    primary: 'tw-text-neutral-800',
    neutral: 'tw-text-neutral-500',
    success: 'tw-text-green-800',
    warning: 'tw-text-orange-800',
    danger: 'tw-text-red-800',
  }

  return classes[internalVariant.value]
})

const subtitleVariantClasses = computed(() => {
  const classes = {
    primary: 'tw-text-neutral-500',
    neutral: 'tw-text-neutral-400',
    success: 'tw-text-green-500',
    warning: 'tw-text-orange-500',
    danger: 'tw-text-red-500',
  }

  return classes[internalVariant.value]
})

const bodyVariantClasses = computed(() => {
  const classes = {
    primary: 'tw-text-neutral-900',
    neutral: 'tw-text-neutral-500',
    success: 'tw-text-green-900',
    warning: 'tw-text-orange-900',
    danger: 'tw-text-red-900',
  }

  return classes[internalVariant.value]
})
</script>
