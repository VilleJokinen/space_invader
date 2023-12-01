<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-alert(
  show
  :class="{ 'shadow-sm': !noShadow }"
  :variant="variant"
  :data-cy="dataCy"
  )
    div.font-weight-bolder(role="heading") {{ title }}
    p.m-0 {{ message }}
      slot
</template>

<script lang="ts" setup>
withDefaults(defineProps<{
  /**
   * Title of the alert. Try to keep this short.
   * @example '☠️ Player Deleted'
   */
  title: string
  /**
   * Main body of the alert. You can also use the 'message' slot for complex rendering.
   * @example 'This player is no more.'
   */
  message?: string
  /**
   * Optional: Remove the drop shadow. Good for when the alert is already inside a card.
   */
  noShadow?: boolean
  /**
   * Optional: Styling of the alert.
   * @example: 'danger'
   */
  variant?: 'info' | 'secondary' | 'warning' | 'danger'
  /**
   * Optional: Cypress label for automated testing.
   * @example 'player-deleted-alert'
   */
  dataCy?: string
}>(), {
  message: undefined,
  variant: 'warning',
  dataCy: undefined
})
</script>
