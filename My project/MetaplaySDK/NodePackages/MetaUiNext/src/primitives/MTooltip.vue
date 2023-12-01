<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- https://zagjs.com/components/vue-sfc/tooltip -->

<template lang="pug">
component(
  :is="triggerHtmlElement ?? 'span'"
  v-if="$slots.content !== undefined || content !== undefined"
  )
  //- Tooltip.
  Teleport(to="body")
    Transition
      //- Note: Bootstrap vue modals use a z-index of 1041, so we use 1050 to be safe.
      //- The custom font size 0.8125rem below is right in between tailwind xs(0.75rem) and sm(0.875rem).
      div(
        v-if="api.isOpen"
        v-bind="api.positionerProps"
        class="tw-bg-neutral-800 tw-text-white tw-rounded-lg tw-transition-opacity tw-border-neutral-600 tw-shadow-sm tw-text-xs+"
        style="z-index: 1050; font-weight: 400;"
        )
        //- Arrow.
        div(v-bind="api.arrowProps")
          div(
            v-bind="api.arrowTipProps"
            class="tw-border-t tw-border-l tw-border-neutral-600 -tw-z-10"
            )

        //- Content.
        div(
          v-bind="api.contentProps"
          class="tw-px-2 tw-py-1.5 tw-relative tw-z-20 tw-text-center tw-max-w-fit tw-whitespace-pre-wrap"
          style="max-width: 12.5rem;"
          )
          slot(name="content")
            span {{ contentToDisplay }}

  //- Trigger.
  span(
    v-bind="triggerProps"
    :class="{'tw-border-b tw-border-dashed tw-border-neutral-400': !noUnderline}"
    )
      slot

//- Fallback for when the tooltip is disabled.
component(
  :is="triggerHtmlElement ?? 'span'"
  v-else
  )
  slot
</template>

<script lang="ts" setup>
import * as tooltip from '@zag-js/tooltip'
import { normalizeProps, useMachine } from '@zag-js/vue'

import { computed } from 'vue'

import { makeIntoUniqueKey } from '../utils/generalUtils'

defineOptions({
  compatConfig: {
    ATTR_FALSE_VALUE: 'suppress-warning'
  },
})

const props = defineProps<{
  /**
   * The content to display in the tooltip. You can also use the `content` slot. The tooltip's visibility is
   * controlled by the presence of content. When both the content prop and the `content` slot are undefined the tooltip
   * component will not render, but everything inside its child slot will render.
   * @example 'This is a tooltip.'
   */
  content?: string
  /**
   * Optional: Disable the underline on the trigger.
   */
  noUnderline?: boolean
  /**
   * Optional: The HTML element to use for the trigger. Defaults to `span`.
   * @example 'div'
   */
  triggerHtmlElement?: string
}>()

const [state, send] = useMachine(tooltip.machine({
  id: makeIntoUniqueKey('tooltip'),
  openDelay: 100,
  closeDelay: 100,
  positioning: {
    placement: 'top'
  }
}))

const api = computed(() => {
  return tooltip.connect(state.value, send, normalizeProps)
})

const contentToDisplay = computed((): string | undefined => {
  // Truncate the content if it starts with a word that's over 25 characters.
  if (props.content && props.content.split(' ')[0].length > 25) {
    return props.content.split(' ')[0].slice(0, 25) + '...'
  }

  // Truncate the content if it's over 180 characters.
  if (props.content && props.content.length > 180) {
    return props.content.slice(0, 180) + '...'
  }

  return props.content
})

// Note: The types where broken as of Aug 7th, 2023. So we have to cast to any.
const triggerProps = computed(() => {
  return api.value.triggerProps as any
})
</script>

<style scoped>
[data-part="arrow"] {
  --arrow-background: rgb(38 38 38);
  --arrow-size: 10px;
}

.v-enter-active,
.v-leave-active {
  transition: opacity 0.1s ease;
}

.v-enter-from,
.v-leave-to {
  opacity: 0;
}
</style>
