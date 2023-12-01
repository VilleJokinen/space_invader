<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
span {{ number }}{{ ordinal }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'

const props = defineProps({
  number: {
    type: Number,
    required: true,
    validator: (number: number) => number > 0
  }
})

const ordinal = computed(() => {
  const s = ['th', 'st', 'nd', 'rd']
  const v = props.number % 100
  return s[(v - 20) % 10] || s[v] || s[0]
})
</script>
