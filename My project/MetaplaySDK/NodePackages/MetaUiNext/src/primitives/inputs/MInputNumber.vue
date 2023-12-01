<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Container.
div(v-bind="api.rootProps")
  //- Label.
  label(
    v-if="label"
    v-bind="labelProps"
    class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900 tw-mb-1"
    ) {{ label }}

  //- Input.
  div(class="tw-relative")

    button(
      v-bind="incrementTriggerProps"
      :class="['tw-absolute tw-top-0.5 tw-right-2 hover:tw-bg-neutral-300 active:tw-bg-neutral-400 tw-rounded', { 'tw-text-red-500': variant === 'danger', 'tw-text-neutral-400 tw-pointer-events-none': disabled || incrementTriggerProps.disabled }]"
      )
      //- Icon from https://heroicons.com/
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
        <path fill-rule="evenodd" d="M14.77 12.79a.75.75 0 01-1.06-.02L10 8.832 6.29 12.77a.75.75 0 11-1.08-1.04l4.25-4.5a.75.75 0 011.08 0l4.25 4.5a.75.75 0 01-.02 1.06z" clip-rule="evenodd" />
      </svg>

    //div(v-bind="api.scrubberProps")

    input(
      v-bind="inputProps"
      :placeholder="placeholder"
      :class="['tw-w-full tw-rounded-md tw-border-0 tw-py-1.5 tw-text-neutral-900 tw-shadow-sm tw-ring-1 tw-ring-inset placeholder:tw-text-neutral-400 focus:tw-ring-2 focus:tw-ring-inset focus:tw-ring-blue-600 sm:tw-text-sm sm:tw-leading-6 disabled:tw-cursor-not-allowed disabled:tw-bg-neutral-50 disabled:tw-text-neutral-500 disabled:ring-neutral-200', variantClasses]"
      :aria-invalid="variant === 'danger'"
      :aria-describedby="hintId"
      )

    button(
      v-bind="decrementTriggerProps"
      :class="['tw-absolute tw-bottom-0.5 tw-right-2 hover:tw-bg-neutral-300 active:tw-bg-neutral-400 tw-rounded', { 'tw-text-red-500': variant === 'danger', 'tw-text-neutral-400 tw-pointer-events-none': disabled || decrementTriggerProps.disabled }]"
      )
      //- Icon from https://heroicons.com/
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
        <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 011.06.02L10 11.168l3.71-3.938a.75.75 0 111.08 1.04l-4.25 4.5a.75.75 0 01-1.08 0l-4.25-4.5a.75.75 0 01.02-1.06z" clip-rule="evenodd" />
      </svg>

    //- Icon.
    div(class="tw-pointer-events-none tw-absolute tw-inset-y-0 tw-right-4 tw-flex tw-items-center tw-pr-3")
      //- Icons from https://heroicons.com/
      <svg v-if="variant === 'danger'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5 tw-text-red-500" aria-hidden="true">
        <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-8-5a.75.75 0 01.75.75v4.5a.75.75 0 01-1.5 0v-4.5A.75.75 0 0110 5zm0 10a1 1 0 100-2 1 1 0 000 2z" clip-rule="evenodd" />
      </svg>
      <svg v-if="variant === 'success'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5 tw-text-green-500" aria-hidden="true">
        <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clip-rule="evenodd" />
      </svg>

  div(
    v-if="hintMessage"
    :id="hintId"
    :class="['tw-text-xs tw-text-neutral-400 tw-mt-1', { 'tw-text-red-400': variant === 'danger' }]"
    ) {{ hintMessage }}
</template>

<script setup lang="ts">
import * as numberInput from '@zag-js/number-input'
import { normalizeProps, useMachine } from '@zag-js/vue'

import { computed, watch } from 'vue'
import { makeIntoUniqueKey } from '../../utils/generalUtils'

const props = withDefaults(defineProps<{
  /**
   * Input value. Can be undefined.
   */
  modelValue?: number
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
   * Optional: Minimum number allowed.
   */
  min?: number
  /**
   * Optional: Maximum number allowed.
   */
  max?: number
  /**
   * Optional: Minimum number of fraction digits to allow.
   */
  minFractionDigits?: number
  /**
   * Optional: Maximum number of fraction digits to allow.
   */
  maxFractionDigits?: number
  /**
   * Optional: Hint message to show below the input.
   */
  hintMessage?: string
  /**
   * Optional: Placeholder text to show in the input. Defaults to 'Enter a number...'
   */
  placeholder?: string
}>(), {
  modelValue: undefined,
  label: undefined,
  variant: 'default',
  min: undefined,
  max: undefined,
  minFractionDigits: undefined,
  maxFractionDigits: 0,
  hintMessage: undefined,
  placeholder: 'Enter a number...',
})

const emit = defineEmits<{
  'update:modelValue': [value?: number]
}>()

const hintId = makeIntoUniqueKey('hint')

const variantClasses = computed(() => {
  switch (props.variant) {
    case 'danger':
      return 'tw-ring-red-400 tw-text-red-400'
    case 'success':
      return 'tw-ring-green-500'
    default:
      return 'tw-ring-neutral-300'
  }
})

// Zag machine options ------------------------------------------------------------------------------------------------

const [state, send] = useMachine(numberInput.machine({
  id: makeIntoUniqueKey('number'),
  value: props.modelValue ? String(props.modelValue) : undefined,
  allowMouseWheel: true,
  disabled: props.disabled,
  min: props.min,
  max: props.max,
  formatOptions: {
    minimumFractionDigits: props.minFractionDigits,
    maximumFractionDigits: props.maxFractionDigits,
  }
}))
const api = computed(() =>
  numberInput.connect(state.value, send, normalizeProps)
)

watch(() => api.value.valueAsNumber, (newValue) => {
  emit('update:modelValue', newValue)
})

watch(() => props.modelValue, (newValue) => {
  api.value.setValue(newValue ?? '')
})

// Types are broken. Sigh.
const labelProps = computed(() => api.value.labelProps as any)
const inputProps = computed(() => api.value.inputProps as any)
const decrementTriggerProps = computed(() => api.value.decrementTriggerProps as any)
const incrementTriggerProps = computed(() => api.value.incrementTriggerProps as any)
</script>
