<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div.mb-1
  div.mb-1
    h6 {{ displayName }}
      MTooltip.ml-2(v-if="displayHint" :content="displayHint" noUnderline): MBadge(shape="pill") ?
    b-row.mb-1(v-for="([key, val], idx) in Object.entries(value)", :key="idx")
      b-col
        b-row
          b-col
            generated-ui-view-dynamic-component(
              v-if="keyType"
              v-bind="$props"
              :fieldInfo="{fieldName: 'Key', fieldType: keyType, typeKind: EGeneratedUiTypeKind.Primitive}"
              :value="key"
              :fieldPath="fieldPath + '/$key$/' + key"
              )
        b-row
          b-col
            meta-button(
              @click="remove(key)"
              variant="danger"
              ) Remove
      b-col.pl-3.pr-3.pt-3.pb-2.bg-light.rounded.border
        generated-ui-form-dynamic-component(
          v-if="valueType && valueTypeKind"
          v-bind="props"
          :fieldInfo="{fieldName: 'Value', fieldType: valueType, typeKind: valueTypeKind}"
          :value="val"
          @input="(newVal: any) => updateValue(key, newVal)"
          :fieldPath="fieldPath + '/' + key"
          )
  div
    div
      generated-ui-form-dynamic-component(
        v-if="keyType"
        v-bind="props"
        :fieldInfo="{fieldName: 'Key to add', fieldType: keyType, typeKind: EGeneratedUiTypeKind.Primitive}"
        :value="newKey"
        @input="updateKey"
        )
    div
      meta-button(
        @click="add"
        variant="primary"
        ) Add New
</template>

<script lang="ts" setup>
import { EGeneratedUiTypeKind } from '../../generatedUiTypes'
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { ref, onMounted } from 'vue'
import { MTooltip, MBadge } from '@metaplay/meta-ui-next'
import GeneratedUiFormDynamicComponent from './GeneratedUiFormDynamicComponent.vue'
import GeneratedUiViewDynamicComponent from '../views/GeneratedUiViewDynamicComponent.vue'
import { GetTypeSchemaForTypeName } from '../../getGeneratedUiTypeSchema'

// Use form props but override default value
const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: Object,
    default: () => ({})
  },
})

const emit = defineEmits(generatedUiFieldFormEmits)

const { displayName, displayHint, update: emitUpdate } = useGeneratedUiFieldForm(props, emit)

const keyType = ref('')
const valueType = ref('')
const valueTypeKind = ref<EGeneratedUiTypeKind>()
const newKey = ref<any>(undefined)

onMounted(() => {
  if (!props.fieldInfo.typeParams || props.fieldInfo.typeParams.length < 2) {
    throw new Error('Dictionary field must have at least 2 type params')
  }

  keyType.value = props.fieldInfo.typeParams[0]
  valueType.value = props.fieldInfo.typeParams[1]

  GetTypeSchemaForTypeName(valueType.value).then((schema) => {
    valueTypeKind.value = schema.typeKind
  }).catch((error) => {
    console.error(error)
  })
})

function updateKey (key: any) {
  newKey.value = key
}
function updateValue (key: any, newValue: any) {
  emitUpdate({
    ...props.value,
    [key]: newValue
  })
}
function add () {
  emitUpdate({
    ...props.value,
    [newKey.value]: undefined
  })
}
function remove (key: any) {
  // Copy props.value and filter out the key
  const newVal = Object.fromEntries(Object.entries(props.value).filter(([k, v]) => k !== key))
  emitUpdate(newVal)
}
</script>
