<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(class="tw-flex tw-h-screen")
  //- Sidebar
  div(:class="['tw-basis-56 tw-flex tw-flex-col tw-shrink-0 tw-border-r-2 tw-border-neutral-200 tw-bg-white tw-transition-all tw-duration-300 tw-overflow-y-auto', { '-tw-mr-56 -tw-translate-x-56': !showSidebar }]")

    //- Sidebar header
    div(class="tw-flex tw-items-center tw-basis-14 tw-shrink-0 tw-space-x-2 tw-px-4")
      slot(name="sidebar-header")
        div(class="tw-flex tw-items-center tw-justify-center tw-bg-green-500 tw-w-10 tw-h-10 tw-rounded-lg")
          MetaplayMonogram(class="tw-w-6 tw-fill-white")
        span(role="heading" class="tw-font-semibold tw-text-xl") {{ projectName }}

    //- Sidebar content
    div(class="tw-space-y-2 tw-py-3 tw-grow tw-flex tw-flex-col tw-justify-between")
      slot(
        name="sidebar"
        :closeSidebarOnNarrowScreens="closeSidebarOnNarrowScreens"
        )

      //- Bottom.
      div(class="tw-pt-6")
        MetaplayLogo(class="tw-mx-auto tw-mb-3 tw-fill-neutral-200 tw-h-9")

  //- Right side.
  div(class="tw-grow tw-relative tw-flex tw-flex-col tw-z-0 tw-overflow-hidden sm:tw-overflow-auto")
    //- Overlay on mobile.
    div(
      v-show="showSidebar"
      @click.stop="showSidebar = !showSidebar"
      class="tw-absolute tw-inset-0 tw-bg-black tw-opacity-50 tw-cursor-pointer tw-z-50 tw-transition-colors sm:tw-hidden tw-touch-none"
      )

    //- Header bar.
    div(
      class="tw-flex tw-basis-14 tw-shrink-0 tw-border-b-2 tw-border-neutral-200 tw-shadow tw-items-center tw-justify-between tw-px-3 tw-space-x-3"
      :style="'min-width: 415px; background-color: ' + headerBackgroundColorString"
      )
      //- Left side.
      div(:class="['tw-flex tw-space-x-3 tw-min-w-0 tw-items-center', { 'tw-text-neutral-800': !headerLightTextColor, 'tw-text-neutral-50': headerLightTextColor }]")
        //- Burger button.
        div(class="tw-rounded hover:tw-bg-neutral-200 active:tw-bg-neutral-300 tw-cursor-pointer tw-w-6 tw-h-6 tw-relative")
          transition(name="close")
            button(
              v-show="showSidebar"
              title="sidebar"
              @click="showSidebar = !showSidebar"
              style="top: 2px; left: 2px"
              class="tw-flex tw-absolute"
              )
                //- Icon from https://heroicons.com/ under the MIT license.
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5">
                  <path fill-rule="evenodd" d="M15.79 14.77a.75.75 0 01-1.06.02l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 111.04 1.08L11.832 10l3.938 3.71a.75.75 0 01.02 1.06zm-6 0a.75.75 0 01-1.06.02l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 111.04 1.08L5.832 10l3.938 3.71a.75.75 0 01.02 1.06z" clip-rule="evenodd" />
                </svg>

          transition(name="open")
            button(
              v-show="!showSidebar"
              title="sidebar"
              @click="showSidebar = !showSidebar"
              style="top: 2px; left: 2px"
              class="tw-flex tw-absolute"
              )
                //- Icon from https://heroicons.com/ under the MIT license.
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5">
                  <path fill-rule="evenodd" d="M2 4.75A.75.75 0 012.75 4h14.5a.75.75 0 010 1.5H2.75A.75.75 0 012 4.75zM2 10a.75.75 0 01.75-.75h14.5a.75.75 0 010 1.5H2.75A.75.75 0 012 10zm0 5.25a.75.75 0 01.75-.75h14.5a.75.75 0 010 1.5H2.75a.75.75 0 01-.75-.75z" clip-rule="evenodd" />
                </svg>

        //- Title label.
        // TODO: How to limit this to not grow with content?
        div(
          role="heading"
          class="tw-font-semibold tw-text-lg tw-truncate tw-min-w-0"
          data-cy="header-bar-title"
          ) {{ title }}

      //- Right side.
      div(class="tw-flex tw-items-center tw-space-x-3 tw-shrink-0")
        //- User name.
        span(
          v-if="headerBadgeLabel"
          class="tw-hidden sm:tw-inline"
          )
          MBadge(variant="neutral") {{ headerBadgeLabel }}

        //- User avatar.
        div(
          v-if="headerAvatarImageUrl || headerBadgeLabel"
          class="tw-rounded-full tw-bg-neutral-100 tw-border tw-border-neutral-200 tw-border-opacity-25 hover:tw-brightness-90 active:tw-brightness-75 tw-cursor-pointer tw-w-9 tw-h-9 tw-flex tw-items-center tw-justify-center"
          @click="$emit('headerAvatarClick')"
          role="link"
          )
          img(
            v-if="headerAvatarImageUrl"
            :src="headerAvatarImageUrl"
            class="tw-rounded-full"
            )
          div(
            v-else
            )
            <svg xmlns="http://www.w3.org/2000/svg" height="1em" viewBox="0 0 448 512" class="tw-fill-neutral-800">
              <!--! Font Awesome Free 6.4.2 by @fontawesome - https://fontawesome.com License - https://fontawesome.com/license (Commercial License) Copyright 2023 Fonticons, Inc. -->
              <path d="M224 256A128 128 0 1 0 224 0a128 128 0 1 0 0 256zm-45.7 48C79.8 304 0 383.8 0 482.3C0 498.7 13.3 512 29.7 512H418.3c16.4 0 29.7-13.3 29.7-29.7C448 383.8 368.2 304 269.7 304H178.3z"/>
            </svg>

    //- Content container.
    div(
      class="tw-overflow-scroll tw-grow tw-bg-neutral-50 tw-relative"
      style="min-width: 415px;"
      data-cy="page-content-container"
      )
      slot
</template>

<script setup lang="ts">
import MetaplayMonogram from '../../assets/MetaplayMonogram.vue'
import MBadge from '../../primitives/MBadge.vue'
import MetaplayLogo from '../../assets/MetaplayLogo.vue'

import { useHeaderbar } from './useMRootLayoutHeader'

import { computed, onMounted, ref } from 'vue'

const props = withDefaults(defineProps<{
  /**
   * The name of the project to display in the sidebar header.
   */
  projectName?: string
  headerBadgeLabel?: string
  headerAvatarImageUrl?: string
  headerBackgroundColorString?: string
}>(), {
  projectName: undefined,
  headerBadgeLabel: undefined,
  headerAvatarImageUrl: undefined,
  headerBackgroundColorString: '#FFFFFF'
})

defineEmits(['headerAvatarClick'])

const { title } = useHeaderbar()

const showSidebar = ref(true)

onMounted(() => {
  // Start with the sidebar closed on narrow browsers.
  // Note: It's important to do this in a mounted hook so that the SSR output does not break.
  showSidebar.value = Math.max(document.documentElement.clientWidth || 0, window.innerWidth || 0) > 768
})

function closeSidebarOnNarrowScreens () {
  if (Math.max(document.documentElement.clientWidth || 0, window.innerWidth || 0) < 768) {
    showSidebar.value = false
  }
}

const headerLightTextColor = computed(() => {
  // Return true if the header background color is dark enough to have better contrast with light text.
  // See https://stackoverflow.com/a/41491220/1243212
  if (!props.headerBackgroundColorString) {
    return false
  }
  const hex = props.headerBackgroundColorString.replace('#', '')
  const c = hex.length === 3
    ? hex.split('').map((x) => x + x)
    : hex.match(/.{2}/g)
  if (!c) {
    return false
  }
  const r = parseInt(c[0], 16)
  const g = parseInt(c[1], 16)
  const b = parseInt(c[2], 16)
  const brightness = (r * 299 + g * 587 + b * 114) / 1000
  return brightness < 125
})
</script>

<style scoped>
.close-enter-active,
.open-enter-active {
  transition: transform .5s ease, opacity .5s;
}

.close-leave-active,
.open-leave-active {
  transition: transform .3s ease, opacity .3s;
}

.close-enter-from,
.close-leave-to {
  opacity: 0;
  transform: translateX(-40px);
}

.open-enter-from,
.open-leave-to {
  opacity: 0;
  transform: translateX(10px);
}
</style>
