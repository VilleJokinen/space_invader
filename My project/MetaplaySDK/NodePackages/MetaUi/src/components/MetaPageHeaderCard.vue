<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- An opinionated compnent for displaying consistent page header cards. -->

<template lang="pug">
div
  b-card.shadow(:class="{ 'bg-light': bgLight }")
    div.d-flex.mb-2
      b-avatar(v-if="avatar" size="lg" :src="avatar").mr-2.border
      div(style="flex-basis: 100%")
        b-row(no-gutters align-v="center" align-h="between")
          b-row(no-gutters align-v="center")
            h3.m-0.text-break-word
              slot(name="title") {{ title }}
            div
              slot(name="title-badge")

          div.text-muted(v-if="id") ID: {{ id }} #[meta-clipboard-copy(:contents="id")]

        div
          slot(name="subtitle")

    div
      slot
        p 🚧 Card content missing!

    div.text-right.mt-4(v-if="$slots.buttons" style="line-height: 260%;")
      slot(name="buttons")

  div.w-100.text-right(v-if="$slots.caption")
    span.small.text-muted.mr-4
      slot(name="caption")
</template>

<script lang="ts" setup>
withDefaults(defineProps<{
  /**
   * A title to be displayed at the top of the card.
   * @example 'Broadcasts'
   */
  title?: string
  /**
   * Optional: An ID string to be show on the card with a copy-to-clipboard button.
   * @example 'Player:ZArvpuPqNL'
   */
  id?: string
  /**
   * Optional: Use an alternative background style for inactive pages.
   */
  bgLight?: boolean
  /**
   * Optional content for an avatar icon.
   * @example: http://placekitten.com/256/256
   */
  avatar?: string
}>(), {
  title: "🚧 Missing 'title' property",
  id: undefined,
  avatar: undefined
})

</script>
