<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Container.
label(v-bind="api.rootProps")
  //- Label.
  div(
    v-if="label"
    class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900 tw-mb-1"
    v-bind="api.labelProps"
    ) {{ label }}

  //- Input.
  // TODO: Consider keyboard navigation and focus states.
  div(class="tw-flex tw-items-top")
    div(
      v-bind="{ ...$attrs, ...api.controlProps }"
      :class="['tw-text-white tw-shrink-0 tw-border tw-w-5 tw-h-5 tw-shadow-inner tw-rounded tw-flex tw-items-center tw-justify-center tw-relative tw-top-0.5', checkboxVariantClasses]"
      )
      <svg v-if="api.isChecked" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5">
        <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 01.143 1.052l-8 10.5a.75.75 0 01-1.127.075l-4.5-4.5a.75.75 0 011.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 011.05-.143z" clip-rule="evenodd" />
      </svg>
    span(:class="['tw-ml-2', descriptionVariantClasses]")
      slot {{ description }}

  input(v-bind="api.hiddenInputProps")

  //- Hint message.
  div(
    v-if="hintMessage"
    :class="['tw-text-xs tw-text-neutral-400 tw-mt-1', { 'tw-text-red-400': variant === 'danger' }]"
    ) {{ hintMessage }}
</template>

<script setup lang="ts">
import { computed, watch } from 'vue'
import { makeIntoUniqueKey } from '../../utils/generalUtils'
import * as checkbox from '@zag-js/checkbox'
import { normalizeProps, useMachine } from '@zag-js/vue'

defineOptions({
  inheritAttrs: false
})

const props = withDefaults(defineProps<{
  /**
   * The value of the input. Can be undefined.
   */
  modelValue: boolean
  /**
   * Optional: Show a label for the input.
   */
  label?: string
  /**
   * Optional: Disable the input. Defaults to false.
   */
  disabled?: boolean
  /**
   * Optional: Visual variant of the input. Defaults to 'default'.
   */
  variant?: 'default' | 'danger' | 'success'
  /**
   * Optional: Hint message to show below the input.
   */
  hintMessage?: string
  /**
   * Optional: Text to show next to the checkbox.
   */
  description?: string
}>(), {
  modelValue: undefined,
  label: undefined,
  variant: 'default',
  hintMessage: undefined,
  description: undefined,
})

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

/**
 * Helper to get variant specific classes.
 */
const checkboxVariantClasses = computed(() => {
  if (props.disabled) {
    if (api.value.isChecked) return 'tw-border-neutral-200 tw-bg-neutral-300'
    else return 'tw-border-neutral-200 tw-bg-neutral-50'
  }

  if (api.value.isChecked) {
    switch (props.variant) {
      case 'danger':
        return 'tw-border-red-400 tw-bg-red-500'
      case 'success':
        return 'tw-border-green-500 tw-bg-green-500'
      default:
        return 'tw-border-blue-300 tw-bg-blue-500'
    }
  } else {
    switch (props.variant) {
      case 'danger':
        return 'tw-border-red-400 tw-bg-neutral-50'
      case 'success':
        return 'tw-border-green-500 tw-bg-neutral-50'
      default:
        return 'tw-border-neutral-300 tw-bg-neutral-50'
    }
  }
})

/**
 * Helper to get variant specific classes.
 */
const descriptionVariantClasses = computed(() => {
  if (props.disabled) return 'tw-text-neutral-400'
  if (props.variant === 'danger') return 'tw-text-red-400'
  else return 'tw-text-inherit'
})

// Zag Checkbox -------------------------------------------------------------------------------------------------------

const id = makeIntoUniqueKey('checkbox')

const [state, send] = useMachine(checkbox.machine({
  id,
  checked: props.modelValue,
  disabled: props.disabled,
  onCheckedChange: (details) => emit('update:modelValue', details.checked !== false),
}))

const api = computed(() =>
  checkbox.connect(state.value, send, normalizeProps),
)

// Watch for prop updates.
watch(() => props.modelValue, (newValue) => {
  api.value.setChecked(newValue)
})
</script>
