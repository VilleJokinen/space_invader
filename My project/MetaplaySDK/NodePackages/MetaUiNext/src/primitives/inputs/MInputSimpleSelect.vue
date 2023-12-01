<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Container.
div
  //- Label.
  label(
    v-bind="api.labelProps"
    v-if="label"
    :for="id"
    class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900 tw-mb-1"
    ) {{ label }}

  //- Input.
  div
    button(
      v-bind="api.triggerProps"
      :class="['tw-w-full tw-flex tw-justify-between tw-px-3 tw-rounded-md tw-shadow-sm tw-border-0 tw-py-1.5 tw-text-neutral-900 tw-ring-1 tw-ring-inset placeholder:tw-text-neutral-400 focus:tw-ring-2 focus:tw-ring-inset focus:tw-ring-blue-600 sm:tw-text-sm sm:tw-leading-6 disabled:tw-cursor-not-allowed disabled:tw-bg-neutral-50 disabled:tw-text-neutral-500 disabled:ring-neutral-200', variantClasses]"
      )
      span(:class="{ 'tw-text-neutral-400': !api.valueAsString }") {{ api.valueAsString || placeholder || 'Select option' }}
      span(class="tw-text-neutral-400") ▼

  //- Options popover.
  Teleport(to="body")
    div(
      v-bind="api.positionerProps"
      )
      ul(
        v-bind="api.contentProps"
        class="tw-rounded-md tw-bg-white tw-shadow-lg tw-border tw-border-neutral-300 tw-text-sm tw-max-w-xl tw-w-48"
        )
        li(
          v-bind="api.getItemProps({ item: option })"
          v-for="option in options"
          :key="option.value"
          :class="['tw-px-3 tw-py-1.5 tw-flex tw-justify-between first:tw-rounded-t-md last:tw-rounded-b-md', { 'tw-bg-blue-500 hover:tw-bg-blue-600 tw-text-white': api.selectedItems.some((selectedOption) => selectedOption.value === option.value) }]"
          )
            span {{ option.label }}
            span(v-bind="api.getItemIndicatorProps({ item: option })") ✓

  //- Hint message.
  div(
    v-if="hintMessage"
    :class="['tw-text-xs tw-text-neutral-400 tw-mt-1', { 'tw-text-red-400': variant === 'danger' }]"
    ) {{ hintMessage }}
</template>

<script setup lang="ts">
import { computed, watch } from 'vue'
import { makeIntoUniqueKey } from '../../utils/generalUtils'
import * as select from '@zag-js/select'
import { normalizeProps, useMachine } from '@zag-js/vue'

const props = withDefaults(defineProps<{
  /**
   * The value of the input. Can be undefined.
   */
  modelValue?: string
  /**
   * The collection of items to show in the select.
   */
  options: Array<{ label: string, value: string }>
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
   * Optional: Placeholder text to show in the input.
   */
  placeholder?: string
}>(), {
  modelValue: undefined,
  label: undefined,
  variant: 'default',
  hintMessage: undefined,
  placeholder: undefined,
})

const emit = defineEmits<{
  'update:modelValue': [value?: string]
}>()

/**
 * Helper to get variant specific classes.
 */
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

// Zag Select ---------------------------------------------------------------------------------------------------------

const id = makeIntoUniqueKey('select')

const [state, send] = useMachine(select.machine({
  id,
  collection: select.collection({ items: props.options }),
  value: props.modelValue ? [props.modelValue] : undefined,
  disabled: props.disabled,
  loop: true,
  onValueChange: ({ value }) => emit('update:modelValue', value[0]),
}))

const api = computed(() => select.connect(state.value, send, normalizeProps))

// Watch for prop updates.
watch(() => props.modelValue, (newValue) => {
  // Zag Select doesn't support undefined values.
  if (newValue === undefined) return
  api.value.setValue([newValue])
})

// Also watch for changes to the options.
watch(() => props.options, (newValue) => {
  api.value.setCollection(select.collection({ items: newValue }))
})
</script>

<style scoped>
[data-part="item"][data-highlighted] {
  @apply tw-bg-blue-500 tw-text-white;
}
</style>
