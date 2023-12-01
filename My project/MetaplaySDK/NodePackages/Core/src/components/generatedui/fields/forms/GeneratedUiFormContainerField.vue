<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="fieldSchema.typeKind === EGeneratedUiTypeKind.Class && fieldSchema.fields")
  b-row.mb-3(v-for="field in editableFields" :key="field.fieldName")
    b-col
      generated-ui-form-dynamic-component(
        v-bind="props"
        :fieldInfo="field"
        :value="hasValue(camelCase(field.fieldName)) ? value[camelCase(field.fieldName)] : (field.default)"
        @input="(newVal: any) => update(camelCase(field.fieldName), newVal)"
        :fieldPath="fieldPath.length === 0 ? field.fieldName : (fieldPath + '/' + field.fieldName)"
        )
div(v-else) {{displayName}}: {{value}}
</template>

<script lang="ts" setup>
import { EGeneratedUiTypeKind } from '../../generatedUiTypes'
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { computed, onMounted } from 'vue'
import { camelCase } from '../../generatedUiUtils'
import GeneratedUiFormDynamicComponent from './GeneratedUiFormDynamicComponent.vue'

const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: Object,
    default: () => ({})
  }
})

const emit = defineEmits(generatedUiFieldFormEmits)

const {
  displayName,
  update: emitUpdate,
} = useGeneratedUiFieldForm(props, emit)

// TODO: Improve the prop typings to avoid the need for these non-null assertions.
// eslint-disable-next-line @typescript-eslint/no-non-null-assertion
const editableFields = computed(() => props.fieldSchema.fields!.filter((x: any) => !x.notEditable))
// eslint-disable-next-line @typescript-eslint/no-non-null-assertion
const schemaFields = computed(() => new Set(props.fieldSchema.fields!.map((x: any) => camelCase(x.fieldName))))
const valueAsInput = computed(() => Object.fromEntries(Object.entries(props.value).filter(([k, v]) => k === '$type' || schemaFields.value.has(k))))

onMounted(() => {
  if (Object.entries(props.value).some(([k, v]) => !(k === '$type' || schemaFields.value.has(k))) ||
    Object.entries(props.value).length <= 1) {
    // Update the value after a timeout to avoid two updates messing eachother up
    setTimeout(() => {
      emitUpdate(valueAsInput.value)
    }, 10)
  }
})

const hasValue = (fieldName: string): boolean => {
  return fieldName in props.value && props.value[fieldName] !== null && props.value[fieldName] !== undefined
}

const update = (fieldName: string, newValue: any) => {
  emitUpdate({
    ...valueAsInput.value,
    [fieldName]: newValue
  })
}
</script>
