<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div(
    role="heading"
    class="tw-px-4 tw-text-neutral-700 tw-font-bold tw-mb-1"
    ) {{ title }}
  ul(class="tw-divide-y tw-divide-neutral-200")
    slot
</template>

<script lang="ts" setup>
defineProps<{
  title: string
}>()
</script>
