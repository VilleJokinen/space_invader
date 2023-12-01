<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div.mb-1.font-weight-bold {{ displayName }}
    MTooltip.ml-2(v-if="displayHint" :content="displayHint" noUnderline): MBadge(shape="pill") ?
  div
    b-form-textarea(
      v-if="fieldInfo.fieldTypeHint && fieldInfo.fieldTypeHint.type === 'textArea'"
      :rows="fieldInfo.fieldTypeHint.props.rows"
      :max-rows="fieldInfo.fieldTypeHint.props.maxRows"
      :value="value"
      @input="update"
      :placeholder="formInputPlaceholder"
      :state="isValid"
      :required="!!notEmptyRule"
      :data-cy="dataCy + '-input'"
    )
    b-input(
      v-else
      :value="value"
      @input="update"
      :type="'text'"
      :placeholder="formInputPlaceholder"
      :state="isValid"
      :required="!!notEmptyRule"
      :data-cy="dataCy + '-input'"
    )
  b-form-invalid-feedback(:state="isValid") {{ validationError }}
</template>

<script lang="ts" setup>
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { computed } from 'vue'
import { MTooltip, MBadge } from '@metaplay/meta-ui-next'

const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: String,
    default: ''
  },
})

const emit = defineEmits(generatedUiFieldFormEmits)

const {
  displayName,
  displayHint,
  formInputPlaceholder,
  isValid: serverIsValid,
  getServerValidationError,
  update,
  dataCy
} = useGeneratedUiFieldForm(props, emit)

const notEmptyRule = computed(() => {
  return props.fieldInfo.validationRules ? props.fieldInfo.validationRules.find((rule: any) => rule.type === 'notEmpty') : null
})

const isValid = computed(() => {
  if (notEmptyRule.value && props.value.length === 0) {
    return false
  } else {
    return serverIsValid.value
  }
})

const validationError = computed((): string | undefined => {
  if (notEmptyRule.value && props.value.length === 0) {
    return notEmptyRule.value.props.message
  } else {
    return (getServerValidationError as any)()
  }
})
</script>
