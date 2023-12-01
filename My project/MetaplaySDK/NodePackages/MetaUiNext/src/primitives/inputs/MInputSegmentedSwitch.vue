<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Container. Z-index here sets a new stacking context for the indicator.
div(class="tw-relative tw-z-0")
  //- Label.
  label(
    v-if="label"
    class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900"
    ) {{ label }}

  //- Body.
  div(class="tw-my-1")
    //- Switch outline.
    div(
      v-bind="api.rootProps"
      :class="['tw-relative tw-mx-0.5 tw-inline-flex tw-px-0.5 tw-rounded-full tw-border', bodyVariantClasses]"
      :style="'box-shadow: inset 0 1px 2px 0 rgba(0,0,0,.1), inset 0 -1px 2px 0 rgba(255,255,255,.8);' + bodySizeStyles"
      )
      div(class="tw-bg-neutral-50 tw-absolute tw-rounded-full -tw-z-20 tw-inset-0")

      //- Selection indicator.
      div(
        v-bind="api.indicatorProps"
        :class="['-tw-z-10 tw-rounded-full', indicatorVariantClasses]"
        style="box-shadow: inset 0 -3px 0 0 rgba(0,0,0,.1), inset 0 2px 0 0 rgba(255,255,255,.2), 0 1px 3px 0 rgb(0 0 0 / 0.2), 0 1px 2px -1px rgb(0 0 0 / 0.4);"
        )

      //- Switch options.
      div(
        class="tw-space-x-0.5 tw-inline-flex tw-flex-shrink-0"
        )
        div(
          v-for="option in options"
          :key="String(option.value)"
          )
          label(
            v-bind="getOptionProps(option.value)"
            :class="['tw-m-0 tw-inline-block tw-rounded-full tw-ring-0 tw-py-1 tw-font-medium tw-transition-colors', optionSizeClasses, { 'tw-cursor-pointer hover:tw-bg-neutral-200': !disabled, 'tw-cursor-not-allowed': disabled, 'tw-pointer-events-none': api.value === option.value, 'tw-text-white': api.value === option.value && !disabled, 'tw-text-neutral-500': disabled }]"
            )
            span(v-bind="getOptionLabelProps(option.value)") {{ option.label }}
            input(v-bind="getRadioHiddenInputProps(option.value)")

  div(
    v-if="hintMessage"
    class="tw-text-xs tw-text-neutral-400"
    ) {{ hintMessage }}
</template>

<script setup lang="ts" generic="T">
import * as zagRadio from '@zag-js/radio-group'
import { normalizeProps, useMachine } from '@zag-js/vue'

import { computed, watch } from 'vue'
import type { Variant } from '../../utils/types'
import { makeIntoUniqueKey } from '../../utils/generalUtils'

const props = withDefaults(defineProps<{
  /**
   * The current value of the switch.
   */
  modelValue: T
  /**
   * The options to display.
   */
  options: Array<{ label: string, value: T }>
  /**
   * Optional: Disable the switch. Defaults to false.
   */
  disabled?: boolean
  /**
   * Optional: The visual variant of the switch. Defaults to 'primary'.
   */
  variant?: Variant
  /**
   * Optional: The size of the switch. Defaults to 'md'.
   */
  size?: 'sm' | 'md'
  /**
   * Optional: Label for the switch.
   */
  label?: string
  /**
   * Optional: Hint message to display under the switch.
   */
  hintMessage?: string
}>(), {
  disabled: false,
  variant: 'primary',
  size: 'md',
  label: undefined,
  hintMessage: undefined,
})

const emit = defineEmits<{
  'update:modelValue': [value: T]
}>()

// Zag Switch ---------------------------------------------------------------------------------------------------------

/**
 * Show a warning if the modelValue is not a string.
 * This is because Zag Radio Group only supports string values.
 */
if (typeof props.modelValue !== 'string') {
  console.error('MInputSegmentedSwitch: modelValue must be a string.')
}

const [state, send] = useMachine(zagRadio.machine({
  id: makeIntoUniqueKey('segmentedswitch'),
  disabled: props.disabled,
  value: props.modelValue as string,
  name: props.label,
  onValueChange: ({ value }) => emit('update:modelValue', value as T),
}))

const api = computed(() =>
  zagRadio.connect(state.value, send, normalizeProps),
)

/**
 * Watch for changes to the modelValue and update the Zag Radio Group.
 */
watch(() => props.modelValue, (newValue) => {
  api.value.setValue(newValue as string)
})

/**
 * Functions that cast the generic type to a string and pass it to the Zag Radio Group.
 */
function getOptionProps (value: T) {
  return api.value.getItemProps({ value: value as string })
}
function getOptionLabelProps (value: T) {
  return api.value.getItemTextProps({ value: value as string })
}
function getRadioHiddenInputProps (value: T) {
  return api.value.getItemHiddenInputProps({ value: value as string })
}

// UI visuals ---------------------------------------------------------------------------------------------------------

/**
 * Computed property to se bot padding based on the switch size.
 */
const bodySizeStyles = computed(() => {
  if (props.size === 'sm') return 'padding-bottom: 2px;'
  else return 'padding-top: 2px; padding-bottom: 2px;'
})

/**
 * Computed property to set the option text and padding based on the switch size.
 */
const optionSizeClasses = computed(() => {
  if (props.size === 'sm') return 'tw-text-xs tw-px-2'
  else return 'tw-text-sm tw-px-3'
})

/**
 * Computed property to set the switch outline color based on the variant and disabled state.
 */
const bodyVariantClasses = computed(() => {
  if (props.variant === 'success') {
    if (props.disabled) return 'tw-border-green-200'
    else return 'tw-border-green-500'
  }

  if (props.variant === 'warning') {
    if (props.disabled) return 'tw-border-orange-200'
    else return 'tw-border-orange-500'
  }

  if (props.variant === 'danger') {
    if (props.disabled) return 'tw-border-red-200'
    else return 'tw-border-red-500'
  }

  if (props.disabled) return 'tw-border-neutral-200'
  else return 'tw-border-neutral-300'
})

/**
 * Computed property to set the indicator color based on the variant and disabled state.
 */
const indicatorVariantClasses = computed(() => {
  if (props.variant === 'success') {
    if (props.disabled) return 'tw-bg-green-200'
    else return 'tw-bg-green-500'
  }

  if (props.variant === 'warning') {
    if (props.disabled) return 'tw-bg-orange-200'
    else return 'tw-bg-orange-500'
  }

  if (props.variant === 'danger') {
    if (props.disabled) return 'tw-bg-red-200'
    else return 'tw-bg-red-500'
  }

  if (props.disabled) return 'tw-bg-neutral-200'
  else return 'tw-bg-blue-500'
})

</script>
