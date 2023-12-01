<template lang="pug">
//- Container
li(
  :class="['tw-block tw-py-3', { 'hover:tw-bg-neutral-200 active:tw-bg-neutral-300 tw-cursor-pointer': clickable, 'even:tw-bg-neutral-100': striped }]"
  )
  //- List item container
  div(class="tw-flex tw-space-x-1")
    //- Optional avatar
    img(
      v-if="avatarUrl"
      :src="avatarUrl"
      class="tw-h-10 tw-w-10 tw-rounded-full"
    )
    div(class="tw-grow tw-space-y-0.5")
      //- Top row
      div(class="tw-flex tw-flex-wrap tw-justify-between tw-items-baseline")
        //- Left
        div(class="tw-flex tw-flex-wrap tw-space-x-1 tw-text-sm+")
          span(
            role="heading"
            :class="['tw-text-ellipsis tw-overflow-hidden', { 'tw-font-semibold': !condensed, 'tw-text-xs+': condensed } ]"
            )
            slot(name="default")

          span(class="tw-flex tw-space-x-1")
            slot(name="badge")

        //- Right
        span(:class="['tw-shrink-0 tw-text-right tw-text-sm', { 'tw-text-xs+': condensed }]")
          slot(name="top-right")

      //- Bottom row
      //- Custom max breakpoint below needs some fine tuning. Storybook not as working as expected.
      div(class="tw-flex max-[350px]:tw-flex-wrap tw-justify-between tw-text-neutral-500 min-[350px]:tw-space-x-20 tw-text-xs+")
        //- Left
        span
          slot(name="bottom-left")

        //- Right
        span(class="tw-shrink-0 tw-text-right tw-flex-grow")
          slot(name="bottom-right")
</template>

<script lang="ts" setup>
// - TODO: make prop for condensed for top left title to be more like generated ui dictionary.
const props = withDefaults(defineProps<{
  clickable?: boolean
  avatarUrl?: string
  striped?: boolean
  condensed?: boolean
}>(), {
  avatarUrl: undefined,
  condensed: false
})
</script>
