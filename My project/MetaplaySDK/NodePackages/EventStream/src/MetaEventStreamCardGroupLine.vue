<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!--
A child component of MetaEventStreamCard.vue
-->

<template lang="pug">
div(v-if="variant === 'newest-terminated'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Horizontal terminator (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; fill: currentColor")
    rect(x="3.5" y="5" width="3" height="1")
  //- Vertical line (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="5" width="1" height="5")

// TODO: something distinct
div(v-else-if="variant === 'newest-unterminated'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Horizontal terminator (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; fill: currentColor")
    rect(x="3.5" y="5" width="3" height="1")
  //- Vertical line (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="5" width="1" height="5")

div(v-else-if="variant === 'oldest-terminated'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Horizontal terminator (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; fill: currentColor")
    rect(x="3.5" y="5" width="3" height="1")
  //- Vertical line (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="0" width="1" height="5")

// TODO: something distinct
div(v-else-if="variant === 'oldest-unterminated'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Horizontal terminator (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; fill: currentColor")
    rect(x="3.5" y="5" width="3" height="1")
  //- Vertical line (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="0" width="1" height="5")

div(v-else-if="variant === 'both-terminated'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Horizontal top terminator (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; fill: currentColor")
    rect(x="3.5" y="0" width="3" height="1")

  //- Horizontal bottom terminator (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; fill: currentColor")
    rect(x="3.5" y="10" width="3" height="1")

  //- Vertical line (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="2.5" width="1" height="5.5")

div(v-else-if="variant === 'skip'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Angled terminators (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; fill: currentColor")
    rect(x="3.5" y="4" width="3" height="1" transform="rotate(-25, 3.5, 4)")
    rect(x="3.5" y="6" width="3" height="1" transform="rotate(-25, 3.5, 6)")
  //- Vertical lines (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="0" width="1" height="4")
    rect(x="4.5" y="6" width="1" height="4")

div(v-else-if="variant === 'line'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Vertical line (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="0" width="1" height="10")

div(v-else-if="variant === 'grouped-event'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Circle (absolute position)
  svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 32.5%; width: 35%; fill: currentColor")
    circle(cx="5" cy="5" r="4.5")
  //- Vertical line (stretch)
  svg(viewBox="0 0 10 10" xmlns="http://www.w3.org/2000/svg" preserveAspectRatio="none" style="fill: currentColor; z-index: 1")
    rect(x="4.5" y="0" width="1" height="10")

div(v-else-if="variant === 'root-event'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  //- Circle (absolute position)
  //svg(viewBox="0 0 10 10" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" style="position: absolute; top: 0; left: 32.5%; width: 35%; fill: currentColor")
    circle(cx="5" cy="5" r="4.5")

div(v-else-if="variant === 'error'" style="position: relative").h-100.d-flex.justify-content-center.text-success
  small variant error

</template>

<script lang="ts" setup>
defineProps({
  variant: {
    type: String,
    required: true,
    validator: (input: string) => ['newest-terminated', 'newest-unterminated', 'oldest-terminated', 'oldest-unterminated', 'both-terminated', 'line', 'skip', 'grouped-event', 'root-event', 'error'].includes(input)
  }
})
</script>
