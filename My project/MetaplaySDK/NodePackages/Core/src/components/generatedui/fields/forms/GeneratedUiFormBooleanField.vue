<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div.mb-1.font-weight-bold {{ displayName }}
    MTooltip.ml-2(v-if="displayHint" :content="displayHint" noUnderline): MBadge(shape="pill") ?
  MInputSwitch(
    :model-value="value"
    @update:model-value="update"
    size="md"
    :variant="isValid ? 'primary' : 'danger'"
    :data-cy="dataCy + '-input'"
  )
  b-form-invalid-feedback(:state="isValid") {{ validationError }}
</template>

<script lang="ts" setup>
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { onMounted } from 'vue'
import { MTooltip, MBadge, MInputSwitch } from '@metaplay/meta-ui-next'

const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: Boolean,
    default: false
  },
})

const emit = defineEmits(generatedUiFieldFormEmits)

const {
  displayName,
  displayHint,
  isValid,
  validationError,
  update,
  dataCy
} = useGeneratedUiFieldForm(props, emit)

onMounted(() => {
  update(props.value)
})
</script>
