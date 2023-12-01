<template lang="pug">
MCard(
  :title="title"
  :subtitle="subtitle"
  :badge="badge"
  :badgeVariant="isOpen ? 'primary' : 'neutral'"
  clickableHeader
  noBodyPadding
  @headerClick="isOpen = !isOpen"
  )
  template(#header-right)
    slot(name="header-right")

  template(#subtitle)
    slot(name="subtitle")

  template(#icon)
    //- Icon from https://heroicons.com/
    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" :class="['tw-w-5 tw-h-5 tw-transition-transform tw-relative', { 'tw-rotate-90': isOpen }]" style="bottom: 1px">
      <path stroke-linecap="round" stroke-linejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
    </svg>

  MTransitionCollapse
    div(
      v-if="isOpen"
      )
      slot Content TBD

</template>

<script lang="ts" setup>
import { ref } from 'vue'

import MCard from '../primitives/MCard.vue'
import MTransitionCollapse from '../primitives/MTransitionCollapse.vue'

const isOpen = ref(false)

defineProps<{
  title: string
  subtitle?: string
  badge?: string | number
}>()
</script>
