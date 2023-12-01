<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div.mb-1.font-weight-bold {{ displayName }}
    MTooltip.ml-2(v-if="displayHint" :content="displayHint" noUnderline): MBadge(shape="pill") ?
  b-form-select(
    :value="value"
    @input="update"
    :options="fieldSchema.possibleValues"
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
    type: String,
    default: ''
  },
})

const emit = defineEmits(generatedUiFieldFormEmits)

const {
  displayName,
  displayHint,
  isValid,
  validationError,
  update,
  dataCy,
  useDefault
} = useGeneratedUiFieldForm(props, emit)

// TODO: Improve the prop typings so we don't need to use non-null assertions.
// eslint-disable-next-line @typescript-eslint/no-non-null-assertion
useDefault('', props.fieldSchema.possibleValues![0])

</script>
