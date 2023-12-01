<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="typeOptions && typeOptions.length === 0") No types available.
div(v-else)
  div.mb-3(v-if="showPicker")
    div.mb-1.font-weight-bold {{displayName}} Type
    b-form-select(
      :value="selectedType"
      @input="updateSelectedType"
      :options="typeOptions"
      :data-cy="dataCy + '-type-input'"
    )
  generated-ui-form-dynamic-component(
    v-if="selectedTypeName"
    v-bind="props"
    :key="selectedTypeName"
    :fieldInfo="{...fieldInfo, fieldType: selectedTypeName }"
    :value="value"
    @input="update"
    )
</template>

<script lang="ts" setup>
import type { IGeneratedUiFieldSchemaDerivedTypeInfo } from '../../generatedUiTypes'
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { computed, onMounted, ref } from 'vue'
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
  dataCy,
  displayName,
  update: emitUpdate,
} = useGeneratedUiFieldForm(props, emit)

// --- Cache values for each type so form is not cleared every time ---

const typeCache: { [key: string]: any } = ref({})

onMounted(() => {
  if ('$type' in props.value) {
    typeCache.value = {
      ...typeCache.value,
      [props.value.$type]: props.value
    }
  }
})

// --- Type options ---

function includeTypeOption (type: IGeneratedUiFieldSchemaDerivedTypeInfo) {
  if (type.jsonType in typeCache.value) {
    return true
  }
  const customFilter = props.abstractTypeFilter?.(props.fieldSchema.typeName)
  if (customFilter) {
    return customFilter(type)
  }
  return !type.isDeprecated
}

const typeOptions = computed(() => {
  if (!props.fieldSchema.derived) throw new Error('Derived schema not defined')
  return props.fieldSchema.derived.filter(includeTypeOption).map((type: IGeneratedUiFieldSchemaDerivedTypeInfo) => {
    return {
      value: type,
      text: (type.isDeprecated ? '(deprecated) ' : '') + type.typeName.split('.').at(-1)
    }
  })
})

const selectedType = computed(() => {
  if ('$type' in props.value) {
    if (!props.fieldSchema.derived) throw new Error('Derived schema not defined')
    return props.fieldSchema.derived.find((x: any) => x.jsonType === props.value.$type)
  } else {
    return typeOptions.value[0]?.value ?? ''
  }
})
const selectedTypeName = computed(() => {
  return selectedType.value?.typeName
})

function updateSelectedType (newValue: any) {
  const newType: string = newValue.jsonType
  if (newType in typeCache.value) {
    emitUpdate(typeCache.value[newType])
  } else {
    emitUpdate({ $type: newType })
  }
}

const showPicker = computed(() => {
  return typeOptions.value.length > 1
})

function update (newValue: any) {
  const type = selectedType.value?.jsonType
  if (!type) throw new Error('Type not defined')
  const val = { ...newValue, $type: type }

  typeCache.value = {
    ...typeCache.value,
    [type]: val
  }
  emitUpdate(val)
}

</script>
