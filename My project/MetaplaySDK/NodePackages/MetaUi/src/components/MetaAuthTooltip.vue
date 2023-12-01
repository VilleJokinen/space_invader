<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MTooltip(
  :content="tooltipContent"
  :class="[classes, 'tw-inline-block']"
  )
  slot
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useGameServerApiStore } from '@metaplay/game-server-api'
import { MTooltip } from '@metaplay/meta-ui-next'

const props = defineProps<{
  /**
   * Optional: Permission checks. Automatically disables the button with a tooltip if the user is missing the permission.
   */
  permission?: string
  /**
   * Optional: Additional classes to apply on the auth tooltip element.
   */
  classes?: string
  /**
   * Optional: Tooltip text to show when user *does* have the required permission.
   */
  tooltip?: string
}>()

const gameServerApiStore = useGameServerApiStore()

const tooltipContent = computed(() => {
  if (!gameServerApiStore.doesHavePermission(props.permission)) {
    return `You need the ${props.permission} permission to use this feature.`
  } else {
    return props.tooltip
  }
})
</script>
