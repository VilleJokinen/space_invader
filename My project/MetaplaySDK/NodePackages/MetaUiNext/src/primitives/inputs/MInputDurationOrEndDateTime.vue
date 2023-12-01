<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  //- Label row.
  div(:class="['tw-flex', !!label ? 'tw-justify-between' : 'tw-justify-end']")
    label(
      v-if="label"
      class="tw-block tw-text-sm tw-font-bold tw-leading-6 tw-text-neutral-900 tw-mb-1"
      ) {{ label }}

    //- Exact date picker switch.
    span
      span(
        @click="internalInputMode = internalInputMode === 'duration' ? 'endDateTime' : 'duration'"
        class="tw-text-xs tw-mr-2"
        ) Exact date picker
      MInputSwitch(
        :model-value="internalInputMode === 'endDateTime' ? true : false"
        @update:model-value="internalInputMode = $event ? 'endDateTime' : 'duration'"
        size="xs"
        class="-tw-mb-1"
        )

  //- Duration picker output is emited as-is.
  MInputDuration(
    v-show="internalInputMode === 'duration'"
    :model-value="modelValue"
    @update:model-value="onDurationChanged"
    :disabled="disabled"
    )
  //- DateTime picker results are transformed into a Duration before emitting.
  MInputDateTime(
    v-show="internalInputMode === 'endDateTime'"
    :model-value="internalDateTime"
    @update:model-value="onDateTimeChanged"
    :min-date-time="referenceDateTime"
    :disabled="disabled"
    )

  div(
    class="tw-text-xs tw-text-neutral-400 tw-mt-1"
    ) {{ internalInputMode === 'duration' ? `Selected duration will end at ${internalDateTime.toLocaleString(DateTime.DATETIME_FULL_WITH_SECONDS)}.` : `Selected end time is ${modelValue.toHuman({ listStyle: "long", unitDisplay: "short" })} after start time.` }}
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'

import { DateTime, Duration } from 'luxon'

import MInputDuration from './MInputDuration.vue'
import MInputDateTime from './MInputDateTime.vue'
import MInputSwitch from './MInputSwitch.vue'

const props = withDefaults(defineProps<{
  /**
   * End date time as a Luxon `Duration` object.
   */
  modelValue: Duration
  /**
   * Optional: Start date time as a Luxon `DateTime` object to calculate the duration preview. Defaults to now.
   */
  referenceDateTime?: DateTime | 'now'
  /**
   * Optional: Input mode. Defaults to `duration`.
   */
  inputMode?: 'duration' | 'endDateTime'
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
}>(), {
  inputMode: 'duration',
  hintMessage: undefined,
  label: undefined,
  referenceDateTime: 'now',
})

const emit = defineEmits<{
  'update:modelValue': [value: Duration]
  'update:duration': [value: Duration]
  'update:endDateTime': [value: DateTime]
}>()

// Keep track of the internal input mode.
const internalInputMode = ref<'duration' | 'endDateTime'>(props.inputMode)
watch(() => props.inputMode, (newValue) => {
  internalInputMode.value = newValue
}, { immediate: true })

// Keep track of the reference date time.
const internalReferenceDateTime = ref<DateTime>(props.referenceDateTime === 'now' ? DateTime.now() : props.referenceDateTime)

// React to outside changes in the reference date time.
watch(() => props.referenceDateTime, (newValue) => {
  internalReferenceDateTime.value = newValue === 'now' ? DateTime.now() : newValue
})

// Use a timer to tick the reference date time every second.
let intervalHandle: ReturnType<typeof setInterval>
onMounted(() => {
  intervalHandle = setInterval(() => {
    if (props.referenceDateTime === 'now' && internalInputMode.value === 'duration') {
      internalReferenceDateTime.value = DateTime.now()
      internalDateTime.value = getSelectedDurationAsDateTime(props.modelValue, internalReferenceDateTime.value)
    }
  }, 1000)
})

onUnmounted(() => {
  clearInterval(intervalHandle)
})

// Keep track of the selected duration as a date time...
const internalDateTime = ref<DateTime>(getSelectedDurationAsDateTime(props.modelValue, internalReferenceDateTime.value))

function getSelectedDurationAsDateTime (selectedDuration: Duration, referenceDateTime: DateTime) {
  return referenceDateTime.plus(selectedDuration)
}

// ...and update it when the model values change...
watch(() => props.modelValue, (newValue) => {
  internalDateTime.value = getSelectedDurationAsDateTime(newValue, internalReferenceDateTime.value)
})
watch(() => props.referenceDateTime, (newValue) => {
  internalDateTime.value = getSelectedDurationAsDateTime(props.modelValue, newValue === 'now' ? DateTime.now() : newValue)
})

function onDurationChanged (newValue?: Duration) {
  if (!newValue) return

  internalDateTime.value = getSelectedDurationAsDateTime(newValue, internalReferenceDateTime.value)
  emit('update:modelValue', newValue)
  emit('update:duration', newValue)
}

function onDateTimeChanged (newValue: DateTime) {
  internalDateTime.value = newValue
  emit('update:modelValue', newValue.diff(internalReferenceDateTime.value, ['days', 'hours', 'minutes']))
  emit('update:endDateTime', newValue)
}
</script>
