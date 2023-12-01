<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(
  v-if="$slots.default"
  :class="['tw-flex tw-justify-center @4xl:tw-space-x-3 m-three-column-parent tw-flex-wrap', {'tw-items-start': !stretchItemsVertically}]"
  )
  slot
</template>

<script setup lang="ts">
defineProps<{
  stretchItemsVertically?: boolean
}>()
</script>

<style>
/* Direct children of the layout container. */
.m-three-column-parent > * {
  @apply tw-mb-3 tw-w-full;
}

/* Force to three columns on 4XL container size. */
@container (min-width: 56rem) {
  .m-three-column-parent > * {
    max-width: calc(33.33% - 0.5rem); /* Single column width is 33.33% of the available size minus one third of the margin between columns. */
  }
}
</style>
