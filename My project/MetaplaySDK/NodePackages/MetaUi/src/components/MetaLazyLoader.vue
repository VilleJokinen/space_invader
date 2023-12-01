<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- A wrapper for a component that causes the wrapped component to be hidden when it is outside of the viewport. -->

<template lang="pug">
div(ref="root")
  slot(v-if="shouldRender")
  div(v-else)
    div(:style="`min-height: ${placeholderEventHeight}px`")
</template>

<script lang="ts" setup>
import { ref, onMounted, onUnmounted } from 'vue'

const props = withDefaults(defineProps<{
  /**
   * When the wrapped component is outside the viewport, it is rendered as a plain div, this many pixels high.
   */
  placeholderEventHeight?: number
  /**
   * Wrapped component is rendered when it is within this many pixels of the viewport.
   */
  margin?: number
  /**
   * Once the wrapped component has become visible, keep it visible forever. Otherwise it becomes hidden again after 10 seconds.
   */
  remainVisible?: boolean

  renderAfterDelayInMs?: number
  hideAfterDelayInMs?: number
}>(), {
  placeholderEventHeight: 100,
  margin: 500,
  remainVisible: true,
  renderAfterDelayInMs: 250,
  hideAfterDelayInMs: 10000,
})

const root = ref<HTMLElement>()
const shouldRender = ref(false)
const showTimer = ref<ReturnType<typeof setTimeout> | undefined>()
const hideTimer = ref<ReturnType<typeof setTimeout> | undefined>()
let observer: IntersectionObserver

onMounted(() => {
  // Create a new visibility observer
  observer = new IntersectionObserver(([entry]) => {
    // Visibility change callback
    if (entry) {
      // Clear any existing timers
      clearTimeout(showTimer.value)
      clearTimeout(hideTimer.value)

      if (entry.isIntersecting) {
        // Element has just become visible. If it is not already visible
        // then start a short timer and make it visible when that timer
        // expires. This avoid making elements visible if the user scrolls
        // past them super quickly
        if (!shouldRender.value) {
          showTimer.value = setTimeout(() => {
            shouldRender.value = true
            if (props.remainVisible) {
              observer.unobserve(root.value as HTMLElement)
            }
          }, props.renderAfterDelayInMs)
        }
      } else if (!entry.isIntersecting) {
        // Element has just become hidden. If it is not already hidden
        // then start a long timer and make it hidden when that timer
        // expires. This avoids constantly hiding relevant elements as
        // the user scrolls them
        if (shouldRender.value) {
          hideTimer.value = setTimeout(() => {
            shouldRender.value = false
          }, props.hideAfterDelayInMs)
        }
      }
    }
  }, {
    // Apply a margin to the observation checks
    rootMargin: `${props.margin}px`,
    root: document
  })

  // Subscribe the observer to this element
  observer.observe(root.value as HTMLElement)
})

onUnmounted(() => {
  observer.disconnect()
  // Clear any existing timers
  clearTimeout(showTimer.value)
  clearTimeout(hideTimer.value)
})
</script>
