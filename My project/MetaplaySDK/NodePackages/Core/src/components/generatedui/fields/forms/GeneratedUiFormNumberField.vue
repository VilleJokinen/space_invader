<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div.mb-1.font-weight-bold {{ displayName }}
    MTooltip.ml-2(v-if="displayHint" :content="displayHint" noUnderline): MBadge(shape="pill") ?
  div
    div(v-if="fieldInfo.fieldTypeHint && fieldInfo.fieldTypeHint.type === 'range'")
      b-input(
        :value="value"
        @input="update"
        :type="'range'"
        :min="fieldInfo.fieldTypeHint.props.min"
        :max="fieldInfo.fieldTypeHint.props.max"
        :step="fieldInfo.fieldTypeHint.props.step"
        :placeholder="formInputPlaceholder"
        :state="isValid"
        :data-cy="dataCy + '-input'"
        )
      div Value: {{ value }}
    b-input(
      v-else
      :value="value"
      @input="update"
      :type="'number'"
      :placeholder="formInputPlaceholder"
      :state="isValid"
      :data-cy="dataCy + '-input'"
      )
  b-form-invalid-feedback(:state="isValid") {{ validationError }}
</template>

<script lang="ts" setup>
import { MTooltip, MBadge } from '@metaplay/meta-ui-next'
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'

const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: Number,
    default: 0
  },
})

const emit = defineEmits(generatedUiFieldFormEmits)

const {
  displayName,
  displayHint,
  formInputPlaceholder,
  isValid,
  validationError,
  update: emitUpdate,
  dataCy
} = useGeneratedUiFieldForm(props, emit)

const update = (newValue: string) => {
  emitUpdate(Number(newValue))
}
</script>
