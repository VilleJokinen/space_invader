<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- A small icon button to copy the given contents into the browser's clipboard. -->

<template lang="pug">
MTooltip(:content="tooltipContent" noUnderline)
  meta-button(
    data-cy="copy-to-clipboard"
    :variant="subtle ? 'outline-primary' : 'primary'"
    @click="copy(contents)"
    :subtle="subtle"
    :disabled="disable"
    )
    fa-icon(v-if="subtle" :icon="copied ? 'check' : 'copy'" style="width: 10px" size='xs')
    fa-icon(v-else :icon="copied ? 'check' : 'copy'")
    span(v-if="$slots.default").mr-2
    slot
</template>

<script lang="ts" setup>
import { computed, useSlots } from 'vue'
import { useClipboard } from '@vueuse/core'
import { MTooltip } from '@metaplay/meta-ui-next'

const slots = useSlots()

const props = withDefaults(defineProps<{
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
  subtle?: boolean
}>(), {
  subtle: true,
})

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
const tooltipContent = computed(() => {
  if (!disable.value && !slots.default) {
    return !copied.value ? 'Copy to clipboard' : 'Copied!'
  } else {
    return undefined
  }
})
</script>
