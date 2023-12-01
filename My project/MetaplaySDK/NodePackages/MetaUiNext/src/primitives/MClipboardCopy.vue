<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- A small icon button to copy the given contents into the browser's clipboard. -->

<template lang="pug">
MTooltip(
  :content="tooltipTitle"
  no-underline
  )
  MButton(
    data-test="copy-to-clipboard"
    @click="copy(contents)"
    :size="fullSize ? 'default' : 'text'"
    :disabled="disable"
    )
    //- Icons from https://heroicons.com/
    span(v-if="!fullSize")
      span(v-if="!copied")
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-3 tw-h-3">
          <path fill-rule="evenodd" d="M15.988 3.012A2.25 2.25 0 0118 5.25v6.5A2.25 2.25 0 0115.75 14H13.5v-3.379a3 3 0 00-.879-2.121l-3.12-3.121a3 3 0 00-1.402-.791 2.252 2.252 0 011.913-1.576A2.25 2.25 0 0112.25 1h1.5a2.25 2.25 0 012.238 2.012zM11.5 3.25a.75.75 0 01.75-.75h1.5a.75.75 0 01.75.75v.25h-3v-.25z" clip-rule="evenodd" />
          <path d="M3.5 6A1.5 1.5 0 002 7.5v9A1.5 1.5 0 003.5 18h7a1.5 1.5 0 001.5-1.5v-5.879a1.5 1.5 0 00-.44-1.06L8.44 6.439A1.5 1.5 0 007.378 6H3.5z" />
        </svg>
      span(v-else)
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-3 tw-h-3">
          <path fill-rule="evenodd" d="M18 5.25a2.25 2.25 0 00-2.012-2.238A2.25 2.25 0 0013.75 1h-1.5a2.25 2.25 0 00-2.238 2.012c-.875.092-1.6.686-1.884 1.488H11A2.5 2.5 0 0113.5 7v7h2.25A2.25 2.25 0 0018 11.75v-6.5zM12.25 2.5a.75.75 0 00-.75.75v.25h3v-.25a.75.75 0 00-.75-.75h-1.5z" clip-rule="evenodd" />
          <path fill-rule="evenodd" d="M3 6a1 1 0 00-1 1v10a1 1 0 001 1h8a1 1 0 001-1V7a1 1 0 00-1-1H3zm6.874 4.166a.75.75 0 10-1.248-.832l-2.493 3.739-.853-.853a.75.75 0 00-1.06 1.06l1.5 1.5a.75.75 0 001.154-.114l3-4.5z" clip-rule="evenodd" />
        </svg>

    //- span(v-if="!subtle")
    //-   template(#icon)
    //-     span(v-if="copied")
    //-       <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5">
    //-         <path fill-rule="evenodd" d="M15.988 3.012A2.25 2.25 0 0118 5.25v6.5A2.25 2.25 0 0115.75 14H13.5v-3.379a3 3 0 00-.879-2.121l-3.12-3.121a3 3 0 00-1.402-.791 2.252 2.252 0 011.913-1.576A2.25 2.25 0 0112.25 1h1.5a2.25 2.25 0 012.238 2.012zM11.5 3.25a.75.75 0 01.75-.75h1.5a.75.75 0 01.75.75v.25h-3v-.25z" clip-rule="evenodd" />
    //-         <path d="M3.5 6A1.5 1.5 0 002 7.5v9A1.5 1.5 0 003.5 18h7a1.5 1.5 0 001.5-1.5v-5.879a1.5 1.5 0 00-.44-1.06L8.44 6.439A1.5 1.5 0 007.378 6H3.5z" />
    //-       </svg>
    //-     span(v-else)
    //-       <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5">
    //-         <path fill-rule="evenodd" d="M18 5.25a2.25 2.25 0 00-2.012-2.238A2.25 2.25 0 0013.75 1h-1.5a2.25 2.25 0 00-2.238 2.012c-.875.092-1.6.686-1.884 1.488H11A2.5 2.5 0 0113.5 7v7h2.25A2.25 2.25 0 0018 11.75v-6.5zM12.25 2.5a.75.75 0 00-.75.75v.25h3v-.25a.75.75 0 00-.75-.75h-1.5z" clip-rule="evenodd" />
    //-         <path fill-rule="evenodd" d="M3 6a1 1 0 00-1 1v10a1 1 0 001 1h8a1 1 0 001-1V7a1 1 0 00-1-1H3zm6.874 4.166a.75.75 0 10-1.248-.832l-2.493 3.739-.853-.853a.75.75 0 00-1.06 1.06l1.5 1.5a.75.75 0 001.154-.114l3-4.5z" clip-rule="evenodd" />
    //-       </svg>

    //-   span Copy to Clipboard

    slot
</template>

<script lang="ts" setup>
import { computed, useSlots } from 'vue'
import { useClipboard } from '@vueuse/core'
import MButton from './MButton.vue'
import MTooltip from './MTooltip.vue'

const slots = useSlots()

const props = defineProps<{
  /**
   * Text to copy to the clipboard.
   */
  contents: string
  /**
   * Optional: Disable the copy button.
   */
  disabled?: boolean
  /**
   * Optional: Extra small button styling for secondary in-line actions. Turned on by default for most use cases.
   */
  fullSize?: boolean
}>()

/**
 * Copies the content passed to the system clipboard.
 */
const { copy, copied } = useClipboard({ source: props.contents })

/**
 * Disable copying if the disabled prop is set, or if there is no content to copy.
 */
const disable = computed(() => {
  return props.disabled || !props.contents
})

/**
 * Calculate the tooltip title.
 * Only show tooltip when not disabled and slots are empty.
 */
const tooltipTitle = computed(() => {
  if (!disable.value && !slots.default) {
    return !copied.value ? 'Copy to clipboard' : 'Copied!'
  } else {
    return undefined
  }
})
</script>
