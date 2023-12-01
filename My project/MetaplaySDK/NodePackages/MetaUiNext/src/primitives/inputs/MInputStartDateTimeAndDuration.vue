<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Note: adding a z-index to the container is necessary to make the date picker's popper show up above sibling elements, as container queries establish a new stacking context.
div(class="tw-relative tw-z-10")
  div(class="tw-@container")
    div(class="tw-w-full tw-space-y-4 @lg:tw-flex @lg:tw-space-x-4 @lg:tw-space-y-0")
      MInputDateTime(
        :model-value="startDateTime"
        @update:model-value="$emit('update:startDateTime', $event)"
        :disabled="disabled"
        label="Start"
        )

      MInputDurationOrEndDateTime(
        :model-value="duration"
        @update:model-value="$emit('update:duration', $event)"
        :referenceDateTime="startDateTime"
        :disabled="disabled"
        label="Duration"
        class="tw-grow"
        )
</template>

<script setup lang="ts">
import { DateTime, Duration } from 'luxon'
import MInputDateTime from './MInputDateTime.vue'
import MInputDurationOrEndDateTime from './MInputDurationOrEndDateTime.vue'

const props = defineProps<{
  /**
   * Start date time as a Luxon `DateTime` object.
   */
  startDateTime: DateTime
  /**
   * Duration as a Luxon `Duration` object.
   */
  duration: Duration
  /**
   * Optional: Disable the inputs. Defaults to false.
   */
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:startDateTime': [value: DateTime]
  'update:duration': [value: Duration]
}>()
</script>
