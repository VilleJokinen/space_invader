<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  label(
    v-if="label"
    :for="id"
    class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900 tw-mb-1"
    ) {{ label }}

  datepicker(
    :id="id"
    :model-value="modelValue"
    @update:model-value="emitUpdatedValue"
    :disabled="disabled"
    :min-date="actualMinDateTime"
    :max-date="actualMaxDateTime"
    :show-now-button="true"
    :text-input="true"
    :clearable="false"
    time-picker-inline
    class="tw-rounded tw-shadow-sm"
    )
    template(#input-icon)
      //- Icon from https://heroicons.com/
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" :class="['tw-w-5 tw-h-5 tw-ml-2', { 'tw-text-neutral-400 tw-cursor-not-allowed': disabled }]">
        <path fill-rule="evenodd" d="M5.75 2a.75.75 0 01.75.75V4h7V2.75a.75.75 0 011.5 0V4h.25A2.75 2.75 0 0118 6.75v8.5A2.75 2.75 0 0115.25 18H4.75A2.75 2.75 0 012 15.25v-8.5A2.75 2.75 0 014.75 4H5V2.75A.75.75 0 015.75 2zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75z" clip-rule="evenodd" />
      </svg>

  div(
    v-if="hintMessage"
    class="tw-text-xs tw-text-neutral-400 tw-mt-1"
    ) {{ hintMessage }}
</template>

<script setup lang="ts">
import { DateTime } from 'luxon'
import { computed } from 'vue'
import Datepicker from '@vuepic/vue-datepicker'
import '@vuepic/vue-datepicker/dist/main.css'
import { makeIntoUniqueKey } from '../../utils/generalUtils'

const props = defineProps<{
  /**
   * Pass a date to the underlying component. Use `v-model="value"` instead.
   */
  modelValue: DateTime
  /**
   * Optional: Show a label for the input.
   */
  label?: string
  /**
   * Optional: Hint message to show below the input.
   */
  hintMessage?: string
  /**
   * Optional: Disables input to the picker.
   */
  disabled?: boolean
  /**
   * Optional: Dates before the given date will be disabled. Passing in the string value `now` sets this value to the
   * current date and time.
   */
  minDateTime?: DateTime | 'now'
  /**
   * Optional: Dates after the given date will be disabled. Passing in the string value `now` sets this value to the
   * current date and time.
   */
  maxDateTime?: DateTime | 'now'
}>()

const id = makeIntoUniqueKey('datetime')

/**
 * The `datepicker` component needs `min-date` as a `Date`, so we need to convert it.
 */
const actualMinDateTime = computed((): Date | undefined => {
  if (props.minDateTime instanceof DateTime) {
    return (props.minDateTime).toJSDate()
  } else if (props.minDateTime === 'now') {
    return DateTime.now().toJSDate()
  } else {
    return undefined
  }
})

/**
 * The `datepicker` component needs `max-date` as a `Date`, so we need to convert it.
 */
const actualMaxDateTime = computed((): Date | undefined => {
  if (props.maxDateTime instanceof DateTime) {
    return (props.maxDateTime).toJSDate()
  } else if (props.maxDateTime === 'now') {
    return DateTime.now().toJSDate()
  } else {
    return undefined
  }
})

const emit = defineEmits<{
  'update:modelValue': [value: DateTime]
}>()

/**
 * Emit value as a `DateTime` object. The `datepicker` component gives us the updated value as a `Date`, so we need to
 * convert it before emitting it.
 * @param value New datetime value
 */
function emitUpdatedValue (value: Date) {
  emit('update:modelValue', DateTime.fromJSDate(value))
}
</script>

<style>
/** Hide the top row month and year selection buttons because they didn't work as of release 23. */
.dp__month_year_select {
  pointer-events: none;
}

/** Scale down the default font size to look better. */
.dp__action_button {
  font-size: 80%;
}

/** Hide the cancel button to give more space to the selected date time preview text label. */
.dp__action_buttons :first-child {
  display: none;
}

/** Nicer visuals for disabled state. */
.dp__disabled {
  cursor: not-allowed;
  color: rgb(163 163 163);
}

:root {
  --dp-font-family: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, "Noto Sans", sans-serif, "Apple Color Emoji", "Segoe UI Emoji", "Segoe UI Symbol", "Noto Color Emoji";
  --dp-font-size: 0.875rem;
  --dp-input-padding: 6px 8px 6px 8px;
  --dp-input-icon-padding: 33px;
}
</style>
