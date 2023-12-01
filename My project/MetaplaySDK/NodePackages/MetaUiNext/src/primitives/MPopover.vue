<template lang="pug">
span(
  v-bind="{ ...triggerProps, ...$attrs}"
  @click="open"
  )
  slot(name="trigger" :isOpen="api.isOpen")
    //- Trigger.
    MButton Trigger TBD

Teleport(to="body")
  div(v-bind="api.positionerProps")
    div(
      v-bind="api.arrowProps"
      v-show="api.isOpen"
      )
      div(
        v-bind="api.arrowTipProps"
        class="tw-border-t tw-border-l tw-border-neutral-200 tw-z-0"
        )
    div(
      v-bind="api.contentProps"
      class="tw-bg-white tw-border tw-border-neutral-200 tw-rounded-lg tw-shadow-md tw-pt-4 tw-max-w-sm"
      )
      div(
        role="heading"
        class="tw-font-bold tw-text-sm tw-px-4"
        ) {{ title }}
      div(
        v-if="subtitle"
        class="tw-text-neutral-500 tw-text-xs tw-mt-0.5 tw-px-4"
        ) {{ subtitle }}
      div(
        class="tw-mt-2 tw-text-sm"
        )
        slot Content TBD
</template>

<script setup lang="ts">
import * as popover from '@zag-js/popover'
import { normalizeProps, useMachine } from '@zag-js/vue'
import { computed, Teleport } from 'vue'

import { makeIntoUniqueKey } from '../utils/generalUtils'
import MButton from './MButton.vue'

const props = defineProps<{
  /**
   * The title of the popover.
   */
  title: string
  /**
   * Optional: The subtitle of the popover.
   */
  subtitle?: string
}>()

const [state, send] = useMachine(popover.machine({
  id: makeIntoUniqueKey('popover'),
  modal: true,
}))

defineExpose({
  open,
})

function open () {
  api.value.open()
}

const api = computed(() => popover.connect(state.value, send, normalizeProps))

// Broken types.
const triggerProps = computed(() => api.value.triggerProps as any)
</script>

<style scoped>
[data-part="arrow"] {
  --arrow-background: white;
  --arrow-size: 16px;
}
</style>
