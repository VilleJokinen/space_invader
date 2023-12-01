<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div.mb-1.font-weight-bold {{ displayName }}
    MTooltip.ml-2(v-if="displayHint" :content="displayHint" noUnderline): MBadge(shape="pill") ?
    MInputDateTime(
      :model-value="DateTime.fromISO(value)"
      @update:model-value="onDateTimeChange"
      :data-cy="dataCy + '-input'"
      )
  b-form-invalid-feedback(:state="isValid") {{ validationError }}
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { MInputDateTime, MTooltip, MBadge } from '@metaplay/meta-ui-next'

const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: String,
    default: ''
  },
})

const emit = defineEmits(generatedUiFieldFormEmits)

/**
 * Utility function to prevent undefined inputs.
 */
function onDateTimeChange (value?: DateTime) {
  if (!value) return
  update(value.toISO())
}

const {
  displayName,
  displayHint,
  isValid,
  validationError,
  update,
  dataCy
} = useGeneratedUiFieldForm(props, emit)
</script>
