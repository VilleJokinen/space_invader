<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A simple component to visualize the standard error message format in an alert. -->

<template lang="pug">
MCallout(
  :title="displayError.title"
  variant="danger"
  data-test="error-callout"
  )
  template(#badge v-if="displayError?.badgeText")
    MBadge(variant="danger") {{ displayError.badgeText }}

  span {{ displayError.message }}
  MList(
    v-if="displayError?.details && displayError?.details.length > 0"
    class="tw-mt-3"
    variant="danger"
    showBorder
    )
    div(v-for="detail in displayError.details")
      MCollapse(extraMListItemMargin extraMCalloutMargin variant="danger")
        template(#header)
          MListItem
            span(v-if="detail.title") {{ detail.title }}
            //- TODO: Removed for now, pending future redesign.
            // template(#bottom-left)
              span(class="tw-font-mono tw-text-red-900") {{ getPreviewString(detail.content) }}

        //- Content
        div(v-if="detail.content"
          class="tw-font-mono tw-border tw-border-neutral-300 tw-rounded tw-bg-neutral-100 tw-text-neutral-600 tw-max-h-80 tw-overflow-y-auto tw-p-2 tw-text-xs tw-mx-3 tw-mb-3"
          )
          pre {{ getDisplayString(detail.content) }}

  div(
    v-else
    class="tw-font-mono tw-text-red-600 tw-text-xs tw-mt-2 tw-ml-0.5"
    ) No details available.

  template(#buttons)
    MClipboardCopy(
      fullSize
      :contents="JSON.stringify(displayError, null, 2)"
      ) Copy to Clipboard
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { createDisplayError, DisplayError } from '../utils/DisplayErrorHandler'
import MBadge from './MBadge.vue'
import MCallout from './MCallout.vue'
import MCollapse from './MCollapse.vue'
import MList from './MList.vue'
import MListItem from './MListItem.vue'
import MClipboardCopy from './MClipboardCopy.vue'

const props = defineProps<{
  /**
   * The error object that we are interested in.
   */
  error: Error | DisplayError
}>()

/**
 * The error that is to be display.
 * If the error is in the correct format it will be displayed as is.
 * Otherwise we map it to 'DisplayError' format.
 */
const displayError = computed(() => {
  if (props.error instanceof Error) {
    return createDisplayError(props.error)
  } else if (props.error instanceof DisplayError) {
    return props.error
  } else {
    // console.error('ErrorCallout: Invalid error format', JSON.stringify(props.error, null, 2))
    return createDisplayError(new Error(JSON.stringify(props.error, null, 2)))
  }
})

/**
 * Transforms the raw error into a string format for display.
 * If the error is an array or object, it joins the elements into a single string.
 */
function getDisplayString (rawError: string | string[] | { [key: string]: any }): string {
  if (typeof rawError === 'string') return rawError
  if (Array.isArray(rawError)) return rawError.join('\n')
  return Object.entries(rawError).map((row) => {
    const key = row[0]
    const rawValue = row[1]
    const isObject = typeof rawValue === 'object' && rawValue !== null
    const value = isObject ? JSON.stringify(rawValue) : rawValue
    return `${key}: ${value}`
  }).join('\n')
}

// TODO: Review desgin for preview text.
/**
 * Returns a preview of the error message, truncating it to 100 characters if it exceeds this length.
function getPreviewString (rawError: string | string[] | { [key: string]: any }): string {
  const displayString = getDisplayString(rawError)

  if (displayString.length > 100) {
    return displayString.slice(0, 100) + '...'
  } else {
    return displayString
  }
} */

</script>
