<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- Metaplay's own number input component. Use this in favour of b-form-input with type="number" where possible. -->
<!-- Currently this is just a light wrapper around b-form-input. -->

<template lang="pug">
div
  b-form-input(
    v-bind:value="internalValue"
    @focus="onFocus"
    v-on:input="onInput($event)"
    @change="$emit('change', $event)"
    @blur="onBlur"

    :data-cy="dataCy"
    :disabled="disabled"
    :name="name"

    :state="state"

    :min="min"
    :max="max"
    :placeholder="placeholderText"

    type="number"
    ).mb-1
  b-form-invalid-feedback(v-if="!disabled" :state="validate(internalValue) ? true : null") {{ validate(internalValue) }}
</template>

<script lang="ts" setup>
import { computed, ref, watch } from 'vue'

const props = defineProps<{
  /**
   * Current value of the input box.
   */
  value?: number | null | undefined
  /**
   * Optional: Placeholder text to show when input box is empty.
   */
  placeholder?: string
  /**
   * Optional: 'data-cy' is passed to the child component.
   */
  dataCy?: string
  /**
   * Optional: If true then the input is greyed out and unselectable.
   */
  disabled?: boolean
  /**
   * Optional: 'name' is passed to the underlying form component.
   */
  name?: string
  /**
   * Optional: Smallest allowed number.
   */
  min?: number
  /**
   * Optional: Largest allowed number.
   */
  max?: number
  /**
   * Optional: Value is only changed when the component loses focus, otherwise value is updated on every input.
   */
  lazy?: boolean
  /**
   * Optional: Allow the user to delete all input to indicate a value of null.
   */
  allowNull?: boolean
  /**
   * Optional: Text to show when value is null. Defaults to 'null'.
   */
  nullText?: string
  /**
   * Optional: Allow only integer values.
   */
  integer?: boolean
}>()

const emits = defineEmits(['input', 'change'])

const isFocused = ref(false)
const originalValue = ref(props.value)
const internalValue = ref(props.value)

/**
 * Calculates the render state of the component.
 */
const state = computed(() => {
  if (!props.disabled) {
    const valid = validate(internalValue.value)
    if (typeof valid === 'string') return false
    else return valid
  } else {
    return null
  }
})

/**
 * What text is shown when the input box is empty?
 */
const placeholderText = computed(() => {
  if (props.allowNull) {
    if (props.nullText) return props.nullText
    else return 'null'
  } else return props.placeholder
})

// When value is changed externally we need to update our internal value.
watch(() => props.value, (newValue) => {
  if (!isFocused.value) {
    internalValue.value = newValue
  }
})

/**
 * Given the properties/constraints, check to see if the given value is valid.
 * @param value Value to test.
 * @returns True if input is valid, null if value is null, otherwise a string explaining why it is not.
 */
function validate (value: number | null | undefined) {
  if (props.allowNull && (value === null || value === undefined)) return true
  if (value === null || value === undefined) return null
  if (isNaN(value)) return 'Must be a valid number'
  if (props.min !== undefined && value < props.min) return `Must be more than or equal to ${props.min}`
  if (props.max !== undefined && value > props.max) return `Must be less than or equal to ${props.max}`
  if (props.integer && !Number.isInteger(value)) return 'Must be a whole number'
  return true
}

/**
 * User has selected the input box.
 */
function onFocus () {
  isFocused.value = true

  // Save current value
  originalValue.value = props.value
  internalValue.value = props.value
}

/**
 * User has typed into the input box.
 */
function onInput (value: string) {
  internalValue.value = value === '' ? null : parseFloat(value)
  if (!props.lazy) {
    if (validate(internalValue.value) === true) {
      emits('input', internalValue.value)
    } else {
      emits('input', undefined)
    }
  }
}

/**
 * Input box has lost focus - user clicked away.
 */
function onBlur () {
  if (validate(internalValue.value) !== true) {
    internalValue.value = originalValue.value
  }
  emits('input', internalValue.value)

  isFocused.value = false
}
</script>
