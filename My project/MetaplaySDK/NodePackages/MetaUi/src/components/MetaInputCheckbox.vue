<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- Metaplay's own checkbox component. Use this in favour of b-form-checkbox where possible. -->
<!-- Currently this is just a light wrapper around b-form-checkbox. -->

<template lang="pug">
b-form-checkbox(
  :checked="value"
  @input="$emit('input', $event)"
  @change="$emit('change', $event)"

  :data-cy="dataCy"
  :disabled="disabled"
  :name="name"

  :size="size"
  :switch="showAs === 'switch'"

  :class="hasLabelSlot ? null : 'no-label'"
  )
    slot
</template>

<script lang="ts" setup>
import { computed, useSlots } from 'vue'

defineProps({
  /**
   *  Value to edit. Use 'v-model="value"' instead.
   */
  value: {
    type: Boolean,
    required: false,
  },
  /**
   * 'data-cy' is passed to the child component.
   */
  dataCy: {
    type: String,
    default: null
  },
  /**
   * If true then the input is greyed out and unselectable.
   */
  disabled: {
    type: Boolean,
    default: false
  },
  /**
   * 'name' is passed to the underlying form component.
   */
  name: {
    type: String,
    required: true,
  },
  /**
   * Render as checkbox or switch.
   */
  showAs: {
    type: String,
    validator: (prop: string) => { return ['checkbox', 'switch'].includes(prop) },
    default: 'checkbox'
  },
  /**
   * Size to render the component at.
   */
  size: {
    type: String,
    validator: (prop: string) => { return ['sm', 'md', 'lg'].includes(prop) },
    default: 'md'
  }
})

defineEmits([
  'input',
  'change'
])

const slots = useSlots()

/**
 * Is there any label text defined?
 */
const hasLabelSlot = computed(() => slots.default !== undefined)
</script>

<style>
.no-label.custom-switch.b-custom-control-lg .custom-control-label::before, .input-group-lg .custom-switch .custom-control-label::before {
  left: -2.25rem;
}

.no-label.custom-switch.b-custom-control-lg .custom-control-label::after, .input-group-lg .custom-switch .custom-control-label::after {
  left: -34px;
}

.no-label.custom-switch.b-custom-control-md .custom-control-label::before, .input-group-md .custom-switch .custom-control-label::before {
  left: -1.85rem;
}

.no-label.custom-switch.b-custom-control-md .custom-control-label::after, .input-group-md .custom-switch .custom-control-label::after {
  left: -28px;
}

.no-label.custom-switch.b-custom-control-sm .custom-control-label::before, .input-group-sm .custom-switch .custom-control-label::before {
  left: -1.65rem;
}

.no-label.custom-switch.b-custom-control-sm .custom-control-label::after, .input-group-sm .custom-switch .custom-control-label::after {
  left: -24px;
}
</style>
