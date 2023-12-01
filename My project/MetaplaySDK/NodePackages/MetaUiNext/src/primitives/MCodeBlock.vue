<template lang="pug">
div(
  class="tw-border tw-border-neutral-200 tw-rounded tw-bg-neutral-100 tw-text-neutral-600 tw-px-2 tw-py-1"
  )
  //- Header
  div(
    v-if="language || fileName"
    class="tw-flex tw-items-center tw-justify-between tw-text-xs tw-text-neutral-500 tw-mb-1"
    )
    h4(class="tw-font-mono") {{ fileName }}
    aside {{ language }}

  //- Code
  div(
    :class="['tw-whitespace-pre tw-text-sm tw-font-mono tw-overflow-scroll', { 'tw-max-h-60': limitHeight }]"
    )
    slot
</template>

<script setup lang="ts">
defineProps<{
  language?: string
  fileName?: string
  limitHeight?: boolean
}>()
</script>
