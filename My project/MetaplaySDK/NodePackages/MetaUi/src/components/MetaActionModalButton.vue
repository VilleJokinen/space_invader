<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-button(
  :disabled="actionButtonDisabled"
  :variant="subtle ? `outline-${variant}` : variant"
  :permission="permission"
  :modal="modalId"
  :data-cy="id + '-button'"
  :block="block"
  :subtle="subtle"
  :tooltip="actionButtonTooltip"
  :size="actionButtonSmall ? 'sm' : undefined"
)
  fa-icon(
    v-if="actionButtonIcon"
    :icon="actionButtonIcon"
    :class="{ 'mr-2': actionButtonText }"
    :size="actionButtonSmall ? 'sm' : undefined"
    )

  | {{ actionButtonText }}

  b-modal(
    :size="modalSize || 'md'"
    @show="internalOnShow"
    @hide="emits('hide')"
    @ok="lazyOk"
    centered
    no-close-on-backdrop
    :id="modalId"
    :data-cy="modalId"
    )
    template(#modal-title) {{ modalTitle || actionButtonText }}
      span.small.text-no-transform.text-muted.ml-1
        slot(name="modal-subtitle")

    //- Loading state
    div(v-if="state === 'loading'").w-100.text-center.my-5
      b-spinner(label="Loading...")

    //- Error state
    MErrorCallout(v-if="state === 'error' && displayError" :error="displayError")

    //- Default state
    slot(v-if="state !== 'loading'")
      p Add some content into the default slot.

      div.font-weight-bold.mb-1 Example Subtitle
      div.mb-3 Maybe a form input here?

      div.small.text-muted Remember to use the default text stylings to keep this modal readable.

    template(#modal-footer="{ ok, cancel }")
      div(v-if="onlyClose || state === 'error'")
        meta-button(
          variant="secondary"
          @click="cancel"
          :data-cy="id + '-close'"
          ) Close

      div(v-else)
        meta-button(
          class="mr-2"
          variant="secondary"
          @click="cancel"
          :disabled="state === 'loading'"
          :data-cy="id + '-cancel'"
          ) {{ cancelButtonText }}
        meta-button(
          :variant="variant"
          @click="ok"
          :safety-lock="!noSafetyLock"
          :data-cy="id + '-ok'"
          :disabled="state !== 'default' || okButtonDisabled"
          ).text-white
            fa-icon(v-if="okButtonIcon" :icon="okButtonIcon").mr-2/
            | {{ okButtonText }}
</template>

<script lang="ts" setup>
import type { BvModalEvent } from 'bootstrap-vue'
import { computed, getCurrentInstance, ref } from 'vue'

import { MErrorCallout } from '@metaplay/meta-ui-next'

interface Props {
  /**
   * Unique ID for this action. Used internally and for automated testing labels.
   * @example 'update-broadcast'
   */
  id: string
  /**
   * Optional: Text for the modal's action button. Should be kept as short as possible.
   * @example 'Update'
   */
  actionButtonText?: string
  /**
   * Optional: A tooltip to show for the action button.
   * @example 'Cancel this scan job.'
   */
  actionButtonTooltip?: string
  /**
   * Optional: Whether to disable the action button.
   */
  actionButtonDisabled?: boolean
  /**
   * Use a subtle action button style. Small and out-of-the-way. Should be used with the optional `actionButtonIcon` argument.
   */
  subtle?: boolean
  /**
   * Optional: A Font-Awesome icon to place in the action button.
   * @example 'paper-plane'
   */
  actionButtonIcon?: string
  /**
   * Optional: A custom title for the pop-over modal. Has more room than the button to describe this action.
   * @example 'Update Broadcast Details'
   */
  modalTitle?: string
  /**
   * Optional: Size of the pop-over modal. Defaults to 'md'.
   * @example 'lg'
   */
  modalSize?: 'sm' | 'md' | 'lg' | 'xl'
  /**
   * Optional: A custom label for the pop-over modal's ok-button. Defaults to 'Ok'.
   * @example 'Update'
   */
  okButtonText?: string
  /**
   * Optional: A Font-Awesome icon to place in the pop-over modal's ok-button.
   * @example 'paper-plane'
   * @example ['far', 'window-close']
   */
  okButtonIcon?: string | string[]
  /**
   * Removes the safety-lock from the ok-button. Useful for modals that don't have any serious consequences and thus
   * don't need the safety-lock feature.
   */
  noSafetyLock?: boolean
  /**
   * Optional: A custom label for the pop-over modal's cancel-button. Defaults to 'Cancel'.
   * @example 'Abort'
   */
  cancelButtonText?: string
  /**
   * Optional: Select a custom color variant for the action button. Defaults to 'primary'.
   * @example 'warning'
   */
  variant?: 'primary' | 'secondary' | 'light' | 'dark' | 'warning' | 'danger'
  /**
   * Optional: Set a permission requirement for being able to use this action.
   * @example 'api.broadcasts.edit'
   */
  permission?: string
  /**
   * Function to execute when the ok-button has been pressed. Async functions show a loading spinner.
   * @example
   * async function doSomething {
   *  await gameServerApi.post('/someRoute', someData) // Await a request
   *  showSuccessToast('Did a thing!') // Show visual feedback
   * someSubscription.triggerRefresh() // Force an immediate data refresh
   * }
   */
  onOk: () => Promise<void>
  /**
   * Optional: Function to execute when the modal is opened.
   * @example
   * function initializeModalState () {
   *  modalState.value = getInitialModalState()
   * }
   */
  onShow?: () => void
  /**
   * Optional: Set a full width block styling to the action button.
   */
  block?: boolean
  /**
   * Optional: Hide the default 'Cancel' and 'Ok' buttons in favour of a 'Close' button.
   */
  onlyClose?: boolean
  /**
   * Optional: Disable the 'Ok' button. Good for form validation.
   */
  okButtonDisabled?: boolean
  /**
   * Optional: Smaller action button size.
   */
  actionButtonSmall?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'primary',
  actionButtonText: '',
  actionButtonTooltip: undefined,
  okButtonText: 'Ok',
  cancelButtonText: 'Cancel',
  actionButtonIcon: undefined,
  modalTitle: undefined,
  modalSize: undefined,
  okButtonIcon: undefined,
  permission: undefined,
  onShow: undefined,
})
const modalId = computed(() => props.id + '-modal')

const emits = defineEmits(['show', 'hide'])

const instance = getCurrentInstance()?.proxy

const state = ref<'default' | 'loading' | 'error'>('default')
const displayError = ref<Error>()

async function lazyOk (bvModalEvent: BvModalEvent) {
  // Prevent hiding.
  bvModalEvent.preventDefault()

  // Show loading spinner.
  state.value = 'loading'

  try {
    // Call the modal's action. Try/catch will catch any unhandled errors and show them in the template.
    await props.onOk()

    // Emit & hide.
    // emits('ok')
    const anyInstance = instance as any
    anyInstance?.$bvModal.hide(modalId.value)
  } catch (err: any) {
    // Remember the returned error.
    displayError.value = err
    state.value = 'error'
  }
}

function internalOnShow () {
  state.value = 'default'
  if (props.onShow) {
    props.onShow()
  }
  emits('show')
}
</script>
