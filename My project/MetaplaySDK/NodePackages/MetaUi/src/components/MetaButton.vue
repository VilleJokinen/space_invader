<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A wrapper for the standard b-link and b-button components to add permission handling, styling and the safety lock feature. -->

<template lang="pug">
meta-auth-tooltip(:permission="permission" :classes="block ? 'w-100 ' + classes : classes" :tooltip='tooltip')
  b-link(
    v-if="link"
    v-b-modal="modal"
    :disabled="disabled || !gameServerApiStore.doesHavePermission(permission) || !isButtonEnabled"
    :to="to"
    :href="href"
    :target="target"
    @click.stop="$emit('click')"
    :class="{ 'success-link-override': variant === 'success' }"
    :data-cy="dataCy"
    )
      slot

  span(v-else).lock-base.d-flex
    div(
      v-if="safetyLock"
      :class="{ 'lock-locked': !isButtonEnabled && safetyLock, 'lock-unlocked': isButtonEnabled && safetyLock }"
      @click="isButtonEnabled = !isButtonEnabled"
      data-cy="safety-lock-button"
      ).lock-color
    MTooltip(
      :content="!isButtonEnabled ? 'Disable the safety lock to perform this action.' : undefined"
      style="border-radius: 10rem; z-index: 1"
      :class="{ 'w-100': block }"
      )
        b-button(
          :variant="variant"
          :disabled="isButtonDisabled"
          :to="isButtonDisabled ? '' : to"
          :href="isButtonDisabled ? '' : href"
          :target="target"
          :size="size"
          :block="block"
          @click.stop="$emit('click'); onClick()"
          :class="{ 'subtle-button p-1': subtle, 'text-light': variant === 'warning' }"
          :data-cy="dataCy"
          :safety-lock-active-cy="(safetyLock && uiStore.isSafetyLockOn) ? 'yes' : 'no'"
          )
            slot
    fa-icon(
      v-if="safetyLock"
      :icon="!isButtonEnabled ? 'lock' : 'lock-open'"
      size="sm"
      class="safety-icon"
      ).ml-2.mr-3.text-light

</template>

<script lang="ts" setup>
import { computed, getCurrentInstance, ref } from 'vue'
import { MTooltip } from '@metaplay/meta-ui-next'
import { useGameServerApiStore } from '@metaplay/game-server-api'

import { useUiStore } from '../uiStore'

const props = defineProps<{
  /**
   * Optional: Enables permission checks. Automatically disables the button with a tooltip if the user is missing the permission.
   */
  permission?: string
  /**
   * Optional: Variant prop that gets passed to the underlying b-button. Defaults to 'secondary' variant style and ignored if 'link' prop is true.
   */
  variant?: 'primary' | 'secondary' | 'success' | 'danger' | 'warning' | 'info' | 'light' | 'dark' | 'outline-primary' | 'outline-secondary' | 'outline-success' | 'outline-danger' | 'outline-warning' | 'outline-info' | 'outline-light' | 'outline-dark'
  /**
   * Optional: Disables the button.
   */
  disabled?: boolean
  /**
   * Optional: Tooltip text.
   */
  tooltip?: string
  /**
   * Optional: Size prop that gets passed to the underlying button. Ignored if the 'link' prop is true.
   */
  size?: string
  /**
   * Optional: Modal ID to open on click.
   */
  modal?: string
  /**
   * Optional: Router link to open on click.
   */
  to?: string
  /**
   * Optional: Additional classes to apply on the auth tooltip element.
   */
  classes?: string
  /**
   * Optional: Sets the button width to 100%.
   */
  block?: boolean
  /**
   * Optional: Extra small button styling for secondary in-line actions. Pairs well with an icon instead of text labels.
   */
  subtle?: boolean
  /**
   * Optional: A vanilla HTML URL to navigate to on click. Use the 'to' prop instead when navigating inside the dashboard.
   */
  href?: string
  /**
   * Optional: Target prop that gets passed to the underlying element.
   */
  target?: string
  /**
   * Optional: Renders a link instead of a button.
   */
  link?: boolean
  /**
   * Optional: Disables the button if the safety lock is on. Ignored if the 'link' prop is true.
   */
  safetyLock?: boolean
  /**
   * Optional: Data-cy attribute for testing.
   */
  dataCy?: string
}>()

defineEmits(['click'])

const uiStore = useUiStore()
const gameServerApiStore = useGameServerApiStore()

/**
 * Initial status of the safety lock. Not a computed variable on purpose.
 */
const isButtonEnabled = ref(!uiStore.isSafetyLockOn || !props.safetyLock)

const isButtonDisabled = computed(() => {
  return props.disabled ||
    !gameServerApiStore.doesHavePermission(props.permission ?? '') ||
    !isButtonEnabled.value
})

const vueInstance = getCurrentInstance()?.proxy
const onClick = () => {
  if (props.modal) {
    const anyVueInstance = vueInstance as any
    anyVueInstance?.$bvModal.show(props.modal)
  }
}

</script>

<style scoped>
.success-link-override {
  color: var(--metaplay-green-darker);
}

.success-link-override:hover {
  color: #5b9e10;
}

.lock-base {
  border-radius: 10rem;
  transition: background-color 0.1s;
  position: relative;
}

.lock-color {
  position: absolute;
  width: 60%;
  height: 100%;
  border-radius: 0 10rem 10rem 0;
  left: 40%;
  z-index: 0;
}

.lock-locked {
  background-color: var(--metaplay-red);
  box-shadow: 0 3px 1px #f9431c inset, 0 -2px 1px rgba(255, 255, 255, 0.25) inset;
}

.lock-locked:hover {
  background-color: #f53106;
  box-shadow: 0 3px 1px #d32a05 inset, 0 -2px 1px rgba(255, 255, 255, 0.25) inset;
}

.lock-locked:active {
  background-color: #e22d06;
  box-shadow: 0 3px 1px #d32a05 inset, 0 -2px 1px rgba(255, 255, 255, 0.25) inset;
}

.lock-unlocked {
  background-color: var(--metaplay-green-dark);
  box-shadow: 0 3px 1px #66ab2e inset, 0 -2px 1px rgba(255, 255, 255, 0.25) inset;
}

.lock-unlocked:hover {
  background-color: #64a72d;
  box-shadow: 0 3px 1px #589327 inset, 0 -2px 1px rgba(255, 255, 255, 0.25) inset;
}

.lock-unlocked:active {
  background-color: #5a9728;
  box-shadow: 0 3px 1px #589327 inset, 0 -2px 1px rgba(255, 255, 255, 0.25) inset;
}

.safety-icon{
  padding-top: .68rem;
  width: .9rem;
  z-index: 1;
  pointer-events: none
}

@media (max-width: 767.98px) {
 .lock-locked, .lock-color {
    margin-top: 4px;
    height: 90%;
  }
  .safety-icon{
    padding-top: .98rem;
  }
}

button.disabled {
  opacity: 1;
  filter: saturate(40%) brightness(110%);
}
</style>
