<template lang="pug">
TransitionRoot(as="template" :show="internalOpen")
  Dialog(as="div" class="tw-relative tw-z-10" @close="cancelClicked")
    //- Background
    TransitionChild(as="template" enter="ease-out duration-300" enter-from="opacity-0" enter-to="opacity-100" leave="ease-in duration-200" leave-from="opacity-100" leave-to="opacity-0")
      div(class="tw-fixed tw-inset-0 tw-bg-neutral-500 tw-bg-opacity-75 tw-transition-opacity")

    //- Full screen container
    div(class="tw-fixed tw-inset-0 tw-z-10 tw-overflow-y-auto")
      //- Positioning (bottom on small screens, center on everything else)
      div(class="tw-flex tw-min-h-full tw-items-end tw-justify-center tw-p-4 sm:tw-items-center sm:tw-p-0")
        //- Modal transition
        TransitionChild(as="template" enter="ease-out duration-300" enter-from="opacity-0 translate-y-4 sm:translate-y-0 sm:scale-95" enter-to="opacity-100 translate-y-0 sm:scale-100" leave="ease-in duration-200" leave-from="opacity-100 translate-y-0 sm:scale-100" leave-to="opacity-0 translate-y-4 sm:translate-y-0 sm:scale-95")
          //- Modal container
          DialogPanel(class="tw-relative tw-transform tw-overflow-hidden tw-rounded-lg tw-bg-white tw-shadow-xl tw-transition-all sm:tw-my-8 sm:tw-w-full sm:tw-max-w-lg tw-p-4 sm:tw-px-6 sm:tw-py-5")
            //- Title
            div(class="tw-flex tw-justify-between tw-mb-2")
              DialogTitle(as="h2" class="tw-leading-6 tw-text-neutral-900 tw-overflow-ellipsis tw-overflow-hidden") {{ title }}
              button(
                @click="cancelClicked"
                class="tw-shrink-0 tw-inline-flex tw-justify-center tw-items-center tw-w-7 tw-h-7 tw-rounded hover:tw-bg-neutral-100 active:tw-bg-neutral-200 tw-relative -tw-top-0.5 tw-font-semibold"
              ) X

            //- Body
            div(class="sm:tw-flex sm:tw-space-x-4")
              //- Left panel
              div(class="sm:tw-flex-1 tw-overflow-x-scroll")
                slot Default modal content goes here...

              //- Right panel (optional)
              div(v-if="$slots['right-panel']" class="sm:tw-flex-1 tw-overflow-x-scroll")
                slot(name="right-panel")

            //- Footer
            div(class="tw-flex tw-justify-end tw-pt-6 tw-space-x-2")
              MButton(
                variant="neutral"
                @click="cancelClicked"
                ) Cancel
              MButton(
                @click="okClicked"
                :variant="variant"
                ) Do the thing
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { Dialog, DialogPanel, DialogTitle, TransitionChild, TransitionRoot } from '@headlessui/vue'
import MButton from '../primitives/MButton.vue'
import type { Variant } from '../utils/types'

export interface Props {
  open?: boolean
  title: string
  variant?: Variant
  size?: 'sm' | 'md' | 'lg'
  onOk: () => void
}
const props = withDefaults(defineProps<Props>(), {
  title: 'Modal Title TBD',
  variant: 'primary',
  size: 'md',
})

const emits = defineEmits(['ok', 'cancel'])

async function okClicked () {
  await props.onOk()
  emits('ok')
  internalOpen.value = false
}

function cancelClicked () {
  emits('cancel')
  internalOpen.value = false
}

const internalOpen = ref(true)
watch(() => props.open, (newVal) => {
  internalOpen.value = Boolean(newVal)
})
</script>
