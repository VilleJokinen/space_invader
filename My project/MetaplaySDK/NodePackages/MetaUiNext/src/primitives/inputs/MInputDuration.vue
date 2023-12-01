<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  label(
    v-if="label"
    class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900 tw-mb-1"
    ) {{ label }}

  div(class="sm:tw-flex sm:tw-space-x-4 tw-space-y-2 sm:tw-space-y-0")
    span(class="tw-space-x-2 tw-flex tw-grow tw-items-baseline")
      label(
        :for="idDays"
        :class="['tw-text-sm', { 'tw-text-red-500': !valid }]"
        ) Days:
      MInputNumber(
        class="tw-grow"
        name="days"
        :id="idDays"
        :model-value="selectedDays"
        @update:model-value="selectedDays = Number($event)"
        :min="0"
        :max-fraction-digits="5"
        :variant="valid ? 'default' : 'danger'"
        :disabled="disabled"
        placeholder="0-365"
        )

    span(class="tw-space-x-2 tw-flex tw-grow  tw-items-baseline")
      label(
        :for="idHours"
        :class="['tw-text-sm', { 'tw-text-red-500': !valid }]"
        ) Hours:
      MInputNumber(
        class="tw-grow"
        name="hours"
        :id="idHours"
        :model-value="selectedHours"
        @update:model-value="selectedHours = Number($event)"
        :min="0"
        :max-fraction-digits="5"
        :variant="valid ? 'default' : 'danger'"
        :disabled="disabled"
        placeholder="0-24"
        )

    span(class="tw-space-x-2 tw-flex tw-grow  tw-items-baseline")
      label(
        :for="idMinutes"
        :class="['tw-text-sm', { 'tw-text-red-500': !valid }]"
        ) Minutes:
      MInputNumber(
        class="tw-grow"
        name="hours"
        :id="idMinutes"
        :model-value="selectedMinutes"
        @update:model-value="selectedMinutes = Number($event)"
        :min="0"
        :max-fraction-digits="5"
        :variant="valid ? 'default' : 'danger'"
        :disabled="disabled"
        placeholder="0-60"
        )

  div(
    v-if="!valid"
    class="tw-text-xs tw-text-red-500 tw-mt-1"
    ) Enter a valid duration.
  div(
    v-else-if="selectedDuration && referenceDateTime"
    class="tw-text-xs tw-text-neutral-400 tw-mt-1"
    ) Selected duration lasts until {{ referenceDateTime.plus(selectedDuration).toLocaleString(DateTime.DATETIME_FULL_WITH_SECONDS) }}
  div(
    v-if="hintMessage"
    class="tw-text-xs tw-text-neutral-400 tw-mt-1"
    ) {{ hintMessage }}
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { makeIntoUniqueKey } from '../../utils/generalUtils'
import { DateTime, Duration } from 'luxon'
import MInputNumber from './MInputNumber.vue'

const props = defineProps<{
  /**
   * Duration in ISO format (PnDTnHnM) or as a Luxon Duration object.
   */
  modelValue: Duration | string
  /**
   * Optional: Luxon `DateTime` to calculate the end date/time preview.
   */
  referenceDateTime?: DateTime
  /**
   * Optional: Show a label for the input.
   */
  label?: string
  /**
   * Optional: Disable the input. Defaults to false.
   */
  disabled?: boolean
  /**
   * Optional: Hint message to show below the input.
   */
  hintMessage?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value?: Duration]
}>()

// Data model ---------------------------------------------------------------------------------------------------------

const selectedDays = ref<number>(typeof props.modelValue === 'string' ? Duration.fromISO(props.modelValue).days : props.modelValue.days)
const selectedHours = ref<number>(typeof props.modelValue === 'string' ? Duration.fromISO(props.modelValue).hours : props.modelValue.hours)
const selectedMinutes = ref<number>(typeof props.modelValue === 'string' ? Duration.fromISO(props.modelValue).minutes : props.modelValue.minutes)

// Update internal model values when the external model value changes.
watch(() => props.modelValue, (newValue) => {
  if (typeof newValue === 'string') newValue = Duration.fromISO(newValue)

  selectedDays.value = newValue.days
  selectedHours.value = newValue.hours
  selectedMinutes.value = newValue.minutes
}, { immediate: true })

/**
 * External model value (duration) based on internal values.
 */
const selectedDuration = computed(() => {
  const durationObject: any = {}

  if (selectedDays.value) {
    durationObject.days = selectedDays.value
  }
  if (selectedHours.value) {
    durationObject.hours = selectedHours.value
  }
  if (selectedMinutes.value) {
    durationObject.minutes = selectedMinutes.value
  }

  return Duration.fromObject(durationObject)
})

// Emit updates to the external model value.
watch(() => selectedDuration.value, (newValue) => {
  emit('update:modelValue', newValue)
})

// UI stuff -----------------------------------------------------------------------------------------------------------

// Durations of zero are not valid.
const valid = computed(() => {
  return selectedDuration.value > Duration.fromObject({ minutes: 0 })
})

const idDays = makeIntoUniqueKey('days')
const idHours = makeIntoUniqueKey('hours')
const idMinutes = makeIntoUniqueKey('minutes')
</script>
