<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Container.
div
  //- Label.
  label(
    v-if="label"
    :for="id"
    class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900 tw-mb-1"
    ) {{ label }}

  //- Input.
  div(class="tw-relative")
    textarea(
      v-bind="$attrs"
      :id="id"
      :value="modelValue"
      :disabled="disabled"
      :rows="rows"
      autocomplete="off"
      autocorrect="off"
      :maxlength="maxLength"
      :minlength="minLength"
      @input="onInput"
      :placeholder="placeholder"
      :class="['tw-w-full tw-rounded-md tw-shadow-sm tw-border-0 tw-py-1.5 tw-text-neutral-900 tw-ring-1 tw-ring-inset placeholder:tw-text-neutral-400 focus:tw-ring-2 focus:tw-ring-inset focus:tw-ring-blue-600 sm:tw-text-sm sm:tw-leading-6 disabled:tw-cursor-not-allowed disabled:tw-bg-neutral-50 disabled:tw-text-neutral-500 disabled:ring-neutral-200', variantClasses]"
      :aria-invalid="variant === 'danger'"
      :aria-describedby="hintId"
      )

    //- Icon.
    div(class="tw-pointer-events-none tw-absolute tw-inset-y-0 tw-right-0 tw-flex tw-items-top tw-pt-2 tw-pr-3")
      //- Icons from https://heroicons.com/
      <svg v-if="variant === 'danger'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" class="tw-w-5 tw-h-5 tw-fill-red-500" aria-hidden="true">
        <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-8-5a.75.75 0 01.75.75v4.5a.75.75 0 01-1.5 0v-4.5A.75.75 0 0110 5zm0 10a1 1 0 100-2 1 1 0 000 2z" clip-rule="evenodd" />
      </svg>
      <svg v-if="variant === 'success'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" class="tw-w-5 tw-h-5 tw-fill-green-500" aria-hidden="true">
        <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clip-rule="evenodd" />
      </svg>
      <svg v-if="variant === 'loading'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" class="tw-w-5 tw-h-5 tw-fill-blue-500 tw-animate-spin" aria-hidden="true">
        <!--! Font Awesome Free 6.4.2 by @fontawesome - https://fontawesome.com License - https://fontawesome.com/license (Commercial License) Copyright 2023 Fonticons, Inc. -->
        <path d="M304 48a48 48 0 1 0 -96 0 48 48 0 1 0 96 0zm0 416a48 48 0 1 0 -96 0 48 48 0 1 0 96 0zM48 304a48 48 0 1 0 0-96 48 48 0 1 0 0 96zm464-48a48 48 0 1 0 -96 0 48 48 0 1 0 96 0zM142.9 437A48 48 0 1 0 75 369.1 48 48 0 1 0 142.9 437zm0-294.2A48 48 0 1 0 75 75a48 48 0 1 0 67.9 67.9zM369.1 437A48 48 0 1 0 437 369.1 48 48 0 1 0 369.1 437z"/>
      </svg>

  //- Hint message.
  div(
    :id="hintId"
    v-if="hintMessage"
    :class="['tw-text-xs tw-text-neutral-400 tw-mt-1', { 'tw-text-red-400': variant === 'danger' }]"
    ) {{ hintMessage }}
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { makeIntoUniqueKey } from '../../utils/generalUtils'

defineOptions({
  inheritAttrs: false
})

const props = withDefaults(defineProps<{
  /**
   * The value of the input.
   */
  modelValue: string
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
  variant?: 'default' | 'danger' | 'success' | 'loading'
  /**
   * Optional: Hint message to show below the input.
   */
  hintMessage?: string
  /**
   * Optional: Placeholder text to show in the input.
   */
  placeholder?: string
  /**
   * Optional: Number of rows to show in the input. Defaults to 3.
   */
  rows?: number
  /**
   * Optional: Maximum length of the input in UTF-16 code units.
   */
  maxLength?: number
  /**
   * Optional: Minimum length of the input in UTF-16 code units.
   */
  minLength?: number
}>(), {
  modelValue: undefined,
  label: undefined,
  variant: 'default',
  hintMessage: undefined,
  placeholder: undefined,
  rows: 3,
  maxLength: undefined,
  minLength: undefined,
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const id = makeIntoUniqueKey('text-input')
const hintId = makeIntoUniqueKey('text-input-hint')

const variantClasses = computed(() => {
  switch (props.variant) {
    case 'danger':
      return 'tw-ring-red-400 tw-text-red-400'
    case 'success':
      return 'tw-ring-green-500'
    case 'loading':
      return 'tw-ring-blue-500'
    default:
      return 'tw-ring-neutral-300'
  }
})

function onInput (event: Event) {
  emit('update:modelValue', (event.target as HTMLTextAreaElement).value)
}
</script>
