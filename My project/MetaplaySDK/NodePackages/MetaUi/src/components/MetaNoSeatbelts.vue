<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- A minimal component to display a standard disclaimer. -->

<template lang="pug">
b-alert(:variant=" variant ? variant : 'warning'" show).text-center
  p.m-0
    span.font-weight-bold ⚠️ No seatbelts!
    span.p-1(v-if="message")  {{ message }}
    span(v-else)  This action can't be undone.
  span(v-if="name").ml-1 Are you sure #[MBadge(variant="warning") {{ name }}] is ok with this?
</template>

<script lang="ts" setup>
import { MBadge } from '@metaplay/meta-ui-next'
defineProps<{
  /**
   * Optional: Alternative variant color for the alert box.
   */
  variant?: string
  /**
   * Optional:  The name of the entity that an action is being performed upon. E.g a player.
   */
  name?: string
  /**
   * Optional: Content to be displayed in the alert box.
   */
  message?: string
}>()
</script>
