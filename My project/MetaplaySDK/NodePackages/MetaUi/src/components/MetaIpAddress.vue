<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A small component to display humanized IP-addresses. -->

<template lang="pug">
span {{ displayIpAddress }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'

const props = defineProps<{
  /**
   * The original IP adress to display.
   * @example '198.168.1.1'
   */
  ipAddress: string
}>()

const displayIpAddress = computed(() => {
  if (props.ipAddress === '::1') {
    return 'Localhost'
  } else {
    return props.ipAddress
  }
})
</script>
